using System;
using Bun3.Unity.Audio.SteamAudio;
using UnityEngine;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Binds one native SFX output to a planar simulation source and its authored distance profile.</summary>
    public class PlanarAcousticSfxBinding : IPlanarAcousticOutput, IPlanarAcousticDiagnostics, IPlanarSpatialBlendDiagnostics, IDisposable
    {
        readonly AudioSource source;
        readonly SteamAudioSoundOutput output;
        readonly PlanarAcousticBinding binding = new();
        long preparedGeneration;
        /// <inheritdoc/>
        public PlanarAcousticSourceHandle SourceHandle => binding.Handle;
        /// <inheritdoc/>
        public string Label => source != null ? source.name : string.Empty;
        /// <inheritdoc/>
        public Vector2 Position => source != null ? (Vector2)source.transform.position : Vector2.zero;
        /// <inheritdoc/>
        public bool IsBlocked => output.CurrentParameters.IsBlocked;
        /// <inheritdoc/>
        public bool DistanceAttenuation => output.Acoustics.DistanceAttenuation;
        /// <inheritdoc/>
        public DistanceAttenuationProfile AttenuationProfile => output.Acoustics.AttenuationProfile;
        /// <inheritdoc/>
        public float MinimumDistance => output.Acoustics.MinDistance;
        /// <inheritdoc/>
        public float MaximumDistance => output.Acoustics.MaxDistance;

        /// <summary>Whether the active native output generation has not been prepared yet.</summary>
        public bool RequiresSimulation => output.CurrentParameters.IsValid && output.CurrentParameters.Generation != preparedGeneration;

        /// <summary>Binds an existing pooled source and native output without owning their disposal.</summary>
        public PlanarAcousticSfxBinding(AudioSource source, SteamAudioSoundOutput output)
        {
            this.source = source;
            this.output = output;

        }

        /// <summary>Synchronizes source position, generation and attenuation before simulation.</summary>
        public void Prepare(PlanarAcousticWorld world)
        {
            var parameters = output.CurrentParameters;
            var acoustic = output.Acoustics;
            bool available = output.IsAvailable;
            binding.Prepare(world, source != null ? (Vector2)source.transform.position : Vector2.zero,
                source != null && parameters.IsValid && available);
            if (parameters.IsValid && available)
                binding.ApplyDistanceProfile(acoustic.DistanceAttenuation, acoustic.AttenuationProfile,
                    acoustic.MinDistance, acoustic.MaxDistance);
            preparedGeneration = parameters.IsValid ? parameters.Generation : 0;
        }

        /// <summary>Publishes current native coefficients and gates missing paths silent.</summary>
        public void Publish(PlanarAcousticWorld world)
        {
            var parameters = output.CurrentParameters;
            if (!parameters.IsValid) return;
            var acoustic = output.Acoustics;
            if (!output.IsAvailable || !binding.TryGet(world, out var path))
            {
                parameters.TrySetBlocked(true);
                return;
            }
            var result = path.SimulationResult;
            bool published = parameters.TryPublish(binding.Coefficients,
                new PathRenderSettings(path.Listener, result.EqLow, result.EqMid, result.EqHigh, path.Gain, result.NormalizeEq,
                    GetSpatialBlend(world, acoustic)));
            parameters.TrySetBlocked(!published);
        }

        /// <inheritdoc/>
        public float GetSpatialBlend(PlanarAcousticWorld world) => GetSpatialBlend(world, output.Acoustics);

        float GetSpatialBlend(PlanarAcousticWorld world, SoundAcousticSettings acoustic) => acoustic.InheritSpatialBlend
            ? world.GetSpatialBlend(binding.Handle)
            : world.GetSpatialBlend(binding.Handle, acoustic.SpatialBlendProfile, acoustic.MonoDistance, acoustic.FullSpatialDistance);

        /// <summary>Immediately gates sources without current connectivity and probe coverage.</summary>
        public void Gate(PlanarAcousticWorld world, Vector2 listener)
        {
            if (!output.CurrentParameters.IsValid) return;
            if (source == null ||
                !world.IsRouteCovered(source.transform.position, listener)) Block();
        }

        /// <summary>Silences the active native generation.</summary>
        public void Block() => output.CurrentParameters.TrySetBlocked(true);

        /// <summary>Silences output and releases source registration.</summary>
        public void BlockAndDetach()
        {
            Block();
            binding.Detach();
            preparedGeneration = 0;
        }

        /// <summary>Detaches this binding; the caller retains ownership of the playback output.</summary>
        public virtual void Dispose()
        {

            BlockAndDetach();
        }
    }
}
