// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Common.Wrappers
{
    using System;
    using Microsoft.ServiceFabric.Services.Remoting;
    using Microsoft.ServiceFabric.Services.Remoting.Client;

    /// <summary>
    /// Wrapper class for the static ServiceProxy.
    /// </summary>
    public class ServiceProxyWrapper : IServiceProxyWrapper
    {
        public TServiceInterface Create<TServiceInterface>(Uri serviceName) where TServiceInterface : IService
        {
            return ServiceProxy.Create<TServiceInterface>(serviceName);
        }

        public TServiceInterface Create<TServiceInterface>(long partitionKey, Uri serviceName) where TServiceInterface : IService
        {
            return ServiceProxy.Create<TServiceInterface>(partitionKey, serviceName);
        }

        public TServiceInterface Create<TServiceInterface>(string partitionKey, Uri serviceName) where TServiceInterface : IService
        {
            return ServiceProxy.Create<TServiceInterface>(partitionKey, serviceName);
        }
    }
}