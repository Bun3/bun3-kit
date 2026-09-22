using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class StereoPlaybackTap : MonoBehaviour
    {
        private float _difference;
        private float _amplitude;
        private long _frames;
        private long _blocks;
        private long _silentBlocks;
        public long Blocks => Interlocked.Read(ref _blocks);
        public long SilentBlocks => Interlocked.Read(ref _silentBlocks);
        public float PeakStereoDifference => Volatile.Read(ref _difference);
        public float PeakAmplitude => Volatile.Read(ref _amplitude);
        public long Frames => Interlocked.Read(ref _frames);
        public void ResetPeak() { Volatile.Write(ref _difference, 0); Volatile.Write(ref _amplitude, 0); }
        private void OnAudioFilterRead(float[] samples, int channels)
        {
            if (channels < 2) return;
            float difference = _difference;
            float amplitude = _amplitude;
            float blockPeak = 0;
            for (int i = 0; i < samples.Length; i++) blockPeak = Math.Max(blockPeak, Math.Abs(samples[i]));
            Interlocked.Increment(ref _blocks);
            if (blockPeak < 1e-5f) Interlocked.Increment(ref _silentBlocks);
            for (int i = 0; i < samples.Length; i++) amplitude = Math.Max(amplitude, Math.Abs(samples[i]));
            for (int i = 0; i < samples.Length; i += channels)
                difference = Math.Max(difference, Math.Abs(samples[i] - samples[i + 1]));
            Volatile.Write(ref _difference, difference);
            Volatile.Write(ref _amplitude, amplitude);
            Interlocked.Add(ref _frames, samples.Length / channels);
        }
    }

    public sealed class DissonanceSteamAudioPlaybackTests
    {
        static Type PlaybackType()
        {
            var type = Type.GetType("Bun3.Unity.Audio.Dissonance.SteamAudio.DissonanceSteamAudioPlayback, Bun3.Unity.Audio.Dissonance.SteamAudio");
            Assert.That(type, Is.Not.Null, "A pool-facing custom stereo streaming playback component is required.");
            return type;
        }

        [Test]
        public void PlaybackComponentImplementsSdkPoolContract()
        {
            var type = PlaybackType();
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(type), Is.True);
            Assert.That(typeof(IVoicePlaybackInternal).IsAssignableFrom(type), Is.True);
        }

        [UnityTest]
        public IEnumerator ActualDecodedSessionFeedsStereoStreamingClipAndDisableReclaimsWithoutMoreCallbacks() => Exercise(false, false);

        [UnityTest]
        public IEnumerator ForceResetAndIdentityReuseWaitForHeldDecoderRead() => Exercise(true, false);

        [UnityTest]
        public IEnumerator DestroyedFacadeKeepsDecoderAliveUntilHeldReaderIsReleased() => Exercise(true, true);

        [UnityTest]
        public IEnumerator CompletedShortSessionPlaysItsFinalBurstThroughTheSourceFilter() => Exercise(false, false, true);

        [UnityTest]
        public IEnumerator InitialPathPublicationDoesNotDiscardShortSpeech() => Exercise(false, false, true, false, true);

        [UnityTest]
        public IEnumerator PacedContinuousPacketsDoNotProduceSilentOutputBlocks() => Exercise(false, false, false, false, false, true);

        [UnityTest]
        public IEnumerator ImmediateGateSilencesQueuedStereoAfterTheSourceMonitor() => Exercise(false, false, false, true);

        [UnityTest]
        public IEnumerator NaturalSpeechRestartReusesNativeRendererAndDriver() => Exercise(false, false, true, reuse: true);

        [UnityTest]
        public IEnumerator ChannelSnapshotRemainsAvailableWhileDecoderReadIsHeld() => Exercise(true, false, metadata: true);

        static IEnumerator Exercise(bool overlapReset, bool destroyWhileReading, bool completeInput = false, bool testGate = false, bool delayedPath = false, bool paced = false, bool reuse = false, bool metadata = false)
        {
            var type = PlaybackType();
            var context = new SA.Context();
            var listenerObject = new GameObject("Native streaming test listener");
            listenerObject.AddComponent<AudioListener>();
            var go = new GameObject("Native streaming playback fixture");
            go.SetActive(false);
            var component = go.AddComponent(type);
            var tap = go.AddComponent<StereoPlaybackTap>();
            var playback = (IVoicePlaybackInternal)component;
            using var volume = new DissonancePathPlaybackTests.BlockingVolume();
            try
            {
                int factoryCalls = 0;
                Func<int, int, SteamAudioPathRenderer> factory = (rate, frame) =>
                {
                    factoryCalls++;
                    var hrtf = new SA.HRTF(context, new SA.AudioSettings { samplingRate = rate, frameSize = frame }, null, null, 0, SA.HRTFNormType.None);
                    try { return new SteamAudioPathRenderer(context, hrtf, rate, frame); }
                    finally { hrtf.Release(); }
                };
                var listener = new SA.CoordinateSpace3
                {
                    right = new SA.Vector3 { x = 1 }, up = new SA.Vector3 { y = 1 }, ahead = new SA.Vector3 { z = -1 }
                };
                type.GetMethod("Configure").Invoke(component, new object[] { factory, new[] { 0.2820948f, 0.4886025f, 0f, 0f }, listener });
                playback.Setup(null, volume);
                playback.PlayerName = "streaming-decoder-test";
                playback.CodecSettings = new CodecSettings(Codec.Identity, 480, 48000);
                playback.AllowPositionalPlayback = true;
                if (testGate || delayedPath)
                {
                    var startBlocked = type.GetProperty("StartBlocked");
                    Assert.That(startBlocked, Is.Not.Null, "Game-owned playback must be blocked before its first PCM callback when requested.");
                    startBlocked.SetValue(component, true);
                }
                go.SetActive(true);
                playback.StartPlayback();
                var samples = new float[480];
                var bytes = new byte[480 * sizeof(float)];
                var channels = new List<RemoteChannel>
                {
                    new RemoteChannel("native-test-room", ChannelType.Room, new PlaybackOptions(true, 1, ChannelPriority.Default))
                };
                double feedStart = Time.realtimeSinceStartupAsDouble;
                for (uint packet = 0; packet < (completeInput ? 4u : paced ? 10u : 30u); packet++)
                {
                    for (int i = 0; i < samples.Length; i++)
                        samples[i] = completeInput ? (packet == 3 && i < 240 ? 0.25f * (float)Math.Sin(i * 0.13) : 0) : 0.1f * (float)Math.Sin((packet * 480 + i) * 0.071);
                    Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                    playback.ReceiveAudioPacket(new VoicePacket(playback.PlayerName, ChannelPriority.Default, 1, true, new ArraySegment<byte>(bytes), packet, channels));
                }
                if (completeInput) playback.StopPlayback();
                var source = go.GetComponent<AudioSource>();
                float deadline = Time.realtimeSinceStartup + 5;
                var parameters = type.GetProperty("Parameters");
                while (parameters.GetValue(component) == null && Time.realtimeSinceStartup < deadline) yield return null;
                // Observe actual generated output after the generation's filter, not the silent driver clip.
                UnityEngine.Object.DestroyImmediate(tap);
                tap = go.AddComponent<StereoPlaybackTap>();
                if (delayedPath)
                {
                    var mailbox = (DissonancePathMailbox)parameters.GetValue(component);
                    Assert.That(mailbox, Is.Not.Null);
                    Assert.That(mailbox.TrySetBlocked(mailbox.Generation, false), Is.True);
                }
                // Decoded amplitude is published before native rendering completes on the audio thread.
                while (((float)type.GetProperty("PeakDecodedAmplitude").GetValue(component) <= 0 ||
                    (float)type.GetProperty("PeakStereoDifference").GetValue(component) <= 1e-6f) &&
                    Time.realtimeSinceStartup < deadline) yield return null;
                TestContext.WriteLine("Streaming state: active={0}, speaking={1}, playing={2}, callbacks={3}, largestCallback={4}, peakDecoded={5}, stereoDifference={6}, clipSamples={7}",
                    playback.IsActive, playback.IsSpeaking, source.isPlaying,
                    type.GetProperty("CallbackCount").GetValue(component), type.GetProperty("LargestCallbackSampleCount").GetValue(component),
                    type.GetProperty("PeakDecodedAmplitude").GetValue(component), type.GetProperty("PeakStereoDifference").GetValue(component),
                    source.clip != null ? source.clip.samples : 0);
                Assert.That(type.GetProperty("Fault").GetValue(component), Is.Null, "A renderer or decoder fault must not be mistaken for missing callbacks.");
                Assert.That((float)type.GetProperty("PeakDecodedAmplitude").GetValue(component), Is.GreaterThan(0), "Real Unity clip callbacks must consume SDK-decoded input.");
                Assert.That((float)type.GetProperty("PeakStereoDifference").GetValue(component), Is.GreaterThan(1e-6f), "Real streaming callbacks must process native directional stereo.");
                var firstClip = source.clip;
                var firstMonitor = type.GetField("_monitor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);
                var firstPump = type.GetField("_lastPlayback", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);
                if (completeInput)
                {
                    deadline = Time.realtimeSinceStartup + 2;
                    while (tap.PeakStereoDifference <= 1e-6f && Time.realtimeSinceStartup < deadline) yield return null;
                    TestContext.WriteLine("Downstream final burst: frames={0}, stereoDifference={1}, playing={2}", tap.Frames, tap.PeakStereoDifference, source.isPlaying);
                    Assert.That(tap.PeakStereoDifference, Is.GreaterThan(1e-6f),
                        "Decoder completion must not stop the source before its queued final burst reaches the downstream audio filter.");
                    deadline = Time.realtimeSinceStartup + 2;
                    while (source.isPlaying && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(source.isPlaying, Is.False, "Natural completion must release the drained source without an external reset.");
                    Assert.That(source.clip, Is.Null);
                    Assert.That(playback.IsSpeaking, Is.False);
                    Assert.That(playback.Priority, Is.EqualTo(ChannelPriority.None));
                    if (reuse)
                    {
                        playback.StartPlayback();
                        for (uint packet = 0; packet < 30; packet++)
                            playback.ReceiveAudioPacket(new VoicePacket(playback.PlayerName, ChannelPriority.Default, 1, true,
                                new ArraySegment<byte>(bytes), packet, channels));
                        deadline = Time.realtimeSinceStartup + 5;
                        while ((parameters.GetValue(component) == null || !source.isPlaying) && Time.realtimeSinceStartup < deadline) yield return null;
                        Assert.That(parameters.GetValue(component), Is.Not.Null);
                        Assert.That(factoryCalls, Is.EqualTo(1), "Natural speech restarts must retain the native context, HRTF and effect.");
                        Assert.That(source.clip, Is.SameAs(firstClip));
                        Assert.That(type.GetField("_monitor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component), Is.SameAs(firstMonitor));
                        var secondPump = type.GetField("_lastPlayback", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);
                        Assert.That(secondPump, Is.Not.SameAs(firstPump), "Generation admission must remain immutable.");
                        var pcm = firstPump.GetType().GetField("_stereo", BindingFlags.Instance | BindingFlags.NonPublic);
                        Assert.That(pcm.GetValue(secondPump), Is.SameAs(pcm.GetValue(firstPump)), "Retired PCM storage is reused only after reader quiescence.");
                        var late = new float[128];
                        ((DissonancePathPlayback)firstPump).ReadStereo(late);
                        Assert.That(late, Is.All.Zero, "A delayed retired generation must never read reused PCM.");
                    }
                    yield break;
                }
                Assert.That(source.isPlaying, Is.True);
                Assert.That(source.clip.channels, Is.EqualTo(2));
                Assert.That(source.spatialBlend, Is.Zero, "Native stereo must not receive Unity distance attenuation again.");
                Assert.That(source.spatialize, Is.False);
                if (paced)
                {
                    uint packet = 10;
                    long baselineBlocks = 0, baselineSilent = 0;
                    bool measuring = false;
                    var liveChannels = new List<RemoteChannel>();
                    while (Time.realtimeSinceStartupAsDouble < feedStart + 3)
                    {
                        uint target = (uint)((Time.realtimeSinceStartupAsDouble - feedStart) * 100) + 10;
                        while (packet < target)
                        {
                            for (int i = 0; i < samples.Length; i++) samples[i] = 0.1f * (float)Math.Sin((packet * 480 + i) * 0.071);
                            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                            playback.ReceiveAudioPacket(new VoicePacket(playback.PlayerName, ChannelPriority.Default, 1, true, new ArraySegment<byte>(bytes), packet++, channels));
                        }
                        playback.GetRemoteChannels(liveChannels);
                        if (!measuring && Time.realtimeSinceStartupAsDouble > feedStart + 1)
                        {
                            baselineBlocks = tap.Blocks; baselineSilent = tap.SilentBlocks; measuring = true;
                        }
                        yield return null;
                    }
                    long blocks = tap.Blocks - baselineBlocks, silent = tap.SilentBlocks - baselineSilent;
                    TestContext.WriteLine("Paced output: blocks={0}, silent={1}", blocks, silent);
                    Assert.That(blocks, Is.GreaterThan(20));
                    Assert.That(silent, Is.LessThanOrEqualTo(blocks / 20), "Continuous real-time input must not repeatedly underflow during playback.");
                    Assert.That(playback.IsSpeaking, Is.True);
                    yield break;
                }
                var reportedChannels = new List<RemoteChannel>();
                deadline = Time.realtimeSinceStartup + 5;
                do
                {
                    playback.GetRemoteChannels(reportedChannels);
                    if (reportedChannels.Count == 0) yield return null;
                } while (reportedChannels.Count == 0 && Time.realtimeSinceStartup < deadline);
                Assert.That(reportedChannels.Count, Is.EqualTo(1));
                Assert.That(reportedChannels[0].TargetName, Is.EqualTo("native-test-room"));
                playback.SetTransform(new Vector3(2, 3, 4), Quaternion.Euler(0, 30, 0));
                Assert.That(go.transform.position, Is.EqualTo(new Vector3(2, 3, 4)));

                if (testGate)
                {
                    var property = type.GetProperty("Parameters");
                    Assert.That(property, Is.Not.Null, "A configured facade must expose its active generation's gate.");
                    var mailbox = (DissonancePathMailbox)property.GetValue(component);
                    Assert.That(mailbox.IsBlocked, Is.True, "The initial gate must be applied before streaming clip creation.");
                    // This tap is added AFTER the generation's output monitor in Unity filter order.
                    var downstream = go.AddComponent<StereoPlaybackTap>();
                    long initialFrames = downstream.Frames;
                    deadline = Time.realtimeSinceStartup + 3;
                    while (downstream.Frames < initialFrames + 8192 && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(downstream.Frames, Is.GreaterThanOrEqualTo(initialFrames + 8192));
                    Assert.That(downstream.PeakStereoDifference, Is.Zero);
                    Assert.That(downstream.PeakAmplitude, Is.Zero);
                    Assert.That(mailbox.TrySetBlocked(mailbox.Generation, false), Is.True);
                    for (uint packet = 30; packet < 90; packet++)
                        playback.ReceiveAudioPacket(new VoicePacket(playback.PlayerName, ChannelPriority.Default, 1, true, new ArraySegment<byte>(bytes), packet, channels));
                    deadline = Time.realtimeSinceStartup + 3;
                    while (downstream.PeakStereoDifference <= 1e-6f && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(downstream.PeakStereoDifference, Is.GreaterThan(1e-6f));
                    Assert.That(mailbox.TrySetBlocked(mailbox.Generation, true), Is.True);
                    yield return null;
                    yield return null; // Exclude a filter invocation which began before the control write.
                    downstream.ResetPeak();
                    long frameStart = downstream.Frames;
                    deadline = Time.realtimeSinceStartup + 3;
                    while (downstream.Frames < frameStart + 8192 && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(downstream.Frames, Is.GreaterThanOrEqualTo(frameStart + 8192));
                    Assert.That(downstream.PeakStereoDifference, Is.Zero, "The final filter must gate already queued native stereo.");
                    Assert.That(downstream.PeakAmplitude, Is.Zero, "Silence must include both stereo channels, not only their difference.");
                    Assert.That(source.isPlaying, Is.True, "Blocked output must keep decoder callbacks alive.");
                    Assert.That(mailbox.TrySetBlocked(mailbox.Generation, false), Is.True);
                    for (uint packet = 90; packet < 150; packet++)
                        playback.ReceiveAudioPacket(new VoicePacket(playback.PlayerName, ChannelPriority.Default, 1, true, new ArraySegment<byte>(bytes), packet, channels));
                    deadline = Time.realtimeSinceStartup + 3;
                    while (downstream.PeakStereoDifference <= 1e-6f && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(downstream.PeakStereoDifference, Is.GreaterThan(1e-6f));
                    Assert.That(property.GetValue(component), Is.SameAs(mailbox), "Unblocking must preserve the active decoded session.");
                }

                if (overlapReset)
                {
                    // Stop Unity callbacks first, then hold the same real decoder read on a controlled worker.
                    source.Stop();
                    var decoder = (BaseVoicePlayback)type.GetField("_decoder", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);
                    var pump = decoder.GetType().GetProperty("Pump", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(decoder);
                    var read = (Action<float[]>)Delegate.CreateDelegate(typeof(Action<float[]>), pump, pump.GetType().GetMethod("ReadStereo"));
                    var oldClip = source.clip;
                    var oldMonitor = type.GetField("_monitor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);
                    var lateOutput = (Action<float[], int>)Delegate.CreateDelegate(typeof(Action<float[], int>), oldMonitor,
                        oldMonitor.GetType().GetMethod("OnAudioFilterRead", BindingFlags.Instance | BindingFlags.NonPublic));
                    var output = new float[512];
                    volume.Armed = true;
                    var worker = Task.Run(() =>
                    {
                        for (int i = 0; i < 16 && !volume.Entered.IsSet; i++) read(output);
                    });
                    try
                    {
                        deadline = Time.realtimeSinceStartup + 5;
                        while (!volume.Entered.IsSet && Time.realtimeSinceStartup < deadline) yield return null;
                        Assert.That(volume.Entered.IsSet, Is.True);
                        if (metadata)
                        {
                            playback.GetRemoteChannels(reportedChannels);
                            Assert.That(reportedChannels.Count, Is.EqualTo(1), "A held PCM read must not suppress the SDK's independent channel snapshot.");
                            Assert.That(reportedChannels[0].TargetName, Is.EqualTo("native-test-room"));
                        }
                        playback.ForceReset();
                        playback.PlayerName = "replacement-player";
                        playback.StartPlayback();
                        Assert.That(type.GetProperty("IsRetiring").GetValue(component), Is.True);
                        Assert.That(decoder.PlayerName, Is.EqualTo("streaming-decoder-test"), "SDK player reassignment must wait for the reader barrier.");
                        if (destroyWhileReading)
                        {
                            UnityEngine.Object.Destroy(go);
                            yield return null;
                            Assert.That(decoder != null, Is.True, "The independent decoder host must survive its destroyed owner while a reader is held.");
                        }
                    }
                    finally
                    {
                        volume.Armed = false;
                        volume.Continue.Set();
                        Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    }
                    Assert.That(output, Is.All.EqualTo(0));
                    yield return null;
                    yield return null;
                    if (destroyWhileReading)
                    {
                        Assert.That(decoder == null, Is.True);
                        yield break;
                    }
                    Assert.That(type.GetProperty("IsRetiring").GetValue(component), Is.False);
                    Assert.That(decoder.PlayerName, Is.EqualTo("replacement-player"));
                    Assert.That(type.GetProperty("DroppedCommands").GetValue(component), Is.GreaterThan(0));
                    playback.StartPlayback();
                    for (uint packet = 0; packet < 20; packet++)
                        playback.ReceiveAudioPacket(new VoicePacket(playback.PlayerName, ChannelPriority.Default, 1, true, new ArraySegment<byte>(bytes), packet, channels));
                    deadline = Time.realtimeSinceStartup + 5;
                    while ((source.clip == null || ReferenceEquals(source.clip, oldClip) ||
                        (float)type.GetProperty("PeakDecodedAmplitude").GetValue(component) <= 0) && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That((float)type.GetProperty("PeakDecodedAmplitude").GetValue(component), Is.GreaterThan(0));
                    Assert.That(source.clip, Is.Not.SameAs(oldClip));
                    Array.Fill(output, 1f);
                    read(output);
                    Assert.That(output, Is.All.EqualTo(0), "The old callback must not consume the replacement player's generation.");
                    source.Stop();
                    yield return null;
                    yield return null;
                    var replacementPump = decoder.GetType().GetProperty("Pump", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(decoder);
                    var delivered = pump.GetType().GetProperty("DeliveredFrames");
                    long replacementFrames = (long)delivered.GetValue(replacementPump);
                    long retiredFrames = (long)delivered.GetValue(pump);
                    lateOutput(output, 2);
                    Assert.That((long)delivered.GetValue(pump), Is.EqualTo(retiredFrames + output.Length / 2));
                    Assert.That((long)delivered.GetValue(replacementPump), Is.EqualTo(replacementFrames),
                        "An old source-filter callback must report only to its immutable retired generation.");
                }
                go.SetActive(false);
                Assert.That(source.isPlaying, Is.False);
                Assert.That(source.clip, Is.Null);
                deadline = Time.realtimeSinceStartup + 5;
                while ((bool)type.GetProperty("IsRetiring").GetValue(component) && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(type.GetProperty("IsRetiring").GetValue(component), Is.False);
                Assert.That(playback.IsSpeaking, Is.False);
                Assert.That(type.GetProperty("Fault").GetValue(component), Is.Null);
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
                UnityEngine.Object.Destroy(listenerObject);
                context.Release();
            }
            yield return null;
            yield return null;
            var decoderType = Type.GetType("Bun3.Unity.Audio.Dissonance.SteamAudio.DissonanceDecoderHost, Bun3.Unity.Audio.Dissonance.SteamAudio");
#if UNITY_6000_5_OR_NEWER
            Assert.That(UnityEngine.Object.FindObjectsByType(decoderType), Is.Empty,
#else
            Assert.That(UnityEngine.Object.FindObjectsByType(decoderType, FindObjectsSortMode.None), Is.Empty,
#endif
                "The detached decoder host must leave after its owner is destroyed and readers are quiescent.");
        }
    }
}
