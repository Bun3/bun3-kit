using System;
using Bun3.Common.Pooling;
using NUnit.Framework;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public class ConcurrentObjectPoolTests
    {
        private class TestItem : IPooledObject<TestItem>
        {
            public IObjectPool<TestItem> Pool;
            public int SetPoolCalls;

            public void SetPool(IObjectPool<TestItem> pool)
            {
                Pool = pool;
                SetPoolCalls++;
            }

            public void Dispose() { }
        }

        [Test]
        public void Get_EmptyPool_CreatesNewItemAndArmsIt()
        {
            var pool = new ConcurrentObjectPool<TestItem>();
            var item = pool.Get();

            Assert.That(item, Is.Not.Null);
            Assert.That(item.Pool, Is.SameAs(pool));
        }

        [Test]
        public void Get_AfterRelease_ReturnsSameInstanceAndRearms()
        {
            var pool = new ConcurrentObjectPool<TestItem>();
            var item = pool.Get();
            pool.Release(item);

            var again = pool.Get();
            Assert.That(again, Is.SameAs(item));
            // Three SetPool calls: Get() arms it, Release() disarms it (sets null),
            // and the second Get() re-arms it. This still fails if re-arm on pool-hit
            // is ever removed, since the count would stay at 2.
            Assert.That(again.SetPoolCalls, Is.EqualTo(3));
            Assert.That(again.Pool, Is.SameAs(pool));
        }

        [Test]
        public void Release_BeyondMaxCapacity_DropsExtraItems()
        {
            var pool = new ConcurrentObjectPool<TestItem>(maxCapacity: 1);
            var first = pool.Get();
            var second = pool.Get();
            pool.Release(first);
            pool.Release(second); // over capacity — dropped

            var fromPool = pool.Get();      // the one retained item
            var created = pool.Get();       // pool empty again — freshly created

            Assert.That(fromPool, Is.SameAs(first).Or.SameAs(second));
            Assert.That(created, Is.Not.SameAs(first));
            Assert.That(created, Is.Not.SameAs(second));
        }

        [Test]
        public void Release_AfterOverCapacityDrop_StillRetainsUpToCapacity()
        {
            // Pins the rollback decrement in Release: without it, the over-capacity drop
            // would leave the counter permanently saturated, and this later release would
            // also be dropped instead of being retained.
            var pool = new ConcurrentObjectPool<TestItem>(maxCapacity: 1);
            var a = pool.Get();
            var b = pool.Get();
            pool.Release(a);
            pool.Release(b); // over capacity — dropped, counter must roll back

            var drained = pool.Get(); // drains the pool (the one retained item)
            pool.Release(drained);

            var next = pool.Get();
            Assert.That(next, Is.SameAs(drained));
        }

        [Test]
        public void Defaults_MatchSpec()
        {
            var pool = new ConcurrentObjectPool<TestItem>();
            Assert.That(pool.MaxCapacity,
                Is.EqualTo(Math.Max(32, 2 * Environment.ProcessorCount)));
            Assert.That(pool.MaxRetainedCount, Is.EqualTo(8192));
        }
    }
}
