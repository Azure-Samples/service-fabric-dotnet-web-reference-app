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

    public class MockReliableStateManager : IReliableStateManager
    {
        private ConcurrentDictionary<Uri, IReliableState> store = new ConcurrentDictionary<Uri, IReliableState>();

        private Dictionary<Type, Type> dependencyMap = new Dictionary<Type, Type>()
        {
            {typeof(IReliableDictionary<,>), typeof(MockReliableDictionary<,>)},
            {typeof(IReliableQueue<>), typeof(MockReliableQueue<>)}
        };

        public Task ClearAsync(ITransaction tx)
        {
            this.store.Clear();
            return Task.FromResult(true);
        }

        public Task ClearAsync()
        {
            this.store.Clear();
            return Task.FromResult(true);
        }

        public ITransaction CreateTransaction()
        {
            return new MockTransaction();
        }

        public Task RemoveAsync(string name)
        {
            IReliableState result;
            this.store.TryRemove(this.ToUri(name), out result);

            return Task.FromResult(true);
        }

        public Task RemoveAsync(ITransaction tx, string name)
        {
            IReliableState result;
            this.store.TryRemove(this.ToUri(name), out result);

            return Task.FromResult(true);
        }

        public Task RemoveAsync(string name, TimeSpan timeout)
        {
            IReliableState result;
            this.store.TryRemove(this.ToUri(name), out result);

            return Task.FromResult(true);
        }

        public Task RemoveAsync(ITransaction tx, string name, TimeSpan timeout)
        {
            IReliableState result;
            this.store.TryRemove(this.ToUri(name), out result);

            return Task.FromResult(true);
        }

        public Task RemoveAsync(Uri name)
        {
            IReliableState result;
            this.store.TryRemove(name, out result);

            return Task.FromResult(true);
        }

        public Task RemoveAsync(Uri name, TimeSpan timeout)
        {
            IReliableState result;
            this.store.TryRemove(name, out result);

            return Task.FromResult(true);
        }

        public Task RemoveAsync(ITransaction tx, Uri name)
        {
            IReliableState result;
            this.store.TryRemove(name, out result);

            return Task.FromResult(true);
        }

        public Task RemoveAsync(ITransaction tx, Uri name, TimeSpan timeout)
        {
            IReliableState result;
            this.store.TryRemove(name, out result);

            return Task.FromResult(true);
        }

        public Task<ConditionalResult<T>> TryGetAsync<T>(string name) where T : IReliableState
        {
            IReliableState result;
            bool success = this.store.TryGetValue(this.ToUri(name), out result);

            return Task.FromResult(ConditionalResultActivator.Create<T>(success, (T) result));

            //return Task.FromResult(new ConditionalResult<T>(success, (T) result));
        }

        public Task<ConditionalResult<T>> TryGetAsync<T>(Uri name) where T : IReliableState
        {
            IReliableState result;
            bool success = this.store.TryGetValue(name, out result);

            return Task.FromResult(ConditionalResultActivator.Create<T>(success, (T) result));

            //return Task.FromResult(new ConditionalResult<T>(success, (T) result));
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.store.Values.GetEnumerator();
        }

        IEnumerator<IReliableState> IEnumerable<IReliableState>.GetEnumerator()
        {
            return this.store.Values.GetEnumerator();
        }

        public Task<T> GetOrAddAsync<T>(string name) where T : IReliableState
        {
            return Task.FromResult((T) this.store.GetOrAdd(this.ToUri(name), this.GetDependency(typeof(T))));
        }

        public Task<T> GetOrAddAsync<T>(ITransaction tx, string name) where T : IReliableState
        {
            return Task.FromResult((T) this.store.GetOrAdd(this.ToUri(name), this.GetDependency(typeof(T))));
        }

        public Task<T> GetOrAddAsync<T>(string name, TimeSpan timeout) where T : IReliableState
        {
            return Task.FromResult((T) this.store.GetOrAdd(this.ToUri(name), this.GetDependency(typeof(T))));
        }

        public Task<T> GetOrAddAsync<T>(ITransaction tx, string name, TimeSpan timeout) where T : IReliableState
        {
            return Task.FromResult((T) this.store.GetOrAdd(this.ToUri(name), this.GetDependency(typeof(T))));
        }

        public Task<T> GetOrAddAsync<T>(Uri name) where T : IReliableState
        {
            return Task.FromResult((T) this.store.GetOrAdd(name, this.GetDependency(typeof(T))));
        }

        public Task<T> GetOrAddAsync<T>(Uri name, TimeSpan timeout) where T : IReliableState
        {
            return Task.FromResult((T) this.store.GetOrAdd(name, this.GetDependency(typeof(T))));
        }

        public Task<T> GetOrAddAsync<T>(ITransaction tx, Uri name) where T : IReliableState
        {
            return Task.FromResult((T) this.store.GetOrAdd(name, this.GetDependency(typeof(T))));
        }

        public Task<T> GetOrAddAsync<T>(ITransaction tx, Uri name, TimeSpan timeout) where T : IReliableState
        {
            return Task.FromResult((T) this.store.GetOrAdd(name, this.GetDependency(typeof(T))));
        }

        public bool TryAddStateSerializer<T>(Microsoft.ServiceFabric.Data.IStateSerializer<T> stateSerializer)
        {
            throw new NotImplementedException();
        }

        public Task<BackupInfo> BackupAsync(Func<BackupInfo, Task<bool>> backupCallback)
        {
            throw new NotImplementedException();
        }

        public Task<BackupInfo> BackupAsync(
            BackupOption option, TimeSpan timeout, CancellationToken cancellationToken, Func<BackupInfo, Task<bool>> backupCallback)
        {
            throw new NotImplementedException();
        }

        public Task RestoreAsync(string backupFolderPath)
        {
            throw new NotImplementedException();
        }

        public Task RestoreAsync(string backupFolderPath, RestorePolicy restorePolicy, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public bool TryAddStateSerializer<T>(Uri name, Microsoft.ServiceFabric.Data.IStateSerializer<T> stateSerializer)
        {
            throw new NotImplementedException();
        }

        public bool TryAddStateSerializer<T>(string name, Microsoft.ServiceFabric.Data.IStateSerializer<T> stateSerializer)
        {
            throw new NotImplementedException();
        }

        private IReliableState GetDependency(Type t)
        {
            Type mockType = this.dependencyMap[t.GetGenericTypeDefinition()];

            return (IReliableState) Activator.CreateInstance(mockType.MakeGenericType(t.GetGenericArguments()));
        }

        private Uri ToUri(string name)
        {
            return new Uri("mock://" + name, UriKind.Absolute);
        }
    }
}