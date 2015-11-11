// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace RestockRequest.Domain
{
    using System;
    using System.Runtime.Serialization;
    using Inventory.Domain;

    [DataContract]
    public sealed class RestockRequest
    {
        public RestockRequest(InventoryItemId itemId, int quantity)
        {
            this.ItemId = itemId;
            this.Quantity = quantity;
        }

        [DataMember]
        public InventoryItemId ItemId { get; private set; }

        [DataMember]
        public int Quantity { get; private set; }

        public override string ToString()
        {
            return String.Format("ItemId: {0}, Quantity: {1}", this.ItemId, this.Quantity);
        }
    }
}