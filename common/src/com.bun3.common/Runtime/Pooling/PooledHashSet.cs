using System.Collections.Generic;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="HashSet{T}"/> rented from a shared pool. Same contract as
    /// <see cref="PooledList{T}"/>.
    /// </summary>
    public class PooledHashSet<T> : HashSet<T>, IPooledObject<PooledHashSet<T>>
    {
        private static readonly ConcurrentObjectPool<PooledHashSet<T>> SharedPool =
            new ConcurrentObjectPool<PooledHashSet<T>>();

        private IObjectPool<PooledHashSet<T>> _pool;

        /// <summary>Rents an empty set from the shared pool.</summary>
        public static PooledHashSet<T> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledHashSet<T>>.SetPool(IObjectPool<PooledHashSet<T>> pool)
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
