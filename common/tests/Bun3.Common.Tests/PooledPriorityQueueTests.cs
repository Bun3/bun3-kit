using Bun3.Common.Pooling;
using NUnit.Framework;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public class PooledPriorityQueueTests
    {
        [Test]
        public void Get_AfterDispose_ReusesSameInstanceCleared()
        {
            var queue = PooledPriorityQueue<string, int>.Get();
            queue.Enqueue("b", 2);
            queue.Enqueue("a", 1);
            Assert.That(queue.Peek(), Is.EqualTo("a"));
            queue.Dispose();

            var reused = PooledPriorityQueue<string, int>.Get();
            Assert.That(reused, Is.SameAs(queue));
            Assert.That(reused.Count, Is.EqualTo(0));

            reused.Enqueue("c", 3);
            Assert.That(reused.Dequeue(), Is.EqualTo("c"));
            reused.Dispose();
        }

        [Test]
        public void DoubleDispose_IsNoOp_InstanceNotPooledTwice()
        {
            var queue = PooledPriorityQueue<double, double>.Get();
            queue.Dispose();
            queue.Dispose();

            var first = PooledPriorityQueue<double, double>.Get();
            var second = PooledPriorityQueue<double, double>.Get();
            Assert.That(first, Is.SameAs(queue));
            Assert.That(second, Is.Not.SameAs(queue));
            first.Dispose();
            second.Dispose();
        }
    }
}
