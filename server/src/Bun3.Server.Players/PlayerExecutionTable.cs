using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Players
{
    internal sealed class PlayerExecutionTable
    {
        internal sealed class Gate
        {
            internal readonly string Key;
            internal readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);
            internal int References;
            internal int Pins;
            internal long Token;
            internal bool Held;
            internal Gate(string key) => Key = key;
        }

        private readonly object _sync = new object();
        private readonly Dictionary<string, Gate> _gates = new Dictionary<string, Gate>(StringComparer.Ordinal);

        internal void RetainKey(string key)
        {
            lock (_sync)
            {
                var gate = Reference(key);
                gate.Pins++;
            }
        }

        internal void ReleaseKey(string key)
        {
            lock (_sync)
            {
                if (!_gates.TryGetValue(key, out var gate) || gate.Pins == 0)
                    throw new InvalidOperationException("Key has no retained reference.");
                gate.Pins--;
                Dereference(gate);
            }
        }

        internal async ValueTask<PlayerExecutionScope> AcquireAsync(string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Player key is empty.", nameof(key));
            ct.ThrowIfCancellationRequested();
            Gate gate;
            lock (_sync) gate = Reference(key);
            try { await gate.Semaphore.WaitAsync(ct).ConfigureAwait(false); }
            catch
            {
                lock (_sync) Dereference(gate);
                throw;
            }
            lock (_sync)
            {
                gate.Held = true;
                gate.Token = checked(gate.Token + 1);
                return new PlayerExecutionScope(this, gate, gate.Token);
            }
        }

        internal async ValueTask<PlayerExecutionScope> AcquireAsync(IReadOnlyList<string> keys, CancellationToken ct)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            for (var i = 0; i < keys.Count; i++)
                if (string.IsNullOrEmpty(keys[i]) || (i > 0 && StringComparer.Ordinal.Compare(keys[i - 1], keys[i]) >= 0))
                    throw new ArgumentException("Keys must be nonempty, ordinal sorted, and unique.", nameof(keys));
            if (keys.Count == 1) return await AcquireAsync(keys[0], ct).ConfigureAwait(false);
            var acquired = new PlayerExecutionScope[keys.Count];
            var count = 0;
            try
            {
                for (; count < keys.Count; count++) acquired[count] = await AcquireAsync(keys[count], ct).ConfigureAwait(false);
                return new PlayerExecutionScope(acquired);
            }
            catch
            {
                while (count > 0) acquired[--count].Dispose();
                throw;
            }
        }

        internal void Release(Gate gate, long token)
        {
            lock (_sync)
            {
                if (!gate.Held || gate.Token != token) return;
                gate.Held = false;
                gate.Semaphore.Release();
                Dereference(gate);
            }
        }

        private Gate Reference(string key)
        {
            if (!_gates.TryGetValue(key, out var gate)) _gates.Add(key, gate = new Gate(key));
            gate.References++;
            return gate;
        }

        private void Dereference(Gate gate)
        {
            if (--gate.References == 0)
            {
                _gates.Remove(gate.Key);
                gate.Semaphore.Dispose();
            }
        }
    }
}
