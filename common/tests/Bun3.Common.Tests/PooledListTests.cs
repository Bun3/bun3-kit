using System;
using Bun3.Common.Pooling;
using NUnit.Framework;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public class PooledListTests
    {
        [Test]
        public void Get_AfterDispose_ReusesSameInstanceCleared()
        {
            var list = PooledList<Guid>.Get();
            list.Add(Guid.NewGuid());
            list.Dispose();

            var reused = PooledList<Guid>.Get();
            Assert.That(reused, Is.SameAs(list));
            Assert.That(reused.Count, Is.EqualTo(0));
            reused.Dispose();
        }

        [Test]
        public void DoubleDispose_IsNoOp_InstanceNotPooledTwice()
        {
            var list = PooledList<double>.Get();
            list.Dispose();
            list.Dispose(); // must not enqueue a second time

            var first = PooledList<double>.Get();
            var second = PooledList<double>.Get();
            Assert.That(first, Is.SameAs(list));
            Assert.That(second, Is.Not.SameAs(list));
            first.Dispose();
            second.Dispose();
        }

        [Test]
        public void DirectlyConstructed_DisposeIsNoOp()
        {
            var list = new PooledList<byte> { 1, 2, 3 };
            Assert.DoesNotThrow(() => list.Dispose());
            Assert.DoesNotThrow(() => list.Dispose());
        }

        [Test]
        public void Dispose_CountOverRetainedThreshold_DropsInstance()
        {
            var pool = new ConcurrentObjectPool<PooledList<short>>(maxRetainedCount: 4);
            var list = pool.Get();
            for (short i = 0; i < 5; i++)
                list.Add(i);
            list.Dispose(); // 5 > 4 — dropped, not pooled

            var next = pool.Get();
            Assert.That(next, Is.Not.SameAs(list));
            next.Dispose();
        }

        [Test]
        public void Dispose_CountAtRetainedThreshold_IsPooled()
        {
            var pool = new ConcurrentObjectPool<PooledList<long>>(maxRetainedCount: 4);
            var list = pool.Get();
            for (long i = 0; i < 4; i++)
                list.Add(i);
            list.Dispose(); // 4 <= 4 — pooled

            var next = pool.Get();
            Assert.That(next, Is.SameAs(list));
            next.Dispose();
        }

        private static PooledList<string> MakeGreetings()
        {
            var result = PooledList<string>.Get();
            result.Add("hello");
            result.Add("world");
            return result; // ownership transfers to the caller
        }

        [Test]
        public void ReturnedFromFunction_CallerOwnsAndDisposes()
        {
            string first;
            using (var greetings = MakeGreetings())
            {
                Assert.That(greetings.Count, Is.EqualTo(2));
                first = greetings[0];
            }
            Assert.That(first, Is.EqualTo("hello"));

            var reused = PooledList<string>.Get();
            Assert.That(reused.Count, Is.EqualTo(0));
            reused.Dispose();
        }
    }
}
