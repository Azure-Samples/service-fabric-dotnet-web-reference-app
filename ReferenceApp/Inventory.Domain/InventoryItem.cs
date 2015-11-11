// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Inventory.Domain
{
    using System;

    [Serializable]
    public sealed class InventoryItem
    {
        public InventoryItem(
            string description, decimal price, int availableStock, int restockThreshold, int maxStockThreshold, InventoryItemId id = null,
            bool onReorder = false)
        {
            this.Id = id ?? new InventoryItemId();
            this.Description = description;
            this.Price = price;
            this.AvailableStock = availableStock;
            this.RestockThreshold = restockThreshold;
            this.MaxStockThreshold = maxStockThreshold;
            this.OnReorder = onReorder;
        }

        /// <summary>
        /// Unique identifier for each item style
        /// </summary>
        public InventoryItemId Id { get; }

        /// <summary>
        /// Quantity in stock
        /// </summary>
        public int AvailableStock { get; private set; }

        /// <summary>
        /// Price
        /// </summary>
        public decimal Price { get; }

        /// <summary>
        /// Brief description of product for display on website
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Available stock at which we should reorder
        /// </summary>
        public int RestockThreshold { get; }

        /// <summary>
        /// Maximum number of units that can be in-stock at any time (due to physicial/logistical constraints in warehouses)
        /// </summary>
        public int MaxStockThreshold { get; }

        /// <summary>
        /// True if item is on reorder
        /// </summary>
        public bool OnReorder { get; set; }

        /// <summary>
        /// Returns an InventoryItemView object, which contains only external, customer-facing data about an item in inventory.
        /// </summary>
        /// <param name="item"></param>
        public static implicit operator InventoryItemView(InventoryItem item)
        {
            return new InventoryItemView
            {
                Id = item.Id,
                Price = item.Price,
                Description = item.Description,
                CustomerAvailableStock = item.AvailableStock - item.RestockThreshold //Business logic: constraint to reduce overordering.
            };
        }

        public override string ToString()
        {
            return string.Format(
                "Item {0}: {1} at a price of {2} with {3} available items at a restock threshold of {4} and with max stocking threshold of {5}.",
                this.Id,
                this.Description,
                this.Price,
                this.AvailableStock.ToString(),
                this.RestockThreshold.ToString(),
                this.MaxStockThreshold.ToString());
        }

        /// <summary>
        /// Increments the quantity of a particular item in inventory.
        /// <param name="quantity"></param>
        /// <returns>int: Returns the quantity that has been added to stock</returns>
        /// </summary>
        public int AddStock(int quantity)
        {
            int original = this.AvailableStock;

            // The quantity that the client is trying to add to stock is greater than what can be physically accommodated in a Fabrikam Warehouse
            if ((this.AvailableStock + quantity) > this.MaxStockThreshold)
            {
                // For now, this method only adds new units up maximum stock threshold. In an expanded version of this application, we
                //could include tracking for the remaining units and store information about overstock elsewhere. 
                this.AvailableStock += (this.MaxStockThreshold - this.AvailableStock);
            }
            else
            {
                this.AvailableStock += quantity;
            }

            this.OnReorder = false;

            return this.AvailableStock - original;
        }

        /// <summary>
        /// Decrements the quantity of a particular item in inventory and ensures the restockThreshold hasn't
        /// been breached. If so, a RestockRequest is generated in CheckThreshold. 
        /// 
        /// If there is sufficient stock of an item, then the integer returned at the end of this call should be the same as quantityDesired. 
        /// In the event that there is not sufficient stock available, the method will remove whatever stock is available and return that quantity to the client.
        /// In this case, it is the responsibility of the client to determine if the amount that is returned is the same as quantityDesired.
        /// It is invalid to pass in a negative number. 
        /// </summary>
        /// <param name="quantityDesired"></param>
        /// <returns>int: Returns the number actually removed from stock. </returns>
        /// 
        public int RemoveStock(int quantityDesired)
        {
            int removed = Math.Min(quantityDesired, this.AvailableStock); //Assumes quantityDesired is a positive integer

            this.AvailableStock -= removed;

            return removed;
        }
    }
}