using System;
using Bun3.Common.Pooling;
using NUnit.Framework;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public class PooledCollectionTests
    {
        /// <summary>
        /// Shared contract: dispose pools the cleared instance for reuse; double dispose
        /// does not pool it twice.
        /// </summary>
        private static void AssertPoolingContract<TCollection>(
            Func<TCollection> get, Action<TCollection> addOne, Func<TCollection, int> count)
            where TCollection : class, IDisposable
        {
            var first = get();
            addOne(first);
            first.Dispose();

            var reused = get();
            Assert.That(reused, Is.SameAs(first), "dispose should pool the instance");
            Assert.That(count(reused), Is.EqualTo(0), "pooled instance should be cleared");

            reused.Dispose();
            reused.Dispose(); // no-op

            var third = get();
            var fourth = get();
            Assert.That(third, Is.SameAs(reused));
            Assert.That(fourth, Is.Not.SameAs(reused), "double dispose must not pool twice");
            third.Dispose();
            fourth.Dispose();
        }

        [Test]
        public void PooledDictionary_FollowsPoolingContract()
        {
            AssertPoolingContract(
                PooledDictionary<Guid, int>.Get,
                d => d[Guid.NewGuid()] = 1,
                d => d.Count);
        }

        [Test]
        public void PooledHashSet_FollowsPoolingContract()
        {
            AssertPoolingContract(
                PooledHashSet<Guid>.Get,
                s => s.Add(Guid.NewGuid()),
                s => s.Count);
        }

        [Test]
        public void PooledQueue_FollowsPoolingContract()
        {
            AssertPoolingContract(
                PooledQueue<Guid>.Get,
                q => q.Enqueue(Guid.NewGuid()),
                q => q.Count);
        }

        [Test]
        public void PooledStack_FollowsPoolingContract()
        {
            AssertPoolingContract(
                PooledStack<Guid>.Get,
                s => s.Push(Guid.NewGuid()),
                s => s.Count);
        }

        [Test]
        public void PooledSortedDictionary_FollowsPoolingContract()
        {
            AssertPoolingContract(
                PooledSortedDictionary<int, Guid>.Get,
                d => d[7] = Guid.NewGuid(),
                d => d.Count);
        }
    }
}
