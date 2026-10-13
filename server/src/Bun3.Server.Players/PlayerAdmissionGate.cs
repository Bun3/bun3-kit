using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Players
{
    internal sealed class PlayerAdmissionGate
    {
        private readonly object _sync = new object();
        private bool _open = true;
        private int _active;
        private TaskCompletionSource<bool>? _drained;
        internal bool IsOpen { get { lock (_sync) return _open; } }
        internal int Active { get { lock (_sync) return _active; } }
        internal bool TryEnter()
        { lock (_sync) { if (!_open) return false; _active++; return true; } }
        internal PlayerAdmissionScope Enter() => TryEnter() ? new PlayerAdmissionScope(this) : default;
        internal void Exit()
        { lock (_sync) { if (--_active == 0 && !_open) _drained?.TrySetResult(true); } }
        internal void Close()
        { lock (_sync) { _open = false; if (_active == 0) _drained?.TrySetResult(true); } }
        internal void Open() { lock (_sync) _open = true; }
        internal async ValueTask DrainAsync(CancellationToken ct)
        {
            Task pending;
            lock (_sync)
            {
                _open = false;
                pending = _active == 0 ? Task.CompletedTask : (_drained ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
            await WaitAsync(pending, ct).ConfigureAwait(false);
        }
        internal static async ValueTask WaitAsync(Task pending, CancellationToken ct)
        {
            if (!ct.CanBeCanceled) { await pending.ConfigureAwait(false); return; }
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), canceled))
            {
                await Task.WhenAny(pending, canceled.Task).ConfigureAwait(false); ct.ThrowIfCancellationRequested();
                await pending.ConfigureAwait(false);
            }
        }
    }

    internal readonly struct PlayerAdmissionScope : IDisposable
    {
        private readonly PlayerAdmissionGate? _gate;
        internal PlayerAdmissionScope(PlayerAdmissionGate gate) => _gate = gate;
        internal bool Entered => _gate != null;
        public void Dispose() => _gate?.Exit();
    }
}
