using System;
using System.Collections.Generic;
using Bun3.Unity.Core.PlayerLoop;
using UnityEngine;

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
        private GameObject _root;
        private bool _disposed;

        /// <summary>Creates the pool and registers the tick. Dispose to tear both down.</summary>
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
            Table = new VoiceTable(config.SfxVoices);
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
            if (def == null || def.Clips == null || def.Clips.Length == 0)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.Play: def has no clips; returning an invalid handle.");
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

            ref var voice = ref Table.Slots[slot];
            voice.Follow = follow;
            if (fadeIn > 0f)
            {
                Table.BeginFadeIn(slot, fadeIn);
            }

            var source = _sources[slot];
            source.clip = clip;
            source.loop = def.Loop;
            source.pitch = voice.Pitch;
            source.volume = Table.CurrentVolume(slot); // reflects FadeFactor 0 when fading in
            source.outputAudioMixerGroup = def.MixerGroup != null ? def.MixerGroup : _config.SfxGroup;
            source.spatialBlend = def.Spatial == SpatialMode.None ? 0f : 1f;
            source.minDistance = def.MinDistance;
            source.maxDistance = def.MaxDistance;
            source.transform.position = position;
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

        private static AudioClip PickClip(SoundDef def)
        {
            var clips = def.Clips;
            if (clips.Length == 1)
            {
                return clips[0];
            }
            int index;
            do
            {
                index = UnityEngine.Random.Range(0, clips.Length);
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

            Live.Remove(this);
            if (Live.Count == 0)
            {
                PlayerLoopSystemHelper.TryRemoveSystem(typeof(TickMarker));
            }
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }

            for (var i = 0; i < _completedScratch.Count; i++)
            {
                _completedScratch[i].Completion?.TrySetResult();
            }
        }

        internal void SetSourcePitch(int slot, float pitch) => _sources[slot].pitch = pitch;

        internal void SetSourcePosition(int slot, Vector3 position) => _sources[slot].transform.position = position;

        private static readonly string[] ChannelParams =
        {
            "MasterVolume", "MusicVolume", "SfxVolume", "VoiceVolume",
        };

        /// <summary>Sets a channel's linear volume [0,1] on the mixer. Persisting the value is the game's job.</summary>
        public void SetChannelVolume(SoundChannel channel, float linear)
        {
            if (_config.Mixer == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.SetChannelVolume: no mixer configured; call ignored.");
#endif
                return;
            }
            _config.Mixer.SetFloat(ChannelParams[(int)channel], AudioMath.LinearToDb(linear));
        }

        /// <summary>Reads a channel's linear volume; 1 when no mixer or parameter is set.</summary>
        public float GetChannelVolume(SoundChannel channel)
        {
            if (_config.Mixer == null || !_config.Mixer.GetFloat(ChannelParams[(int)channel], out var db))
            {
                return 1f;
            }
            return AudioMath.DbToLinear(db);
        }
    }
}
