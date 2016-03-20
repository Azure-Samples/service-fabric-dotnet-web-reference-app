// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Inventory.Domain
{
    using Common;
    using Microsoft.ServiceFabric.Services.Remoting;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IInventoryService : IService
    {
        Task<int> AddStockAsync(InventoryItemId itemId, int quantity);
        Task<int> RemoveStockAsync(InventoryItemId itemId, int quantity, CustomerOrderActorMessageId messageId);
        Task<bool> IsItemInInventoryAsync(InventoryItemId itemId, CancellationToken cancellationToken);
        Task<IEnumerable<InventoryItemView>> GetCustomerInventoryAsync(CancellationToken cancellationToken);
        Task<bool> CreateInventoryItemAsync(InventoryItem item);
    }
}