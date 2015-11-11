// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Inventory.UnitTests
{
    using System.Threading.Tasks;
    using Common;
    using Inventory.Domain;
    using Inventory.Service;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Mocks;

    [TestClass]
    public class InventoryServiceTests
    {
        [TestMethod]
        public async Task TestCreateAndIsItemInInventoryAsync()
        {
            MockReliableStateManager stateManager = new MockReliableStateManager();
            InventoryService target = new InventoryService(stateManager);

            InventoryItem expected = new InventoryItem("test", 1, 10, 1, 10);

            await target.CreateInventoryItemAsync(expected);
            bool resultTrue = await target.IsItemInInventoryAsync(expected.Id);
            bool resultFalse = await target.IsItemInInventoryAsync(new InventoryItemId());

            Assert.IsTrue(resultTrue);
            Assert.IsFalse(resultFalse);
        }

        [TestMethod]
        public async Task TestAddStock()
        {
            int expectedQuantity = 10;
            int quantityToAdd = 3;
            MockReliableStateManager stateManager = new MockReliableStateManager();
            InventoryService target = new InventoryService(stateManager);

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
            InventoryService target = new InventoryService(stateManager);

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
            InventoryService target = new InventoryService(stateManager);

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