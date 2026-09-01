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

        /// <summary>Whether the foreground track is paused.</summary>
        public bool IsMusicPaused
            => ActiveMusic >= 0 && MusicChannels[ActiveMusic].Paused;

        /// <summary>
        /// Plays a music track. A negative <paramref name="fade"/> uses the def's DefaultFade.
        /// Silent: fades the new track in on channel 0 (0 = instant). Playing: crossfades —
        /// the current track fades out while the new one fades in on the other channel.
        /// Mid-crossfade: newest wins — the fading-out channel is stolen (silenced instantly)
        /// and the new track starts there, while the previously active channel fades out.
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

            int channel;
            AutoResetUniTaskCompletionSource stolen = null;
            AutoResetUniTaskCompletionSource silenced = null;
            if (ActiveMusic < 0)
            {
                channel = 0;
            }
            else
            {
                var other = 1 - ActiveMusic;
                if (MusicChannels[other].State == MusicState.FadingOut)
                {
                    // Third request mid-crossfade: newest wins — cut the dying track now.
                    stolen = SilenceMusicChannel(other);
                }
                channel = other;
                silenced = BeginMusicFadeOut(ActiveMusic, fade);
            }
            StartMusicOnChannel(channel, def, fade);
            ActiveMusic = channel;
            stolen?.TrySetResult(); // last, after all state mutation (two-phase)
            silenced?.TrySetResult();
        }

        /// <summary>Stops the current track, optionally fading out first.</summary>
        public void StopMusic(float fadeOut = 0f)
        {
            if (_disposed || ActiveMusic < 0)
            {
                return;
            }
            var completion = BeginMusicFadeOut(ActiveMusic, fadeOut);
            ActiveMusic = -1;
            completion?.TrySetResult(); // last, after all state mutation (two-phase)
        }

        /// <summary>
        /// Pauses all music channels. A loop that is scheduled but not yet started is
        /// cancelled (the DSP clock keeps running while paused; a live schedule would
        /// fire mid-pause) and rescheduled from the intro's remaining time on resume.
        /// </summary>
        public void PauseMusic()
        {
            if (_disposed)
            {
                return;
            }
            for (var i = 0; i < MusicChannelCount; i++)
            {
                ref var ch = ref MusicChannels[i];
                if (ch.State == MusicState.Idle || ch.Paused)
                {
                    continue;
                }
                if (ch.LoopScheduled && AudioSettings.dspTime < ch.LoopStartDsp)
                {
                    MusicLoopSources[i].Stop();
                    ch.LoopScheduled = false;
                }
                MusicIntroSources[i].Pause();
                MusicLoopSources[i].Pause();
                ch.Paused = true;
            }
        }

        /// <summary>Resumes paused music, rescheduling a cancelled loop from the intro's remaining time.</summary>
        public void ResumeMusic()
        {
            if (_disposed)
            {
                return;
            }
            for (var i = 0; i < MusicChannelCount; i++)
            {
                ref var ch = ref MusicChannels[i];
                if (ch.State == MusicState.Idle || !ch.Paused)
                {
                    continue;
                }
                ch.Paused = false;
                MusicIntroSources[i].UnPause();
                MusicLoopSources[i].UnPause();
                if (!ch.LoopScheduled && ch.Def != null && ch.Def.Intro != null)
                {
                    var intro = ch.Def.Intro;
                    var remaining = MusicMath.RemainingSeconds(
                        MusicIntroSources[i].timeSamples, intro.samples, intro.frequency);
                    ch.LoopStartDsp = AudioSettings.dspTime + remaining;
                    MusicLoopSources[i].PlayScheduled(ch.LoopStartDsp);
                    ch.LoopScheduled = true;
                }
            }
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

        // Returns the collected Completion when the fade is instant (SilenceMusicChannel's
        // result); null when it starts a fade (TickMusic signals completion later).
        // Callers must TrySetResult() the return value last, after all state mutation (two-phase).
        private AutoResetUniTaskCompletionSource BeginMusicFadeOut(int channel, float duration)
        {
            ref var ch = ref MusicChannels[channel];
            if (ch.State == MusicState.Idle)
            {
                return null;
            }
            if (duration <= 0f)
            {
                return SilenceMusicChannel(channel);
            }
            ch.FadeFrom = ch.FadeFactor;
            ch.FadeTo = 0f;
            ch.FadeElapsed = 0f;
            ch.FadeDuration = duration;
            ch.State = MusicState.FadingOut;
            return null;
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
