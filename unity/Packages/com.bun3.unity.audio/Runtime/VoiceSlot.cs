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
        public float FadeElapsed;
        public float FadeDuration;
        public float FadeFrom;
        public float FadeTo;
        public float FadeFactor;
        public float BaseVolume;
        public float VolumeScale;
        public float Pitch;
        public float StartTime;
        public Transform Follow;
        public AutoResetUniTaskCompletionSource Completion;
        public float OcclusionCurrent;
        public float OcclusionTarget;
    }
}
