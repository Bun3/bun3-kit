using System;
using System.Collections.Generic;
using Bun3.Unity.Audio;
using UnityEngine;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>
    /// Wires a <see cref="SoundSystemConfig"/> to Steam Audio through one of two separate paths, both of
    /// which disable the core linecast occlusion/low-pass pipeline: <see cref="Apply"/> chains a per-voice
    /// binder that attaches and configures a <see cref="global::SteamAudio.SteamAudioSource"/> on every
    /// configured SFX voice; <see cref="ApplyNativeOutput"/> gives every pooled voice a native
    /// <see cref="SteamAudioSoundOutput"/> instead.
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
        /// Wires <paramref name="config"/> to native pooled SFX output. This is a separate path from <see cref="Apply"/>:
        /// every pooled voice gets a <see cref="SteamAudioSoundOutput"/> that plays its authored clip through the Steam Audio
        /// Unity spatializer, plus a <see cref="PlanarAcousticSfxBinding"/> registered in <paramref name="acousticOutputs"/>
        /// so an acoustic session can publish paths to it. No managed code runs on the audio thread. Sets
        /// <see cref="SoundSystemConfig.OcclusionChecksPerFrame"/> to 0, keeps an existing
        /// <see cref="SoundSystemConfig.OnDisposed"/> hook, and reports the detected <see cref="SteamAudioSpatializerSupport"/>
        /// once per system: an error when the Steam Audio spatializer is not selected, a warning when the stock plugin lacks
        /// near-field mono. Positional voices stay blocked until an acoustic session publishes a covered path. Use one live
        /// <see cref="SoundSystem"/> per config. Returns <paramref name="config"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> or <paramref name="acousticOutputs"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><see cref="SoundSystemConfig.CreateVoiceOutput"/> is already set.</exception>
        public static SoundSystemConfig ApplyNativeOutput(SoundSystemConfig config, PlanarAcousticOutputs acousticOutputs)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (acousticOutputs == null) throw new ArgumentNullException(nameof(acousticOutputs));
            if (config.CreateVoiceOutput != null)
                throw new InvalidOperationException("CreateVoiceOutput is already set; native output owns every pooled voice.");
            var assembly = new NativeOutputAssembly(acousticOutputs);
            config.OcclusionChecksPerFrame = 0;
            config.CreateVoiceOutput = assembly.CreateVoiceOutput;
            config.OnDisposed += assembly.Release;
            return config;
        }

        // Rebuilds its bindings for each SoundSystem built from the config.
        private sealed class NativeOutputAssembly
        {
            private readonly PlanarAcousticOutputs _acousticOutputs;
            private readonly List<PlanarAcousticSfxBinding> _bindings = new();
            private bool _reported;

            internal NativeOutputAssembly(PlanarAcousticOutputs acousticOutputs) => _acousticOutputs = acousticOutputs;

            internal ISoundVoiceOutput CreateVoiceOutput(AudioSource source)
            {
                if (!_reported) Report(SteamAudioSpatializer.Detect());
                var output = new SteamAudioSoundOutput(source);
                var binding = new PlanarAcousticSfxBinding(source, output);
                _acousticOutputs.Register(binding);
                _bindings.Add(binding);
                return output;
            }

            private void Report(SteamAudioSpatializerSupport support)
            {
                _reported = true;
                if (support == SteamAudioSpatializerSupport.Missing)
                    Debug.LogError("Bun3.Unity.Audio.SteamAudio: native output needs Project Settings > Audio > Spatializer Plugin = \""
                        + SteamAudioSpatializer.PluginName + "\"; positional sound plays unspatialized.");
                else if (support == SteamAudioSpatializerSupport.Stock)
                    Debug.LogWarning("Bun3.Unity.Audio.SteamAudio: the stock Steam Audio spatializer has no pathing stereo width; "
                        + "near-field mono is unavailable. Install the patched audioplugin_phonon.dll (see Native~/README.md).");
            }

            // Runs inside SoundSystem.Dispose before pending completions fire, so it must not throw.
            internal void Release()
            {
                for (int i = 0; i < _bindings.Count; i++)
                {
                    try { _acousticOutputs.Unregister(_bindings[i]); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                _bindings.Clear();
                _reported = false;
            }
        }

        /// <summary>
        /// Per-play voice binder. Defensive no-op on a null <paramref name="source"/>
        /// or <paramref name="def"/> (chained hooks may be invoked with either null
        /// in tests). 2D voices (the configured source has zero spatial blend) never get a
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

            if (source.spatialBlend <= 0f)
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
            steamSource.occlusion = def.EffectiveOcclusion;
        }
    }
}
