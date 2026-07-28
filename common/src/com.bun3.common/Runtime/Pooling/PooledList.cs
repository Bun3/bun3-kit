using System.Collections.Generic;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="List{T}"/> rented from a shared pool. Dispose returns it to its pool,
    /// so <c>using var list = PooledList&lt;T&gt;.Get();</c> is allocation free after warm-up.
    /// Returning one from a method transfers ownership to the caller, who must dispose it.
    /// Dispose is idempotent; disposing a directly-constructed instance is a no-op.
    /// </summary>
    public class PooledList<T> : List<T>, IPooledObject<PooledList<T>>
    {
        private static readonly ConcurrentObjectPool<PooledList<T>> SharedPool =
            new ConcurrentObjectPool<PooledList<T>>();

        private IObjectPool<PooledList<T>> _pool;

        /// <summary>Rents an empty list from the shared pool.</summary>
        public static PooledList<T> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledList<T>>.SetPool(IObjectPool<PooledList<T>> pool)
        {
            _pool = pool;
        }

        public void Dispose()
        {
            var pool = Interlocked.Exchange(ref _pool, null);
            if (pool == null)
                return; // double dispose, or directly-constructed instance
            if (Count > pool.MaxRetainedCount)
                return; // grew too large — let the GC take it
            Clear();
            pool.Release(this);
        }
    }
}
