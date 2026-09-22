using NUnit.Framework;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class SteamAudioPathFactoryTests
    {
        [Test]
        public void DefaultFactoryReturnsAnIndependentlyOwnedWorkingRenderer()
        {
            using var renderer = SteamAudioPathRenderer.CreateDefault(48000, 256);
            var input = new float[256];
            input[0] = 1;
            var output = new float[512];
            renderer.Render(input, output, new[] { .2820948f, .4886025f, 0, 0 }, 1, 1, 1,
                new SA.CoordinateSpace3 { right = new SA.Vector3 { x = 1 }, up = new SA.Vector3 { y = 1 }, ahead = new SA.Vector3 { z = -1 } });
            double energy = 0;
            foreach (float value in output) energy += value * value;
            Assert.That(energy, Is.GreaterThan(1e-10));
        }
    }
}
