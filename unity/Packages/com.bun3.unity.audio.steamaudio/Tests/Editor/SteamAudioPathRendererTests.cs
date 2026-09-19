using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class SteamAudioPathRendererTests
    {
        private delegate void RenderFrame(float[] mono, float[] stereo, float[] coefficients,
            float low, float mid, float high, SA.CoordinateSpace3 listener, float gain, bool normalizeEq);

        private sealed class Renderer : IDisposable
        {
            private readonly IDisposable _instance;
            internal readonly RenderFrame Render;
            internal readonly Action Reset;

            internal Renderer(SA.Context context, SA.HRTF hrtf)
            {
                var type = RendererType();
                _instance = (IDisposable)Activator.CreateInstance(type, context, hrtf, 48000, 256, 1);
                Render = (RenderFrame)Delegate.CreateDelegate(typeof(RenderFrame), _instance, type.GetMethod("Render"));
                Reset = (Action)Delegate.CreateDelegate(typeof(Action), _instance, type.GetMethod("Reset"));
            }
            public void Dispose() => _instance.Dispose();
        }

        private static Type RendererType()
        {
            var type = Type.GetType("Bun3.Unity.Audio.SteamAudio.SteamAudioPathRenderer, Bun3.Unity.Audio.SteamAudio");
            Assert.That(type, Is.Not.Null, "The optional package must supply a path PCM renderer.");
            return type;
        }

        private static SA.CoordinateSpace3 Listener => new SA.CoordinateSpace3
        {
            right = new SA.Vector3 { x = 1 }, up = new SA.Vector3 { y = 1 }, ahead = new SA.Vector3 { z = -1 }
        };

        private static SA.HRTF CreateHrtf(SA.Context context) => new SA.HRTF(context,
            new SA.AudioSettings { samplingRate = 48000, frameSize = 256 }, null, null, 0, SA.HRTFNormType.None);

        private static double Energy(float[] samples)
        {
            double energy = 0;
            foreach (var sample in samples)
            {
                Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False);
                energy += sample * sample;
            }
            return energy;
        }

        [Test]
        public void PackageProvidesNativePathPcmRenderer()
        {
            RendererType();
        }

        [Test]
        public void NativePathEffectProducesFinitePcmAndDifferentDirections()
        {
            RendererType();
            var context = new SA.Context();
            var hrtf = CreateHrtf(context);
            try
            {
                using var renderer = new Renderer(context, hrtf);
                var mono = new float[256];
                for (int i = 0; i < mono.Length; i++) mono[i] = (float)Math.Sin(i * 0.17) * 0.1f;
                var left = new float[512];
                var right = new float[512];
                var leftCoefficients = new[] { 0.2820948f, 0.4886025f, 0f, 0f };
                var rightCoefficients = new[] { 0.2820948f, -0.4886025f, 0f, 0f };
                for (int i = 0; i < 8; i++) renderer.Render(mono, left, leftCoefficients, 1, 1, 1, Listener, 1, false);
                renderer.Reset();
                for (int i = 0; i < 8; i++) renderer.Render(mono, right, rightCoefficients, 1, 1, 1, Listener, 1, false);
                Assert.That(Energy(left), Is.GreaterThan(1e-8));
                Assert.That(Energy(right), Is.GreaterThan(1e-8));
                double difference = 0;
                for (int i = 0; i < left.Length; i++) difference += Math.Abs(left[i] - right[i]);
                TestContext.WriteLine("Direction sample difference: " + difference);
                Assert.That(difference, Is.GreaterThan(1e-5));
            }
            finally { hrtf.Release(); context.Release(); }
        }

        [Test]
        public void ResetRestoresImpulseResponseAndZeroGainWritesSilence()
        {
            RendererType();
            var context = new SA.Context();
            var hrtf = CreateHrtf(context);
            try
            {
                using var renderer = new Renderer(context, hrtf);
                var mono = new float[256]; mono[0] = 1;
                var first = new float[512]; var second = new float[512];
                var coefficients = new[] { 0.2820948f, 0.4886025f, 0f, 0f };
                renderer.Render(mono, first, coefficients, 1, 1, 1, Listener, 1, false);
                renderer.Reset();
                renderer.Render(mono, second, coefficients, 1, 1, 1, Listener, 1, false);
                Assert.That(Energy(first), Is.GreaterThan(1e-8));
                Assert.That(second, Is.EqualTo(first).Within(1e-6));
                renderer.Render(mono, second, coefficients, 1, 1, 1, Listener, 0, false);
                Assert.That(Energy(second), Is.Zero);
                renderer.Reset();
                Array.Clear(mono, 0, mono.Length);
                renderer.Render(mono, second, coefficients, 1, 1, 1, Listener, 1, false);
                Assert.That(Energy(second), Is.LessThan(1e-12), "Reset silence allows native floating-point filter noise.");
            }
            finally { hrtf.Release(); context.Release(); }
        }

        [Test]
        public void RetainedOwnershipSurvivesOriginalReleaseAndDisposeIsIdempotent()
        {
            RendererType();
            var context = new SA.Context();
            var hrtf = CreateHrtf(context);
            using var renderer = new Renderer(context, hrtf);
            hrtf.Release(); context.Release();
            var mono = new float[256]; mono[0] = 1;
            var stereo = new float[512];
            renderer.Render(mono, stereo, new[] { 0.2820948f, 0.4886025f, 0f, 0f }, 1, 1, 1, Listener, 1, false);
            Assert.That(Energy(stereo), Is.GreaterThan(1e-8));
            renderer.Dispose();
            Assert.DoesNotThrow(() => renderer.Dispose());
            Assert.Throws<ObjectDisposedException>(() => renderer.Reset());
        }

        [Test]
        public void WarmRenderDoesNotAllocateManagedMemory()
        {
            RendererType();
            var context = new SA.Context();
            var hrtf = CreateHrtf(context);
            try
            {
                using var renderer = new Renderer(context, hrtf);
                var mono = new float[256]; var stereo = new float[512];
                var coefficients = new[] { 0.2820948f, 0.4886025f, 0f, 0f };
                var listener = Listener;
                for (int i = 0; i < 32; i++) renderer.Render(mono, stereo, coefficients, 1, 1, 1, listener, 1, false);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) renderer.Render(mono, stereo, coefficients, 1, 1, 1, listener, 1, false);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);
            }
            finally { hrtf.Release(); context.Release(); }
        }

        [Test]
        public void ParameterLayoutIncludesTrailingNormalizeEqInNativeAbi()
        {
            var assembly = RendererType().Assembly;
            var type = assembly.GetType("Bun3.Unity.Audio.SteamAudio.NativePathAudio+Parameters");
            Assert.That(type, Is.Not.Null);
            Assert.That(Marshal.SizeOf(type), Is.EqualTo(IntPtr.Size == 8 ? 96 : 80));
            Assert.That(Marshal.OffsetOf(type, "ShCoefficients").ToInt32(), Is.EqualTo(IntPtr.Size == 8 ? 16 : 12));
            Assert.That(Marshal.OffsetOf(type, "NormalizeEq").ToInt32(), Is.EqualTo(IntPtr.Size == 8 ? 88 : 76));
        }

        [Test]
        public void InvalidBuffersAndCoordinatesAreRejectedBeforeNativeProcessing()
        {
            RendererType();
            var context = new SA.Context();
            var hrtf = CreateHrtf(context);
            try
            {
                using var renderer = new Renderer(context, hrtf);
                var mono = new float[256]; var stereo = new float[512];
                var coefficients = new[] { 0.2820948f, 0.4886025f, 0f, 0f };
                Assert.Throws<ArgumentException>(() => renderer.Render(new float[255], stereo, coefficients, 1, 1, 1, Listener, 1, false));
                Assert.Throws<ArgumentException>(() => renderer.Render(mono, new float[511], coefficients, 1, 1, 1, Listener, 1, false));
                Assert.Throws<ArgumentException>(() => renderer.Render(mono, stereo, new float[3], 1, 1, 1, Listener, 1, false));
                Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(mono, stereo, coefficients, float.NaN, 1, 1, Listener, 1, false));
                Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(mono, stereo, coefficients, 1, 1, 1, Listener, -1, false));
                Assert.Throws<ArgumentException>(() => renderer.Render(mono, stereo, coefficients, 1, 1, 1, default, 1, false));
                mono[0] = float.PositiveInfinity;
                Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(mono, stereo, coefficients, 1, 1, 1, Listener, 1, false));
                mono[0] = 1;
                renderer.Render(mono, stereo, coefficients, 1, 1, 1, Listener, 1, false);
                Assert.That(Energy(stereo), Is.GreaterThan(1e-8));
            }
            finally { hrtf.Release(); context.Release(); }
        }
    }
}
