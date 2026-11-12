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
        public bool IsBlocked => output.IsBlocked;
        /// <inheritdoc/>
        public bool DistanceAttenuation => output.Acoustics.DistanceAttenuation;
        /// <inheritdoc/>
        public DistanceAttenuationProfile AttenuationProfile => output.Acoustics.AttenuationProfile;
        /// <inheritdoc/>
        public float MinimumDistance => output.Acoustics.MinDistance;
        /// <inheritdoc/>
        public float MaximumDistance => output.Acoustics.MaxDistance;

        /// <summary>Whether the active native output generation has not been prepared yet.</summary>
        public bool RequiresSimulation => output.IsActive && output.Generation != preparedGeneration;

        /// <summary>Binds an existing pooled source and native output without owning their disposal.</summary>
        public PlanarAcousticSfxBinding(AudioSource source, SteamAudioSoundOutput output)
        {
            this.source = source;
            this.output = output;

        }

        /// <summary>Synchronizes source position, generation and attenuation before simulation.</summary>
        public void Prepare(PlanarAcousticWorld world)
        {
            var acoustic = output.Acoustics;
            bool active = output.IsActive && output.IsAvailable;
            binding.Prepare(world, source != null ? (Vector2)source.transform.position : Vector2.zero, source != null && active);
            if (active)
                binding.ApplyDistanceProfile(acoustic.DistanceAttenuation, acoustic.AttenuationProfile,
                    acoustic.MinDistance, acoustic.MaxDistance);
            preparedGeneration = output.Generation;
        }

        /// <summary>Publishes current native coefficients and gates missing paths silent.</summary>
        public void Publish(PlanarAcousticWorld world)
        {
            if (!output.IsActive) return;
            var acoustic = output.Acoustics;
            if (!output.IsAvailable || !binding.TryGet(world, out var path))
            {
                output.Block();
                return;
            }
            output.SetSimulationSource(world.NativeSource(binding.Handle));
            output.SetPath(path.Gain, GetSpatialBlend(world, acoustic), path.SimulationResult.NormalizeEq);
        }

        /// <inheritdoc/>
        public float GetSpatialBlend(PlanarAcousticWorld world) => GetSpatialBlend(world, output.Acoustics);

        float GetSpatialBlend(PlanarAcousticWorld world, SoundAcousticSettings acoustic) => acoustic.InheritSpatialBlend
            ? world.GetSpatialBlend(binding.Handle)
            : world.GetSpatialBlend(binding.Handle, acoustic.SpatialBlendProfile, acoustic.MonoDistance, acoustic.FullSpatialDistance);

        /// <summary>Immediately gates sources without current connectivity and probe coverage.</summary>
        public void Gate(PlanarAcousticWorld world, Vector2 listener)
        {
            if (!output.IsActive) return;
            if (source == null ||
                !world.IsRouteCovered(source.transform.position, listener)) Block();
        }

        /// <summary>Silences the active native generation.</summary>
        public void Block() => output.Block();

        /// <summary>Silences output and releases source registration.</summary>
        public void BlockAndDetach()
        {
            Block();
            output.SetSimulationSource(IntPtr.Zero);
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
