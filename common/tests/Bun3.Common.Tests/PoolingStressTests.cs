using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Common.Pooling;
using NUnit.Framework;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public class PoolingStressTests
    {
        [Test]
        public void ParallelGetAndDispose_NeverHandsOneInstanceToTwoThreads()
        {
            var owned = new ConcurrentDictionary<PooledList<byte>, byte>();
            var doubleRentals = 0;

            Parallel.For(0, 200_000, _ =>
            {
                var list = PooledList<byte>.Get();
                if (!owned.TryAdd(list, 0))
                    Interlocked.Increment(ref doubleRentals);

                list.Add(1);

                // Give up ownership tracking BEFORE dispose — after dispose another
                // thread may legitimately rent this instance.
                owned.TryRemove(list, out var unused);
                list.Dispose();
            });

            Assert.That(doubleRentals, Is.EqualTo(0));
        }
    }
}
