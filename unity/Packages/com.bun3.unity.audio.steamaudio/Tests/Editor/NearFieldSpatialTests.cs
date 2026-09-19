using System;
using NUnit.Framework;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class NearFieldSpatialTests
    {
        delegate void SpatialFrame(float[] mono, float[] stereo, float[] coefficients, float low, float mid,
            float high, SA.CoordinateSpace3 listener, float gain, bool normalizeEq, float spatialBlend);
        static SA.CoordinateSpace3 Listener => new SA.CoordinateSpace3 {
            right = new SA.Vector3 { x = 1 }, up = new SA.Vector3 { y = 1 }, ahead = new SA.Vector3 { z = -1 } };
        static SpatialFrame Bind(SteamAudioPathRenderer renderer)
        {
            var method = typeof(SteamAudioPathRenderer).GetMethod("RenderSpatial");
            Assert.That(method, Is.Not.Null, "Renderer must expose a path-preserving stereo-width control.");
            return (SpatialFrame)Delegate.CreateDelegate(typeof(SpatialFrame), renderer, method);
        }
        sealed class Settings : PlanarAcousticSettings
        {
            public float Near = 1, Far = 3;
            public SpatialBlendProfile Profile;
            public override SpatialBlendProfile SpatialBlendProfile => Profile;
            public override float MonoDistance => Near;
            public override float FullSpatialDistance => Far;
        }
        [Test]
        public void SharedNearFieldProfileWinsWhileInlineValuesRemainAvailable()
        {
            var profile = UnityEngine.ScriptableObject.CreateInstance<SpatialBlendProfile>();
            try
            {
                var first = new Settings { Near = 0, Far = 0, Profile = profile };
                var second = new Settings { Near = 0, Far = 0, Profile = profile };
                Assert.That(first.EvaluateSpatialBlend(.5f), Is.Zero);
                profile.MonoDistance = 2; profile.FullSpatialDistance = 4;
                Assert.That(first.EvaluateSpatialBlend(2), Is.Zero);
                Assert.That(second.EvaluateSpatialBlend(2), Is.Zero);
                first.Profile = null;
                Assert.That(first.EvaluateSpatialBlend(.5f), Is.EqualTo(1));
                Assert.That(second.EvaluateSpatialBlend(.5f), Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }
        [Test]
        public void NearFieldRangeHasMonoTransitionAndSpatialEndpointsAndLiveDisable()
        {
            var settings = new Settings();
            Assert.That(settings.EvaluateSpatialBlend(0), Is.Zero);
            Assert.That(settings.EvaluateSpatialBlend(1), Is.Zero);
            Assert.That(settings.EvaluateSpatialBlend(2), Is.EqualTo(.5f));
            Assert.That(settings.EvaluateSpatialBlend(3), Is.EqualTo(1));
            Assert.That(settings.EvaluateSpatialBlend(100), Is.EqualTo(1));
            Assert.That(settings.EvaluateSpatialBlend(float.NaN), Is.EqualTo(1));
            Assert.That(settings.EvaluateSpatialBlend(float.PositiveInfinity), Is.EqualTo(1));
            settings.Near = 2; settings.Far = 4;
            Assert.That(settings.EvaluateSpatialBlend(2), Is.Zero);
            settings.Far = 0;
            Assert.That(settings.EvaluateSpatialBlend(0), Is.EqualTo(1));
            settings.Far = 2;
            Assert.That(settings.EvaluateSpatialBlend(0), Is.EqualTo(1));
            settings.Far = float.NaN;
            Assert.That(settings.EvaluateSpatialBlend(0), Is.EqualTo(1));
        }
        [Test]
        public void NearFieldWidthCrossesMailboxWithoutChangingGainOrGate()
        {
            var mailbox = new PathParameterMailbox(4);
            var lease = mailbox.BeginGeneration(1);
            var target = new float[4];
            Assert.That(lease.TryPublish(Coefficients, new PathRenderSettings(Listener, gain: .25f, spatialBlend: .3f)), Is.True);
            Assert.That(mailbox.TryRead(1, target, out var value), Is.True);
            Assert.That(value.SpatialBlend, Is.EqualTo(.3f));
            Assert.That(value.Gain, Is.EqualTo(.25f));
            Assert.That(mailbox.IsBlocked, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => lease.TryPublish(Coefficients, new PathRenderSettings(Listener, spatialBlend: float.NaN)));
            Assert.That(mailbox.TryRead(1, target, out value), Is.True);
            Assert.That(value.SpatialBlend, Is.EqualTo(.3f));
        }
        static float[] Input()
        {
            var values = new float[256];
            for (int i = 0; i < values.Length; i++) values[i] = (float)Math.Sin(i * .17) * .1f;
            return values;
        }
        static readonly float[] Coefficients = { .2820948f, .4886025f, 0, 0 };
        static double Difference(float[] output)
        {
            double total = 0;
            for (int i = 0; i < output.Length; i += 2) total += Math.Abs(output[i] - output[i + 1]);
            return total;
        }
        [Test]
        public void NearFieldMonoHasIdenticalChannelsAndPreservesProcessedMidSignal()
        {
            using var renderer = SteamAudioPathRenderer.CreateDefault(48000, 256);
            var render = Bind(renderer);
            var input = Input(); var stereo = new float[512]; var mono = new float[512];
            for (int i = 0; i < 8; i++) render(input, stereo, Coefficients, .8f, .7f, .4f, Listener, .3f, false, 1);
            Assert.That(Difference(stereo), Is.GreaterThan(1e-5));
            renderer.Reset();
            for (int i = 0; i < 8; i++) render(input, mono, Coefficients, .8f, .7f, .4f, Listener, .3f, false, 0);
            Assert.That(Difference(mono), Is.Zero);
            double energy = 0;
            for (int i = 0; i < mono.Length; i += 2)
            {
                Assert.That(mono[i], Is.EqualTo((stereo[i] + stereo[i + 1]) * .5f).Within(1e-6));
                energy += mono[i] * mono[i];
            }
            Assert.That(energy, Is.GreaterThan(1e-9));
            while (renderer.TailSamplesRemaining > 0) { renderer.RenderTail(mono); Assert.That(Difference(mono), Is.Zero); }
        }
        [Test]
        public void SpatialWidthTransitionConvergesAndResetRestoresFullWidth()
        {
            using var renderer = SteamAudioPathRenderer.CreateDefault(48000, 256);
            var render = Bind(renderer); var input = Input(); var output = new float[512];
            for (int i = 0; i < 8; i++) render(input, output, Coefficients, 1, 1, 1, Listener, 1, false, 1);
            render(input, output, Coefficients, 1, 1, 1, Listener, 1, false, 0);
            Assert.That(Difference(output), Is.GreaterThan(0), "An active transition should ramp instead of snapping.");
            for (int i = 0; i < 16; i++) render(input, output, Coefficients, 1, 1, 1, Listener, 1, false, 0);
            Assert.That(Difference(output), Is.Zero);
            renderer.Reset();
            for (int i = 0; i < 8; i++) renderer.Render(input, output, Coefficients, 1, 1, 1, Listener);
            Assert.That(Difference(output), Is.GreaterThan(1e-5));
        }
        [Test]
        public void SpatialWidthRejectsInvalidValuesAndWarmProcessingDoesNotAllocate()
        {
            using var renderer = SteamAudioPathRenderer.CreateDefault(48000, 256);
            var render = Bind(renderer); var input = Input(); var output = new float[512]; var listener = Listener;
            Assert.Throws<ArgumentOutOfRangeException>(() => render(input, output, Coefficients, 1, 1, 1, listener, 1, false, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => render(input, output, Coefficients, 1, 1, 1, listener, 1, false, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => render(input, output, Coefficients, 1, 1, 1, listener, 1, false, 2));
            for (int i = 0; i < 32; i++) render(input, output, Coefficients, 1, 1, 1, listener, 1, false, .5f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) render(input, output, Coefficients, 1, 1, 1, listener, 1, false, .5f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            render(input, output, Coefficients, 1, 1, 1, listener, 0, false, 0);
            Assert.That(output, Is.All.EqualTo(0));
        }
    }
}
