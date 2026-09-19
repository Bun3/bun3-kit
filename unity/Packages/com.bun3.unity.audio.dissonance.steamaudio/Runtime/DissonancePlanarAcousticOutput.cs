using System;
using Bun3.Unity.Audio.SteamAudio;
using UnityEngine;
using SA = global::SteamAudio;
namespace Bun3.Unity.Audio.Dissonance.SteamAudio
{
    /// <summary>Application-owned voice distance tuning.</summary>
    public interface IPlanarVoiceSettings
    {
        /// <summary>Whether application tuning is currently available; unavailable settings gate output silent.</summary>
        bool IsAvailable { get; }
        /// <summary>Whether distance attenuation applies.</summary>
        bool DistanceAttenuation { get; }
        /// <summary>The live authored distance curve.</summary>
        DistanceAttenuationProfile AttenuationProfile { get; }
    }
    /// <summary>Optional per-voice spatial-width override; absent/null profile inherits world defaults.</summary>
    public interface IPlanarVoiceSpatialSettings
    {
        /// <summary>Live shared profile for this voice output, or null to inherit the world.</summary>
        SpatialBlendProfile SpatialBlendProfile { get; }
    }
    /// <summary>Connects Dissonance output generations to a planar simulation without game dependencies.</summary>
    public sealed class DissonancePlanarAcousticOutput : IPlanarAcousticOutput, IPlanarAcousticDiagnostics, IPlanarSpatialBlendDiagnostics, IDisposable
    {
        readonly PlanarAcousticBinding binding = new();
        readonly DissonanceSteamAudioPlayback playback;
        readonly IPlanarVoiceSettings settings;
        long preparedGeneration;
        /// <inheritdoc/>
        public PlanarAcousticSourceHandle SourceHandle => binding.Handle;
        /// <inheritdoc/>
        public string Label => playback != null ? playback.PlayerName : string.Empty;
        /// <inheritdoc/>
        public Vector2 Position => playback != null ? (Vector2)playback.transform.position : Vector2.zero;
        /// <inheritdoc/>
        public bool IsBlocked => playback == null || playback.Parameters == null || playback.Parameters.IsBlocked;
        /// <inheritdoc/>
        public bool DistanceAttenuation => settings.DistanceAttenuation;
        /// <inheritdoc/>
        public DistanceAttenuationProfile AttenuationProfile => settings.AttenuationProfile;
        /// <inheritdoc/>
        public float MinimumDistance => 1;
        /// <inheritdoc/>
        public float MaximumDistance => 15;
        /// <inheritdoc/>
        public bool RequiresSimulation => playback != null && playback.IsSpeaking && !playback.IsRetiring &&
            playback.Parameters != null && playback.Parameters.Generation != preparedGeneration;

        /// <summary>Configures the retained native playback and begins a detached source binding.</summary>
        public DissonancePlanarAcousticOutput(DissonanceSteamAudioPlayback playback, IPlanarVoiceSettings settings)
        {
            this.playback = playback != null ? playback : throw new System.ArgumentNullException(nameof(playback));
            this.settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
            playback.StartBlocked = true;
            playback.Configure(CreateRenderer, binding.Coefficients, new SA.CoordinateSpace3
            {
                right = new SA.Vector3 { x = 1 }, up = new SA.Vector3 { y = 1 }, ahead = new SA.Vector3 { z = -1 }
            });
        }
        static SteamAudioPathRenderer CreateRenderer(int rate, int frame) => SteamAudioPathRenderer.CreateDefault(rate, frame);
        /// <summary>Detaches the source registration. Playback owns its DSP lifetime.</summary>
        public void Dispose() => BlockAndDetach();

        /// <inheritdoc/>
        public void Prepare(PlanarAcousticWorld world)
        {
            var parameters = playback != null ? playback.Parameters : null;
            bool active = playback != null && playback.IsSpeaking && !playback.IsRetiring && parameters != null;
            PrepareBinding(world, active, settings);
            // Capacity failure retries on the normal interval, without forcing a native tick every frame.
            preparedGeneration = active ? parameters.Generation : 0;
        }

        void PrepareBinding(PlanarAcousticWorld world, bool active, IPlanarVoiceSettings config)
        {
            binding.Prepare(world, playback.transform.position, active && config != null && config.IsAvailable);
            if (!active || config == null || !config.IsAvailable) return;
            binding.ApplyDistanceProfile(config.DistanceAttenuation, config.AttenuationProfile);
        }

        /// <inheritdoc/>
        public void Publish(PlanarAcousticWorld world)
        {
            var parameters = playback != null ? playback.Parameters : null;
            if (parameters == null) return;
            var config = settings;
            if (config == null || !config.IsAvailable || !binding.TryGet(world, out var path))
            {
                parameters.TrySetBlocked(parameters.Generation, true);
                return;
            }
            var result = path.SimulationResult;
            bool published = parameters.TryPublish(parameters.Generation, binding.Coefficients,
                new PathPlaybackSettings(path.Listener, result.EqLow, result.EqMid, result.EqHigh, path.Gain, result.NormalizeEq, GetSpatialBlend(world)));
            parameters.TrySetBlocked(parameters.Generation, !published);
        }

        /// <inheritdoc/>
        public float GetSpatialBlend(PlanarAcousticWorld world)
        {
            var profile = (settings as IPlanarVoiceSpatialSettings)?.SpatialBlendProfile;
            return profile != null ? world.GetSpatialBlend(binding.Handle, profile, 0, 0) : world.GetSpatialBlend(binding.Handle);
        }

        /// <inheritdoc/>
        public void Gate(PlanarAcousticWorld world, Vector2 listener)
        {
            var config = settings;
            if (config == null || !config.IsAvailable ||
                !world.IsRouteCovered(playback.transform.position, listener)) Block();
        }

        /// <inheritdoc/>
        public void Block()
        {
            var parameters = playback != null ? playback.Parameters : null;
            parameters?.TrySetBlocked(parameters.Generation, true);
        }

        /// <inheritdoc/>
        public void BlockAndDetach()
        {
            Block();
            binding.Detach();
            preparedGeneration = 0;
        }
    }
}
