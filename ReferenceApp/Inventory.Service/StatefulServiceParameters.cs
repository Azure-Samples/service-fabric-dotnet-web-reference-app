using System;
using System.Fabric;

namespace Inventory.Service
{
    internal class StatefulServiceParameters
    {
        private CodePackageActivationContext codePackageActivationContext;
        private byte[] initializationData;
        private Guid partitionId;
        private long replicaId;
        private Uri serviceName;
        private string serviceTypeName;

        public StatefulServiceParameters(CodePackageActivationContext codePackageActivationContext, byte[] initializationData, Guid partitionId, Uri serviceName, string serviceTypeName, long replicaId)
        {
            this.codePackageActivationContext = codePackageActivationContext;
            this.initializationData = initializationData;
            this.partitionId = partitionId;
            this.serviceName = serviceName;
            this.serviceTypeName = serviceTypeName;
            this.replicaId = replicaId;
        }

        public CodePackageActivationContext CodePackageActivationContext
        {
            get
            {
                return codePackageActivationContext;
            }
        }

        public byte[] InitializationData
        {
            get
            {
                return initializationData;
            }
        }

        public Guid PartitionId
        {
            get
            {
                return partitionId;
            }
        }

        public long ReplicaId
        {
            get
            {
                return replicaId;
            }
        }

        public Uri ServiceName
        {
            get
            {
                return serviceName;
            }
        }

        public string ServiceTypeName
        {
            get
            {
                return serviceTypeName;
            }
        }
    }
}