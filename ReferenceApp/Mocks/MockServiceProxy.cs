// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Mocks
{
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Communication.Client;
    using Microsoft.ServiceFabric.Services.Remoting;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using Microsoft.ServiceFabric.Services.Remoting.V1.Client;
    using Microsoft.ServiceFabric.Services.Remoting.V2.Client;
    using System;
    using System.Collections.Generic;

    public class MockServiceProxy : IServiceProxy
    {
        private IDictionary<Type, Func<Uri, object>> createFunctions = new Dictionary<Type, Func<Uri, object>>();

        public Type ServiceInterfaceType { get; private set; }

        public Microsoft.ServiceFabric.Services.Remoting.V2.Client.IServiceRemotingPartitionClient ServicePartitionClient2 { get; private set; }

        Microsoft.ServiceFabric.Services.Remoting.V1.Client.IServiceRemotingPartitionClient IServiceProxy.ServicePartitionClient { get; }

        public TServiceInterface Create<TServiceInterface>(Uri serviceName) where TServiceInterface : IService
        {
            this.ServiceInterfaceType = typeof(TServiceInterface);
            return (TServiceInterface)this.createFunctions[typeof(TServiceInterface)](serviceName);
        }

        //public TServiceInterface Create<TServiceInterface>(Uri serviceName, ServicePartitionKey key) where TServiceInterface : IService
        //{
        //    return (TServiceInterface)this.createFunctions[typeof(TServiceInterface)](serviceName);
        //}

        public TServiceInterface Create<TServiceInterface>(Uri serviceUri, ServicePartitionKey partitionKey = null, TargetReplicaSelector targetReplicaSelector = TargetReplicaSelector.Default, string listenerName = null) where TServiceInterface : IService
        {
            return (TServiceInterface)this.createFunctions[typeof(TServiceInterface)](serviceUri);
        }

        public void Supports<TServiceInterface>(Func<Uri, object> Create)
        {
            this.createFunctions[typeof(TServiceInterface)] = Create;
        }
    }
}