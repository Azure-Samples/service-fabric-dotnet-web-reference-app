// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace RestockRequestManager.Domain
{
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Services.Remoting;
    using RestockRequest.Domain;

    public interface IRestockRequestManager : IService
    {
        Task AddRestockRequestAsync(RestockRequest request);
    }
}