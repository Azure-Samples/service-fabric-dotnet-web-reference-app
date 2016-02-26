// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace RestockRequest.Domain
{
    using Microsoft.ServiceFabric.Actors;
    using System.Threading.Tasks;

    public interface IRestockRequestActor : IActor, IActorEventPublisher<IRestockRequestEvents>
    {
        Task AddRestockRequestAsync(RestockRequest request);
    }
}