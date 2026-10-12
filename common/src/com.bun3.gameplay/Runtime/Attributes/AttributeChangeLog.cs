#nullable enable
using System;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>Current-change log shared by every attribute set attached to one pipeline, in emission order.</summary>
    internal sealed class AttributeChangeLog
    {
        private AttributeChange[] _changes = new AttributeChange[16];
        private int _count;

        internal ReadOnlySpan<AttributeChange> Pending => _changes.AsSpan(0, _count);

        internal void Clear() => _count = 0;

        internal void Append(in AttributeChange change)
        {
            if (_count == _changes.Length) Array.Resize(ref _changes, _changes.Length * 2);
            _changes[_count++] = change;
        }
    }
}
