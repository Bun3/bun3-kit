using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Logical volume channels mapped to exposed mixer parameters.</summary>
    public enum SoundChannel
    {
        /// <summary>Exposed parameter "MasterVolume".</summary>
        Master,

        /// <summary>Exposed parameter "MusicVolume".</summary>
        Music,

        /// <summary>Exposed parameter "SfxVolume".</summary>
        Sfx,

        /// <summary>Exposed parameter "VoiceVolume" (dialogue).</summary>
        Voice,
    }

    /// <summary>Linear/decibel conversions with a -80 dB silence floor.</summary>
    internal static class AudioMath
    {
        private const float SilenceDb = -80f;

        public static float LinearToDb(float linear)
            => linear <= 0.0001f ? SilenceDb : Mathf.Log10(linear) * 20f;

        public static float DbToLinear(float db)
            => db <= SilenceDb ? 0f : Mathf.Pow(10f, db / 20f);
    }
}
