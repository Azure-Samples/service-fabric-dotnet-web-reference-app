﻿// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Inventory.Service
{
    using Microsoft.ServiceFabric.Services.Runtime;
    using System;
    using System.Diagnostics.Tracing;
    using System.Fabric;

    [EventSource(Name = "MyCompany-Web_UIApplication-InventoryService")]
    internal sealed class ServiceEventSource : EventSource
    {
        public static ServiceEventSource Current = new ServiceEventSource();

        [NonEvent]
        public void Message(string message, params object[] args)
        {
            if (this.IsEnabled())
            {
                string finalMessage = string.Format(message, args);
                this.Message(finalMessage);
            }
        }

        [Event(1, Level = EventLevel.Informational, Message = "{0}")]
        public void Message(string message)
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(1, message);
            }
        }

        [NonEvent]
        public void ServiceMessage(StatefulService service, string message, params object[] args)
        {
            if (this.IsEnabled() && service.ServiceInitializationParameters != null)
            {
                string finalMessage = string.Format(message, args);
                this.ServiceMessage(
                    service.ServiceInitializationParameters.ServiceName.ToString(),
                    service.ServiceInitializationParameters.ServiceTypeName,
                    service.ServiceInitializationParameters.ReplicaId,
                    service.ServiceInitializationParameters.PartitionId,
                    service.ServiceInitializationParameters.CodePackageActivationContext.ApplicationName,
                    service.ServiceInitializationParameters.CodePackageActivationContext.ApplicationTypeName,
                    FabricRuntime.GetNodeContext().NodeName,
                    finalMessage);
            }
        }

        [Event(3, Level = EventLevel.Informational, Message = "Service host process {0} registered service type {1}")]
        public void ServiceTypeRegistered(int hostProcessId, string serviceType)
        {
            this.WriteEvent(3, hostProcessId, serviceType);
        }

        [NonEvent]
        public void ServiceHostInitializationFailed(Exception e)
        {
            this.ServiceHostInitializationFailed(e.ToString());
        }

        [Event(2, Level = EventLevel.Informational, Message = "{7}")]
        private void ServiceMessage(
            string serviceName,
            string serviceTypeName,
            long replicaOrInstanceId,
            Guid partitionId,
            string applicationName,
            string applicationTypeName,
            string nodeName,
            string message)
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(2, serviceName, serviceTypeName, replicaOrInstanceId, partitionId, applicationName, applicationTypeName, nodeName, message);
            }
        }

        [Event(4, Level = EventLevel.Error, Message = "Service host initialization failed")]
        private void ServiceHostInitializationFailed(string exception)
        {
            this.WriteEvent(4, exception);
        }
    }
}