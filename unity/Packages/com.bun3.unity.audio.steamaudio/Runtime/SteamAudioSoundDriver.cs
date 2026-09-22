using UnityEngine;

namespace Bun3.Unity.Audio.SteamAudio
{
    internal sealed class SteamAudioSoundDriver : MonoBehaviour
    {
        private SteamAudioSoundOutput _owner;
        internal void Bind(SteamAudioSoundOutput owner) => _owner = owner;
        private void OnAudioFilterRead(float[] samples, int channels) => _owner?.Process(samples, channels);
        private void OnDestroy() => _owner?.Dispose();
    }

    internal sealed class SteamAudioSoundRetirementHost : MonoBehaviour
    {
        private SteamAudioSoundOutput _owner;
        internal void Bind(SteamAudioSoundOutput owner) => _owner = owner;
        private void Update() => _owner?.ControlUpdate();
    }
}
