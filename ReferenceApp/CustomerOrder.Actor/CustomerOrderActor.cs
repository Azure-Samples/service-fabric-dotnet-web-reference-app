// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace CustomerOrder.Actor
{
    using Common;
    using CustomerOrder.Domain;
    using Inventory.Domain;
    using Microsoft.ServiceFabric.Actors;
    using Microsoft.ServiceFabric.Actors.Runtime;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using Mocks;
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    internal class CustomerOrderActor : MockableActor, ICustomerOrderActor, IRemindable
    {
        private const string InventoryServiceName = "InventoryService";
        private const string OrderItemListPropertyName = "OrderList";
        private const string OrderStatusPropertyName = "CustomerOrderStatus";
        private const string RequestIdPropertyName = "RequestId";
        private IServiceProxyFactory ServiceProxyFactory;
        private ServiceUriBuilder builder;
        private CancellationTokenSource tokenSource = null;


        /// <summary>
        /// This method accepts a list of CustomerOrderItems, representing a customer order, and sets the actor's state
        /// to reflect the status and contents of the order. Then, the order is fulfilled with a private FulfillOrder call
        /// that abstracts away the entire backorder process from the user. 
        /// </summary>
        /// <param name="orderList"></param>
        /// <returns></returns>
        public async Task SubmitOrderAsync(IEnumerable<CustomerOrderItem> orderList)
        {
            try
            {
                await this.StateManager.SetStateAsync<List<CustomerOrderItem>>(OrderItemListPropertyName, new List<CustomerOrderItem>(orderList));
                await this.StateManager.SetStateAsync<CustomerOrderStatus>(OrderStatusPropertyName, CustomerOrderStatus.Submitted);

                await this.RegisterReminderAsync(
                    CustomerOrderReminderNames.FulfillOrderReminder,
                    null,
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(10));
            }
            catch (Exception e)
            {
                ActorEventSource.Current.Message(e.ToString());
            }

            ActorEventSource.Current.Message("Order submitted with {0} items", orderList.Count());

            return;
        }

        /// <summary>
        /// Returns the status of the Customer Order. 
        /// </summary>
        /// <returns></returns>
        public async Task<string> GetOrderStatusAsStringAsync()
        {
            return (await this.GetOrderStatusAsync()).ToString();
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] context, TimeSpan dueTime, TimeSpan period)
        {
            switch (reminderName)
            {
                case CustomerOrderReminderNames.FulfillOrderReminder:

                    await this.FulfillOrderAsync();

                    CustomerOrderStatus orderStatus = await this.GetOrderStatusAsync();

                    if (orderStatus == CustomerOrderStatus.Shipped || orderStatus == CustomerOrderStatus.Canceled)
                    {
                        //Remove fulfill order reminder so Actor can be gargabe collected.
                        IActorReminder orderReminder = this.GetReminder(CustomerOrderReminderNames.FulfillOrderReminder);
                        await this.UnregisterReminderAsync(orderReminder);
                    }

                    break;

                default:
                    // We should never arrive here normally. The system won't call reminders that don't exist. 
                    // But for our own sake in case we add a new reminder somewhere and forget to handle it, this will remind us.
                    throw new InvalidOperationException("Unknown reminder: " + reminderName);
            }
        }

        /// <summary>
        /// Initializes CustomerOrderActor state. Because an order actor will only be activated
        /// once in this scenario and never used again, when we initiate the actor's state we
        /// change the order's status to "Confirmed," and do not need to check if the actor's 
        /// state was already set to this. 
        /// </summary>
        /// 
        protected override async Task OnActivateAsync()
        {
            await InternalActivateAsync(this.ActorService.Context.CodePackageActivationContext, new ServiceProxyFactory());

            CustomerOrderStatus orderStatusResult = await this.GetOrderStatusAsync();

            if (orderStatusResult == CustomerOrderStatus.Unknown)
            {
                await this.StateManager.SetStateAsync<List<CustomerOrderItem>>(OrderItemListPropertyName, new List<CustomerOrderItem>());
                await this.StateManager.SetStateAsync<long>(RequestIdPropertyName, 0);
                await this.SetOrderStatusAsync(CustomerOrderStatus.New);
            }

            return;
        }

        /// <summary>
        /// Adding this method to support DI/Testing 
        /// We need to do some work to create the actor object and make sure it is constructed completely
        /// In local testing we can inject the components we need, but in a real cluster
        /// those items are not established until the actor object is activated. Thus we need to 
        /// have this method so that the tests can have the same init path as the actor would in prod
        /// </summary>
        /// <returns></returns>
        public async Task InternalActivateAsync(ICodePackageActivationContext context, IServiceProxyFactory proxyFactory)
        {
            this.tokenSource = new CancellationTokenSource();
            this.builder = new ServiceUriBuilder(context, InventoryServiceName);
            this.ServiceProxyFactory = proxyFactory;
        }

        /// <summary>
        /// Deactivates the actor object
        /// </summary>
        /// <returns></returns>
        protected override Task OnDeactivateAsync()
        {
            this.tokenSource.Cancel();
            this.tokenSource.Dispose();
            return Task.FromResult(true);
        }

        /// <summary>
        /// This method takes in a list of CustomerOrderItem objects. Using a Service Proxy to access the Inventory Service,
        /// the method iterates onces through the order and tries to remove the quantity specified in the order from inventory. 
        /// If the inventory has insufficient stock to remove the requested amount for a particular item, the entire order is 
        /// marked as backordered and the item in question is added to a "backordered" item list, which is fulfilled in a separate 
        /// method. 
        /// 
        /// In its current form, this application addresses the question of race conditions to remove the same item by making a rule
        /// that no order ever fails. While an item that is displayed in the store may not be available any longer by the time an order is placed,
        /// the automatic restock policy instituted in the Inventory Service means that our FulfillOrder method and its sub-methods can continue to 
        /// query the Inventory Service on repeat (with a timer in between each cycle) until the order is fulfilled. 
        /// 
        /// </summary>
        /// <returns>The number of items put on backorder after fulfilling the order.</returns>
        internal async Task FulfillOrderAsync()
        {

            await this.SetOrderStatusAsync(CustomerOrderStatus.InProcess);

            IList<CustomerOrderItem> orderedItems = await this.StateManager.GetStateAsync<IList<CustomerOrderItem>>(OrderItemListPropertyName);

            ActorEventSource.Current.ActorMessage(this, "Fullfilling customer order. ID: {0}. Items: {1}", this.Id.GetGuidId(), orderedItems.Count);

            foreach (CustomerOrderItem tempitem in orderedItems)
            {
                ActorEventSource.Current.Message("OrderContains:{0}", tempitem);
            }

            //We loop through the customer order list. 
            //For every item that cannot be fulfilled, we add to backordered. 
            foreach (CustomerOrderItem item in orderedItems.Where(x => x.FulfillmentRemaining > 0))
            {
                IInventoryService inventoryService = this.ServiceProxyFactory.CreateServiceProxy<IInventoryService>(this.builder.ToUri(), item.ItemId.GetPartitionKey());

                //First, check the item is listed in inventory.  
                //This will avoid infinite backorder status.
                if ((await inventoryService.IsItemInInventoryAsync(item.ItemId, this.tokenSource.Token)) == false)
                {
                    await this.SetOrderStatusAsync(CustomerOrderStatus.Canceled);
                    return;
                }

                int numberItemsRemoved =
                    await
                        inventoryService.RemoveStockAsync(
                            item.ItemId,
                            item.Quantity,
                            new CustomerOrderActorMessageId(
                                new ActorId(this.Id.GetGuidId()),
                                await this.StateManager.GetStateAsync<long>(RequestIdPropertyName)));

                item.FulfillmentRemaining -= numberItemsRemoved;
            }

            IList<CustomerOrderItem> items = await this.StateManager.GetStateAsync<IList<CustomerOrderItem>>(OrderItemListPropertyName);
            bool backordered = false;

            // Set the status appropriately
            foreach (CustomerOrderItem item in items)
            {
                if (item.FulfillmentRemaining > 0)
                {
                    backordered = true;
                    break;
                }
            }

            if (backordered)
            {
                await this.SetOrderStatusAsync(CustomerOrderStatus.Backordered);
            }
            else
            {
                await this.SetOrderStatusAsync(CustomerOrderStatus.Shipped);
            }

            ActorEventSource.Current.ActorMessage(
                this,
                "{0}; Fulfilled: {1}. Backordered: {2}",
                await this.GetOrderStatusAsStringAsync(),
                items.Count(x => x.FulfillmentRemaining == 0),
                items.Count(x => x.FulfillmentRemaining > 0));

            long messageRequestId = await this.StateManager.GetStateAsync<long>(RequestIdPropertyName);
            await this.StateManager.SetStateAsync<long>(RequestIdPropertyName, ++messageRequestId);
        }

        private async Task<CustomerOrderStatus> GetOrderStatusAsync()
        {
            ConditionalValue<CustomerOrderStatus> orderStatusResult = await this.StateManager.TryGetStateAsync<CustomerOrderStatus>(OrderStatusPropertyName);
            if (orderStatusResult.HasValue)
            {
                return orderStatusResult.Value;
            }
            else
            {
                return CustomerOrderStatus.Unknown;
            }
        }

        private async Task SetOrderStatusAsync(CustomerOrderStatus orderStatus)
        {
            await this.StateManager.SetStateAsync<CustomerOrderStatus>(OrderStatusPropertyName, orderStatus);
        }
    }
}