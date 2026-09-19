using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Shared playback settings for sound definitions. Assigning this asset replaces the entire local group.</summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Sound Playback Profile", fileName = "SoundPlaybackProfile")]
    public sealed class SoundPlaybackProfile : ScriptableObject
    {
        /// <summary>Base volume range rolled per play.</summary>
        public FloatRange Volume = new(1f, 1f);

        /// <summary>Pitch range rolled per play.</summary>
        public FloatRange Pitch = new(1f, 1f);

        /// <summary>Whether playback loops until stopped.</summary>
        public bool Loop;
    }
}
