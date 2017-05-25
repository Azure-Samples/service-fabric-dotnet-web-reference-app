﻿// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Web.Service.Controllers
{
    using System;
    using System.Threading.Tasks;
    using Common;
    using Inventory.Domain;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using Microsoft.AspNetCore.Mvc;

    public class InventoryController : Controller
    {
        private const string InventoryServiceName = "InventoryService";


        /// <summary>
        /// Calls and adds a new item to inventory.
        /// </summary>
        /// <param name="customerOrderId"></param>
        ///Description
        ///Price
        ///Number
        ///Reorder Threshold
        ///Max
        /// <returns>String</returns>
        [HttpPost]
        [Route("api/inventory/add/{description}/{price}/{number}/{reorderThreshold}/{max}")]
        public Task<bool> CreateInventoryItem(string description, decimal price, int number, int reorderThreshold, int max)
        {
            InventoryItem i = new InventoryItem(description, price, number, reorderThreshold, max);

            ServiceUriBuilder builder = new ServiceUriBuilder(InventoryServiceName);
            IInventoryService inventoryServiceClient = ServiceProxy.Create<IInventoryService>(builder.ToUri(), i.Id.GetPartitionKey());

            try
            {
                return inventoryServiceClient.CreateInventoryItemAsync(i);
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Message("Web Service: Exception creating {0}: {1}", i, ex);
                throw;
            }
        }
    }
}