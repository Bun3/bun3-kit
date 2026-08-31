using Bun3.Unity.Audio;
using NUnit.Framework;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class FloatRangeTests
    {
        [Test]
        public void Roll_StaysWithinBounds()
        {
            var range = new FloatRange(0.8f, 1.2f);
            for (var i = 0; i < 100; i++)
            {
                var value = range.Roll();
                Assert.That(value, Is.InRange(0.8f, 1.2f));
            }
        }

        [Test]
        public void Roll_DegenerateRange_ReturnsConstant()
        {
            var range = new FloatRange(1f, 1f);
            Assert.That(range.Roll(), Is.EqualTo(1f));
        }
    }
}
