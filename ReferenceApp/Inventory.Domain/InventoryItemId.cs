// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Inventory.Domain
{
    using System;
    using System.Runtime.Serialization;
    using Microsoft.ServiceFabric.Actors;

    [DataContract]
    public class InventoryItemId : IFormattable, IComparable, IComparable<InventoryItemId>, IEquatable<InventoryItemId>
    {
        [DataMember] private Guid id;

        public InventoryItemId()
        {
            this.id = Guid.NewGuid();
        }

        public int CompareTo(object obj)
        {
            return this.id.CompareTo(((InventoryItemId) obj).id);
        }

        public int CompareTo(InventoryItemId other)
        {
            return this.id.CompareTo(other.id);
        }

        public bool Equals(InventoryItemId other)
        {
            return this.id.Equals(other.id);
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return this.id.ToString(format, formatProvider);
        }

        public long GetPartitionKey()
        {
            return (long) CRC64.ToCRC64(this.id.ToByteArray());
        }

        public static bool operator ==(InventoryItemId item1, InventoryItemId item2)
        {
            return item1.Equals(item2);
        }

        public static bool operator !=(InventoryItemId item1, InventoryItemId item2)
        {
            return !item1.Equals(item2);
        }

        public override bool Equals(object obj)
        {
            return (obj is InventoryItemId) ? this.id.Equals(((InventoryItemId) obj).id) : false;
        }

        public override int GetHashCode()
        {
            return this.id.GetHashCode();
        }

        public override string ToString()
        {
            return this.id.ToString();
        }

        public string ToString(string format)
        {
            return this.id.ToString(format);
        }
    }
}