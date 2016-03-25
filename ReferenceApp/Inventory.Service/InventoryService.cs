// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Inventory.Service
{
    using Common;
    using Inventory.Domain;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using Microsoft.ServiceFabric.Services.Remoting.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;
    using RestockRequest.Domain;
    using RestockRequestManager.Domain;
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    internal class InventoryService : StatefulService, IInventoryService
    {
        private const string InventoryItemDictionaryName = "inventoryItems";
        private const string ActorMessageDictionaryName = "incomingMessages";
        private const string RestockRequestManagerServiceName = "RestockRequestManager";
        private const string RequestHistoryDictionaryName = "RequestHistory";
        private const string BackupCountDictionaryName = "BackupCountingDictionary";
        internal const string InventoryServiceType = "InventoryServiceType";

        //private IReliableStateManager stateManager;
        private IBackupStore backupManager;

        //Set local or cloud backup, or none. Disabled is the default. Overridden by config.
        private BackupManagerType backupStorageType = BackupManagerType.None;

        /// <summary>
        /// This constructor is used in unit tests to inject a different state manager for unit testing.
        /// </summary>
        /// <param name="stateManager"></param>

        public InventoryService(StatefulServiceContext serviceContext) : this(serviceContext, (new ReliableStateManager(serviceContext)))
        {

        }

        public InventoryService(StatefulServiceContext context, IReliableStateManagerReplica stateManagerReplica) : base(context, stateManagerReplica)
        {
            var partitionId = context.PartitionId.ToString("N");
            var minKey = ((Int64RangePartitionInformation)this.Partition.PartitionInfo).LowKey;
            var maxKey = ((Int64RangePartitionInformation)this.Partition.PartitionInfo).HighKey;

            if (context.CodePackageActivationContext != null)
            {
                ICodePackageActivationContext codePackageContext = context.CodePackageActivationContext;
                var configPackage = codePackageContext.GetConfigurationPackageObject("Config");
                var configSection = configPackage.Settings.Sections["Inventory.Service.Settings"];

                string backupSettingValue = configSection.Parameters["BackupMode"].Value;

                if (string.Equals(backupSettingValue, "none", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.backupStorageType = BackupManagerType.None;
                }
                else if (string.Equals(backupSettingValue, "azure", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.backupStorageType = BackupManagerType.Azure;

                    var azureBackupConfigSection = configPackage.Settings.Sections["Inventory.Service.BackupSettings.Azure"];

                    this.backupManager = new AzureBlobBackupManager(azureBackupConfigSection, partitionId, minKey, maxKey, codePackageContext.TempDirectory);
                }
                else if (string.Equals(backupSettingValue, "local", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.backupStorageType = BackupManagerType.Local;

                    var localBackupConfigSection = configPackage.Settings.Sections["Inventory.Service.BackupSettings.Local"];

                    this.backupManager = new DiskBackupManager(localBackupConfigSection, partitionId, minKey, maxKey, codePackageContext.TempDirectory);
                }
                else
                {
                    throw new ArgumentException("Unknown backup type");
                }
            }
        }

        /// <summary>
        /// Used internally to generate inventory items and adds them to the ReliableDict we have.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public async Task<bool> CreateInventoryItemAsync(InventoryItem item)
        {
            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                await inventoryItems.AddAsync(tx, item.Id, item);
                await tx.CommitAsync();
                ServiceEventSource.Current.ServiceMessage(this, "Created inventory item: {0}", item);
            }

            return true;
        }

        /// <summary>
        /// Tries to add the given quantity to the inventory item with the given ID without going over the maximum quantity allowed for an item.
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="quantity"></param>
        /// <returns>The quantity actually added to the item.</returns>
        public async Task<int> AddStockAsync(InventoryItemId itemId, int quantity)
        {
            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);

            int quantityAdded = 0;

            ServiceEventSource.Current.ServiceMessage(this, "Received add stock request. Item: {0}. Quantity: {1}.", itemId, quantity);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                // Try to get the InventoryItem for the ID in the request.
                ConditionalValue<InventoryItem> item = await inventoryItems.TryGetValueAsync(tx, itemId);

                // We can only update the stock for InventoryItems in the system - we are not adding new items here.
                if (item.HasValue)
                {
                    // Update the stock quantity of the item.
                    // This only updates the copy of the Inventory Item that's in local memory here;
                    // It's not yet saved in the dictionary.
                    quantityAdded = item.Value.AddStock(quantity);

                    // We have to store the item back in the dictionary in order to actually save it.
                    // This will then replicate the updated item for
                    await inventoryItems.SetAsync(tx, item.Value.Id, item.Value);
                }

                // nothing will happen unless we commit the transaction!
                await tx.CommitAsync();

                ServiceEventSource.Current.ServiceMessage(
                    this,
                    "Add stock complete. Item: {0}. Added: {1}. Total: {2}",
                    item.Value.Id,
                    quantityAdded,
                    item.Value.AvailableStock);
            }


            return quantityAdded;
        }

        /// <summary>
        /// Removes the given quantity of stock from an in item in the inventory.
        /// </summary>
        /// <param name="request"></param>
        /// <returns>int: Returns the quantity removed from stock.</returns>
        public async Task<int> RemoveStockAsync(InventoryItemId itemId, int quantity, CustomerOrderActorMessageId amId)
        {
            ServiceEventSource.Current.ServiceMessage(this, "inside remove stock {0}|{1}", amId.GetHashCode(), amId.GetHashCode());

            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);

            IReliableDictionary<CustomerOrderActorMessageId, DateTime> recentRequests =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<CustomerOrderActorMessageId, DateTime>>(ActorMessageDictionaryName);

            IReliableDictionary<CustomerOrderActorMessageId, Tuple<InventoryItemId, int>> requestHistory =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<CustomerOrderActorMessageId, Tuple<InventoryItemId, int>>>(RequestHistoryDictionaryName);

            int removed = 0;

            ServiceEventSource.Current.ServiceMessage(this, "Received remove stock request. Item: {0}. Quantity: {1}.", itemId, quantity);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                //first let's see if this is a duplicate request
                ConditionalValue<DateTime> previousRequest = await recentRequests.TryGetValueAsync(tx, amId);
                if (!previousRequest.HasValue)
                {
                    //first time we've seen the request or it was a dupe from so long ago we have forgotten

                    // Try to get the InventoryItem for the ID in the request.
                    ConditionalValue<InventoryItem> item = await inventoryItems.TryGetValueAsync(tx, itemId);

                    // We can only remove stock for InventoryItems in the system.
                    if (item.HasValue)
                    {
                        // Update the stock quantity of the item.
                        // This only updates the copy of the Inventory Item that's in local memory here;
                        // It's not yet saved in the dictionary.
                        removed = item.Value.RemoveStock(quantity);

                        // We have to store the item back in the dictionary in order to actually save it.
                        // This will then replicate the updated item
                        await inventoryItems.SetAsync(tx, itemId, item.Value);

                        //we also have to make a note that we have returned this result, so that we can protect
                        //ourselves from stale or duplicate requests that come back later
                        await requestHistory.SetAsync(tx, amId, new Tuple<InventoryItemId, int>(itemId, removed));

                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "Removed stock complete. Item: {0}. Removed: {1}. Remaining: {2}",
                            item.Value.Id,
                            removed,
                            item.Value.AvailableStock);
                    }
                }
                else
                {
                    //this is a duplicate request. We need to send back the result we already came up with and hope they get it this time
                    //find the previous result and send it back
                    ConditionalValue<Tuple<InventoryItemId, int>> previousResponse = await requestHistory.TryGetValueAsync(tx, amId);

                    if (previousResponse.HasValue)
                    {
                        removed = previousResponse.Value.Item2;
                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "Retrieved previous response for request {0}, from {1}, for Item {2} and quantity {3}",
                            amId,
                            previousRequest.Value,
                            previousResponse.Value.Item1,
                            previousResponse.Value.Item2);
                    }
                    else
                    {
                        //we've seen the request before but we don't have a record for what we responded, inconsistent state
                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "Inconsistent State: recieved duplicate request {0} but don't have matching response in history",
                            amId);
                        this.Partition.ReportFault(System.Fabric.FaultType.Transient);
                    }


                    //note about duplicate Requests: technically if a duplicate request comes in and we have 
                    //sufficient invintory to return more now that we did previously, we could return more of the order and decrement 
                    //the difference to reduce the total number of round trips. This optimization is not currently implemented
                }


                //always update the datetime for the given request
                await recentRequests.SetAsync(tx, amId, DateTime.UtcNow);

                // nothing will happen unless we commit the transaction!
                ServiceEventSource.Current.Message("Committing Changes in Inventory Service");
                await tx.CommitAsync();
                ServiceEventSource.Current.Message("Inventory Service Changes Committed");
            }

            ServiceEventSource.Current.Message("Removed {0} of item {1}", removed, itemId);
            return removed;
        }

        public async Task<bool> IsItemInInventoryAsync(InventoryItemId itemId, CancellationToken ct)
        {
            ServiceEventSource.Current.Message("checking item {0} to see if it is in inventory", itemId);
            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);

            await this.PrintInventoryItemsAsync(inventoryItems, ct);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ConditionalValue<InventoryItem> item = await inventoryItems.TryGetValueAsync(tx, itemId);
                return item.HasValue;
            }
        }

        /// <summary>
        /// Retrieves a customer-specific view (defined in the InventoryItemView class in the Fabrikam Common namespace)
        /// af all items in the IReliableDictionary in InventoryService. Only items with a CustomerAvailableStock greater than
        /// zero are returned as a business logic constraint to reduce overordering. 
        /// </summary>
        /// <returns>IEnumerable of InventoryItemView</returns>
        public async Task<IEnumerable<InventoryItemView>> GetCustomerInventoryAsync(CancellationToken ct)
        {
            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);

            ServiceEventSource.Current.Message("Called GetCustomerInventory to return InventoryItemView");

            await this.PrintInventoryItemsAsync(inventoryItems, ct);

            IList<InventoryItemView> results = new List<InventoryItemView>();

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ServiceEventSource.Current.Message("Generating item views for {0} items", await inventoryItems.GetCountAsync(tx));

                var enumerator = (await inventoryItems.CreateEnumerableAsync(tx)).GetAsyncEnumerator();

                while (await enumerator.MoveNextAsync(ct))
                {
                    if (enumerator.Current.Value.AvailableStock > 0)
                    {
                        results.Add(enumerator.Current.Value);
                    }
                }
            }


            return results;
        }

        /// <summary>
        /// NOTE: This should not be used in published MVP code. 
        /// This function allows us to remove inventory items from inventory.
        /// </summary>
        /// <param name="itemId"></param>
        /// <returns></returns>
        public async Task DeleteInventoryItemAsync(InventoryItemId itemId)
        {
            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                await inventoryItems.TryRemoveAsync(tx, itemId);
                await tx.CommitAsync();
            }
        }
        /// <summary>
        /// Creates a new communication listener
        /// </summary>
        /// <returns></returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[]
            {
                new ServiceReplicaListener(context => this.CreateServiceRemotingListener(context))
            };
        }

        //Dataloss testing can be triggered via powershell. To do so, run the following commands as a script
        //Connect-ServiceFabricCluster
        //$s = "fabric:/WebReferenceApplication/InventoryService"
        //$p = Get-ServiceFabricApplication | Get-ServiceFabricService -ServiceName $s | Get-ServiceFabricPartition | Select -First 1
        //$p | Invoke-ServiceFabricPartitionDataLoss -DataLossMode FullDataLoss -ServiceName $s

        protected override async Task<bool> OnDataLossAsync(RestoreContext restoreCtx, CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.ServiceMessage(this, "OnDataLoss Invoked!");

            try
            {
                string backupFolder;

                if (this.backupStorageType == BackupManagerType.None)
                {
                    //since we have no backup configured, we return false to indicate
                    //that state has not changed. This replica will become the basis
                    //for future replica builds
                    return false;
                }
                else
                {
                    backupFolder = await this.backupManager.RestoreLatestBackupToTempLocation(cancellationToken);
                }

                ServiceEventSource.Current.ServiceMessage(this, "Restoration Folder Path " + backupFolder);

                RestoreDescription restoreRescription = new RestoreDescription(backupFolder, RestorePolicy.Force);

                await restoreCtx.RestoreAsync(restoreRescription, cancellationToken);

                ServiceEventSource.Current.ServiceMessage(this, "Restore completed");

                DirectoryInfo tempRestoreDirectory = new DirectoryInfo(backupFolder);
                tempRestoreDirectory.Delete(true);

                return true;
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Restoration failed: " + "{0} {1}" + e.GetType() + e.Message);

                throw;
            }
        }


        protected override Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                ServiceEventSource.Current.ServiceMessage(this, "inside RunAsync for Inventory Service");

                return Task.WhenAll(
                    this.PeriodicInventoryCheck(cancellationToken),
                    this.PeriodicOldMessageTrimming(cancellationToken),
                    this.PeriodicTakeBackupAsync(cancellationToken));
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this, "RunAsync Failed, {0}", e);
                throw;
            }
        }


        private async Task<bool> BackupCallbackAsync(BackupInfo backupInfo, CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.ServiceMessage(this, "Inside backup callback for replica {0}|{1}", this.Context.PartitionId, this.Context.ReplicaId);
            long totalBackupCount;

            IReliableDictionary<string, long> backupCountDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, long>>(BackupCountDictionaryName);
            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                var value = await backupCountDictionary.TryGetValueAsync(tx, "backupCount");

                if (!value.HasValue)
                {
                    totalBackupCount = 0;
                }
                else
                {
                    totalBackupCount = value.Value;
                }

                await backupCountDictionary.SetAsync(tx, "backupCount", ++totalBackupCount);

                await tx.CommitAsync();
            }

            ServiceEventSource.Current.Message("Backup count dictionary updated, total backup count is {0}", totalBackupCount);

            try
            {
                ServiceEventSource.Current.ServiceMessage(this, "Archiving backup");
                await this.backupManager.ArchiveBackupAsync(backupInfo, cancellationToken);
                ServiceEventSource.Current.ServiceMessage(this, "Backup archived");
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Archive of backup failed: Source: {0} Exception: {1}", backupInfo.Directory, e.Message);
            }

            await this.backupManager.DeleteBackupsAsync(cancellationToken);

            ServiceEventSource.Current.Message("Backups deleted");

            return true;
        }

        private async Task PrintInventoryItemsAsync(IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems, CancellationToken cancellationToken)
        {
            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ServiceEventSource.Current.Message("Printing Inventory for {0} items:", await inventoryItems.GetCountAsync(tx));

                var enumerator = (await inventoryItems.CreateEnumerableAsync(tx)).GetAsyncEnumerator();

                while (await enumerator.MoveNextAsync(cancellationToken))
                {
                    ServiceEventSource.Current.Message("ID:{0}|Item:{1}", enumerator.Current.Key, enumerator.Current.Value);
                }
            }
        }

        private async Task PeriodicOldMessageTrimming(CancellationToken cancellationToken)
        {
            IReliableDictionary<CustomerOrderActorMessageId, DateTime> recentRequests =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<CustomerOrderActorMessageId, DateTime>>(ActorMessageDictionaryName);

            IReliableDictionary<CustomerOrderActorMessageId, Tuple<InventoryItemId, int>> requestHistory =
                await
                    this.StateManager.GetOrAddAsync<IReliableDictionary<CustomerOrderActorMessageId, Tuple<InventoryItemId, int>>>(RequestHistoryDictionaryName);

            while (!cancellationToken.IsCancellationRequested)
            {
                using (ITransaction tx = this.StateManager.CreateTransaction())
                {
                    var enumerator = (await recentRequests.CreateEnumerableAsync(tx)).GetAsyncEnumerator();

                    while (await enumerator.MoveNextAsync(cancellationToken))
                    {
                        //if we have a record of a message that is older than 2 hours from current time, then remove that record
                        //from both of the stale message tracking dictionaries.
                        if (enumerator.Current.Value < (DateTime.UtcNow.AddHours(-2)))
                        {
                            await recentRequests.TryRemoveAsync(tx, enumerator.Current.Key);
                            await requestHistory.TryRemoveAsync(tx, enumerator.Current.Key);
                        }
                    }

                    await tx.CommitAsync();
                }

                //sleep for 5 minutes then scan again
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
        }

        private async Task PeriodicInventoryCheck(CancellationToken cancellationToken)
        {
            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IList<InventoryItem> items = new List<InventoryItem>();

                using (ITransaction tx = this.StateManager.CreateTransaction())
                {
                    ServiceEventSource.Current.ServiceMessage(this, "Checking inventory stock for {0} items.", await inventoryItems.GetCountAsync(tx));

                    var enumerator = (await inventoryItems.CreateEnumerableAsync(tx)).GetAsyncEnumerator();

                    while (await enumerator.MoveNextAsync(cancellationToken))
                    {
                        var item = enumerator.Current.Value;

                        //Check if stock is below restockThreshold and if the item is not already on reorder
                        if ((item.AvailableStock <= item.RestockThreshold) && !item.OnReorder)
                        {
                            items.Add(enumerator.Current.Value);
                        }
                    }
                }

                foreach (InventoryItem item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {

                        ServiceUriBuilder builder = new ServiceUriBuilder(RestockRequestManagerServiceName);

                        IRestockRequestManager restockRequestManagerClient = ServiceProxy.Create<IRestockRequestManager>(builder.ToUri(), new ServicePartitionKey());

                        // we reduce the quantity passed in to RestockRequest to ensure we don't overorder   
                        RestockRequest newRequest = new RestockRequest(item.Id, (item.MaxStockThreshold - item.AvailableStock));

                        InventoryItem updatedItem = new InventoryItem(
                            item.Description,
                            item.Price,
                            item.AvailableStock,
                            item.RestockThreshold,
                            item.MaxStockThreshold,
                            item.Id,
                            true);

                        // TODO: this call needs to be idempotent in case we fail to update the InventoryItem after this completes.
                        await restockRequestManagerClient.AddRestockRequestAsync(newRequest);

                        // Write operations take an exclusive lock on an item, which means we can't do anything else with that item while the transaction is open.
                        // If something blocks before the transaction is committed, the open transaction on the item will prevent all operations on it, including reads.
                        // Once the transaction commits, the lock is released and other operations on the item can proceed.
                        // Operations on the transaction all have timeouts to prevent deadlocking an item, 
                        // but we should do as little work inside the transaction as possible that is not related to the transaction itself.
                        using (ITransaction tx = this.StateManager.CreateTransaction())
                        {
                            await inventoryItems.TryUpdateAsync(tx, item.Id, updatedItem, item);

                            await tx.CommitAsync();
                        }

                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "Restock order placed. Item ID: {0}. Quantity: {1}",
                            newRequest.ItemId,
                            newRequest.Quantity);

                    }
                    catch (Exception e)
                    {
                        ServiceEventSource.Current.ServiceMessage(this, "Failed to place restock order for item {0}. {1}", item.Id, e.ToString());
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }

        private async Task PeriodicTakeBackupAsync(CancellationToken cancellationToken)
        {
            long backupsTaken = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (this.backupStorageType == BackupManagerType.None)
                {
                    break;
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(this.backupManager.backupFrequencyInSeconds));


                    BackupDescription backupDescription = new BackupDescription(BackupOption.Full, this.BackupCallbackAsync);

                    await this.BackupAsync(backupDescription, TimeSpan.FromHours(1), cancellationToken);

                    backupsTaken++;

                    ServiceEventSource.Current.ServiceMessage(this, "Backup {0} taken", backupsTaken);
                }
            }
        }

        private enum BackupManagerType
        {
            Azure,
            Local,
            None
        };
    }
}