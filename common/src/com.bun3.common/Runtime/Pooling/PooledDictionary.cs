using System.Collections.Generic;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> rented from a shared pool. Same contract as
    /// <see cref="PooledList{T}"/>: dispose returns it, dispose is idempotent, ownership
    /// transfers with the reference.
    /// </summary>
    public class PooledDictionary<TKey, TValue>
        : Dictionary<TKey, TValue>, IPooledObject<PooledDictionary<TKey, TValue>>
    {
        private static readonly ConcurrentObjectPool<PooledDictionary<TKey, TValue>> SharedPool =
            new ConcurrentObjectPool<PooledDictionary<TKey, TValue>>();

        private IObjectPool<PooledDictionary<TKey, TValue>> _pool;

        /// <summary>Rents an empty dictionary from the shared pool.</summary>
        public static PooledDictionary<TKey, TValue> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledDictionary<TKey, TValue>>.SetPool(
            IObjectPool<PooledDictionary<TKey, TValue>> pool)
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
