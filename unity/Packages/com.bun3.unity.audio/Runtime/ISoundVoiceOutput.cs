using System;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Whether an output owner accepts a prepared voice.</summary>
    public enum VoiceOutputStartResult
    {
        /// <summary>This request is not handled and prior processing is quiescent; ordinary playback may start.</summary>
        Unsupported,
        /// <summary>The owner prepared its output source and now controls processing and completion.</summary>
        Started,
        /// <summary>The request is handled but cannot start; ordinary dry playback must not start.</summary>
        Unavailable
    }

    /// <summary>An optional output extension that accepts request-local spatial overrides.</summary>
    public interface ISpatialSoundVoiceOutput : ISoundVoiceOutput
    {
        /// <summary>Starts with the effective request mode, leaving the shared definition unchanged.</summary>
        VoiceOutputStartResult TryStart(SoundDef definition, AudioClip selectedClip, float logicalPitch, SpatialMode spatial);
    }

    /// <summary>
    /// A prewarmed per-source output owner. Control methods run on the sound system's thread and must not reenter it.
    /// Implementations own spatial processing, driver configuration and reader-safe native reclamation;
    /// the sound system retains source gain, fades, mixer routing and source Play/Stop.
    /// </summary>
    public interface ISoundVoiceOutput : IDisposable
    {
        /// <summary>
        /// Prepares a fully configured, stopped source before the system calls Play. No per-play allocation.
        /// Return Unavailable while an old reader prevents reuse; never reset reader-owned processing state.
        /// Expected failures return Unavailable rather than throwing. Unsupported requires prior processing to be quiescent.
        /// </summary>
        VoiceOutputStartResult TryStart(SoundDef definition, AudioClip selectedClip, float logicalPitch);

        /// <summary>True only after owned input and output tails are drained, or the owner has safely failed closed.</summary>
        bool IsComplete { get; }

        /// <summary>Updates logical playback speed without changing native output sample rate. Zero pauses progress.</summary>
        void SetPitch(float logicalPitch);

        /// <summary>
        /// Immediately gates this generation and requests retirement without waiting for an audio reader.
        /// Repeated calls are harmless. Reclamation continues independently if the source is stopped or destroyed.
        /// </summary>
        void Retire();
    }
}
