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
    /// Partial layout:
    /// <list type="bullet">
    /// <item><description>SoundSystem.cs: construction, playback, disposal.</description></item>
    /// <item><description>SoundSystem.Tick.cs: per-frame mirror of <see cref="VoiceTable"/>/music state onto AudioSources.</description></item>
    /// <item><description>SoundSystem.Music.cs: intro+loop music channels, crossfade, pause/resume.</description></item>
    /// <item><description>SoundSystem.Occlusion.cs: round-robin occlusion evaluation and low-pass/volume application.</description></item>
    /// <item><description>SoundSystem.Async.cs: UniTask entry points built on voice completion sources.</description></item>
    /// <item><description>SoundSystem.Addressables.cs: Addressable clip preload/release (compiled under BUN3_ADDRESSABLES).</description></item>
    /// </list>
    /// </summary>
    public sealed partial class SoundSystem : IDisposable
    {
        private static readonly List<SoundSystem> Live = new();

        private struct TickMarker
        {
        }

        internal readonly VoiceTable Table;
        private readonly AudioSource[] _sources;
        private readonly AudioRolloffMode[] _sourceRolloffModes;
        private readonly AnimationCurve[] _sourceRolloffCurves;
        private readonly bool[] _distanceAttenuationDisabled;
        private readonly AnimationCurve _flatRolloff = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        private readonly ISoundVoiceOutput[] _outputs;
        private readonly bool[] _outputActive;
        private readonly List<(int Slot, uint Generation, Cysharp.Threading.Tasks.AutoResetUniTaskCompletionSource Completion, Action<SoundHandle> Callback)> _completedScratch;
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
            _sourceRolloffModes = new AudioRolloffMode[config.SfxVoices];
            _sourceRolloffCurves = new AnimationCurve[config.SfxVoices];
            _distanceAttenuationDisabled = new bool[config.SfxVoices];
            _outputs = new ISoundVoiceOutput[config.SfxVoices];
            _outputActive = new bool[config.SfxVoices];
            // At most one completion per slot per tick; preallocate to that bound so the
            // hot Tick path never grows this list (List.Add would allocate on growth).
            _completedScratch = new(config.SfxVoices);
            _root = new GameObject("Bun3.SoundSystem");
            if (Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(_root);
            }
            try
            {
                for (var i = 0; i < _sources.Length; i++)
                {
                    var go = new GameObject("Voice");
                    go.transform.SetParent(_root.transform, false);
                    _sources[i] = go.AddComponent<AudioSource>();
                    _sources[i].playOnAwake = false;
                    config.OnSourceCreated?.Invoke(_sources[i]);
                    _sourceRolloffModes[i] = _sources[i].rolloffMode;
                    if (_sourceRolloffModes[i] == AudioRolloffMode.Custom)
                        _sourceRolloffCurves[i] = _sources[i].GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                    _outputs[i] = config.CreateVoiceOutput?.Invoke(_sources[i]);
                }
                for (var i = 0; i < MusicChannelCount; i++)
                {
                    MusicIntroSources[i] = CreateMusicSource("MusicIntro");
                    MusicLoopSources[i] = CreateMusicSource("MusicLoop");
                }
                InitializeOcclusion();
            }
            catch
            {
                DisposeVoiceOutputs();
                if (Application.isPlaying) { UnityEngine.Object.Destroy(_root); }
                else { UnityEngine.Object.DestroyImmediate(_root); }
                _root = null;
                throw;
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

        /// <summary>
        /// Registers a callback invoked once when the voice ends (natural end, fade-out,
        /// steal, Stop, or Dispose), with the original (now-stale) handle. No-op for a stale
        /// handle. Overwrites any previously registered callback for this voice — one callback
        /// per voice, mirroring the one-awaiter contract of <see cref="SoundHandle.WaitAsync"/>.
        /// Cold-path registration; cache the delegate rather than allocating a closure per call.
        /// </summary>
        public void SetCompletionCallback(SoundHandle handle, Action<SoundHandle> callback)
        {
            if (TryGetSlot(handle, out var slot))
            {
                Table.Slots[slot].CompletionCallback = callback;
            }
        }

        internal bool TryGetSlot(SoundHandle handle, out int slot)
        {
            slot = handle.SlotIndex;
            return !_disposed && handle.Owner == this && Table.IsValid(slot, handle.Generation);
        }

        /// <summary>Plays with a request-local spatial mode and gain without modifying the shared definition.</summary>
        public SoundHandle Play(SoundDef def, Vector3 position, SpatialMode spatial, float volumeScale = 1f, float fadeIn = 0f)
            => PlayCore(def, position, null, fadeIn, spatial, volumeScale);

        /// <summary>Prepares one definition for cooldown tracking before its first hot-path playback.</summary>
        public void Prepare(SoundDef definition)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SoundSystem));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Table.Prepare(definition);
        }

        /// <summary>Prepares catalog definitions for cooldown tracking before hot-path playback.</summary>
        public void Prepare(SoundCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            catalog.ValidateOrThrow();
            for (int i = 0; i < catalog.Entries.Count; i++) Table.Prepare(catalog.Entries[i].Definition);
        }

        private float GroupGain(SoundDef def)
        {
            float gain = _config.GroupGain != null ? _config.GroupGain(def.EffectiveVolumeGroup) : 1f;
            return float.IsNaN(gain) || float.IsInfinity(gain) ? 0f : Mathf.Max(0f, gain);
        }

        private SoundHandle PlayCore(SoundDef def, Vector3 position, Transform follow, float fadeIn,
            SpatialMode? spatial = null, float volumeScale = 1f)
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

            if (float.IsNaN(volumeScale) || float.IsInfinity(volumeScale) || volumeScale < 0f)
                return SoundHandle.Invalid;
            var clip = PickClip(def);
            if (clip == null) return SoundHandle.Invalid;
            if (!Table.TryAllocate(def, clip.length, out var slot, out var stolen, out var stolenSignal))
            {
                return SoundHandle.Invalid;
            }
            RetireVoiceOutput(slot);
            _sources[slot].Stop();
            // A stolen (or reused) slot's filter may still be muffled from the voice it just
            // replaced; clear it before the new source plays so the new voice starts open.
            ResetOcclusionFilter(slot);

            ref var voice = ref Table.Slots[slot];
            voice.Follow = follow;
            voice.VolumeScale = volumeScale;
            if (fadeIn > 0f)
            {
                Table.BeginFadeIn(slot, fadeIn);
            }

            var source = _sources[slot];
            source.clip = clip;
            source.loop = def.EffectiveLoop;
            source.pitch = _config.PitchWithTimescale ? voice.Pitch * _lastTimeScale : voice.Pitch;
            voice.PlaybackRate = source.pitch;
            source.volume = Table.CurrentVolume(slot) * GroupGain(def); // reflects Fade.Factor 0 when fading in
            source.outputAudioMixerGroup = def.EffectiveMixerGroup != null ? def.EffectiveMixerGroup : _config.SfxGroup;
            source.spatialBlend = (spatial ?? def.EffectiveSpatial) == SpatialMode.None ? 0f : 1f;
            source.minDistance = def.EffectiveMinDistance;
            source.maxDistance = def.EffectiveMaxDistance;
            ConfigureDistanceAttenuation(slot, def.EffectiveDistanceAttenuation);
            source.transform.position = position;
            var generation = voice.Generation;
            var accepted = true;
            _config.OnVoiceConfigured?.Invoke(source, def);
            if (!_disposed && Table.IsValid(slot, generation))
            {
                var output = _outputs[slot];
                var result = output is ISpatialSoundVoiceOutput spatialOutput
                    ? spatialOutput.TryStart(def, clip, voice.PlaybackRate, spatial ?? def.EffectiveSpatial)
                    : output != null ? output.TryStart(def, clip, voice.PlaybackRate) : VoiceOutputStartResult.Unsupported;
                if (result == VoiceOutputStartResult.Started)
                {
                    _outputActive[slot] = true;
                    voice.ExternalCompletion = true;
                    ResetOcclusionFilter(slot);
                    source.Play();
                }
                else if (result == VoiceOutputStartResult.Unsupported)
                {
                    source.Play();
                }
                else
                {
                    output?.Retire();
                    source.Stop();
                    source.clip = null;
                    Table.Release(slot);
                    accepted = false;
                }
            }

            // Fired only after the new source is fully configured and playing: a continuation
            // may re-enter PlayCore (this is a stolen voice's awaiter) and must never observe
            // a half-configured slot.
            if (stolen >= 0)
            {
                stolenSignal.Completion?.TrySetResult();
                stolenSignal.Callback?.Invoke(new SoundHandle(this, stolen, stolenSignal.Generation));
            }

            return accepted ? new SoundHandle(this, slot, generation) : SoundHandle.Invalid;
        }

        private void ConfigureDistanceAttenuation(int slot, bool enabled)
        {
            if (_distanceAttenuationDisabled[slot] == !enabled) return;
            var source = _sources[slot];
            if (enabled)
            {
                if (_sourceRolloffCurves[slot] != null)
                    source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, _sourceRolloffCurves[slot]);
                source.rolloffMode = _sourceRolloffModes[slot];
            }
            else
            {
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, _flatRolloff);
                source.rolloffMode = AudioRolloffMode.Custom;
            }
            _distanceAttenuationDisabled[slot] = !enabled;
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
            DisposeVoiceOutputs();

            // Two-phase, same discipline as Tick: capture every active slot's awaiter before
            // releasing (Release nulls Completion), finish all teardown, then fire the
            // awaiters last — a continuation re-entering this instance must see it fully
            // disposed, not mid-teardown. Uses a LOCAL list rather than _completedScratch: a
            // completion callback can call Dispose() re-entrantly while Tick's own signal loop
            // is still iterating _completedScratch, and clearing/refilling that shared list here
            // would corrupt the outer iteration (dropped or double-fired entries).
            var completed = new List<(int Slot, uint Generation, Cysharp.Threading.Tasks.AutoResetUniTaskCompletionSource Completion, Action<SoundHandle> Callback)>(Table.Slots.Length);
            for (var i = 0; i < Table.Slots.Length; i++)
            {
                if (Table.Slots[i].State != VoiceState.Idle)
                {
                    var generation = Table.Slots[i].Generation;
                    var completion = Table.Slots[i].Completion;
                    var callback = Table.Slots[i].CompletionCallback;
                    Table.Release(i);
                    completed.Add((i, generation, completion, callback));
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

            for (var i = 0; i < completed.Count; i++)
            {
                var entry = completed[i];
                entry.Completion?.TrySetResult();
                entry.Callback?.Invoke(new SoundHandle(this, entry.Slot, entry.Generation));
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
            var effective = _config.PitchWithTimescale ? pitch * _lastTimeScale : pitch;
            if (_outputActive[slot])
            {
                _outputs[slot].SetPitch(effective);
                Table.Slots[slot].PlaybackRate = Mathf.Max(0, effective);
            }
            else
            {
                _sources[slot].pitch = effective;
                Table.Slots[slot].PlaybackRate = _sources[slot].pitch;
            }
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
