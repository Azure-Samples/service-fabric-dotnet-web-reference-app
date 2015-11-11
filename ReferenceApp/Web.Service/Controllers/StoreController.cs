// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Web.Service.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Fabric.Query;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Common;
    using Inventory.Domain;
    using Microsoft.ServiceFabric.Services.Remoting.Client;

    public class StoreController : ApiController
    {
        public const string InventoryServiceName = "InventoryService";
        private static FabricClient fc = new FabricClient();

        /// <summary>
        /// Right now, this method makes an API call via a ServiceProxy to retrieve Inventory Data directly
        /// from InventoryService. In the future, this call will be made with a specified category parameter, 
        /// and based on this could call a specific materialized view to return. There would be no option 
        /// to return the entire inventory service in one call, as this would be slow and expensive at scale.  
        /// </summary>
        /// <returns>Task of type IEnumerable of InventoryItemView objects</returns>
        [HttpGet]
        [Route("api/store")]
        public async Task<IEnumerable<InventoryItemView>> GetStore()
        {
            ServiceUriBuilder builder = new ServiceUriBuilder(InventoryServiceName);
            Uri serviceName = builder.ToUri();

            List<InventoryItemView> itemList = new List<InventoryItemView>();

            ServicePartitionList partitions = await fc.QueryManager.GetPartitionListAsync(serviceName);

            foreach(Partition p in partitions)
            {
                long minKey = (p.PartitionInformation as Int64RangePartitionInformation).LowKey;
                IInventoryService inventoryServiceClient = ServiceProxy.Create<IInventoryService>(minKey, serviceName);
                itemList.AddRange(await inventoryServiceClient.GetCustomerInventoryAsync());
            }

            return itemList;
        }
    }
}