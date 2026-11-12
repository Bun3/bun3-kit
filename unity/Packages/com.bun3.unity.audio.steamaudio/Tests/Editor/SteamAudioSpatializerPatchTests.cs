using System;
using NUnit.Framework;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class SteamAudioSpatializerPatchTests
    {
        const int Rate = 48000;
        const int Frames = 1024;

        // Reference: the managed renderer's stereo width with its 20 ms slew, as shipped before the native route.
        static void Reference(float[] left, float[] right, ref float current, float target, int rate)
        {
            float step = 1f / Math.Max(1, rate * .02f);
            for (int i = 0; i < left.Length; i++)
            {
                if (current < target) current = Math.Min(target, current + step);
                else if (current > target) current = Math.Max(target, current - step);
                if (current == 1) continue;
                float mid = (left[i] + right[i]) * .5f;
                float side = (left[i] - right[i]) * (.5f * current);
                left[i] = mid + side;
                right[i] = mid - side;
            }
        }

        static (float[], float[]) Noise(int seed)
        {
            var random = new Random(seed);
            var left = new float[Frames];
            var right = new float[Frames];
            for (int i = 0; i < Frames; i++)
            {
                left[i] = (float)(random.NextDouble() * 2 - 1);
                right[i] = (float)(random.NextDouble() * 2 - 1);
            }
            return (left, right);
        }

        [Test]
        public void PathingWidthMatchesManagedReference()
        {
            Assume.That(SteamAudioSpatializerPatch.IsPatched, "The patched audioplugin_phonon.dll is not installed.");
            var (expectedLeft, expectedRight) = Noise(7);
            var left = (float[])expectedLeft.Clone();
            var right = (float[])expectedRight.Clone();
            float expectedCurrent = 1, current = 1;
            Reference(expectedLeft, expectedRight, ref expectedCurrent, 0, Rate);
            SteamAudioSpatializerPatch.ApplyPathingWidthForTest(left, right, ref current, 0, Rate);
            for (int i = 0; i < Frames; i++)
            {
                Assert.That(left[i], Is.EqualTo(expectedLeft[i]).Within(1e-6f), "left " + i);
                Assert.That(right[i], Is.EqualTo(expectedRight[i]).Within(1e-6f), "right " + i);
            }
            Assert.That(current, Is.EqualTo(expectedCurrent).Within(1e-7f));
        }

        [Test]
        public void ZeroWidthMakesChannelsEqual()
        {
            Assume.That(SteamAudioSpatializerPatch.IsPatched, "The patched audioplugin_phonon.dll is not installed.");
            var (left, right) = Noise(11);
            float current = 0;
            SteamAudioSpatializerPatch.ApplyPathingWidthForTest(left, right, ref current, 0, Rate);
            for (int i = 0; i < Frames; i++) Assert.That(left[i], Is.EqualTo(right[i]), "frame " + i);
        }

        [Test]
        public void FullWidthIsIdentity()
        {
            Assume.That(SteamAudioSpatializerPatch.IsPatched, "The patched audioplugin_phonon.dll is not installed.");
            var (expectedLeft, expectedRight) = Noise(13);
            var left = (float[])expectedLeft.Clone();
            var right = (float[])expectedRight.Clone();
            float current = 1;
            SteamAudioSpatializerPatch.ApplyPathingWidthForTest(left, right, ref current, 1, Rate);
            Assert.That(left, Is.EqualTo(expectedLeft));
            Assert.That(right, Is.EqualTo(expectedRight));
        }
    }
}
