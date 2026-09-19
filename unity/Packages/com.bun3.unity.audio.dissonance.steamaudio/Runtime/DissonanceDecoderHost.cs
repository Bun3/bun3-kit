using System;
using System.Collections.Generic;
using System.Threading;
using global::Dissonance;
using global::Dissonance.Audio.Playback;
using global::Dissonance.Networking;
using UnityEngine;

namespace Bun3.Unity.Audio.Dissonance.SteamAudio
{
    // Kept outside the playback hierarchy so its main-thread reaper survives source disable/destruction.
    internal sealed class DissonanceDecoderHost : BaseVoicePlayback
    {
        private SpeechSession? _current;
        private DissonancePathPlayback _pump;
        private bool _retiring;
        private bool _resetStream;
        private bool _releaseOwner;
        private bool _inputOpen;
        private readonly List<RemoteChannel> _channels = new();
        internal bool IsRetiring => _retiring;
        internal DissonancePathPlayback Pump => Volatile.Read(ref _pump);
        internal long DroppedCommands { get; private set; }
        public override float Amplitude => Pump?.Amplitude ?? 0;

        protected override SpeechSession? TryGetActiveSession()
        {
            var pump = Pump;
            return pump != null && !pump.IsInputComplete ? _current : null;
        }

        internal SpeechSession? Dequeue(int rate) => _retiring ? null : TryDequeueSession(rate);
        internal void Publish(SpeechSession session, DissonancePathPlayback pump)
        {
            _current = session;
            Volatile.Write(ref _pump, pump);
        }

        internal void Read(float[] output)
        {
            var pump = Pump;
            if (pump == null) Array.Clear(output, 0, output.Length);
            else pump.ReadStereo(output);
        }

        internal void StartInput()
        {
            if (_retiring || _releaseOwner || PlayerName == null) { DroppedCommands++; return; }
            if (_inputOpen) ((IVoicePlaybackInternal)this).StopPlayback();
            ((IVoicePlaybackInternal)this).StartPlayback();
            _inputOpen = true;
        }

        internal void StopInput()
        {
            if (_retiring) { DroppedCommands++; return; }
            if (!_inputOpen) return;
            ((IVoicePlaybackInternal)this).StopPlayback();
            _inputOpen = false;
        }

        internal void Receive(VoicePacket packet)
        {
            if (_retiring || !_inputOpen || _releaseOwner) { DroppedCommands++; return; }
            ((IVoicePlaybackInternal)this).ReceiveAudioPacket(packet);
        }

        internal void Retire(bool resetStream, bool releaseOwner = false)
        {
            _releaseOwner |= releaseOwner;
            _resetStream |= resetStream;
            if (resetStream && _inputOpen)
            {
                ((IVoicePlaybackInternal)this).StopPlayback();
                _inputOpen = false;
            }
            _retiring = true;
            Pump?.RequestRetirement();
            Reclaim();
        }

        private void Reclaim()
        {
            if (!_retiring) return;
            var pump = Pump;
            if (pump != null && !pump.TryDisposeRetired()) return;
            Volatile.Write(ref _pump, null);
            _current = null;
            _channels.Clear();
            if (_resetStream) ((IVoicePlaybackInternal)this).ForceReset();
            _resetStream = false;
            _retiring = false;
            if (_releaseOwner) Destroy(gameObject);
        }

        protected override void Update() => Reclaim();

        internal void ReadChannels(List<RemoteChannel> output)
        {
            var pump = Pump;
            if (pump == null) { output.Clear(); return; }
            if (pump.TryClaimMetadata())
            {
                try
                {
                    if (pump.IsInputComplete) _channels.Clear();
                    else base.GetRemoteChannels(_channels);
                }
                finally { pump.ReleaseMetadata(); }
            }
            output.Clear();
            output.AddRange(_channels);
        }
    }
}
