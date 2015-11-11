// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace CustomerOrder.Domain
{
    using System;
    using System.Runtime.Serialization;
    using Inventory.Domain;

    [DataContract]
    public sealed class CustomerOrderItem
    {
        public CustomerOrderItem(InventoryItemId itemId, int quantity)
        {
            this.ItemId = itemId;
            this.Quantity = quantity;
            this.FulfillmentRemaining = quantity;
        }

        [DataMember]
        public InventoryItemId ItemId { get; set; }

        [DataMember]
        public int Quantity { get; set; }

        [DataMember]
        public int FulfillmentRemaining { get; set; }

        public override string ToString()
        {
            return String.Format("ID: {0}, Quantity: {1}, Fulfillment Remaing: {2}", this.ItemId, this.Quantity, this.FulfillmentRemaining);
        }
    }
}