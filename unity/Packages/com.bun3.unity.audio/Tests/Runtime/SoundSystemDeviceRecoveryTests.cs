using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemDeviceRecoveryTests
    {
        private const int Frequency = 44100;
        private const int Tolerance = Frequency / 10;
        private readonly List<Object> _created = new();

        private sealed class StartedOutput : ISoundVoiceOutput
        {
            internal int Retires;
            public bool IsComplete => false;
            public VoiceOutputStartResult TryStart(SoundDef definition, AudioClip selectedClip, float logicalPitch)
                => VoiceOutputStartResult.Started;
            public void SetPitch(float logicalPitch) { }
            public void Retire() => Retires++;
            public void Dispose() { }
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        private AudioClip Clip(float seconds)
        {
            var clip = AudioClip.Create("recovery", (int)(seconds * Frequency), 1, Frequency, false);
            _created.Add(clip);
            return clip;
        }

        private SoundDef Sfx(bool loop)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { Clip(1f) };
            def.Loop = loop;
            _created.Add(def);
            return def;
        }

        private MusicDef Music(float introSeconds, float loopSeconds)
        {
            var def = ScriptableObject.CreateInstance<MusicDef>();
            def.Intro = introSeconds > 0f ? Clip(introSeconds) : null;
            def.Loop = Clip(loopSeconds);
            _created.Add(def);
            return def;
        }

        [Test]
        public void LostLoopVoice_IsCompletedAndSlotReusable()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            var def = Sfx(loop: true);
            var handle = sys.Play(def);
            var completions = 0;
            sys.SetCompletionCallback(handle, _ => completions++);
            sys.SourceForTest(0).Stop();

            sys.RecoverLostPlayback();

            Assert.That(sys.Table.Slots[0].State, Is.EqualTo(VoiceState.Idle));
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(sys.Play(def).IsValid, Is.True);
        }

        [Test]
        public void PlayingVoice_IsUntouched()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            var handle = sys.Play(Sfx(loop: true));

            sys.RecoverLostPlayback();

            Assert.That(handle.IsValid, Is.True);
            Assert.That(sys.SourceForTest(0).isPlaying, Is.True);
        }

        [Test]
        public void LostExternalOutputVoice_IsCompleted()
        {
            var config = new SoundSystemConfig { SfxVoices = 1, OcclusionChecksPerFrame = 0 };
            StartedOutput output = null;
            config.CreateVoiceOutput = _ => output = new StartedOutput();
            using var sys = new SoundSystem(config);
            var handle = sys.Play(Sfx(loop: false));
            sys.Tick(10f);
            Assume.That(handle.IsValid, Is.True, "an external-completion voice outlives its clip clock");
            sys.SourceForTest(0).Stop();

            sys.RecoverLostPlayback();

            Assert.That(sys.Table.Slots[0].State, Is.EqualTo(VoiceState.Idle));
            Assert.That(output.Retires, Is.EqualTo(1));
        }

        [Test]
        public void ListenerPausedVoices_AreNotCompletedOnRecovery()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            var handle = sys.Play(Sfx(loop: true));
            sys.SourceForTest(0).Stop();
            AudioListener.pause = true;
            try
            {
                sys.RecoverLostPlayback();

                Assert.That(handle.IsValid, Is.True);
            }
            finally
            {
                AudioListener.pause = false;
            }
        }

        [UnityTest]
        public IEnumerator VoiceLostWhileListenerPaused_CompletesOnFirstUnpausedTick()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var lost = sys.Play(Sfx(loop: true));
            var kept = sys.Play(Sfx(loop: true));
            var completions = 0;
            sys.SetCompletionCallback(lost, _ => completions++);
            yield return null;
            AudioListener.pause = true;
            try
            {
                yield return null;
                sys.SourceForTest(lost.SlotIndex).Stop();
                sys.RecoverLostPlayback();
                yield return null;
                Assert.That(lost.IsValid, Is.True, "voices are left alone while the listener is paused");

                AudioListener.pause = false;
                sys.Tick(0.016f);

                Assert.That(sys.Table.Slots[lost.SlotIndex].State, Is.EqualTo(VoiceState.Idle));
                Assert.That(completions, Is.EqualTo(1));
                Assert.That(kept.IsValid, Is.True, "a voice the listener only paused is not lost");
                sys.Tick(0.016f);
                Assert.That(completions, Is.EqualTo(1));
            }
            finally
            {
                AudioListener.pause = false;
            }
        }

        [UnityTest]
        public IEnumerator LostMusicLoop_ResumesNearCachedPosition()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            sys.PlayMusic(Music(0f, 10f), 0f);
            var ch = sys.ActiveMusic;
            var loop = sys.MusicLoopSources[ch];
            yield return new WaitForSecondsRealtime(0.3f);
            var cached = sys.MusicChannels[ch].PlayheadSamples;
            Assume.That(cached, Is.GreaterThan(0));
            loop.Stop();

            sys.RecoverLostPlayback();

            Assert.That(loop.isPlaying, Is.True);
            Assert.That(loop.timeSamples, Is.InRange(cached, cached + Tolerance));
            Assert.That(sys.MusicChannels[ch].State, Is.EqualTo(MusicState.FadingIn));
            Assert.That(sys.MusicChannels[ch].Fade.Factor, Is.LessThan(1f));
        }

        [UnityTest]
        public IEnumerator LostIntro_ReschedulesLoopFromRemainingIntro()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            sys.PlayMusic(Music(2f, 2f), 0f);
            var ch = sys.ActiveMusic;
            var intro = sys.MusicIntroSources[ch];
            yield return new WaitForSecondsRealtime(0.3f);
            var cached = sys.MusicChannels[ch].PlayheadSamples;
            Assume.That(sys.MusicChannels[ch].InIntro, Is.True);
            intro.Stop();
            sys.MusicLoopSources[ch].Stop();

            sys.RecoverLostPlayback();

            Assert.That(intro.isPlaying, Is.True);
            Assert.That(intro.timeSamples, Is.InRange(cached, cached + Tolerance));
            Assert.That(sys.MusicChannels[ch].LoopScheduled, Is.True);
            var remaining = (double)(intro.clip.samples - cached) / Frequency;
            Assert.That(sys.MusicChannels[ch].LoopStartDsp - AudioSettings.dspTime, Is.EqualTo(remaining).Within(0.1));
        }

        [UnityTest]
        public IEnumerator PausedMusic_StaysPausedAndRecoversOnResume()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            sys.PlayMusic(Music(0f, 10f), 0f);
            var ch = sys.ActiveMusic;
            var loop = sys.MusicLoopSources[ch];
            yield return new WaitForSecondsRealtime(0.3f);
            var cached = sys.MusicChannels[ch].PlayheadSamples;
            sys.PauseMusic();
            loop.Stop();

            sys.RecoverLostPlayback();

            Assert.That(sys.IsMusicPaused, Is.True);
            Assert.That(loop.isPlaying, Is.False);

            sys.ResumeMusic();

            Assert.That(loop.isPlaying, Is.True);
            Assert.That(loop.timeSamples, Is.InRange(cached, cached + Tolerance));
        }

        [UnityTest]
        public IEnumerator PlayingMusic_IsUntouched()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            sys.PlayMusic(Music(0f, 10f), 0f);
            var ch = sys.ActiveMusic;
            yield return new WaitForSecondsRealtime(0.3f);
            var cached = sys.MusicChannels[ch].PlayheadSamples;
            Assume.That(cached, Is.GreaterThan(0));

            sys.RecoverLostPlayback();

            Assert.That(sys.MusicChannels[ch].State, Is.EqualTo(MusicState.Playing));
            Assert.That(sys.MusicLoopSources[ch].timeSamples, Is.GreaterThanOrEqualTo(cached));
        }

        [UnityTest]
        public IEnumerator CrossfadeDuringLoss_SilencesOutgoingAndResumesIncoming()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            sys.PlayMusic(Music(0f, 10f), 0f);
            var outgoing = sys.ActiveMusic;
            yield return new WaitForSecondsRealtime(0.3f);
            sys.PlayMusic(Music(0f, 10f), 1f);
            var incoming = sys.ActiveMusic;
            yield return new WaitForSecondsRealtime(0.2f);
            Assume.That(sys.MusicChannels[outgoing].State, Is.EqualTo(MusicState.FadingOut));
            sys.MusicLoopSources[outgoing].Stop();
            sys.MusicLoopSources[incoming].Stop();

            sys.RecoverLostPlayback();

            Assert.That(sys.MusicChannels[outgoing].State, Is.EqualTo(MusicState.Idle));
            Assert.That(sys.MusicChannels[incoming].State, Is.EqualTo(MusicState.FadingIn));
            Assert.That(sys.MusicLoopSources[incoming].isPlaying, Is.True);
            Assert.That(sys.ActiveMusic, Is.EqualTo(incoming));
        }

        [Test]
        public void MusicPlayheadCache_DoesNotAllocate()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            sys.PlayMusic(Music(2f, 2f), 0f);
            sys.Tick(0.016f);

            Assert.That(() => System.GC.KeepAlive(new byte[1024]), Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() => sys.Tick(0.016f), Is.Not.AllocatingGCMemory());
        }

        [UnityTest]
        public IEnumerator RealAudioReset_ResumesMusicAndFreesLostVoices()
        {
#if UNITY_EDITOR
            // Reset discards the data of AudioClip.Create clips; imported clips survive it.
            var loopClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Packages/com.bun3.unity.audio/Tests/Runtime/DeviceRecoveryLoop.wav");
#else
            AudioClip loopClip = null;
            Assert.Ignore("Needs an imported clip asset; Editor only.");
#endif
            Assert.That(loopClip, Is.Not.Null);
            var music = ScriptableObject.CreateInstance<MusicDef>();
            music.Loop = loopClip;
            _created.Add(music);
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            sys.PlayMusic(music, 0f);
            var ch = sys.ActiveMusic;
            sys.Play(Sfx(loop: true));
            yield return new WaitForSecondsRealtime(0.3f);

            AudioSettings.Reset(AudioSettings.GetConfiguration());
            yield return null;
            yield return null;

            Assert.That(sys.MusicLoopSources[ch].isPlaying, Is.True);
            Assert.That(sys.MusicLoopSources[ch].timeSamples, Is.GreaterThanOrEqualTo((int)(0.2f * loopClip.frequency)));
            Assert.That(sys.Table.Slots[0].State, Is.EqualTo(VoiceState.Idle));
        }

        [UnityTest]
        public IEnumerator RealAudioReset_SilencesMusicWhoseScriptClipLostItsData()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            sys.PlayMusic(Music(0f, 10f), 0f);
            var ch = sys.ActiveMusic;
            var completions = 0;
            sys.SetCompletionCallback(sys.Play(Sfx(loop: true)), _ => completions++);
            yield return new WaitForSecondsRealtime(0.3f);
            var errors = 0;
            Application.LogCallback onLog = (_, _, type) => { if (type is LogType.Exception or LogType.Error) errors++; };
            Application.logMessageReceived += onLog;
            try
            {
                AudioSettings.Reset(AudioSettings.GetConfiguration());
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
            yield return null;

            Assert.That(errors, Is.Zero);
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(sys.MusicChannels[ch].State, Is.EqualTo(MusicState.Idle));
            Assert.That(sys.IsMusicPlaying, Is.False);
        }
    }
}
