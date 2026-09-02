using System;
using System.Collections.Generic;
using Bun3.Unity.Core.PlayerLoop;
using UnityEngine;
using UnityEngine.Audio;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Instance sound service: a prewarmed AudioSource pool driven by a single
    /// player-loop tick. No MonoBehaviours, no coroutines, no per-play allocation.
    /// Partial layout: this file owns construction/playback/disposal;
    /// SoundSystem.Tick.cs owns the per-frame mirror of <see cref="VoiceTable"/> state.
    /// </summary>
    public sealed partial class SoundSystem : IDisposable
    {
        private static readonly List<SoundSystem> Live = new();

        private struct TickMarker
        {
        }

        internal readonly VoiceTable Table;
        private readonly AudioSource[] _sources;
        private readonly List<(int Slot, Cysharp.Threading.Tasks.AutoResetUniTaskCompletionSource Completion)> _completedScratch;
        private readonly SoundSystemConfig _config;
        private readonly AudioMixer _mixer;
        private readonly System.Random _rng;
        private GameObject _root;
        private bool _disposed;

        /// <summary>
        /// Creates the pool and registers the tick. Dispose to tear both down. When no
        /// mixer/groups are configured, the bundled default mixer is loaded and the config's
        /// SfxGroup/MusicGroup are populated in place.
        /// </summary>
        public SoundSystem(SoundSystemConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (config.SfxVoices <= 0)
            {
                throw new ArgumentException("SfxVoices must be positive.", nameof(config));
            }

            _config = config;
            // Bundled fallback: ships in Runtime/Resources so games that never configure a
            // mixer still get channel volumes and default routing. Resources.Load returns
            // null when the asset was stripped from the build; every mixer read below stays
            // null-tolerant for that case.
            _mixer = config.Mixer != null ? config.Mixer : Resources.Load<AudioMixer>("Bun3DefaultAudioMixer");
            if (config.SfxGroup == null && _mixer != null)
            {
                var sfxGroups = _mixer.FindMatchingGroups("SFX");
                if (sfxGroups.Length > 0)
                {
                    config.SfxGroup = sfxGroups[0];
                }
            }
            if (config.MusicGroup == null && _mixer != null)
            {
                var musicGroups = _mixer.FindMatchingGroups("Music");
                if (musicGroups.Length > 0)
                {
                    config.MusicGroup = musicGroups[0];
                }
            }
            // Private variation stream: cosmetic randomness must never consume
            // UnityEngine.Random state (seeded gameplay would desync otherwise).
            _rng = config.RandomSeed.HasValue
                ? new System.Random(config.RandomSeed.Value)
                : new System.Random();
            Table = new VoiceTable(config.SfxVoices, _rng, config.OcclusionSmoothingSeconds);
            _sources = new AudioSource[config.SfxVoices];
            // At most one completion per slot per tick; preallocate to that bound so the
            // hot Tick path never grows this list (List.Add would allocate on growth).
            _completedScratch = new(config.SfxVoices);
            _root = new GameObject("Bun3.SoundSystem");
            if (Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(_root);
            }
            for (var i = 0; i < _sources.Length; i++)
            {
                var go = new GameObject("Voice");
                go.transform.SetParent(_root.transform, false);
                _sources[i] = go.AddComponent<AudioSource>();
                _sources[i].playOnAwake = false;
            }
            for (var i = 0; i < MusicChannelCount; i++)
            {
                MusicIntroSources[i] = CreateMusicSource("MusicIntro");
                MusicLoopSources[i] = CreateMusicSource("MusicLoop");
            }
            InitializeOcclusion();

            // Checks actual player-loop insertion rather than Live.Count: with domain reload
            // disabled, Application.quitting can remove the tick while stale entries survive
            // in the static Live list, which would otherwise block re-registration.
            if (!PlayerLoopSystemHelper.IsInserted(typeof(TickMarker)))
            {
                PlayerLoopSystemHelper.InsertSystemAfter(
                    typeof(TickMarker), TickAll,
                    typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate));
            }
            Live.Add(this);
        }

        /// <summary>Plays a 2D (or def-default) sound. Returns an invalid handle when blocked.</summary>
        /// <param name="def">Sound definition to play.</param>
        /// <param name="fadeIn">When > 0, ramps volume from silence over this many seconds.</param>
        public SoundHandle Play(SoundDef def, float fadeIn = 0f) => PlayCore(def, Vector3.zero, null, fadeIn);

        /// <summary>Plays at a fixed world position (def should use SpatialMode.Positional).</summary>
        /// <param name="def">Sound definition to play.</param>
        /// <param name="position">Fixed world position for the voice.</param>
        /// <param name="fadeIn">When > 0, ramps volume from silence over this many seconds.</param>
        public SoundHandle Play(SoundDef def, Vector3 position, float fadeIn = 0f) => PlayCore(def, position, null, fadeIn);

        /// <summary>Plays tracking a transform every frame (def should use SpatialMode.Follow).</summary>
        /// <param name="def">Sound definition to play.</param>
        /// <param name="follow">Transform to track every frame.</param>
        /// <param name="fadeIn">When > 0, ramps volume from silence over this many seconds.</param>
        public SoundHandle Play(SoundDef def, Transform follow, float fadeIn = 0f) => PlayCore(def, follow != null ? follow.position : Vector3.zero, follow, fadeIn);

        /// <summary>Stops the voice, optionally fading out first. No-op for stale handles.</summary>
        public void Stop(SoundHandle handle, float fadeOut = 0f)
        {
            if (!TryGetSlot(handle, out var slot))
            {
                return;
            }
            Table.BeginFadeOut(slot, fadeOut);
        }

        internal bool TryGetSlot(SoundHandle handle, out int slot)
        {
            slot = handle.SlotIndex;
            return !_disposed && handle.Owner == this && Table.IsValid(slot, handle.Generation);
        }

        private SoundHandle PlayCore(SoundDef def, Vector3 position, Transform follow, float fadeIn)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SoundSystem));
            }
            if (def == null || def.EffectiveClips == null || def.EffectiveClips.Length == 0)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.Play: def has no loaded clips (assign Clips or preload AddressableClips); returning an invalid handle.");
#endif
                return SoundHandle.Invalid;
            }

            var clip = PickClip(def);
            if (!Table.TryAllocate(def, clip.length, out var slot, out var stolen, out var stolenCompletion))
            {
                return SoundHandle.Invalid;
            }
            if (stolen >= 0)
            {
                _sources[stolen].Stop();
            }
            // A stolen (or reused) slot's filter may still be muffled from the voice it just
            // replaced; clear it before the new source plays so the new voice starts open.
            ResetOcclusionFilter(slot);

            ref var voice = ref Table.Slots[slot];
            voice.Follow = follow;
            if (fadeIn > 0f)
            {
                Table.BeginFadeIn(slot, fadeIn);
            }

            var source = _sources[slot];
            source.clip = clip;
            source.loop = def.Loop;
            source.pitch = _config.PitchWithTimescale ? voice.Pitch * _lastTimeScale : voice.Pitch;
            voice.PlaybackRate = source.pitch;
            source.volume = Table.CurrentVolume(slot); // reflects FadeFactor 0 when fading in
            source.outputAudioMixerGroup = def.MixerGroup != null ? def.MixerGroup : _config.SfxGroup;
            source.spatialBlend = def.Spatial == SpatialMode.None ? 0f : 1f;
            source.minDistance = def.MinDistance;
            source.maxDistance = def.MaxDistance;
            source.transform.position = position;
            _config.OnVoiceConfigured?.Invoke(source, def);
            source.Play();

            // Fired only after the new source is fully configured and playing: a continuation
            // may re-enter PlayCore (this is a stolen voice's awaiter) and must never observe
            // a half-configured slot.
            if (stolen >= 0)
            {
                stolenCompletion?.TrySetResult();
            }

            return new SoundHandle(this, slot, voice.Generation);
        }

        private AudioSource CreateMusicSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = _config.MusicGroup;
            return source;
        }

        private AudioClip PickClip(SoundDef def)
        {
            var clips = def.EffectiveClips;
            if (clips.Length == 1)
            {
                return clips[0];
            }
            int index;
            do
            {
                index = _rng.Next(0, clips.Length);
            }
            while (index == def.LastClipIndex);
            def.LastClipIndex = index;
            return clips[index];
        }

        /// <summary>
        /// Stops all voices, destroys the pool, and unregisters the tick when last alive.
        /// Any pending <see cref="SoundHandle.WaitAsync"/> awaiters complete normally (never
        /// with an exception).
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            // Two-phase, same discipline as Tick: capture every active slot's awaiter before
            // releasing (Release nulls Completion), finish all teardown, then fire the
            // awaiters last — a continuation re-entering this instance must see it fully
            // disposed, not mid-teardown.
            _completedScratch.Clear();
            for (var i = 0; i < Table.Slots.Length; i++)
            {
                if (Table.Slots[i].State != VoiceState.Idle)
                {
                    var completion = Table.Slots[i].Completion;
                    Table.Release(i);
                    _completedScratch.Add((i, completion));
                }
            }

            Cysharp.Threading.Tasks.AutoResetUniTaskCompletionSource musicCompletion0 = null;
            Cysharp.Threading.Tasks.AutoResetUniTaskCompletionSource musicCompletion1 = null;
            for (var i = 0; i < MusicChannelCount; i++)
            {
                if (MusicChannels[i].State != MusicState.Idle)
                {
                    var completion = SilenceMusicChannel(i);
                    if (i == 0) { musicCompletion0 = completion; } else { musicCompletion1 = completion; }
                }
            }
            ActiveMusic = -1;

            Live.Remove(this);
            if (Live.Count == 0)
            {
                PlayerLoopSystemHelper.TryRemoveSystem(typeof(TickMarker));
            }
            // Addressable handles reference AudioClips the sources may still hold; releasing
            // before the sources/root are destroyed is safe either way, but doing it here
            // keeps teardown ordered top-down (voices/music already silenced above).
            ReleaseAllPreloadedOnDispose();
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }

            for (var i = 0; i < _completedScratch.Count; i++)
            {
                _completedScratch[i].Completion?.TrySetResult();
            }
            musicCompletion0?.TrySetResult();
            musicCompletion1?.TrySetResult();
        }

        /// <summary>
        /// Releases every preloaded Addressables handle. Implemented only in
        /// SoundSystem.Addressables.cs (compiled under BUN3_ADDRESSABLES); with that define
        /// off, this partial method call compiles out entirely (C# partial method semantics).
        /// </summary>
        partial void ReleaseAllPreloadedOnDispose();

        internal void SetSourcePitch(int slot, float pitch)
        {
            _sources[slot].pitch = _config.PitchWithTimescale ? pitch * _lastTimeScale : pitch;
            Table.Slots[slot].PlaybackRate = _sources[slot].pitch;
        }

        internal void SetSourcePosition(int slot, Vector3 position) => _sources[slot].transform.position = position;

        internal float SourcePitchForTest(int slot) => _sources[slot].pitch;

        internal AudioSource SourceForTest(int slot) => _sources[slot];

        /// <summary>Thin wrapper over AudioMixerSnapshot.TransitionTo; no-op on null.</summary>
        public void TransitionTo(UnityEngine.Audio.AudioMixerSnapshot snapshot, float seconds)
        {
            if (snapshot == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.TransitionTo: null snapshot; ignored.");
#endif
                return;
            }
            snapshot.TransitionTo(seconds);
        }

        private static readonly string[] ChannelParams =
        {
            "MasterVolume", "MusicVolume", "SfxVolume", "VoiceVolume",
        };

        /// <summary>Sets a channel's linear volume [0,1] on the mixer. Persisting the value is the game's job.</summary>
        public void SetChannelVolume(SoundChannel channel, float linear)
        {
            if (_mixer == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.SetChannelVolume: no mixer configured; call ignored.");
#endif
                return;
            }
            _mixer.SetFloat(ChannelParams[(int)channel], AudioMath.LinearToDb(linear));
        }

        /// <summary>Reads a channel's linear volume; 1 when no mixer or parameter is set.</summary>
        public float GetChannelVolume(SoundChannel channel)
        {
            if (_mixer == null || !_mixer.GetFloat(ChannelParams[(int)channel], out var db))
            {
                return 1f;
            }
            return AudioMath.DbToLinear(db);
        }
    }
}
