using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Unity.Audio.SteamAudio;
using global::Dissonance;
using global::Dissonance.Audio.Codecs;
using global::Dissonance.Audio.Playback;
using global::Dissonance.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.Dissonance.SteamAudio.Tests
{
    public sealed class TestSessionDecoder : BaseVoicePlayback
    {
        public SpeechSession? Dequeue() => TryDequeueSession(48000);
        protected override SpeechSession? TryGetActiveSession() => null;
        public override float Amplitude => 0;
    }

    public sealed class DissonancePathPlaybackTests
    {
        internal sealed class BlockingVolume : IVolumeProvider, IDisposable
        {
            internal readonly ManualResetEventSlim Entered = new(false);
            internal readonly ManualResetEventSlim Continue = new(false);
            internal volatile bool Armed;
            public float TargetVolume
            {
                get
                {
                    if (Armed)
                    {
                        Entered.Set();
                        if (!Continue.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test reader was not released.");
                    }
                    return 1;
                }
            }
            public void Dispose() { Entered.Dispose(); Continue.Dispose(); }
        }
        static Type PumpType()
        {
            var type = Type.GetType("Bun3.Unity.Audio.Dissonance.SteamAudio.DissonancePathPlayback, Bun3.Unity.Audio.Dissonance.SteamAudio");
            Assert.That(type, Is.Not.Null, "A retirement-safe real SDK session to native stereo pump is required.");
            return type;
        }

        static SA.CoordinateSpace3 Listener => new SA.CoordinateSpace3
        {
            right = new SA.Vector3 { x = 1 }, up = new SA.Vector3 { y = 1 }, ahead = new SA.Vector3 { z = -1 }
        };

        [Test]
        public void ProvidesSessionPump() => PumpType();

        [UnityTest]
        public IEnumerator RealSdkDecodedPacketsRenderStereoThenCompleteToSilence() => ExerciseSession(false);

        [UnityTest]
        public IEnumerator RetirementBeforeFirstCallbackReclaimsWithoutWaitingForAnotherCallback() => ExerciseSession(true);

        [UnityTest]
        public IEnumerator RetirementCannotReclaimWhileActualSdkReadIsInProgress() => ExerciseSession(false, true);

        [UnityTest]
        public IEnumerator LiveParametersChangeNativeOutputWithoutReplacingTheDecodedSession() => ExerciseSession(false, false, true);

        [UnityTest]
        public IEnumerator MetadataSnapshotClaimDoesNotMuteCachedPcm() => ExerciseSession(false, metadata: true);

        [UnityTest]
        public IEnumerator BoundMonitorWaitsForSourceStartBeforeReadingDecoder() => ExerciseSession(false, monitorStart: true);

        static IEnumerator ExerciseSession(bool retireBeforeRead, bool overlapRetirement = false, bool liveParameters = false, bool metadata = false, bool monitorStart = false)
        {
            var type = PumpType();
            var go = new GameObject("Decoded speech pump test");
            var decoder = go.AddComponent<TestSessionDecoder>();
            var playback = (IVoicePlaybackInternal)decoder;
            using var volume = new BlockingVolume();
            playback.Setup(null, volume);
            var context = new SA.Context();
            var hrtf = new SA.HRTF(context, new SA.AudioSettings { samplingRate = 48000, frameSize = 256 }, null, null, 0, SA.HRTFNormType.None);
            var renderer = new SteamAudioPathRenderer(context, hrtf, 48000, 256);
            object pump = null;
            try
            {
                playback.PlayerName = "generated-identity-voice";
                playback.CodecSettings = new CodecSettings(Codec.Identity, 480, 48000);
                playback.StartPlayback();
                var mono = new float[480];
                var bytes = new byte[480 * sizeof(float)];
                for (uint packet = 0; packet < 12; packet++)
                {
                    for (int i = 0; i < mono.Length; i++) mono[i] = 0.1f * (float)Math.Sin((packet * 480 + i) * 0.071);
                    Buffer.BlockCopy(mono, 0, bytes, 0, bytes.Length);
                    playback.ReceiveAudioPacket(new VoicePacket(playback.PlayerName, ChannelPriority.Default, 1, true,
                        new ArraySegment<byte>(bytes), packet));
                }
                playback.StopPlayback();
                SpeechSession? session = null;
                float deadline = Time.realtimeSinceStartup + 5;
                while (!session.HasValue && Time.realtimeSinceStartup < deadline)
                {
                    session = decoder.Dequeue();
                    if (!session.HasValue) yield return null;
                }
                Assert.That(session.HasValue, Is.True, "The actual SDK activation queue must produce a prepared decoder session.");
                pump = Activator.CreateInstance(type, session.Value, renderer, 1L,
                    new[] { 0.2820948f, 0.4886025f, 0f, 0f }, Listener);
                var read = (Action<float[]>)Delegate.CreateDelegate(typeof(Action<float[]>), pump, type.GetMethod("ReadStereo"));
                var output = new float[384]; // Callback size deliberately differs from the native frame size.
                if (monitorStart)
                {
                    var monitorType = type.Assembly.GetType("Bun3.Unity.Audio.Dissonance.SteamAudio.DissonanceOutputMonitor");
                    var monitor = go.AddComponent(monitorType);
                    monitorType.GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(monitor, new[] { pump });
                    var callback = (Action<float[], int>)Delegate.CreateDelegate(typeof(Action<float[], int>), monitor,
                        monitorType.GetMethod("OnAudioFilterRead", BindingFlags.Instance | BindingFlags.NonPublic));
                    callback(output, 2);
                    Assert.That(((DissonancePathPlayback)pump).ReadCalls, Is.Zero,
                        "Binding before initial acoustic publication must not admit a stopped-source callback.");
                    Assert.That(output, Is.All.Zero);
                    yield break;
                }
                if (metadata)
                {
                    output = new float[128];
                    read(output);
                    var claim = type.GetMethod("TryClaimMetadata", BindingFlags.Instance | BindingFlags.NonPublic);
                    var release = type.GetMethod("ReleaseMetadata", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(claim.Invoke(pump, null), Is.True);
                    try
                    {
                        read(output);
                        double metadataEnergy = 0;
                        for (int i = 0; i < output.Length; i++) metadataEnergy += output[i] * output[i];
                        Assert.That(metadataEnergy, Is.GreaterThan(1e-8), "Reading channel metadata must never mute cached PCM.");
                        ((DissonancePathPlayback)pump).RequestRetirement();
                        Assert.That(((DissonancePathPlayback)pump).TryDisposeRetired(), Is.False,
                            "Resource reuse must wait for a held metadata snapshot as well as PCM readers.");
                    }
                    finally { release.Invoke(pump, null); }
                    Assert.That(((DissonancePathPlayback)pump).TryDisposeRetired(), Is.True);
                    Assert.That(claim.Invoke(pump, null), Is.False, "A retired snapshot must not claim storage reused by a later generation.");
                    yield break;
                }
                if (liveParameters)
                {
                    var parameters = type.GetProperty("Parameters");
                    Assert.That(parameters, Is.Not.Null, "The active pump must expose a generation-bound live path mailbox.");
                    var mailbox = (DissonancePathMailbox)parameters.GetValue(pump);
                    var coefficients = new[] { 0.2820948f, 0.4886025f, 0f, 0f };
                    Assert.That(mailbox.TryPublish(1, coefficients, new PathPlaybackSettings(Listener, gain: 0)), Is.True);
                    output = new float[512];
                    for (int n = 0; n < 4; n++) read(output);
                    Assert.That(output, Is.All.EqualTo(0), "Live gain zero must apply after native rendering while decoder state advances.");
                    Assert.That((float)type.GetProperty("PeakDecodedAmplitude").GetValue(pump), Is.GreaterThan(0));
                    var movedListener = Listener;
                    movedListener.origin.x = 5;
                    movedListener.right.x = -1;
                    movedListener.ahead.z = 1;
                    coefficients[1] = -coefficients[1];
                    Assert.That(mailbox.TryPublish(1, coefficients, new PathPlaybackSettings(movedListener, .5f, .7f, .9f, 1, true, spatialBlend: 0)), Is.True);
                    double updatedEnergy = 0;
                    for (int n = 0; n < 5; n++)
                    {
                        read(output);
                        for (int i = 0; i < output.Length; i++) updatedEnergy += output[i] * output[i];
                    }
                    Assert.That(updatedEnergy, Is.GreaterThan(1e-8));
                    for (int i = 0; i < output.Length; i += 2)
                        Assert.That(output[i], Is.EqualTo(output[i + 1]), "Near-field decoded speech must reach both ears identically after its width ramp.");
                    Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                        UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                        "GC allocation recorder must detect a known allocation before measuring this path.");
                    Assert.That(() =>
                    {
                        for (int n = 0; n < 4; n++) read(output);
                    }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                        "Warm live native decoding/rendering must not allocate on its processing owner.");

                    Assert.That(type.GetProperty("Generation").GetValue(pump), Is.EqualTo(1L));
                    Assert.That(type.GetProperty("Fault").GetValue(pump), Is.Null);
                    yield break;
                }
                if (overlapRetirement)
                {
                    volume.Armed = true;
                    var worker = Task.Run(() => read(output));
                    try
                    {
                        float readDeadline = Time.realtimeSinceStartup + 5;
                        while (!volume.Entered.IsSet && Time.realtimeSinceStartup < readDeadline) yield return null;
                        Assert.That(volume.Entered.IsSet, Is.True, "The worker must hold a real decoder read before retirement.");
                        type.GetMethod("RequestRetirement").Invoke(pump, null);
                        Assert.That(type.GetMethod("TryDisposeRetired").Invoke(pump, null), Is.False);
                    }
                    finally
                    {
                        volume.Continue.Set();
                        Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    }
                    Assert.That(output, Is.All.EqualTo(0), "Retirement during an in-flight callback must clear its output.");
                    Assert.That(type.GetMethod("TryDisposeRetired").Invoke(pump, null), Is.True);
                    Assert.That(type.GetProperty("Fault").GetValue(pump), Is.Null);
                    yield break;
                }
                if (retireBeforeRead)
                {
                    type.GetMethod("RequestRetirement").Invoke(pump, null);
                    Assert.That(type.GetMethod("TryDisposeRetired").Invoke(pump, null), Is.True);
                    Array.Fill(output, 1f);
                    read(output);
                    Assert.That(output, Is.All.EqualTo(0), "A callback delayed before its claim cannot enter a retired generation.");
                    Assert.That(type.GetMethod("TryDisposeRetired").Invoke(pump, null), Is.True);
                    yield break;
                }
                double energy = 0;
                double stereoDifference = 0;
                for (int n = 0; n < 80; n++)
                {
                    read(output);
                    for (int i = 0; i < output.Length; i += 2)
                    {
                        Assert.That(float.IsNaN(output[i]) || float.IsInfinity(output[i]), Is.False);
                        energy += output[i] * output[i] + output[i + 1] * output[i + 1];
                        stereoDifference += Math.Abs(output[i] - output[i + 1]);
                    }
                }
                Assert.That(energy, Is.GreaterThan(1e-8));
                Assert.That(stereoDifference, Is.GreaterThan(1e-6));
                Assert.That(type.GetProperty("IsComplete").GetValue(pump), Is.True);
                read(output);
                Assert.That(output, Is.All.EqualTo(0));
                type.GetMethod("RequestRetirement").Invoke(pump, null);
                Assert.That(type.GetMethod("TryDisposeRetired").Invoke(pump, null), Is.True);
                read(output);
                Assert.That(output, Is.All.EqualTo(0), "A delayed callback cannot revive a reclaimed generation.");
            }
            finally
            {
                if (pump != null)
                {
                    type.GetMethod("RequestRetirement").Invoke(pump, null);
                    type.GetMethod("TryDisposeRetired").Invoke(pump, null);
                }
                renderer.Dispose();
                hrtf.Release(); context.Release();
                UnityEngine.Object.Destroy(go);
            }
        }
    }
}
