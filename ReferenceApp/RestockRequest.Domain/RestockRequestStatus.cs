// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace RestockRequest.Domain
{
    public enum RestockRequestStatus
    {
        NA = 0,
        Accepted,
        Manufacturing,
        Completed,
    }
}