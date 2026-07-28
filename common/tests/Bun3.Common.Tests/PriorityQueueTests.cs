using System;
using System.Collections.Generic;
using NUnit.Framework;
using PQ = Bun3.Common.Collections.PriorityQueue<string, int>;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public class PriorityQueueTests
    {
        [Test]
        public void Dequeue_ReturnsElementsInPriorityOrder()
        {
            var queue = new PQ();
            queue.Enqueue("c", 3);
            queue.Enqueue("a", 1);
            queue.Enqueue("d", 4);
            queue.Enqueue("b", 2);

            Assert.That(queue.Dequeue(), Is.EqualTo("a"));
            Assert.That(queue.Dequeue(), Is.EqualTo("b"));
            Assert.That(queue.Dequeue(), Is.EqualTo("c"));
            Assert.That(queue.Dequeue(), Is.EqualTo("d"));
            Assert.That(queue.Count, Is.EqualTo(0));
        }

        [Test]
        public void Dequeue_ManyRandomItems_ComesOutSorted()
        {
            var queue = new Bun3.Common.Collections.PriorityQueue<int, int>();
            var random = new Random(12345);
            var expected = new List<int>();
            for (var i = 0; i < 1000; i++)
            {
                var value = random.Next(0, 100);
                expected.Add(value);
                queue.Enqueue(value, value);
            }
            expected.Sort();

            foreach (var value in expected)
                Assert.That(queue.Dequeue(), Is.EqualTo(value));
        }

        [Test]
        public void Enqueue_DuplicatePriorities_AllElementsComeOut()
        {
            var queue = new PQ();
            queue.Enqueue("x", 1);
            queue.Enqueue("y", 1);
            queue.Enqueue("z", 1);

            var results = new List<string> { queue.Dequeue(), queue.Dequeue(), queue.Dequeue() };
            Assert.That(results, Is.EquivalentTo(new[] { "x", "y", "z" }));
        }

        [Test]
        public void CustomComparer_ReversesOrder()
        {
            var maxFirst = Comparer<int>.Create((a, b) => b.CompareTo(a));
            var queue = new Bun3.Common.Collections.PriorityQueue<string, int>(maxFirst);

            queue.Enqueue("low", 1);
            queue.Enqueue("high", 10);

            Assert.That(queue.Comparer, Is.SameAs(maxFirst));
            Assert.That(queue.Dequeue(), Is.EqualTo("high"));
            Assert.That(queue.Dequeue(), Is.EqualTo("low"));
        }

        [Test]
        public void DequeueAndPeek_EmptyQueue_Throw()
        {
            var queue = new PQ();
            Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
            Assert.Throws<InvalidOperationException>(() => queue.Peek());
        }

        [Test]
        public void TryDequeueAndTryPeek_EmptyQueue_ReturnFalse()
        {
            var queue = new PQ();
            Assert.That(queue.TryDequeue(out _, out _), Is.False);
            Assert.That(queue.TryPeek(out _, out _), Is.False);
        }

        [Test]
        public void TryPeek_DoesNotRemove_TryDequeueRemoves()
        {
            var queue = new PQ();
            queue.Enqueue("a", 1);

            Assert.That(queue.TryPeek(out var peeked, out var peekedPriority), Is.True);
            Assert.That(peeked, Is.EqualTo("a"));
            Assert.That(peekedPriority, Is.EqualTo(1));
            Assert.That(queue.Count, Is.EqualTo(1));

            Assert.That(queue.TryDequeue(out var dequeued, out var priority), Is.True);
            Assert.That(dequeued, Is.EqualTo("a"));
            Assert.That(priority, Is.EqualTo(1));
            Assert.That(queue.Count, Is.EqualTo(0));
        }

        [Test]
        public void Clear_EmptiesQueue_AndQueueRemainsUsable()
        {
            var queue = new PQ();
            queue.Enqueue("a", 1);
            queue.Enqueue("b", 2);
            queue.Clear();

            Assert.That(queue.Count, Is.EqualTo(0));
            queue.Enqueue("c", 3);
            Assert.That(queue.Dequeue(), Is.EqualTo("c"));
        }

        [Test]
        public void Constructor_NegativeCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Bun3.Common.Collections.PriorityQueue<string, int>(-1));
        }
    }
}
