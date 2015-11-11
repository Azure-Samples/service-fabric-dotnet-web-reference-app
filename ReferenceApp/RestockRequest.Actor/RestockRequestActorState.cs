// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace RestockRequest.Actor
{
    using System;
    using System.Runtime.Serialization;
    using RestockRequest.Domain;

    [DataContract]
    internal sealed class RestockRequestActorState
    {
        [DataMember]
        public RestockRequest Request { get; set; }

        [DataMember]
        public RestockRequestStatus Status { get; set; }

        public bool IsStarted()
        {
            return this.Status == RestockRequestStatus.Manufacturing ||
                   this.Status == RestockRequestStatus.Accepted;
        }

        public override string ToString()
        {
            if (this.Request == null)
            {
                return String.Format("{0}", this.Status);
            }

            return string.Format("{0}: {1}", this.Request, this.Status);
        }
    }
}