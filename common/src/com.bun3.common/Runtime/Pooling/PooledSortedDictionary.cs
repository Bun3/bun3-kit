using System.Collections.Generic;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="SortedDictionary{TKey, TValue}"/> rented from a shared pool. Same
    /// contract as <see cref="PooledList{T}"/>.
    /// </summary>
    public class PooledSortedDictionary<TKey, TValue>
        : SortedDictionary<TKey, TValue>, IPooledObject<PooledSortedDictionary<TKey, TValue>>
    {
        private static readonly ConcurrentObjectPool<PooledSortedDictionary<TKey, TValue>> SharedPool =
            new ConcurrentObjectPool<PooledSortedDictionary<TKey, TValue>>();

        private IObjectPool<PooledSortedDictionary<TKey, TValue>> _pool;

        /// <summary>Rents an empty sorted dictionary from the shared pool.</summary>
        public static PooledSortedDictionary<TKey, TValue> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledSortedDictionary<TKey, TValue>>.SetPool(
            IObjectPool<PooledSortedDictionary<TKey, TValue>> pool)
        {
            _pool = pool;
        }

        public void Dispose()
        {
            var pool = Interlocked.Exchange(ref _pool, null);
            if (pool == null)
                return;
            if (Count > pool.MaxRetainedCount)
                return;
            Clear();
            pool.Release(this);
        }
    }
}
