using System.Collections.Generic;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="Stack{T}"/> rented from a shared pool. Same contract as
    /// <see cref="PooledList{T}"/>.
    /// </summary>
    public class PooledStack<T> : Stack<T>, IPooledObject<PooledStack<T>>
    {
        private static readonly ConcurrentObjectPool<PooledStack<T>> SharedPool =
            new ConcurrentObjectPool<PooledStack<T>>();

        private IObjectPool<PooledStack<T>> _pool;

        /// <summary>Rents an empty stack from the shared pool.</summary>
        public static PooledStack<T> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledStack<T>>.SetPool(IObjectPool<PooledStack<T>> pool)
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
