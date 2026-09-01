using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Designer-tuned music definition: an optional intro that plays once, then a loop
    /// that repeats until stopped. The asset reference itself is the runtime key.
    /// </summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Music Def", fileName = "MusicDef")]
    public sealed class MusicDef : ScriptableObject
    {
        /// <summary>Optional intro clip played once before the loop; null starts on the loop.</summary>
        public AudioClip Intro;

        /// <summary>Loop clip repeated until the track is stopped or replaced. Required.</summary>
        public AudioClip Loop;

        /// <summary>Track volume multiplier applied on top of fades.</summary>
        public float Volume = 1f;

        /// <summary>Default fade seconds used when PlayMusic is called without an explicit fade.</summary>
        public float DefaultFade = 2f;
    }
}
