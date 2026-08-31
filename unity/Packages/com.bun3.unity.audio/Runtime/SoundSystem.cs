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

            if (Live.Count == 0)
            {
                PlayerLoopSystemHelper.InsertSystemAfter(
                    typeof(TickMarker), TickAll,
                    typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate));
            }
            Live.Add(this);
        }

        /// <summary>Plays a 2D (or def-default) sound. Returns an invalid handle when blocked.</summary>
        public SoundHandle Play(SoundDef def) => PlayCore(def, Vector3.zero, null);

        /// <summary>Plays at a fixed world position (def should use SpatialMode.Positional).</summary>
        public SoundHandle Play(SoundDef def, Vector3 position) => PlayCore(def, position, null);

        /// <summary>Plays tracking a transform every frame (def should use SpatialMode.Follow).</summary>
        public SoundHandle Play(SoundDef def, Transform follow) => PlayCore(def, follow != null ? follow.position : Vector3.zero, follow);

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

        private SoundHandle PlayCore(SoundDef def, Vector3 position, Transform follow)
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
                stolenCompletion?.TrySetResult();
            }

            ref var voice = ref Table.Slots[slot];
            voice.Follow = follow;

            var source = _sources[slot];
            source.clip = clip;
            source.loop = def.Loop;
            source.pitch = voice.Pitch;
            source.volume = Table.CurrentVolume(slot);
            source.outputAudioMixerGroup = def.MixerGroup != null ? def.MixerGroup : _config.SfxGroup;
            source.spatialBlend = def.Spatial == SpatialMode.None ? 0f : 1f;
            source.minDistance = def.MinDistance;
            source.maxDistance = def.MaxDistance;
            source.transform.position = position;
            source.Play();

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

        /// <summary>Stops all voices, destroys the pool, and unregisters the tick when last alive.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            for (var i = 0; i < Table.Slots.Length; i++)
            {
                if (Table.Slots[i].State != VoiceState.Idle)
                {
                    Table.Release(i);
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
