// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Inventory.Domain
{
    using System.Runtime.Serialization;

    //Guid will always be key to this value pair
    [DataContract]
    public sealed class InventoryItemView
    {
        [DataMember]
        public InventoryItemId Id { get; set; }

        [DataMember]
        public string Description { get; set; }

        [DataMember]
        public decimal Price { get; set; }

        [DataMember]
        public int CustomerAvailableStock { get; set; }
    }
}