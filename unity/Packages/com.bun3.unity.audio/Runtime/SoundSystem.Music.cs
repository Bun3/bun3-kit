// Music subsystem: two fixed channels (A/B), each an intro+loop AudioSource pair.
// Intro→loop handoff is sample-accurate via PlayScheduled on the DSP clock.
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;

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
            PlayMusicCore(def, null, 0f, def.Intro, def.Loop, _config.MusicGroup, fade);
        }

        /// <summary>
        /// Plays a sound definition on the music channels with the same crossfade rules as the
        /// <see cref="MusicDef"/> overload, so catalog entries can serve as music. One clip is
        /// picked per call and loops until stopped or replaced. Volume is the rolled Volume times
        /// the live group gain of its VolumeGroup; MixerGroup falls back to the music group.
        /// Pitch, Loop, concurrency, spatial and occlusion settings do not apply.
        /// </summary>
        /// <param name="def">Definition whose clips play as music.</param>
        /// <param name="fade">Fade-in (and crossfade) seconds; 0 or negative is instant.</param>
        /// <exception cref="System.ObjectDisposedException">The SoundSystem has been disposed.</exception>
        public void PlayMusic(SoundDef def, float fade = 0f)
        {
            if (_disposed)
            {
                throw new System.ObjectDisposedException(nameof(SoundSystem));
            }
            if (def == null || def.EffectiveClips == null || def.EffectiveClips.Length == 0)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.PlayMusic: def has no loaded clips; ignored.");
#endif
                return;
            }
            var clip = PickClip(def);
            if (clip == null)
            {
                return;
            }
            var group = def.EffectiveMixerGroup != null ? def.EffectiveMixerGroup : _config.MusicGroup;
            PlayMusicCore(null, def, def.EffectiveVolume.Roll(_rng), null, clip, group, Mathf.Max(0f, fade));
        }

        private void PlayMusicCore(
            MusicDef music, SoundDef sound, float soundVolume, AudioClip intro, AudioClip loop,
            AudioMixerGroup group, float fade)
        {
            var channel = SelectMusicChannel(
                ActiveMusic, MusicChannels[0].State, MusicChannels[1].State);
            AutoResetUniTaskCompletionSource stolen = null;
            AutoResetUniTaskCompletionSource silenced = null;
            if (ActiveMusic < 0)
            {
                if (MusicChannels[channel].State != MusicState.Idle)
                {
                    stolen = SilenceMusicChannel(channel);
                }
            }
            else
            {
                if (MusicChannels[channel].State == MusicState.FadingOut)
                {
                    stolen = SilenceMusicChannel(channel);
                }
                silenced = BeginMusicFadeOut(ActiveMusic, fade);
            }
            ref var track = ref MusicChannels[channel];
            track.Def = music;
            track.Sound = sound;
            track.SoundVolume = soundVolume;
            StartMusicOnChannel(channel, intro, loop, group, fade);
            ActiveMusic = channel;
            stolen?.TrySetResult(); // last, after all state mutation (two-phase)
            silenced?.TrySetResult();
        }

        private static int SelectMusicChannel(int activeChannel, MusicState first, MusicState second)
        {
            if (activeChannel >= 0)
            {
                return 1 - activeChannel;
            }
            // Preserve a stopped track's fade tail when the other channel is free.
            if (first == MusicState.Idle)
            {
                return 0;
            }
            return second == MusicState.Idle ? 1 : 0;
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
                ref var track = ref MusicChannels[i];
                if (track.State == MusicState.Idle || track.Paused)
                {
                    continue;
                }
                if (track.LoopScheduled && AudioSettings.dspTime < track.LoopStartDsp)
                {
                    MusicLoopSources[i].Stop();
                    track.LoopScheduled = false;
                }
                MusicIntroSources[i].Pause();
                MusicLoopSources[i].Pause();
                track.Paused = true;
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
                ref var track = ref MusicChannels[i];
                if (track.State == MusicState.Idle || !track.Paused)
                {
                    continue;
                }
                track.Paused = false;
                MusicIntroSources[i].UnPause();
                MusicLoopSources[i].UnPause();
                if (!track.LoopScheduled)
                {
                    var intro = track.Def != null ? track.Def.Intro : null;
                    var delay = intro != null
                        ? MusicMath.RemainingSeconds(MusicIntroSources[i].timeSamples, intro.samples, intro.frequency)
                        : MusicScheduleHeadroom;
                    track.LoopStartDsp = AudioSettings.dspTime + delay;
                    MusicLoopSources[i].PlayScheduled(track.LoopStartDsp);
                    track.LoopScheduled = true;
                }
            }
        }

        private void StartMusicOnChannel(int channel, AudioClip intro, AudioClip loop, AudioMixerGroup group, float fadeIn)
        {
            ref var track = ref MusicChannels[channel];
            track.State = fadeIn > 0f ? MusicState.FadingIn : MusicState.Playing;
            track.Paused = false;
            if (fadeIn > 0f)
            {
                track.Fade.Begin(0f, 1f, fadeIn);
            }
            else
            {
                track.Fade.SetInstant(1f);
            }
            track.Completion = null;

            var introSource = MusicIntroSources[channel];
            var loopSource = MusicLoopSources[channel];
            var startDsp = AudioSettings.dspTime + MusicScheduleHeadroom;

            introSource.outputAudioMixerGroup = group;
            loopSource.outputAudioMixerGroup = group;
            loopSource.clip = loop;
            loopSource.loop = true;
            if (intro != null)
            {
                introSource.clip = intro;
                introSource.PlayScheduled(startDsp);
                track.LoopStartDsp = startDsp + MusicMath.ClipSeconds(intro);
                loopSource.PlayScheduled(track.LoopStartDsp);
                track.LoopScheduled = true;
            }
            else
            {
                introSource.clip = null;
                loopSource.PlayScheduled(startDsp);
                track.LoopStartDsp = startDsp;
                track.LoopScheduled = true;
            }
            ApplyMusicVolume(channel);
        }

        // Returns the collected Completion when the fade is instant (SilenceMusicChannel's
        // result); null when it starts a fade (TickMusic signals completion later).
        // Callers must TrySetResult() the return value last, after all state mutation (two-phase).
        private AutoResetUniTaskCompletionSource BeginMusicFadeOut(int channel, float duration)
        {
            ref var track = ref MusicChannels[channel];
            if (track.State == MusicState.Idle)
            {
                return null;
            }
            if (duration <= 0f || track.Paused)
            {
                // A paused track is inaudible; fading it is meaningless — silence instantly so awaiters resolve.
                return SilenceMusicChannel(channel);
            }
            track.Fade.Begin(track.Fade.Factor, 0f, duration);
            track.State = MusicState.FadingOut;
            return null;
        }

        // Stops both sources and frees the channel. Does NOT signal Completion —
        // callers collect it first and fire signals last (two-phase discipline).
        private AutoResetUniTaskCompletionSource SilenceMusicChannel(int channel)
        {
            ref var track = ref MusicChannels[channel];
            var completion = track.Completion;
            MusicIntroSources[channel].Stop();
            MusicLoopSources[channel].Stop();
            track.State = MusicState.Idle;
            track.Def = null;
            track.Sound = null;
            track.Paused = false;
            track.LoopScheduled = false;
            track.Completion = null;
            return completion;
        }

        private void ApplyMusicVolume(int channel)
        {
            ref var track = ref MusicChannels[channel];
            var volume = track.Def != null ? track.Def.Volume
                : track.Sound != null ? track.SoundVolume * GroupGain(track.Sound) : 0f;
            volume *= track.Fade.Factor;
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
            ref var track = ref MusicChannels[ActiveMusic];
            if (track.State == MusicState.Playing)
            {
                return UniTask.CompletedTask; // zero-fade path: already done
            }
            track.Completion ??= AutoResetUniTaskCompletionSource.Create();
            var task = track.Completion.Task;
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
            ref var track = ref MusicChannels[ActiveMusic];
            track.Completion ??= AutoResetUniTaskCompletionSource.Create();
            var task = track.Completion.Task;
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
            // Advance both channels before continuations can replace either track.
            var firstCompletion = AdvanceMusicChannel(0, dt);
            var secondCompletion = AdvanceMusicChannel(1, dt);
            firstCompletion?.TrySetResult();
            secondCompletion?.TrySetResult();
        }

        private AutoResetUniTaskCompletionSource AdvanceMusicChannel(int channel, float deltaTime)
        {
            ref var track = ref MusicChannels[channel];
            if (track.State == MusicState.Idle || track.Paused)
            {
                return null;
            }

            AutoResetUniTaskCompletionSource completion = null;
            if (track.Fade.Advance(deltaTime))
            {
                if (track.State == MusicState.FadingOut)
                {
                    return SilenceMusicChannel(channel);
                }
                track.State = MusicState.Playing;
                completion = track.Completion;
                track.Completion = null;
            }
            ApplyMusicVolume(channel);
            return completion;
        }
    }
}
