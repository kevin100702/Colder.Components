﻿using Colder.DistributedLock.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Colder.DistributedLock.InMemory
{
    internal class InMemoryDistributedLock : IDistributedLock
    {
        private readonly IMemoryCache _lockDic  = new MemoryCache(new MemoryCacheOptions());
        private readonly ConcurrentDictionary<string, object> _cacheLock
            = new ConcurrentDictionary<string, object>();
        public Task<IDisposable> Lock(string key, TimeSpan? timeout)
        {
            timeout = timeout ?? TimeSpan.FromSeconds(10);

            SemaphoreSlimLock theLock;

            lock (_cacheLock.GetOrAdd(key, new object()))
            {
                theLock = _lockDic.GetOrCreate(key, cacheEntry =>
                {
                    var newLock = new SemaphoreSlimLock();

                    cacheEntry.SlidingExpiration = TimeSpan.FromMinutes(1);
                    cacheEntry.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
                    {
                        EvictionCallback = (_, _, _, _) =>
                        {
                            _cacheLock.TryRemove(key, out object _);
                            newLock.DisposeLock();
                        }
                    });

                    return newLock;
                });
            }

            theLock.WaitOne(timeout);

            return Task.FromResult((IDisposable)theLock);
        }

        private class SemaphoreSlimLock : IDisposable
        {
            private readonly Semaphore _semaphore = new Semaphore(1, 1);
            public void WaitOne(TimeSpan? timeout)
            {
                if (timeout == null)
                {
                    _semaphore.WaitOne();
                }
                else
                {
                    _semaphore.WaitOne(timeout.Value);
                }
            }
            public void Dispose()
            {
                _semaphore.Release();
            }
            public void DisposeLock()
            {
                _semaphore.Dispose();
            }
        }
    }
}
