// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Mocks
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Common;
    using Inventory.Domain;

    public class MockInventoryService : IInventoryService
    {
        public MockInventoryService()
        {
            this.AddStockAsyncFunc = (itemId, quantity) => Task.FromResult(quantity);
            this.RemoveStockAsyncFunc = (itemId, quantity, amId) => Task.FromResult(quantity);
            this.IsItemInInventoryAsyncFunc = (itemId) => Task.FromResult(true);
            this.GetCustomerInventoryAsyncFunc = () => Task.FromResult<IEnumerable<InventoryItemView>>(new List<InventoryItemView>() {new InventoryItemView()});
            this.CreateInventoryItemAsyncFunc = item => Task.FromResult(true);
        }

        public Func<InventoryItemId, int, Task<int>> AddStockAsyncFunc { get; set; }

        public Func<InventoryItem, Task<bool>> CreateInventoryItemAsyncFunc { get; set; }

        public Func<Task<IEnumerable<InventoryItemView>>> GetCustomerInventoryAsyncFunc { get; set; }

        public Func<InventoryItemId, Task<bool>> IsItemInInventoryAsyncFunc { get; set; }

        public Func<InventoryItemId, int, CustomerOrderActorMessageId, Task<int>> RemoveStockAsyncFunc { get; set; }

        public Task<int> AddStockAsync(InventoryItemId itemId, int quantity)
        {
            return this.AddStockAsyncFunc(itemId, quantity);
        }

        public Task<bool> CreateInventoryItemAsync(InventoryItem item)
        {
            return this.CreateInventoryItemAsyncFunc(item);
        }

        public Task<IEnumerable<InventoryItemView>> GetCustomerInventoryAsync()
        {
            return this.GetCustomerInventoryAsyncFunc();
        }

        public Task<bool> IsItemInInventoryAsync(InventoryItemId itemId)
        {
            return this.IsItemInInventoryAsyncFunc(itemId);
        }

        public Task<int> RemoveStockAsync(InventoryItemId itemId, int quantity, CustomerOrderActorMessageId amId)
        {
            return this.RemoveStockAsyncFunc(itemId, quantity, amId);
        }
    }
}