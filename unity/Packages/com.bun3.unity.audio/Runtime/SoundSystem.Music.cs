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

        /// <summary>True while a foreground track is active (from PlayMusic until StopMusic is issued).</summary>
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
        /// <exception cref="System.ObjectDisposedException">The SoundSystem has been disposed.</exception>
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
                if (MusicChannels[0].State == MusicState.Idle)
                {
                    channel = 0;
                }
                else if (MusicChannels[1].State == MusicState.Idle)
                {
                    channel = 1; // preserve the other channel's fade tail (e.g. StopMusicAsync in flight)
                }
                else
                {
                    // Both channels still busy (e.g. crossfade cut short by StopMusic): steal
                    // channel 0 so any awaiter on it (StopMusicAsync) still resolves.
                    channel = 0;
                    stolen = SilenceMusicChannel(0);
                }
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

        /// <summary>
        /// Resumes paused music, rescheduling a cancelled loop from the intro's remaining time.
        /// The resumed intro restarts via UnPause at normal latency, so the intro→loop handoff
        /// on resume can be off by up to one audio buffer; only the initial handoff is sample-accurate.
        /// </summary>
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
                if (!ch.LoopScheduled && ch.Def != null)
                {
                    var intro = ch.Def.Intro;
                    var delay = intro != null
                        ? MusicMath.RemainingSeconds(MusicIntroSources[i].timeSamples, intro.samples, intro.frequency)
                        : MusicScheduleHeadroom;
                    ch.LoopStartDsp = AudioSettings.dspTime + delay;
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
            if (duration <= 0f || ch.Paused)
            {
                // A paused track is inaudible; fading it is meaningless — silence instantly so awaiters resolve.
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

        /// <summary>
        /// Plays a track and completes when the transition finishes (fade-in end; immediately
        /// when the effective fade is 0). Cancelling stops the music. Cancellation is a cold
        /// path and may allocate. On cancellation, stops whatever track is current at that
        /// moment — not necessarily the one this call started, if it was since replaced.
        /// </summary>
        public UniTask PlayMusicAsync(MusicDef def, float fade = -1f, System.Threading.CancellationToken ct = default)
        {
            if (def == null || def.Loop == null)
            {
                PlayMusic(def, fade); // keeps the dev-only warning path consistent
                return UniTask.CompletedTask;
            }
            PlayMusic(def, fade);
            ref var ch = ref MusicChannels[ActiveMusic];
            if (ch.State == MusicState.Playing)
            {
                return UniTask.CompletedTask; // zero-fade path: already done
            }
            ch.Completion ??= AutoResetUniTaskCompletionSource.Create();
            var task = ch.Completion.Task;
            return ct.CanBeCanceled ? WithMusicCancellation(task, ct) : task;
        }

        /// <summary>
        /// Fades the current track out and completes when it is silent. On cancellation,
        /// stops whatever track is current at that moment — not necessarily the one this
        /// call started, if it was since replaced.
        /// </summary>
        public UniTask StopMusicAsync(float fadeOut, System.Threading.CancellationToken ct = default)
        {
            if (_disposed || ActiveMusic < 0)
            {
                return UniTask.CompletedTask;
            }
            ref var ch = ref MusicChannels[ActiveMusic];
            ch.Completion ??= AutoResetUniTaskCompletionSource.Create();
            var task = ch.Completion.Task;
            StopMusic(fadeOut);
            return ct.CanBeCanceled ? WithMusicCancellation(task, ct) : task;
        }

        private async UniTask WithMusicCancellation(UniTask task, System.Threading.CancellationToken ct)
        {
            try
            {
                await task.AttachExternalCancellation(ct);
            }
            catch (System.OperationCanceledException)
            {
                StopMusic();
                throw;
            }
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
                        // Fade-in finished: signal the awaiter (PlayMusicAsync).
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
