// Discards the operation's captured cache generations while preserving recovery ownership.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Bun3.Server.Players
{
    public sealed partial class PlayerRegistry<TPlayer> where TPlayer : Player
    {
        private async ValueTask InvalidateOperationAsync(IReadOnlyList<string> keys, Entry?[] entries, bool recovery, Exception error)
        {
            for (var i = 0; i < keys.Count; i++)
            {
                _discarded.TryAdd(keys[i], 0);
                if (recovery && _recovery.TryAdd(keys[i], 0)) _execution.RetainKey(keys[i]);
                var expected = entries[i];
                if (expected != null && _entries.TryGetValue(keys[i], out var current)
                    && ReferenceEquals(current, expected) && current.Generation == expected.Generation)
                    await InvalidateKeyAsync(keys[i], recovery, error).ConfigureAwait(false);
            }
        }
    }
}
