using System;
using Bun3.Unity.Audio;
using UnityEngine;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>
    /// Wires a <see cref="SoundSystemConfig"/> to Steam Audio: disables the core
    /// linecast occlusion/low-pass pipeline (Steam Audio owns spatialization and
    /// occlusion instead) and chains a per-voice binder that attaches and
    /// configures a <see cref="global::SteamAudio.SteamAudioSource"/> on every
    /// configured SFX voice.
    /// </summary>
    public static class SteamAudioSoundSetup
    {
        // Cached so Apply can detect (and skip) a duplicate chain via reference
        // equality against Delegate.GetInvocationList entries.
        private static readonly Action<AudioSource, SoundDef> Binder = Bind;

        /// <summary>
        /// Sets <see cref="SoundSystemConfig.OcclusionChecksPerFrame"/> to 0 (the
        /// framework's off switch for the core occlusion/low-pass pipeline) and
        /// chains the Steam Audio voice binder onto
        /// <see cref="SoundSystemConfig.OnVoiceConfigured"/>, preserving any
        /// existing hook — the existing hook runs first, the binder second. Safe
        /// to call more than once on the same config: the binder is registered
        /// at most once (checked by reference against the existing invocation
        /// list). Returns <paramref name="config"/> for chaining.
        /// </summary>
        public static SoundSystemConfig Apply(SoundSystemConfig config)
        {
            config.OcclusionChecksPerFrame = 0;

            if (config.OnVoiceConfigured == null)
            {
                config.OnVoiceConfigured = Binder;
                return config;
            }

            // Cold path (setup-time, not the hot play/tick path) — the
            // GetInvocationList allocation is sanctioned here.
            var invocations = config.OnVoiceConfigured.GetInvocationList();
            for (var i = 0; i < invocations.Length; i++)
            {
                if (ReferenceEquals(invocations[i], Binder))
                {
                    return config;
                }
            }

            config.OnVoiceConfigured += Binder;
            return config;
        }

        /// <summary>
        /// Per-play voice binder. Defensive no-op on a null <paramref name="source"/>
        /// or <paramref name="def"/> (chained hooks may be invoked with either null
        /// in tests). 2D voices (<see cref="SoundDef.Spatial"/> is
        /// <see cref="SpatialMode.None"/>) never get a
        /// <see cref="global::SteamAudio.SteamAudioSource"/> attached — attaching one
        /// triggers native Steam Audio simulator registration on <c>Awake</c>, which
        /// a 2D-only voice never needs; an already-attached component (from a pooled
        /// source reused across defs) is disabled instead. 3D voices ensure the
        /// component exists (added once, on first use — the pooled source keeps it
        /// afterward), enable it, and map <see cref="SoundDef.Occlusion"/> onto its
        /// <c>occlusion</c> field. Other Steam Audio fields (<c>occlusionType</c>,
        /// <c>occlusionInput</c>, <c>transmission*</c>) are left at Steam Audio's own
        /// defaults — this adapter only maps spatialization/occlusion on-off, not
        /// per-def transmission tuning.
        /// </summary>
        private static void Bind(AudioSource source, SoundDef def)
        {
            if (source == null || def == null)
            {
                return;
            }

            if (def.Spatial == SpatialMode.None)
            {
                source.spatialize = false;
                if (source.TryGetComponent<global::SteamAudio.SteamAudioSource>(out var existing))
                {
                    existing.enabled = false;
                }

                return;
            }

            source.spatialize = true;
            if (!source.TryGetComponent<global::SteamAudio.SteamAudioSource>(out var steamSource))
            {
                steamSource = source.gameObject.AddComponent<global::SteamAudio.SteamAudioSource>();
            }

            steamSource.enabled = true;
            steamSource.occlusion = def.Occlusion;
        }
    }
}
