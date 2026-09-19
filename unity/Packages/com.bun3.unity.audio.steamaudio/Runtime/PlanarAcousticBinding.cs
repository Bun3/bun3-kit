using UnityEngine;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Coordinates output generation lifetime with a planar acoustic world.</summary>
    public interface IPlanarAcousticOutput
    {
        /// <summary>Whether a new output generation needs immediate simulation.</summary>
        bool RequiresSimulation { get; }
        /// <summary>Registers or updates the source before simulation.</summary>
        void Prepare(PlanarAcousticWorld world);
        /// <summary>Publishes the latest native result to the output.</summary>
        void Publish(PlanarAcousticWorld world);
        /// <summary>Applies immediate connectivity gating between scheduled simulations.</summary>
        void Gate(PlanarAcousticWorld world, Vector2 listener);
        /// <summary>Silences the current output generation.</summary>
        void Block();
        /// <summary>Silences output and releases its source registration.</summary>
        void BlockAndDetach();
    }

    /// <summary>Coordinates output generation lifetime with a planar acoustic world.</summary>
    public sealed class PlanarAcousticBinding
    {
        /// <summary>Reusable order-one spherical harmonic coefficient storage.</summary>
        public readonly float[] Coefficients = new float[4];
        PlanarAcousticWorld owner;
        PlanarAcousticSourceHandle handle;
        /// <summary>Current source registration; invalid while detached or capacity constrained.</summary>
        public PlanarAcousticSourceHandle Handle => handle;

        /// <summary>Attaches an active source, retries capacity failures and detaches on world replacement.</summary>
        public void Prepare(PlanarAcousticWorld world, Vector2 position, bool active)
        {
            if (!ReferenceEquals(owner, world)) Detach();
            if (!active) { Detach(); return; }
            owner = world;
            if (!handle.IsValid) world.TryRegisterSource(position, out handle);
            else world.SetSourcePosition(handle, position);
        }

        /// <summary>Applies the fallback native attenuation minimum to the current source.</summary>
        public bool SetSourceMinimumDistance(float minimumDistance) =>
            owner != null && !owner.IsDisposed && handle.IsValid && owner.SetSourceMinimumDistance(handle, minimumDistance);

        /// <summary>Copies a valid path into the retained coefficient buffer.</summary>
        public bool TryGet(PlanarAcousticWorld world, out PlanarAcousticPath path)
        {
            path = default;
            return ReferenceEquals(world, owner) && handle.IsValid && world.TryGetPath(handle, Coefficients, out path);
        }

        /// <summary>Applies the fallback maximum distance and edge fade.</summary>
        public bool SetSourceDistanceRange(float maximum, float fadeFraction) =>
            owner != null && !owner.IsDisposed && handle.IsValid && owner.SetSourceDistanceRange(handle, maximum, fadeFraction);

        /// <summary>Applies an immutable authored curve or sanitized fallback range without steady-state allocation.</summary>
        public void ApplyDistanceProfile(bool enabled, Bun3.Unity.Audio.DistanceAttenuationProfile profile,
            float fallbackMinimum = 1, float fallbackMaximum = 15)
        {
            if (owner == null || owner.IsDisposed || !handle.IsValid) return;
            owner.SetSourceDistanceAttenuationEnabled(handle, enabled);
            owner.SetSourceDistanceCurve(handle, profile != null ? profile.GetSnapshot() : null);
            if (profile != null) return;
            float minimum = fallbackMinimum;
            float maximum = fallbackMaximum;
            float fade = .2f;
            if (float.IsNaN(minimum) || float.IsInfinity(minimum)) minimum = 0;
            if (float.IsNaN(maximum) || float.IsInfinity(maximum)) maximum = .01f;
            if (float.IsNaN(fade) || float.IsInfinity(fade)) fade = .2f;
            SetSourceMinimumDistance(Mathf.Max(0, minimum));
            SetSourceDistanceRange(Mathf.Max(.01f, maximum), Mathf.Clamp(fade, .01f, 1));
        }

        /// <summary>Releases the source if its owning world is still live.</summary>
        public void Detach()
        {
            if (owner != null && !owner.IsDisposed) owner.RemoveSource(handle);
            owner = null;
            handle = default;
        }
    }
}
