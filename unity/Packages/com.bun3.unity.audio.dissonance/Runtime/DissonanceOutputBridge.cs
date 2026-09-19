using System;
using System.Threading;
using global::Dissonance.Audio.Playback;
using UnityEngine;

namespace Bun3.Unity.Audio.Dissonance
{
    /// <summary>Scopes explicitly owned output controls to each enabled Dissonance playback lifetime.</summary>
    /// <remarks>Attach to the playback prefab before SamplePlaybackComponent.Start. Control methods are main-thread only.</remarks>
    [DisallowMultipleComponent]
    public sealed class DissonanceOutputBridge : MonoBehaviour, IAudioOutputSubscriber
    {
        private sealed class Diagnostics
        {
            internal long Samples;
        }

        private ExternalAudioRegistry _registry;
        private ExternalAudioSettings _settings;
        private ExternalAudioHandle _handle;
        private Diagnostics _diagnostics;

        /// <summary>Whether the current output registration remains valid.</summary>
        public bool IsRegistered => _handle.IsValid;

        /// <summary>Mono samples observed during this enabled registration, or zero when released.</summary>
        public long ReceivedSamples
        {
            get
            {
                var diagnostics = Volatile.Read(ref _diagnostics);
                return diagnostics == null ? 0 : Interlocked.Read(ref diagnostics.Samples);
            }
        }

        /// <summary>Binds output ownership. Rebinding first restores the previous registration.</summary>
        public void Bind(ExternalAudioRegistry registry, ExternalAudioSettings settings)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (GetComponent<VoicePlayback>() == null || GetComponent<SamplePlaybackComponent>() == null ||
                GetComponent<AudioSource>() == null)
                throw new InvalidOperationException("The bridge requires VoicePlayback, SamplePlaybackComponent and AudioSource on the same object.");

            Unbind();
            _registry = registry;
            _settings = settings;
            if (isActiveAndEnabled) Register();
        }

        /// <summary>Restores owned controls and prevents registration on subsequent enables until bound again.</summary>
        public void Unbind()
        {
            Release();
            _registry = null;
        }

        /// <summary>Changes explicitly owned additional source gain for this enabled lifetime.</summary>
        public void SetGain(float gain) => _handle.SetGain(gain);

        /// <summary>Changes the explicitly owned existing filter for this enabled lifetime.</summary>
        public void SetLowPassCutoff(float cutoff) => _handle.SetLowPassCutoff(cutoff);

        /// <summary>Checks managed components and ownership, without probing native codecs, networking or audibility.</summary>
        public bool TryGetReadiness(out string reason)
        {
            var playback = GetComponent<VoicePlayback>();
            var samples = GetComponent<SamplePlaybackComponent>();
            var source = GetComponent<AudioSource>();
            if (playback == null || samples == null || source == null)
                reason = "Required Dissonance playback components are missing.";
            else if (!isActiveAndEnabled || !playback.isActiveAndEnabled || !samples.isActiveAndEnabled || !source.enabled)
                reason = "Playback output is inactive.";
            else if (!_handle.IsValid)
                reason = "Output has no live external registration.";
            else
            {
                reason = null;
                return true;
            }
            return false;
        }

        void IAudioOutputSubscriber.OnAudioPlayback(ArraySegment<float> data, bool complete)
        {
            var diagnostics = Volatile.Read(ref _diagnostics);
            if (diagnostics != null) Interlocked.Add(ref diagnostics.Samples, data.Count);
        }

        private void OnEnable()
        {
            if (_registry != null) Register();
        }

        private void OnDisable() => Release();

        private void OnDestroy() => Unbind();

        private void Register()
        {
            if (_handle.IsValid) return;
            _handle = _registry.Register(GetComponent<AudioSource>(), _settings);
            Volatile.Write(ref _diagnostics, new Diagnostics());
        }

        private void Release()
        {
            Volatile.Write(ref _diagnostics, null);
            _handle.Release();
            _handle = default;
        }
    }
}
