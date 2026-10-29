using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Shared concurrency settings for sound definitions. Assigning this asset replaces the entire local group.</summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Sound Concurrency Profile", fileName = "SoundConcurrencyProfile")]
    public sealed class SoundConcurrencyProfile : ScriptableObject
    {
        /// <summary>Max simultaneous voices for this def; 0 = unlimited. Exceeding steals the oldest.</summary>
        public int MaxInstances;

        /// <summary>Minimum seconds between retriggers; 0 = none. Blocked plays return an invalid handle.</summary>
        public float Cooldown;

        /// <summary>Voice importance, 0 = most important, 255 = least. See <see cref="SoundDef.Priority"/>.</summary>
        [Range(0, 255)]
        public int Priority = 128;
    }
}
