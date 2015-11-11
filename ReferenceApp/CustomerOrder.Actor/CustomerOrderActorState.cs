// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace CustomerOrder.Actor
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;
    using System.Text;
    using CustomerOrder.Domain;

    [DataContract]
    internal sealed class CustomerOrderActorState
    {
        [DataMember]
        public IList<CustomerOrderItem> OrderedItems { get; set; }

        [DataMember]
        public CustomerOrderStatus Status { get; set; }

        [DataMember]
        public long RequestId { get; set; }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Status: " + this.Status + ".");
            if (this.OrderedItems != null)
            {
                sb.Append("Ordered Items: ");
                sb.Append(String.Join(";", this.OrderedItems));
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}