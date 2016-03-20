// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Web.Service
{
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;
    using System.Collections.Generic;
    using System.Fabric;

    internal class WebService : StatelessService
    {
        public WebService(StatelessServiceContext serviceContext) : base(serviceContext)
        {
        }

        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new List<ServiceInstanceListener>() {
                new ServiceInstanceListener(
                    (initParams) =>
                       new OwinCommunicationListener("fabrikam", new Startup(), this.Context))
                       };
        }

    }
}