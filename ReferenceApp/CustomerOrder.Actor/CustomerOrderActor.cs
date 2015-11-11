// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace CustomerOrder.Actor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Common;
    using Common.Wrappers;
    using CustomerOrder.Domain;
    using Inventory.Domain;
    using Microsoft.ServiceFabric.Actors;

    internal class CustomerOrderActor : StatefulActor<CustomerOrderActorState>, ICustomerOrderActor, IRemindable
    {
        private const string InventoryServiceName = "InventoryService";

        /// <summary>
        /// TODO: Temporary property-injection for an IServiceProxyWrapper until constructor injection is available.
        /// </summary>
        public IServiceProxyWrapper ServiceProxy { private get; set; }

        /// <summary>
        /// This method accepts a list of CustomerOrderItems, representing a customer order, and sets the actor's state
        /// to reflect the status and contents of the order. Then, the order is fulfilled with a private FulfillOrder call
        /// that abstracts away the entire backorder process from the user. 
        /// </summary>
        /// <param name="orderList"></param>
        /// <returns></returns>
        public Task SubmitOrderAsync(IEnumerable<CustomerOrderItem> orderList)
        {
            this.State.OrderedItems = new List<CustomerOrderItem>(orderList);
            this.State.Status = CustomerOrderStatus.Submitted;

            ActorEventSource.Current.ActorMessage(this, this.State.ToString());

            return this.RegisterReminderAsync(
                CustomerOrderReminderNames.FulfillOrderReminder,
                null,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(10),
                ActorReminderAttributes.None);
        }

        /// <summary>
        /// Returns the status of the Customer Order. 
        /// </summary>
        /// <returns></returns>
        public Task<string> GetStatusAsync()
        {
            return Task.FromResult(this.State.Status.ToString());
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] context, TimeSpan dueTime, TimeSpan period)
        {
            switch (reminderName)
            {
                case CustomerOrderReminderNames.FulfillOrderReminder:

                    await this.FulfillOrderAsync();

                    if (this.State.Status == CustomerOrderStatus.Shipped ||
                        this.State.Status == CustomerOrderStatus.Canceled)
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
        protected override Task OnActivateAsync()
        {
            if (this.State == null)
            {
                this.State = new CustomerOrderActorState();
                this.State.Status = CustomerOrderStatus.New;
            }

            this.ServiceProxy = new ServiceProxyWrapper();

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
            ServiceUriBuilder builder = new ServiceUriBuilder(InventoryServiceName);

            this.State.Status = CustomerOrderStatus.InProcess;

            ActorEventSource.Current.ActorMessage(this, "Fullfilling customer order. ID: {0}. Items: {1}", this.Id.GetGuidId(), this.State.OrderedItems.Count);

            foreach (CustomerOrderItem tempitem in this.State.OrderedItems)
            {
                ActorEventSource.Current.Message("OrderContains:{0}", tempitem);
            }

            //We loop through the customer order list. 
            //For every item that cannot be fulfilled, we add to backordered. 
            foreach (CustomerOrderItem item in this.State.OrderedItems.Where(x => x.FulfillmentRemaining > 0))
            {
                IInventoryService inventoryService = this.ServiceProxy.Create<IInventoryService>(item.ItemId.GetPartitionKey(), builder.ToUri());

                //First, check the item is listed in inventory.  
                //This will avoid infinite backorder status.
                if ((await inventoryService.IsItemInInventoryAsync(item.ItemId)) == false)
                {
                    this.State.Status = CustomerOrderStatus.Canceled;
                    return;
                }

                int numberItemsRemoved =
                    await
                        inventoryService.RemoveStockAsync(
                            item.ItemId,
                            item.Quantity,
                            new CustomerOrderActorMessageId(new ActorId(this.Id.GetGuidId()), this.State.RequestId));

                item.FulfillmentRemaining -= numberItemsRemoved;
            }

            // Set the status appropriately
            this.State.Status = this.State.OrderedItems.Any(x => x.FulfillmentRemaining > 0)
                ? this.State.Status = CustomerOrderStatus.Backordered
                : this.State.Status = CustomerOrderStatus.Shipped;

            ActorEventSource.Current.ActorMessage(
                this,
                "{0}; Fulfilled: {1}. Backordered: {2}",
                this.State,
                this.State.OrderedItems.Count(x => x.FulfillmentRemaining == 0),
                this.State.OrderedItems.Count(x => x.FulfillmentRemaining > 0));

            this.State.RequestId++;
        }
    }
}