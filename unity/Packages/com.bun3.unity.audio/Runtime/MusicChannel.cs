using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Lifecycle state of a music channel.</summary>
    internal enum MusicState : byte
    {
        Idle,
        FadingIn,
        Playing,
        FadingOut,
    }

    /// <summary>
    /// Per-channel music state driven by the SoundSystem tick — no coroutines.
    /// Never touches audio APIs; SoundSystem.Music.cs mirrors it onto the channel's
    /// intro/loop AudioSource pair.
    /// </summary>
    internal struct MusicChannel
    {
        public MusicState State;
        public MusicDef Def;
        public bool Paused;
        public bool LoopScheduled;
        public double LoopStartDsp;
        public FadeState Fade;
        public AutoResetUniTaskCompletionSource Completion;
    }

    /// <summary>Pure DSP-schedule arithmetic, kept engine-free for direct testing.</summary>
    internal static class MusicMath
    {
        /// <summary>Exact clip length in seconds from sample count (float clip.length loses precision).</summary>
        public static double ClipSeconds(AudioClip clip)
            => (double)clip.samples / clip.frequency;

        /// <summary>Seconds of playback left given the current playhead; clamps at zero.</summary>
        public static double RemainingSeconds(int timeSamples, int totalSamples, int frequency)
        {
            var remaining = (double)(totalSamples - timeSamples) / frequency;
            return remaining > 0.0 ? remaining : 0.0;
        }
    }
}
