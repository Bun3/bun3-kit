using Bun3.Unity.Audio;
using NUnit.Framework;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class ChannelVolumeTests
    {
        [Test]
        public void LinearToDb_FullVolumeIsZeroDb()
            => Assert.That(AudioMath.LinearToDb(1f), Is.EqualTo(0f).Within(0.001f));

        [Test]
        public void LinearToDb_ZeroClampsToFloor()
            => Assert.That(AudioMath.LinearToDb(0f), Is.EqualTo(-80f));

        [Test]
        public void RoundTrip_PreservesValue()
            => Assert.That(AudioMath.DbToLinear(AudioMath.LinearToDb(0.5f)), Is.EqualTo(0.5f).Within(0.001f));
    }
}
