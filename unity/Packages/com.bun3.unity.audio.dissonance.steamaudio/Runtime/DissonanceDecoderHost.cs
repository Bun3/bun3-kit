using System;
using System.Collections.Generic;
using System.Threading;
using Bun3.Unity.Audio.SteamAudio;
using SA = global::SteamAudio;
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
        private DissonancePlaybackResources _resources;
        private Action<List<RemoteChannel>> _captureChannels;
        private int _channelCapacity;
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
        internal DissonancePathPlayback CreatePump(SpeechSession session, Func<int, int, SteamAudioPathRenderer> factory,
            int rate, int frame, long generation, float[] coefficients, SA.CoordinateSpace3 listener)
        {
            if (_retiring || Pump != null) throw new InvalidOperationException("The previous reader must be reclaimed before reuse.");
            if (_resources == null) _resources = new DissonancePlaybackResources(factory(rate, frame));
            return new DissonancePathPlayback(session, _resources, generation, coefficients, listener);
        }

        internal void Publish(SpeechSession session, DissonancePathPlayback pump)
        {
            _current = session;
            Volatile.Write(ref _pump, pump);
            _captureChannels ??= CaptureChannels;
            pump.SetChannelCapture(_captureChannels, _channelCapacity);
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
            _channelCapacity = Math.Max(_channelCapacity, packet.Channels?.Count ?? 0);
            Pump?.ReserveChannelCapacity(_channelCapacity);
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
            if (pump != null && !pump.TryReclaimRetired(false)) return;
            if (_resetStream)
            {
                _resources?.Dispose();
                _resources = null;
            }
            Volatile.Write(ref _pump, null);
            _current = null;
            _channels.Clear();
            if (_resetStream) ((IVoicePlaybackInternal)this).ForceReset();
            _resetStream = false;
            _retiring = false;
            if (_releaseOwner) Destroy(gameObject);
        }

        protected override void Update()
        {
            Reclaim();
            Pump?.ReserveChannelCapacity(_channelCapacity);
        }

        private void CaptureChannels(List<RemoteChannel> output) => base.GetRemoteChannels(output);

        internal void ReadChannels(List<RemoteChannel> output)
        {
            var pump = Pump;
            if (pump == null) { output.Clear(); return; }
            pump.TryCopyChannels(_channels);
            output.Clear();
            output.AddRange(_channels);
        }
    }
}
