using System.Collections.Generic;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="Queue{T}"/> rented from a shared pool. Same contract as
    /// <see cref="PooledList{T}"/>.
    /// </summary>
    public class PooledQueue<T> : Queue<T>, IPooledObject<PooledQueue<T>>
    {
        private static readonly ConcurrentObjectPool<PooledQueue<T>> SharedPool =
            new ConcurrentObjectPool<PooledQueue<T>>();

        private IObjectPool<PooledQueue<T>> _pool;

        /// <summary>Rents an empty queue from the shared pool.</summary>
        public static PooledQueue<T> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledQueue<T>>.SetPool(IObjectPool<PooledQueue<T>> pool)
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
