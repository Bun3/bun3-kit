using System;
using NUnit.Framework;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class SteamAudioPathTailTests
    {
        [Test]
        public void NativeTailCanBeDrainedWithoutSubmittingMoreInput()
        {
            var tailMethod = typeof(SteamAudioPathRenderer).GetMethod("RenderTail");
            var remaining = typeof(SteamAudioPathRenderer).GetProperty("TailSamplesRemaining");
            Assert.That(tailMethod, Is.Not.Null, "Native path tails must have an explicit output operation.");
            Assert.That(remaining, Is.Not.Null);
            var context = new SA.Context();
            var hrtf = new SA.HRTF(context, new SA.AudioSettings { samplingRate = 48000, frameSize = 256 }, null, null, 0, SA.HRTFNormType.None);
            try
            {
                using var renderer = new SteamAudioPathRenderer(context, hrtf, 48000, 256);
                var mono = new float[256]; mono[255] = 1;
                var stereo = new float[512];
                var coordinates = new SA.CoordinateSpace3
                {
                    right = new SA.Vector3 { x = 1 }, up = new SA.Vector3 { y = 1 }, ahead = new SA.Vector3 { z = -1 }
                };
                renderer.Render(mono, stereo, new[] { 0.2820948f, 0.4886025f, 0f, 0f }, 1, 1, 1, coordinates);
                Assert.That((int)remaining.GetValue(renderer), Is.GreaterThan(0));
                double energy = 0;
                int frames = 0;
                while ((int)remaining.GetValue(renderer) > 0 && frames++ < 64)
                {
                    tailMethod.Invoke(renderer, new object[] { stereo, 1f });
                    foreach (float sample in stereo) energy += sample * sample;
                }
                Assert.That((int)remaining.GetValue(renderer), Is.Zero);
                Assert.That(energy, Is.GreaterThan(1e-10));
                renderer.Reset();
                Assert.That((int)remaining.GetValue(renderer), Is.Zero);
            }
            finally { hrtf.Release(); context.Release(); }
        }
    }
}
