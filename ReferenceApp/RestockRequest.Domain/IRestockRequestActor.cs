// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace RestockRequest.Domain
{
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Actors;

    public interface IRestockRequestActor : IActor, IActorEventPublisher<IRestockRequestEvents>
    {
        Task AddRestockRequestAsync(RestockRequest request);
    }
}