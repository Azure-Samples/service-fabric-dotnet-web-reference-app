using Microsoft.ServiceFabric.Data;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Service
{
    internal class InventoryServiceFactory : IStatefulServiceFactory
    {
        public IStatefulServiceReplica CreateReplica(string serviceTypeName, Uri serviceName, byte[] initializationData, Guid partitionId, long replicaId)
        {
            StatefulServiceParameters parameters = new StatefulServiceParameters(
                FabricRuntime.GetActivationContext(),
                initializationData,
                partitionId,
                serviceName,
                serviceTypeName,
                replicaId);

            IReliableStateManager stateManager = null;

            return new InventoryService(stateManager, parameters);
        }
    }
}
