using System;
using FixedMathSharp;
using NUnit.Framework;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public sealed class Fixed64AllocationTests
    {
        [Test]
        public void Arithmetic_tick_loop_does_not_allocate()
        {
            var step = Fixed64.FromRaw(429_496_728L);
            var position = Fixed64.Zero;

            position += step; // JIT warm-up
            position -= step;
            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < 10_000; i++)
            {
                position += step;
                position -= step;
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(position, Is.EqualTo(Fixed64.Zero));
        }
    }
}
