using UnityEngine;
namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Read-only adapter diagnostics; sampling is performed on the control thread.</summary>
    public interface IPlanarAcousticDiagnostics
    {
        /// <summary>Source registration for native path queries.</summary>
        PlanarAcousticSourceHandle SourceHandle { get; }
        /// <summary>Display label for diagnostic tools.</summary>
        string Label { get; }
        /// <summary>Current position in planar coordinates.</summary>
        Vector2 Position { get; }
        /// <summary>Whether output is currently gated silent.</summary>
        bool IsBlocked { get; }
        /// <summary>Whether path distance attenuation is enabled.</summary>
        bool DistanceAttenuation { get; }
        /// <summary>Authored distance curve, or null for the fallback range.</summary>
        DistanceAttenuationProfile AttenuationProfile { get; }
        /// <summary>Fallback minimum distance.</summary>
        float MinimumDistance { get; }
        /// <summary>Fallback maximum distance.</summary>
        float MaximumDistance { get; }
    }
    /// <summary>Optional output-specific stereo-width diagnostic contract.</summary>
    public interface IPlanarSpatialBlendDiagnostics
    {
        /// <summary>Gets the width requested by this output's effective profile.</summary>
        float GetSpatialBlend(PlanarAcousticWorld world);
    }
}
