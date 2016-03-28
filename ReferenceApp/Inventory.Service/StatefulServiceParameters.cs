// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Inventory.Service
{
    using System;
    using System.Fabric;

    internal class StatefulServiceParameters
    {
        public StatefulServiceParameters(
            CodePackageActivationContext codePackageActivationContext, byte[] initializationData, Guid partitionId, Uri serviceName, string serviceTypeName,
            long replicaId)
        {
            this.CodePackageActivationContext = codePackageActivationContext;
            this.InitializationData = initializationData;
            this.PartitionId = partitionId;
            this.ServiceName = serviceName;
            this.ServiceTypeName = serviceTypeName;
            this.ReplicaId = replicaId;
        }

        public CodePackageActivationContext CodePackageActivationContext { get; }

        public byte[] InitializationData { get; }

        public Guid PartitionId { get; }

        public long ReplicaId { get; }

        public Uri ServiceName { get; }

        public string ServiceTypeName { get; }
    }
}