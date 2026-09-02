using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Lifecycle state of a voice slot.</summary>
    internal enum VoiceState : byte
    {
        Idle,
        FadingIn,
        Playing,
        FadingOut,
    }

    /// <summary>
    /// Per-voice state driven entirely by <see cref="VoiceTable.Tick"/> — no coroutines.
    /// The struct never touches audio APIs; <c>SoundSystem</c> mirrors it onto an AudioSource.
    /// </summary>
    internal struct VoiceSlot
    {
        public uint Generation;
        public VoiceState State;
        public SoundDef Def;
        public float Elapsed;
        public float ClipLength;
        public bool Loop;
        public FadeState Fade;
        public float BaseVolume;
        public float VolumeScale;
        public float Pitch;

        /// <summary>
        /// Effective playback speed (rolled pitch times any timescale multiplier); drives
        /// <see cref="VoiceTable.Tick"/> completion progress, not fades.
        /// </summary>
        public float PlaybackRate;

        public float StartTime;
        public Transform Follow;
        public AutoResetUniTaskCompletionSource Completion;

        /// <summary>
        /// Invoked once when the voice ends (natural end, fade-out, steal, Stop, or dispose),
        /// with the original (now-stale) handle. Registered via
        /// <see cref="SoundSystem.SetCompletionCallback"/>; cleared on allocate/release.
        /// </summary>
        public Action<SoundHandle> CompletionCallback;

        public float OcclusionCurrent;
        public float OcclusionTarget;
    }
}
