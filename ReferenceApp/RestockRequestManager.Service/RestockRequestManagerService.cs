// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace RestockRequestManager.Service
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Common;
    using Inventory.Domain;
    using Microsoft.ServiceFabric.Actors;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using Microsoft.ServiceFabric.Services.Remoting.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;
    using RestockRequest.Domain;
    using RestockRequestManager.Domain;

    internal class RestockRequestManagerService : StatefulService, IRestockRequestManager, IRestockRequestEvents
    {
        //TODO: Look@ use of these variables.
        private const string ItemIdToActorIdMapName = "actorIdToMapName"; //Name of ItemId-ActorId IReliableDictionary
        private const string CompletedRequestsQueueName = "completedRequests"; //Name of CompletedRequests IReliableQueue
        private const string InventoryServiceName = "InventoryService";
        private static TimeSpan CompletedRequestsBatchInterval = TimeSpan.FromSeconds(1);
        private static TimeSpan TxTimeout = TimeSpan.FromSeconds(4);

        public string ApplicationName
        {
            get { return this.ServiceInitializationParameters.CodePackageActivationContext.ApplicationName; }
        }

        /// <summary>
        /// This method uses an IReliableQueue to store completed RestockRequests which are later sent to the client using batch processing.
        /// We could send the request immediately but we prefer to minimize traffic back to the Inventory Service by batching multiple requests
        /// in one trip. 
        /// </summary>
        /// <param name="actorId"></param>
        /// <param name="request"></param>
        public async void RestockRequestCompleted(ActorId actorId, RestockRequest request)
        {
            IReliableQueue<RestockRequest> completedRequests = await this.StateManager.GetOrAddAsync<IReliableQueue<RestockRequest>>(CompletedRequestsQueueName);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                await completedRequests.EnqueueAsync(tx, request);
                await tx.CommitAsync();
            }

            IRestockRequestActor restockRequestActor = ActorProxy.Create<IRestockRequestActor>(actorId, this.ApplicationName);
            await restockRequestActor.UnsubscribeAsync<IRestockRequestEvents>(this); //QUESTION:What does this method do?
        }

        /// <summary>
        /// This method activates an actor to fulfill the RestockRequest.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task AddRestockRequestAsync(RestockRequest request)
        {
            try
            {
                //Get dictionary of Restock Requests
                IReliableDictionary<InventoryItemId, ActorId> requestDictionary =
                    await this.StateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, ActorId>>(ItemIdToActorIdMapName);

                ActorId actorId = ActorId.NewId();

                try
                {
                    using (ITransaction tx = this.StateManager.CreateTransaction())
                    {
                        await requestDictionary.AddAsync(tx, request.ItemId, actorId);
                        await tx.CommitAsync();
                    }
                }
                catch (ArgumentException)
                {
                    // restock request already exists
                    return;
                }

                // Create actor proxy and send the request
                IRestockRequestActor restockRequestActor = ActorProxy.Create<IRestockRequestActor>(actorId, this.ApplicationName);

                await restockRequestActor.AddRestockRequestAsync(request);

                // Successfully added, register for event notifications for completion
                await restockRequestActor.SubscribeAsync<IRestockRequestEvents>(this);

                ServiceEventSource.Current.ServiceMessage(this, "Created restock request. Item ID: {0}. Actor ID: {1}", request.ItemId, actorId);
            }
            catch (InvalidOperationException ex)
            {
                ServiceEventSource.Current.Message(string.Format("RestockRequestManagerService: Actor rejected {0}: {1}", request, ex));
                throw;
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Message(string.Format("RestockRequestManagerService: Exception {0}: {1}", request, ex));
                throw;
            }
        }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new List<ServiceReplicaListener>()
            {
                new ServiceReplicaListener(
                    (initParams) =>
                        new ServiceRemotingListener<IRestockRequestManager>(initParams, this))
            };
        }

        /// <summary>
        /// Drains the queue of completed restock requests sends them to InventoryService.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            IReliableQueue<RestockRequest> completedRequests = await this.StateManager.GetOrAddAsync<IReliableQueue<RestockRequest>>(CompletedRequestsQueueName);

            while (!cancellationToken.IsCancellationRequested)
            {
                using (ITransaction tx = this.StateManager.CreateTransaction())
                {
                    ConditionalResult<RestockRequest> result = await completedRequests.TryDequeueAsync(tx, TxTimeout, cancellationToken);

                    if (result.HasValue)
                    {
                        ServiceUriBuilder builder = new ServiceUriBuilder(InventoryServiceName);
                        IInventoryService inventoryService = ServiceProxy.Create<IInventoryService>(result.Value.ItemId.GetPartitionKey(), builder.ToUri());

                        await inventoryService.AddStockAsync(result.Value.ItemId, result.Value.Quantity);

                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "Adding stock to inventory service. ID: {0}. Quantity: {1}",
                            result.Value.ItemId,
                            result.Value.Quantity);
                    }

                    // This commits the dequeue operations.
                    // If the request to add the stock to the inventory service throws, this commit will not execute
                    // and the items will remain on the queue, so we can be sure that we didn't dequeue items
                    // that didn't get saved successfully in the inventory service.
                    // However there is a very small chance that the stock was added to the inventory service successfully,
                    // but service execution stopped before reaching this commit (machine crash, for example).
                    await tx.CommitAsync();
                }

                await Task.Delay(CompletedRequestsBatchInterval, cancellationToken);
            }
        }
    }
}