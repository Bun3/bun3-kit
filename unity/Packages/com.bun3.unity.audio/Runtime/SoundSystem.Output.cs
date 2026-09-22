// Per-source output ownership: retirement precedes source mutation and disposal.
namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        private void RetireVoiceOutput(int slot)
        {
            if (!_outputActive[slot]) return;
            _outputs[slot].Retire();
            _outputActive[slot] = false;
        }

        private void DisposeVoiceOutputs()
        {
            for (var i = 0; i < _outputs.Length; i++)
            {
                var output = _outputs[i];
                output?.Retire();
                _outputActive[i] = false;
                if (_sources[i] != null) _sources[i].Stop();
                output?.Dispose();
                _outputs[i] = null;
            }
        }
    }
}
