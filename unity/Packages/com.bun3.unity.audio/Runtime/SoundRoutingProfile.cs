using UnityEngine;
using UnityEngine.Audio;

namespace Bun3.Unity.Audio
{
    /// <summary>Shared routing settings for sound definitions. Assigning this asset replaces the entire local group.</summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Sound Routing Profile", fileName = "SoundRoutingProfile")]
    public sealed class SoundRoutingProfile : ScriptableObject
    {
        /// <summary>Target mixer group; null falls back to the system's SFX group.</summary>
        public AudioMixerGroup MixerGroup;

        /// <summary>Optional logical group passed to the system gain resolver; independent of mixer routing.</summary>
        [Tooltip("Logical volume group interpreted by the game's gain resolver, e.g. sfx or ui.")]
        public string VolumeGroup = "sfx";
    }
}
