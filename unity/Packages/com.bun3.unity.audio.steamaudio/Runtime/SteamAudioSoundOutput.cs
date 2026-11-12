using System;
using UnityEngine;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>
    /// Positional SoundSystem output rendered by the Steam Audio Unity spatializer. Unity plays the authored clip;
    /// this output only selects the spatializer and writes its parameters, so no managed code runs on the audio thread.
    /// Main thread only.
    /// </summary>
    public sealed class SteamAudioSoundOutput : ISpatialSoundVoiceOutput, IResolvedSoundAcousticSettings
    {
        // Unity feeds a mono clip into the spatializer at 1/sqrt(2) per channel; the spatializer downmix averages them.
        private const float MonoInputCompensation = 1.41421356f;

        private readonly AudioSource _source;
        private SoundDef _definition;
        private long _generation, _activeGeneration;
        private bool _loop, _blocked = true, _normalizeEq, _disposed;
        private float _inputCompensation = 1, _pathGain, _width = 1;
        private int _unityHandle = -1;
        private IntPtr _registered;
        private SteamAudioSoundFailure _failure;

        /// <summary>Creates an output for one pooled voice source.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
        public SteamAudioSoundOutput(AudioSource source)
        {
            _source = source != null ? source : throw new ArgumentNullException(nameof(source));
        }

        /// <inheritdoc/>
        public bool IsAvailable => _definition != null;

        /// <inheritdoc/>
        public SoundAcousticSettings Acoustics => _definition != null ? _definition.EffectiveAcoustics : SoundAcousticSettings.Default;

        /// <summary>Whether the active definition uses native distance attenuation.</summary>
        public bool DistanceAttenuation => _definition != null && Acoustics.DistanceAttenuation;

        /// <summary>Distance attenuation profile of the active definition.</summary>
        public DistanceAttenuationProfile AttenuationProfile => _definition != null ? Acoustics.AttenuationProfile : null;

        /// <summary>Whether the active definition inherits the world near-field range.</summary>
        public bool InheritSpatialBlend => _definition == null || Acoustics.InheritSpatialBlend;

        /// <summary>Near-field profile of the active definition.</summary>
        public SpatialBlendProfile SpatialBlendProfile => _definition != null ? Acoustics.SpatialBlendProfile : null;

        /// <summary>Inline near-field mono distance of the active definition.</summary>
        public float MonoDistance => _definition != null ? Acoustics.MonoDistance : 0;

        /// <summary>Inline full spatial width distance of the active definition.</summary>
        public float FullSpatialDistance => _definition != null ? Acoustics.FullSpatialDistance : 0;

        /// <summary>Whether a started voice stays silent until its first published path. Default true.</summary>
        public bool StartBlocked { get; set; } = true;

        /// <summary>Nonzero identity of the playing voice; zero while idle or retired.</summary>
        public long Generation => _activeGeneration;

        /// <summary>Whether a positional voice is playing through this output.</summary>
        public bool IsActive => _activeGeneration != 0;

        /// <summary>Whether a simulation source is registered with the spatializer.</summary>
        public bool HasSimulationSource => _unityHandle >= 0;

        /// <summary>Whether the pathing output is currently gated silent.</summary>
        public bool IsBlocked => _blocked;

        /// <summary>Maximum distance of the active definition.</summary>
        public float MaxDistance => IsActive ? Acoustics.MaxDistance : 0;

        /// <summary>Minimum distance of the active definition.</summary>
        public float MinDistance => IsActive ? Math.Max(0, Acoustics.MinDistance) : 0;

        /// <summary>Why the last start failed or the voice ended early.</summary>
        public SteamAudioSoundFailure Failure => _failure;

        /// <inheritdoc/>
        // A paused listener also reports isPlaying false; that is not the end of the clip.
        public bool IsComplete => _failure != SteamAudioSoundFailure.None ||
            IsActive && !_loop && !_source.isPlaying && !(AudioListener.pause && !_source.ignoreListenerPause);

        /// <inheritdoc/>
        public VoiceOutputStartResult TryStart(SoundDef definition, AudioClip selectedClip, float logicalPitch)
            => TryStart(definition, selectedClip, logicalPitch, definition != null ? definition.EffectiveSpatial : SpatialMode.None);

        /// <inheritdoc/>
        public VoiceOutputStartResult TryStart(SoundDef definition, AudioClip selectedClip, float logicalPitch, SpatialMode spatial)
        {
            Retire();
            if (_disposed) return VoiceOutputStartResult.Unavailable;
            _failure = SteamAudioSoundFailure.None;
            if (definition == null) return Fail(SteamAudioSoundFailure.InvalidRequest);
            if (spatial == SpatialMode.None)
            {
                _source.spatialize = false;
                return VoiceOutputStartResult.Unsupported;
            }
            if (!Finite(logicalPitch)) return Fail(SteamAudioSoundFailure.InvalidPitch);
            if (!Finite(definition.EffectiveAcoustics.MinDistance)) return Fail(SteamAudioSoundFailure.InvalidRequest);
            _definition = definition;
            _loop = definition.EffectiveLoop;
            _inputCompensation = selectedClip != null && selectedClip.channels == 1 ? MonoInputCompensation : 1;
            _blocked = StartBlocked;
            _activeGeneration = ++_generation;
            _source.pitch = Mathf.Clamp(logicalPitch, 0, 3);
            _source.spatialBlend = 1;
            _source.dopplerLevel = 0;
            _source.spatialize = true;
            Apply();
            return VoiceOutputStartResult.Started;
        }

        /// <inheritdoc/>
        public void SetPitch(float logicalPitch)
        {
            if (!Finite(logicalPitch)) { Fail(SteamAudioSoundFailure.InvalidPitch); Retire(); return; }
            _source.pitch = Mathf.Clamp(logicalPitch, 0, 3);
        }

        /// <inheritdoc/>
        public void Retire()
        {
            if (!IsActive) return;
            _activeGeneration = 0;
            _blocked = true;
            Apply();
        }

        /// <summary>Registers the simulation source whose pathing outputs the spatializer renders, or clears it with zero.</summary>
        public void SetSimulationSource(IntPtr source)
        {
            if (source == _registered || _disposed) return;
            Unregister();
            if (source != IntPtr.Zero)
            {
                _unityHandle = SA.API.iplUnityAddSource(source);
                _registered = _unityHandle >= 0 ? source : IntPtr.Zero;
            }
            Apply();
        }

        /// <summary>Opens the gate with the current path gain, stereo width and EQ normalization.</summary>
        public void SetPath(float gain, float width, bool normalizeEq)
        {
            if (!IsActive) return;
            _pathGain = Finite(gain) ? Math.Max(0, gain) : 0;
            _width = Finite(width) ? Mathf.Clamp01(width) : 1;
            _normalizeEq = normalizeEq;
            _blocked = false;
            Apply();
        }

        /// <summary>Gates the pathing output silent until the next published path.</summary>
        public void Block()
        {
            if (_blocked) return;
            _blocked = true;
            Apply();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            Retire();
            Unregister();
            _disposed = true;
        }

        private void Apply()
        {
            if (_source == null) return;
            // Re-sent every time: a recreated effect instance comes back with the plugin defaults (direct on, pathing off).
            SteamAudioSpatializerParams.ConfigurePathingOnly(_source);
            _source.SetSpatializerFloat(SteamAudioSpatializerParams.SimulationOutputsHandle, _unityHandle);
            bool audible = IsActive && !_blocked && _unityHandle >= 0;
            _source.SetSpatializerFloat(SteamAudioSpatializerParams.PathingMixLevel, audible ? _pathGain * _inputCompensation : 0);
            _source.SetSpatializerFloat(SteamAudioSpatializerParams.PathingNormalizeEq, _normalizeEq ? 1 : 0);
            if (SteamAudioSpatializerPatch.IsPatched) _source.SetSpatializerFloat(SteamAudioSpatializerParams.PathingStereoWidth, _width);
        }

        private void Unregister()
        {
            if (_unityHandle < 0) return;
            if (_source != null) _source.SetSpatializerFloat(SteamAudioSpatializerParams.SimulationOutputsHandle, -1);
            SA.API.iplUnityRemoveSource(_unityHandle);
            _unityHandle = -1;
            _registered = IntPtr.Zero;
        }

        private VoiceOutputStartResult Fail(SteamAudioSoundFailure failure)
        {
            _failure = failure;
            return VoiceOutputStartResult.Unavailable;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Why a native positional voice could not start or ended early.</summary>
    public enum SteamAudioSoundFailure
    {
        /// <summary>No failure.</summary>
        None,

        /// <summary>The definition is missing or has invalid acoustic settings.</summary>
        InvalidRequest,

        /// <summary>The logical pitch is not finite.</summary>
        InvalidPitch
    }
}
