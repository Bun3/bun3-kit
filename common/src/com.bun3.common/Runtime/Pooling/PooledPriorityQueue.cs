using System.Threading;
using Bun3.Common.Collections;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="PriorityQueue{TElement, TPriority}"/> rented from a shared pool. Same
    /// contract as <see cref="PooledList{T}"/>.
    /// </summary>
    public class PooledPriorityQueue<TElement, TPriority>
        : PriorityQueue<TElement, TPriority>, IPooledObject<PooledPriorityQueue<TElement, TPriority>>
    {
        private static readonly ConcurrentObjectPool<PooledPriorityQueue<TElement, TPriority>> SharedPool =
            new ConcurrentObjectPool<PooledPriorityQueue<TElement, TPriority>>();

        private IObjectPool<PooledPriorityQueue<TElement, TPriority>> _pool;

        /// <summary>Rents an empty priority queue from the shared pool.</summary>
        public static PooledPriorityQueue<TElement, TPriority> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledPriorityQueue<TElement, TPriority>>.SetPool(
            IObjectPool<PooledPriorityQueue<TElement, TPriority>> pool)
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
