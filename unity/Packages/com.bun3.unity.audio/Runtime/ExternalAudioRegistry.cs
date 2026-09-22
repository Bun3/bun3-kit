using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Bun3.Unity.Audio
{
    /// <summary>Main-thread registry owning selected controls, never playback, of external sources.</summary>
    public sealed class ExternalAudioRegistry : IDisposable
    {
        private struct Registration
        {
            internal bool Active;
            internal ulong Generation;
            internal AudioSource Source;
            internal bool OwnsGain;
            internal float OriginalVolume;
            internal bool OwnsRouting;
            internal AudioMixerGroup OriginalGroup;
            internal AudioLowPassFilter Filter;
            internal bool OriginalFilterEnabled;
            internal float OriginalCutoff;
        }

        private readonly Registration[] _registrations;
        private bool _disposed;

        /// <summary>Creates a fixed-capacity registry.</summary>
        public ExternalAudioRegistry(int capacity = 32)
        {
            if (capacity <= 0) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
            _registrations = new Registration[capacity];
        }
        /// <summary>Registers an external source. Duplicate sources and capacity exhaustion throw.</summary>
        public ExternalAudioHandle Register(AudioSource source, ExternalAudioSettings settings)
        {
            if (_disposed) { throw new ObjectDisposedException(nameof(ExternalAudioRegistry)); }
            if (source == null) { throw new ArgumentNullException(nameof(source)); }
            ValidateSettings(source, settings);
            var index = FindAvailableSlot(source, settings.LowPassFilter);
            return AcquireSlot(index, source, settings);
        }

        private static void ValidateSettings(AudioSource source, ExternalAudioSettings settings)
        {
            if (settings.Gain.HasValue) { ValidateGain(settings.Gain.Value); }
            var filter = settings.LowPassFilter;
            if (!ReferenceEquals(filter, null))
            {
                if (filter == null || filter.gameObject != source.gameObject)
                {
                    throw new ArgumentException("The low-pass filter must be alive and belong to the source's GameObject.", nameof(settings));
                }
                ValidateCutoff(settings.LowPassCutoff);
            }
        }

        private int FindAvailableSlot(AudioSource source, AudioLowPassFilter filter)
        {
            var index = -1;
            for (var i = 0; i < _registrations.Length; i++)
            {
                ref var entry = ref _registrations[i];
                if (entry.Active)
                {
                    if (entry.Source == source)
                    {
                        throw new InvalidOperationException("The source is already registered.");
                    }
                    if (entry.Source == null) { ReleaseSlot(i); }
                    else if (filter != null && entry.Filter == filter)
                    {
                        throw new InvalidOperationException("The low-pass filter is already owned by a registration.");
                    }
                }
                if (!entry.Active && entry.Generation != ulong.MaxValue && index < 0) { index = i; }
            }
            if (index < 0) { throw new InvalidOperationException("External audio registry capacity is exhausted."); }

            return index;
        }

        private ExternalAudioHandle AcquireSlot(int index, AudioSource source, ExternalAudioSettings settings)
        {
            var filter = settings.LowPassFilter;
            ref var registration = ref _registrations[index];
            registration.Generation++;
            registration.Active = true;
            registration.Source = source;
            registration.OwnsGain = settings.Gain.HasValue;
            registration.OriginalVolume = registration.OwnsGain ? source.volume : 0f;
            registration.OwnsRouting = settings.OverrideMixerGroup;
            registration.OriginalGroup = registration.OwnsRouting ? source.outputAudioMixerGroup : null;
            registration.Filter = filter;
            if (filter != null)
            {
                registration.OriginalFilterEnabled = filter.enabled;
                registration.OriginalCutoff = filter.cutoffFrequency;
                filter.cutoffFrequency = settings.LowPassCutoff;
                filter.enabled = true;
            }
            if (registration.OwnsGain) { source.volume = settings.Gain.Value; }
            if (registration.OwnsRouting) { source.outputAudioMixerGroup = settings.MixerGroup; }
            return new ExternalAudioHandle(this, index, registration.Generation);
        }
        /// <summary>Restores all owned properties and invalidates all handles. Safe to repeat.</summary>
        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            for (var i = 0; i < _registrations.Length; i++) { ReleaseSlot(i); }
        }

        internal bool IsValid(int index, ulong generation)
            => IsCurrent(index, generation) && _registrations[index].Source != null;

        internal void SetGain(int index, ulong generation, float gain)
        {
            if (!IsValid(index, generation)) { return; }
            ref var registration = ref _registrations[index];
            if (!registration.OwnsGain) { throw new InvalidOperationException("This registration does not own source gain."); }
            ValidateGain(gain);
            registration.Source.volume = gain;
        }

        internal void SetLowPassCutoff(int index, ulong generation, float cutoff)
        {
            if (!IsValid(index, generation)) { return; }
            var filter = _registrations[index].Filter;
            if (ReferenceEquals(filter, null)) { throw new InvalidOperationException("This registration does not own a low-pass filter."); }
            ValidateCutoff(cutoff);
            if (filter != null) { filter.cutoffFrequency = cutoff; }
        }

        internal void Release(int index, ulong generation)
        {
            if (IsCurrent(index, generation)) { ReleaseSlot(index); }
        }

        private bool IsCurrent(int index, ulong generation)
            => !_disposed && (uint)index < (uint)_registrations.Length
                && _registrations[index].Active && _registrations[index].Generation == generation;

        private void ReleaseSlot(int index)
        {
            ref var registration = ref _registrations[index];
            if (!registration.Active) { return; }
            var source = registration.Source;
            if (source != null)
            {
                if (registration.OwnsGain) { source.volume = registration.OriginalVolume; }
                if (registration.OwnsRouting) { source.outputAudioMixerGroup = registration.OriginalGroup; }
            }
            var filter = registration.Filter;
            if (filter != null)
            {
                filter.cutoffFrequency = registration.OriginalCutoff;
                filter.enabled = registration.OriginalFilterEnabled;
            }
            var generation = registration.Generation;
            registration = default;
            registration.Generation = generation;
        }

        private static void ValidateGain(float gain)
        {
            if (float.IsNaN(gain) || gain < 0f || gain > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(gain), "Gain must be finite and in [0,1].");
            }
        }

        private static void ValidateCutoff(float cutoff)
        {
            if (float.IsNaN(cutoff) || cutoff < 10f || cutoff > 22000f)
            {
                throw new ArgumentOutOfRangeException(nameof(cutoff), "Cutoff must be finite and in [10,22000] hertz.");
            }
        }
    }
}
