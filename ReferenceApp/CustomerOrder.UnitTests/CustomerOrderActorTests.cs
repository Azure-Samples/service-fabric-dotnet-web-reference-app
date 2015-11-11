// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace CustomerOrder.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading.Tasks;
    using CustomerOrder.Actor;
    using CustomerOrder.Domain;
    using Inventory.Domain;
    using Microsoft.ServiceFabric.Actors;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Mocks;

    [TestClass]
    public class CustomerOrderActorTests
    {
        /// <summary>
        /// Tests FulfillOrder ships an order when all items are available from the InventoryService.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestFulfillOrderSimple()
        {
            // The default mock inventory service behavior is to always complete an order.
            MockInventoryService inventoryService = new MockInventoryService();

            MockServiceProxy serviceProxy = new MockServiceProxy();
            serviceProxy.Supports<IInventoryService>(serviceUri => inventoryService);

            CustomerOrderActor target = new CustomerOrderActor();

            PropertyInfo idProperty = typeof(ActorBase).GetProperty("Id");
            idProperty.SetValue(target, new ActorId(Guid.NewGuid()));

            target.ServiceProxy = serviceProxy;
            target.State = new CustomerOrderActorState();

            target.State.Status = CustomerOrderStatus.Submitted;
            target.State.OrderedItems = new List<CustomerOrderItem>()
            {
                new CustomerOrderItem(new InventoryItemId(), 4)
            };

            await target.FulfillOrderAsync();

            Assert.AreEqual<CustomerOrderStatus>(CustomerOrderStatus.Shipped, target.State.Status);
        }

        /// <summary>
        /// Tests FulfillOrder does not ship when not all items could be fulfilled by InventoryService.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestFulfillOrderWithBackorder()
        {
            // instruct the mock inventory service to always return less quantity than requested 
            // so that FulfillOrder always ends up in backordered status.            

            MockInventoryService inventoryService = new MockInventoryService()
            {
                RemoveStockAsyncFunc = (itemId, quantity, cmid) => Task.FromResult(quantity - 1)
            };

            MockServiceProxy serviceProxy = new MockServiceProxy();
            serviceProxy.Supports<IInventoryService>(serviceUri => inventoryService);

            CustomerOrderActor target = new CustomerOrderActor();

            PropertyInfo idProperty = typeof(ActorBase).GetProperty("Id");
            idProperty.SetValue(target, new ActorId(Guid.NewGuid()));

            target.ServiceProxy = serviceProxy;
            target.State = new CustomerOrderActorState();
            target.State.Status = CustomerOrderStatus.Submitted;
            target.State.OrderedItems = new List<CustomerOrderItem>()
            {
                new CustomerOrderItem(new InventoryItemId(), 4)
            };

            await target.FulfillOrderAsync();

            Assert.AreEqual<CustomerOrderStatus>(CustomerOrderStatus.Backordered, target.State.Status);
        }

        /// <summary>
        /// Tests FulfillOrder completes a shipment after multiple iterations when a limited quantity is available from InventoryService.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestFulfillOrderToCompletion()
        {
            int itemCount = 5;

            // instruct the mock inventory service to only fulfill one item each time
            // so that FulfillOrder has to make multiple iterations to complete an order
            MockInventoryService inventoryService = new MockInventoryService()
            {
                RemoveStockAsyncFunc = (itemId, quantity, cmid) => Task.FromResult(1)
            };

            MockServiceProxy serviceProxy = new MockServiceProxy();
            serviceProxy.Supports<IInventoryService>(serviceUri => inventoryService);

            CustomerOrderActor target = new CustomerOrderActor();

            PropertyInfo idProperty = typeof(ActorBase).GetProperty("Id");
            idProperty.SetValue(target, new ActorId(Guid.NewGuid()));

            target.ServiceProxy = serviceProxy;
            target.State = new CustomerOrderActorState();
            target.State.Status = CustomerOrderStatus.Submitted;
            target.State.OrderedItems = new List<CustomerOrderItem>()
            {
                new CustomerOrderItem(new InventoryItemId(), itemCount)
            };

            for (int i = 0; i < itemCount - 1; ++i)
            {
                await target.FulfillOrderAsync();
                Assert.AreEqual<CustomerOrderStatus>(CustomerOrderStatus.Backordered, target.State.Status);
            }

            await target.FulfillOrderAsync();
            Assert.AreEqual<CustomerOrderStatus>(CustomerOrderStatus.Shipped, target.State.Status);
        }

        [TestMethod]
        public async Task TestFulfillOrderCancelled()
        {
            // instruct the mock inventory service to return 0 for all items to simulate items that don't exist.
            // and have it return false when asked if an item exists to make sure FulfillOrder doesn't get into
            // an infinite backorder loop.
            MockInventoryService inventoryService = new MockInventoryService()
            {
                IsItemInInventoryAsyncFunc = itemId => Task.FromResult(false),
                RemoveStockAsyncFunc = (itemId, quantity, cmid) => Task.FromResult(0)
            };

            MockServiceProxy serviceProxy = new MockServiceProxy();
            serviceProxy.Supports<IInventoryService>(serviceUri => inventoryService);

            CustomerOrderActor target = new CustomerOrderActor();

            PropertyInfo idProperty = typeof(ActorBase).GetProperty("Id");
            idProperty.SetValue(target, new ActorId(Guid.NewGuid()));

            target.ServiceProxy = serviceProxy;
            target.State = new CustomerOrderActorState();
            target.State.Status = CustomerOrderStatus.Submitted;
            target.State.OrderedItems = new List<CustomerOrderItem>()
            {
                new CustomerOrderItem(new InventoryItemId(), 5)
            };

            await target.FulfillOrderAsync();

            Assert.AreEqual<CustomerOrderStatus>(CustomerOrderStatus.Canceled, target.State.Status);
        }
    }
}