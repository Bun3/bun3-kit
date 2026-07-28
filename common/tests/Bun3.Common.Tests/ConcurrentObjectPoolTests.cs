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
            Assert.That(again.SetPoolCalls, Is.EqualTo(2));
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
        public void Defaults_MatchSpec()
        {
            var pool = new ConcurrentObjectPool<TestItem>();
            Assert.That(pool.MaxCapacity,
                Is.EqualTo(Math.Max(32, 2 * Environment.ProcessorCount)));
            Assert.That(pool.MaxRetainedCount, Is.EqualTo(8192));
        }
    }
}
