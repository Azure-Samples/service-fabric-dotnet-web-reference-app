// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Mocks
{
    using Microsoft.ServiceFabric.Data;
    using System;
    using System.Threading.Tasks;

    public class MockTransaction : ITransaction
    {
        public Task<long> CommitAsync()
        {
            return Task.FromResult(0L);
        }

        public void Abort()
        {
        }

        public long TransactionId
        {
            get { return 0L; }
        }

        public long CommitSequenceNumber
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public void Dispose()
        {
        }

        public Task<long> GetVisibilitySequenceNumberAsync()
        {
            return Task.FromResult(0L);
        }
    }
}