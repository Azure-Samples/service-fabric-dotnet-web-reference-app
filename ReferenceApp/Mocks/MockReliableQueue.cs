// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Mocks
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;

    public class MockReliableQueue<T> : IReliableQueue<T>
    {
        private ConcurrentQueue<T> queue = new ConcurrentQueue<T>();

        public Task EnqueueAsync(ITransaction tx, T item, TimeSpan timeout, CancellationToken cancellationToken)
        {
            this.queue.Enqueue(item);

            return Task.FromResult(true);
        }

        public Task EnqueueAsync(ITransaction tx, T item)
        {
            this.queue.Enqueue(item);

            return Task.FromResult(true);
        }

        public Task<ConditionalResult<T>> TryDequeueAsync(ITransaction tx, TimeSpan timeout, CancellationToken cancellationToken)
        {
            T item;
            bool result = this.queue.TryDequeue(out item);

            return Task.FromResult(ConditionalResultActivator.Create<T>(result, item));

            //return Task.FromResult(new ConditionalResult<T>(result, item));
        }

        public Task<ConditionalResult<T>> TryDequeueAsync(ITransaction tx)
        {
            T item;
            bool result = this.queue.TryDequeue(out item);

            return Task.FromResult(ConditionalResultActivator.Create<T>(result, item));

            //return Task.FromResult(new ConditionalResult<T>(result, item));
        }

        public Task<ConditionalResult<T>> TryPeekAsync(ITransaction tx, LockMode lockMode, TimeSpan timeout, CancellationToken cancellationToken)
        {
            T item;
            bool result = this.queue.TryPeek(out item);

            return Task.FromResult(ConditionalResultActivator.Create<T>(result, item));

            //return Task.FromResult(new ConditionalResult<T>(result, item));
        }

        public Task<ConditionalResult<T>> TryPeekAsync(ITransaction tx, LockMode lockMode)
        {
            T item;
            bool result = this.queue.TryPeek(out item);

            return Task.FromResult(ConditionalResultActivator.Create<T>(result, item));

            //return Task.FromResult(new ConditionalResult<T>(result, item));
        }

        public Task<ConditionalResult<T>> TryPeekAsync(ITransaction tx, TimeSpan timeout, CancellationToken cancellationToken)
        {
            T item;
            bool result = this.queue.TryPeek(out item);

            return Task.FromResult(ConditionalResultActivator.Create<T>(result, item));

            //return Task.FromResult(new ConditionalResult<T>(result, item));
        }

        public Task<ConditionalResult<T>> TryPeekAsync(ITransaction tx)
        {
            T item;
            bool result = this.queue.TryPeek(out item);

            return Task.FromResult(ConditionalResultActivator.Create<T>(result, item));

            //return Task.FromResult(new ConditionalResult<T>(result, item));
        }

        public Task ClearAsync()
        {
            while (!this.queue.IsEmpty)
            {
                T result;
                this.queue.TryDequeue(out result);
            }

            return Task.FromResult(true);
        }

        public Task<long> GetCountAsync()
        {
            return Task.FromResult((long) this.queue.Count);
        }

        public Uri Name { get; set; }

        public IEnumerator<T> GetEnumerator()
        {
            return this.queue.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.queue.GetEnumerator();
        }
    }
}