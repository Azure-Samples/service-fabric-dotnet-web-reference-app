// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Mocks
{
    using System;
    using System.Collections.Generic;
    using Common.Wrappers;
    using Microsoft.ServiceFabric.Services.Remoting;

    public class MockServiceProxy : IServiceProxyWrapper
    {
        private IDictionary<Type, Func<Uri, object>> createFunctions = new Dictionary<Type, Func<Uri, object>>();

        public TServiceInterface Create<TServiceInterface>(Uri serviceName) where TServiceInterface : IService
        {
            return (TServiceInterface) this.createFunctions[typeof(TServiceInterface)](serviceName);
        }

        public TServiceInterface Create<TServiceInterface>(long partitionKey, Uri serviceName) where TServiceInterface : IService
        {
            return (TServiceInterface) this.createFunctions[typeof(TServiceInterface)](serviceName);
        }

        public TServiceInterface Create<TServiceInterface>(string partitionKey, Uri serviceName) where TServiceInterface : IService
        {
            return (TServiceInterface) this.createFunctions[typeof(TServiceInterface)](serviceName);
        }

        public void Supports<TServiceInterface>(Func<Uri, object> Create)
        {
            this.createFunctions[typeof(TServiceInterface)] = Create;
        }
    }
}