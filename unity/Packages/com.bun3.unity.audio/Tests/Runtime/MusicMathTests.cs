using Bun3.Unity.Audio;
using NUnit.Framework;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class MusicMathTests
    {
        [Test]
        public void RemainingSeconds_AtStart_IsFullLength()
            => Assert.That(
                MusicMath.RemainingSeconds(timeSamples: 0, totalSamples: 44100, frequency: 44100),
                Is.EqualTo(1.0).Within(1e-9));

        [Test]
        public void RemainingSeconds_Midway_IsHalf()
            => Assert.That(
                MusicMath.RemainingSeconds(22050, 44100, 44100),
                Is.EqualTo(0.5).Within(1e-9));

        [Test]
        public void RemainingSeconds_PastEnd_ClampsToZero()
            => Assert.That(
                MusicMath.RemainingSeconds(44100, 44100, 44100),
                Is.EqualTo(0.0));
    }
}
