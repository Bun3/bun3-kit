using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// Thread-safe object pool. Backed by <see cref="ConcurrentBag{T}"/> (thread-local
    /// queues with work stealing), so the common same-thread Get/Release cycle is nearly
    /// lock free. Size is tracked with an <see cref="Interlocked"/> counter instead of
    /// <c>ConcurrentBag.Count</c>, which would lock and walk every thread-local queue.
    /// </summary>
    public class ConcurrentObjectPool<T> : IObjectPool<T> where T : class, IPooledObject<T>, new()
    {
        private readonly ConcurrentBag<T> _items = new ConcurrentBag<T>();
        private int _count;

        public int MaxCapacity { get; }
        public int MaxRetainedCount { get; }

        /// <param name="maxCapacity">
        /// Most items the pool retains; further releases are dropped. Values &lt;= 0 select
        /// the default <c>Math.Max(32, 2 * Environment.ProcessorCount)</c>.
        /// </param>
        /// <param name="maxRetainedCount">See <see cref="IObjectPool{T}.MaxRetainedCount"/>.</param>
        public ConcurrentObjectPool(int maxCapacity = 0, int maxRetainedCount = 8192)
        {
            MaxCapacity = maxCapacity > 0
                ? maxCapacity
                : Math.Max(32, 2 * Environment.ProcessorCount);
            MaxRetainedCount = maxRetainedCount;
        }

        public T Get()
        {
            if (_items.TryTake(out var item))
                Interlocked.Decrement(ref _count);
            else
                item = new T();
            item.SetPool(this);
            return item;
        }

        public void Release(T item)
        {
            // Disarm immediately: once released, the item no longer references this pool,
            // so a caller who releases directly (instead of via Dispose) can't double-insert
            // the same instance by later calling Dispose() too.
            item.SetPool(null);

            if (Interlocked.Increment(ref _count) > MaxCapacity)
            {
                Interlocked.Decrement(ref _count);
                return;
            }
            _items.Add(item);
        }
    }
}
