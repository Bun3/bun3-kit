namespace Bun3.Unity.Audio.Dissonance
{
    /// <summary>An atomic observation of raw microphone speech detection, independent of output volume or permission.</summary>
    public readonly struct DissonanceVoiceActivitySnapshot
    {
        /// <summary>Whether VAD currently detects speech. Always false after the scope is disposed.</summary>
        public bool IsSpeechActive { get; }

        /// <summary>
        /// Cumulative inactive-to-active transitions since subscription, saturating at 2^61 - 1.
        /// Compare successive values to detect short speech that started and stopped between polls.
        /// This count does not preserve transition times or speech duration.
        /// </summary>
        public long ActivationCount { get; }

        internal DissonanceVoiceActivitySnapshot(bool isSpeechActive, long activationCount)
        {
            IsSpeechActive = isSpeechActive;
            ActivationCount = activationCount;
        }
    }
}
