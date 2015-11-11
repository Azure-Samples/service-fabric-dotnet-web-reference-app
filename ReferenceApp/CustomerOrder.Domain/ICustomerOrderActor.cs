// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace CustomerOrder.Domain
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Actors;

    public interface ICustomerOrderActor : IActor
    {
        [Readonly]
        Task<string> GetStatusAsync();

        Task SubmitOrderAsync(IEnumerable<CustomerOrderItem> orderList);
    }
}