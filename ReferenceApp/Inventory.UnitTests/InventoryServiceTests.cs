// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Inventory.UnitTests
{
    using Common;
    using Inventory.Domain;
    using Inventory.Service;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Mocks;
    using System;
    using System.Fabric;
    using System.Threading;
    using System.Threading.Tasks;

    [TestClass]
    public class InventoryServiceTests
    {

        private static ICodePackageActivationContext codePackageContext = new MockCodePackageActivationContext(
            "fabric:/someapp",
            "SomeAppType",
            "Code",
            "1.0.0.0",
            Guid.NewGuid().ToString(),
            @"C:\Log",
            @"C:\Temp",
            @"C:\Work",
            "ServiceManifest",
            "1.0.0.0"
            );


        StatefulServiceContext statefulServiceContext = new StatefulServiceContext(
            new NodeContext("Node0", new NodeId(0, 1), 0, "NodeType1", "TEST.MACHINE"),
            codePackageContext,
            InventoryService.InventoryServiceType,
            new Uri("fabric:/someapp/someservice"),
            null,
            Guid.NewGuid(),
            long.MaxValue
            );



        [TestMethod]
        public async Task TestCreateAndIsItemInInventoryAsync()
        {
            MockReliableStateManager stateManager = new MockReliableStateManager();

            InventoryService target = new InventoryService(statefulServiceContext, stateManager);

            InventoryItem expected = new InventoryItem("test", 1, 10, 1, 10);

            await target.CreateInventoryItemAsync(expected);
            bool resultTrue = await target.IsItemInInventoryAsync(expected.Id, CancellationToken.None);
            bool resultFalse = await target.IsItemInInventoryAsync(new InventoryItemId(), CancellationToken.None);

            Assert.IsTrue(resultTrue);
            Assert.IsFalse(resultFalse);
        }

        [TestMethod]
        public async Task TestAddStock()
        {
            int expectedQuantity = 10;
            int quantityToAdd = 3;
            MockReliableStateManager stateManager = new MockReliableStateManager();
            InventoryService target = new InventoryService(statefulServiceContext, stateManager);

            InventoryItem item = new InventoryItem("test", 1, expectedQuantity - quantityToAdd, 1, expectedQuantity);

            RestockRequest.Domain.RestockRequest request = new RestockRequest.Domain.RestockRequest(item.Id, quantityToAdd);

            await target.CreateInventoryItemAsync(item);
            int actualAdded = await target.AddStockAsync(request.ItemId, quantityToAdd);

            Assert.AreEqual(quantityToAdd, actualAdded);
            Assert.AreEqual(item.AvailableStock, expectedQuantity);
        }

        [TestMethod]
        public async Task TestRemoveStock()
        {
            int expectedQuantity = 5;
            int quantityToRemove = 3;
            MockReliableStateManager stateManager = new MockReliableStateManager();
            InventoryService target = new InventoryService(statefulServiceContext, stateManager);

            InventoryItem item = new InventoryItem("test", 1, expectedQuantity + quantityToRemove, 1, expectedQuantity);

            await target.CreateInventoryItemAsync(item);
            int actualRemoved = await target.RemoveStockAsync(item.Id, quantityToRemove, CustomerOrderActorMessageId.GetRandom());

            Assert.AreEqual(quantityToRemove, actualRemoved);
            Assert.AreEqual(expectedQuantity, item.AvailableStock);
        }

        [TestMethod]
        public async Task TestRemoveStockWithDuplicateRequest()
        {
            int totalStartingStock = 8;
            int expectedQuantity = 5;
            int quantityToRemove = 3;
            MockReliableStateManager stateManager = new MockReliableStateManager();
            InventoryService target = new InventoryService(statefulServiceContext, stateManager);

            InventoryItem item = new InventoryItem("test", 1, totalStartingStock, 1, expectedQuantity);

            await target.CreateInventoryItemAsync(item);

            CustomerOrderActorMessageId cmid = CustomerOrderActorMessageId.GetRandom();

            int actualRemoved = await target.RemoveStockAsync(item.Id, quantityToRemove, cmid);

            Assert.AreEqual(quantityToRemove, actualRemoved);
            Assert.AreEqual(expectedQuantity, item.AvailableStock);

            //save the current availablestock so we can check to be sure it doesn't change
            int priorAvailableStock = item.AvailableStock;

            //but now lets say that the reciever didn't get the response and so sends the exact same request again
            int actualRemoved2 = await target.RemoveStockAsync(item.Id, quantityToRemove, cmid);

            //in this case the response for the amount removed should be the same
            Assert.AreEqual(actualRemoved, actualRemoved2);

            //also, since the request was a duplicate the remaining invintory should be the same as it was before. 
            Assert.AreEqual(item.AvailableStock, priorAvailableStock);
        }
    }
}