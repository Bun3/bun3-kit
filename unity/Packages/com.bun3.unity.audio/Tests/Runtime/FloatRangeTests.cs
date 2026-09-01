using Bun3.Unity.Audio;
using NUnit.Framework;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class FloatRangeTests
    {
        [Test]
        public void Roll_StaysWithinBounds()
        {
            var rng = new System.Random(1234);
            var range = new FloatRange(0.8f, 1.2f);
            for (var i = 0; i < 100; i++)
            {
                var value = range.Roll(rng);
                Assert.That(value, Is.InRange(0.8f, 1.2f));
            }
        }

        [Test]
        public void Roll_DegenerateRange_ReturnsConstant()
        {
            var rng = new System.Random(1234);
            var range = new FloatRange(1f, 1f);
            Assert.That(range.Roll(rng), Is.EqualTo(1f));
        }

        [Test]
        public void Roll_SameSeed_ProducesSameSequence()
        {
            var range = new FloatRange(0f, 1f);
            var a = new System.Random(42);
            var b = new System.Random(42);
            for (var i = 0; i < 20; i++)
            {
                Assert.That(range.Roll(a), Is.EqualTo(range.Roll(b)));
            }
        }
    }
}
