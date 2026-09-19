using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Reusable acoustic settings shared by sound and voice playback.</summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Sound Acoustic Profile", fileName = "SoundAcousticProfile")]
    public sealed class SoundAcousticProfile : ScriptableObject
    {
        /// <summary>Gets or replaces the shared acoustic settings.</summary>
        public SoundAcousticSettings Settings = SoundAcousticSettings.Default;
    }
}
