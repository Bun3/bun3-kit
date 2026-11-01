using System;
using System.Collections.Generic;
using UnityEngine;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>
    /// One native context, HRTF, looping driver clip and retirement host shared by pooled native SFX outputs.
    /// Main thread only. Renderers retain the context and HRTF natively, so disposal never invalidates a live renderer.
    /// Audio configuration changes rebuild only what they invalidated: the driver clip when its data was lost, and the
    /// HRTF and every output renderer when the sample rate or DSP frame size changed.
    /// </summary>
    internal sealed class SteamAudioSoundRuntime : IDisposable
    {
        private readonly List<SteamAudioSoundOutput> _outputs = new();
        // Replacements for outputs whose reader was held when the format changed; retried on Update.
        private readonly List<(SteamAudioSoundOutput Output, SteamAudioPathRenderer Renderer)> _pending = new();
        private readonly float[] _probe = new float[2];
        private readonly AudioSettings.AudioConfigurationChangeHandler _onAudioConfigurationChanged;
        private SA.Context _context;
        private SA.HRTF _hrtf;
        private SteamAudioSoundRetirementHost _host;
        private bool _disposed;

        internal SteamAudioSoundRuntime()
        {
            SampleRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int frame, out _);
            FrameSize = frame;
            _onAudioConfigurationChanged = deviceWasChanged =>
            {
                // Isolated so a native failure here cannot skip the core's recovery handler.
                try
                {
                    AudioSettings.GetDSPBufferSize(out int current, out _);
                    HandleAudioConfigurationChanged(AudioSettings.outputSampleRate, current);
                }
                catch (Exception e) { Debug.LogException(e); }
            };
            try
            {
                _context = new SA.Context();
                _hrtf = CreateHrtf(SampleRate, FrameSize);
                CreateDriverClip();
                var host = new GameObject("Bun3 native sound retirement") { hideFlags = HideFlags.HideInHierarchy };
                UnityEngine.Object.DontDestroyOnLoad(host);
                _host = host.AddComponent<SteamAudioSoundRetirementHost>();
                _host.Bind(this);
                AudioSettings.OnAudioConfigurationChanged += _onAudioConfigurationChanged;
            }
            catch { Dispose(); throw; }
        }

        internal int SampleRate { get; private set; }
        internal int FrameSize { get; private set; }
        internal AudioClip DriverClip { get; private set; }

        internal SteamAudioPathRenderer CreateRenderer()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SteamAudioSoundRuntime));
            return new SteamAudioPathRenderer(_context, _hrtf, SampleRate, FrameSize);
        }

        // Ends active outputs and rebuilds only what the change invalidated. Cold path; allocates.
        internal void HandleAudioConfigurationChanged(int sampleRate, int frameSize)
        {
            if (_disposed) return;
            if (sampleRate != SampleRate || frameSize != FrameSize)
            {
                ChangeFormat(sampleRate, frameSize);
                return;
            }
            if (!DriverClipLost()) return;
            for (int i = 0; i < _outputs.Count; i++) _outputs[i].EndForDeviceChange();
            CreateDriverClip();
        }

        private void ChangeFormat(int sampleRate, int frameSize)
        {
            var old = _hrtf;
            _hrtf = CreateHrtf(sampleRate, frameSize);
            SampleRate = sampleRate;
            FrameSize = frameSize;
            try
            {
                for (int i = 0; i < _outputs.Count; i++)
                {
                    var output = _outputs[i];
                    output.EndForDeviceChange();
                    DropPending(output);
                    var renderer = CreateRenderer();
                    if (!output.TryReplaceRenderer(renderer)) _pending.Add((output, renderer));
                }
            }
            // Renderers retain their HRTF natively, so old ones keep it alive past this release.
            finally { old.Release(); }
            CreateDriverClip();
        }

        // Unity empties AudioClip.Create data on a device reset (samples reads zero).
        private bool DriverClipLost() =>
            DriverClip == null || DriverClip.samples == 0 || !DriverClip.GetData(_probe, 0) || _probe[0] != 1f;

        private SA.HRTF CreateHrtf(int sampleRate, int frameSize) =>
            new(_context, new SA.AudioSettings { samplingRate = sampleRate, frameSize = frameSize }, null, null, 0, SA.HRTFNormType.None);

        private void CreateDriverClip()
        {
            if (DriverClip != null) UnityEngine.Object.Destroy(DriverClip);
            DriverClip = AudioClip.Create("Bun3 native SFX driver", FrameSize, 2, SampleRate, false);
            var ones = new float[FrameSize * 2];
            Array.Fill(ones, 1f);
            DriverClip.SetData(ones, 0);
        }

        private void DropPending(SteamAudioSoundOutput output)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Output != output) continue;
                _pending[i].Renderer.Dispose();
                _pending.RemoveAt(i);
            }
        }

        internal void Attach(SteamAudioSoundOutput output) => _outputs.Add(output);

        internal void Detach(SteamAudioSoundOutput output)
        {
            _outputs.Remove(output);
            DropPending(output);
            if (_disposed && _outputs.Count == 0) DestroyHost();
        }

        // Backwards: finalizing outputs detach themselves during the loop.
        internal void ControlUpdate()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
                if (_pending[i].Output.TryReplaceRenderer(_pending[i].Renderer)) _pending.RemoveAt(i);
            for (int i = _outputs.Count - 1; i >= 0; i--) _outputs[i].ControlUpdate();
        }

        /// <summary>Releases the shared context and HRTF and destroys the driver clip; the host outlives outputs still finalizing.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            AudioSettings.OnAudioConfigurationChanged -= _onAudioConfigurationChanged;
            for (int i = 0; i < _pending.Count; i++) _pending[i].Renderer.Dispose();
            _pending.Clear();
            _hrtf?.Release();
            _context?.Release();
            if (DriverClip != null) UnityEngine.Object.Destroy(DriverClip);
            if (_outputs.Count == 0) DestroyHost();
        }

        private void DestroyHost()
        {
            if (_host != null) UnityEngine.Object.Destroy(_host.gameObject);
            _host = null;
        }
    }
}
