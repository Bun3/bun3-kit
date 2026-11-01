using System;
using System.Collections.Generic;
using UnityEngine;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>
    /// One native context, HRTF, looping driver clip and retirement host shared by pooled native SFX outputs.
    /// Main thread only. Renderers retain the context and HRTF natively, so disposal never invalidates a live renderer.
    /// </summary>
    internal sealed class SteamAudioSoundRuntime : IDisposable
    {
        private readonly List<SteamAudioSoundOutput> _outputs = new();
        private SA.Context _context;
        private SA.HRTF _hrtf;
        private SteamAudioSoundRetirementHost _host;
        private bool _disposed;

        internal SteamAudioSoundRuntime()
        {
            SampleRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int frame, out _);
            FrameSize = frame;
            try
            {
                _context = new SA.Context();
                _hrtf = new SA.HRTF(_context, new SA.AudioSettings { samplingRate = SampleRate, frameSize = FrameSize },
                    null, null, 0, SA.HRTFNormType.None);
                DriverClip = AudioClip.Create("Bun3 native SFX driver", FrameSize, 2, SampleRate, false);
                var ones = new float[FrameSize * 2];
                Array.Fill(ones, 1f);
                DriverClip.SetData(ones, 0);
                var host = new GameObject("Bun3 native sound retirement") { hideFlags = HideFlags.HideInHierarchy };
                UnityEngine.Object.DontDestroyOnLoad(host);
                _host = host.AddComponent<SteamAudioSoundRetirementHost>();
                _host.Bind(this);
            }
            catch { Dispose(); throw; }
        }

        internal int SampleRate { get; }
        internal int FrameSize { get; }
        internal AudioClip DriverClip { get; private set; }

        internal SteamAudioPathRenderer CreateRenderer()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SteamAudioSoundRuntime));
            return new SteamAudioPathRenderer(_context, _hrtf, SampleRate, FrameSize);
        }

        internal void Attach(SteamAudioSoundOutput output) => _outputs.Add(output);

        internal void Detach(SteamAudioSoundOutput output)
        {
            _outputs.Remove(output);
            if (_disposed && _outputs.Count == 0) DestroyHost();
        }

        // Backwards: finalizing outputs detach themselves during the loop.
        internal void ControlUpdate()
        {
            for (int i = _outputs.Count - 1; i >= 0; i--) _outputs[i].ControlUpdate();
        }

        /// <summary>Releases the shared context and HRTF and destroys the driver clip; the host outlives outputs still finalizing.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
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
