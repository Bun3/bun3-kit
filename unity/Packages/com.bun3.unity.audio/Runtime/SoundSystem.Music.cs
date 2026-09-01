// Music subsystem: two fixed channels (A/B), each an intro+loop AudioSource pair.
// Intro→loop handoff is sample-accurate via PlayScheduled on the DSP clock.
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        private const int MusicChannelCount = 2;
        private const double MusicScheduleHeadroom = 0.05;

        internal readonly MusicChannel[] MusicChannels = new MusicChannel[MusicChannelCount];
        internal readonly AudioSource[] MusicIntroSources = new AudioSource[MusicChannelCount];
        internal readonly AudioSource[] MusicLoopSources = new AudioSource[MusicChannelCount];

        /// <summary>Channel currently owning the foreground track; -1 when silent.</summary>
        internal int ActiveMusic { get; private set; } = -1;

        /// <summary>True while any music channel is audible (fading counts).</summary>
        public bool IsMusicPlaying => ActiveMusic >= 0;

        /// <summary>
        /// Plays a music track. A negative <paramref name="fade"/> uses the def's DefaultFade.
        /// When another track is playing, the fade crossfades the two (see Task 3);
        /// when silent, it fades the new track in (0 = instant).
        /// </summary>
        public void PlayMusic(MusicDef def, float fade = -1f)
        {
            if (_disposed)
            {
                throw new System.ObjectDisposedException(nameof(SoundSystem));
            }
            if (def == null || def.Loop == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.PlayMusic: def has no loop clip; ignored.");
#endif
                return;
            }
            if (fade < 0f)
            {
                fade = def.DefaultFade;
            }

            var channel = 0; // silent path: always channel 0. Task 3 picks the free channel.
            StartMusicOnChannel(channel, def, fade);
            ActiveMusic = channel;
        }

        /// <summary>Stops the current track, optionally fading out first.</summary>
        public void StopMusic(float fadeOut = 0f)
        {
            if (_disposed || ActiveMusic < 0)
            {
                return;
            }
            BeginMusicFadeOut(ActiveMusic, fadeOut);
            ActiveMusic = -1;
        }

        private void StartMusicOnChannel(int channel, MusicDef def, float fadeIn)
        {
            ref var ch = ref MusicChannels[channel];
            ch.State = fadeIn > 0f ? MusicState.FadingIn : MusicState.Playing;
            ch.Def = def;
            ch.Paused = false;
            ch.FadeElapsed = 0f;
            ch.FadeDuration = fadeIn;
            ch.FadeFrom = 0f;
            ch.FadeTo = 1f;
            ch.FadeFactor = fadeIn > 0f ? 0f : 1f;
            ch.Completion = null;

            var introSource = MusicIntroSources[channel];
            var loopSource = MusicLoopSources[channel];
            var startDsp = AudioSettings.dspTime + MusicScheduleHeadroom;

            loopSource.clip = def.Loop;
            loopSource.loop = true;
            if (def.Intro != null)
            {
                introSource.clip = def.Intro;
                introSource.PlayScheduled(startDsp);
                ch.LoopStartDsp = startDsp + MusicMath.ClipSeconds(def.Intro);
                loopSource.PlayScheduled(ch.LoopStartDsp);
                ch.LoopScheduled = true;
            }
            else
            {
                introSource.clip = null;
                loopSource.PlayScheduled(startDsp);
                ch.LoopStartDsp = startDsp;
                ch.LoopScheduled = true;
            }
            ApplyMusicVolume(channel);
        }

        private void BeginMusicFadeOut(int channel, float duration)
        {
            ref var ch = ref MusicChannels[channel];
            if (ch.State == MusicState.Idle)
            {
                return;
            }
            if (duration <= 0f)
            {
                SilenceMusicChannel(channel);
                return;
            }
            ch.FadeFrom = ch.FadeFactor;
            ch.FadeTo = 0f;
            ch.FadeElapsed = 0f;
            ch.FadeDuration = duration;
            ch.State = MusicState.FadingOut;
        }

        // Stops both sources and frees the channel. Does NOT signal Completion —
        // callers collect it first and fire signals last (two-phase discipline).
        private AutoResetUniTaskCompletionSource SilenceMusicChannel(int channel)
        {
            ref var ch = ref MusicChannels[channel];
            var completion = ch.Completion;
            MusicIntroSources[channel].Stop();
            MusicLoopSources[channel].Stop();
            ch.State = MusicState.Idle;
            ch.Def = null;
            ch.Paused = false;
            ch.LoopScheduled = false;
            ch.Completion = null;
            return completion;
        }

        private void ApplyMusicVolume(int channel)
        {
            ref var ch = ref MusicChannels[channel];
            var volume = (ch.Def != null ? ch.Def.Volume : 0f) * ch.FadeFactor;
            MusicIntroSources[channel].volume = volume;
            MusicLoopSources[channel].volume = volume;
        }

        internal void TickMusic(float dt)
        {
            // Phase 1: advance state; collect at most one signal per channel.
            AutoResetUniTaskCompletionSource signal0 = null;
            AutoResetUniTaskCompletionSource signal1 = null;
            for (var i = 0; i < MusicChannelCount; i++)
            {
                ref var ch = ref MusicChannels[i];
                if (ch.State == MusicState.Idle || ch.Paused)
                {
                    continue;
                }

                if (ch.FadeDuration > 0f)
                {
                    ch.FadeElapsed += dt;
                    var t = Mathf.Clamp01(ch.FadeElapsed / ch.FadeDuration);
                    ch.FadeFactor = Mathf.Lerp(ch.FadeFrom, ch.FadeTo, t);
                    if (t >= 1f)
                    {
                        ch.FadeDuration = 0f;
                        if (ch.State == MusicState.FadingOut)
                        {
                            var completion = SilenceMusicChannel(i);
                            if (i == 0) { signal0 = completion; } else { signal1 = completion; }
                            continue;
                        }
                        // Fade-in finished: signal the awaiter (PlayMusicAsync, Task 5).
                        ch.State = MusicState.Playing;
                        if (i == 0) { signal0 = ch.Completion; } else { signal1 = ch.Completion; }
                        ch.Completion = null;
                    }
                }
                ApplyMusicVolume(i);
            }

            // Phase 2: user signals last — continuations run inline and may re-enter.
            signal0?.TrySetResult();
            signal1?.TrySetResult();
        }
    }
}
