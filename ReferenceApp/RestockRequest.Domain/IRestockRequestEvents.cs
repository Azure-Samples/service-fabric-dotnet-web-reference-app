// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace RestockRequest.Domain
{
    using Microsoft.ServiceFabric.Actors;

    public interface IRestockRequestEvents : IActorEvents
    {
        // Notify that the actor idenfitied by actor id has completed the request.
        // The recipient can find the actor id based on the request item id, but we want to avoid another lookup
        void RestockRequestCompleted(ActorId actorId, RestockRequest request);
    }
}