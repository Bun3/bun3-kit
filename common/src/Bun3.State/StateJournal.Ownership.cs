// Ownership undo and release of detached transaction members.
using System;
using System.Collections.Generic;

namespace Bun3.State
{
    public sealed partial class StateJournal
    {
        private OwnershipBuffer? _ownership;

        internal void RecordOwnership(StateBinding binding, StateNode? parent,
            ITrackedNode? oldNode, ITrackedNode? newNode)
        {
            if (oldNode == null && newNode == null) return;
            BeginRecord();
            if (_ownership == null)
            {
                _ownership = new OwnershipBuffer();
                _buffers.Add(_ownership);
            }
            var index = _ownership.Add(binding, parent, oldNode, newNode);
            _entries.Add(new UndoEntry(_ownership, index));
        }

        private sealed class OwnershipBuffer : IUndoBuffer
        {
            private readonly List<OwnershipRecord> _records = new List<OwnershipRecord>();

            internal int Add(StateBinding binding, StateNode? parent, ITrackedNode? oldNode, ITrackedNode? newNode)
            {
                var index = _records.Count;
                _records.Add(new OwnershipRecord(binding, parent, oldNode, newNode));
                return index;
            }

            public void Restore(int index)
            {
                var record = _records[index];
                if (record.Parent == null) record.Binding.RestoreRoot(record.OldNode, record.NewNode);
                else record.Parent.RestoreChild(record.OldNode, record.NewNode);
            }

            internal void ReleaseDetached(ref CompletionFailures failures)
            {
                foreach (var record in _records)
                {
                    Release(record.OldNode, record.Binding, ref failures);
                    Release(record.NewNode, record.Binding, ref failures);
                }
            }

            private static void Release(ITrackedNode? target, StateBinding binding, ref CompletionFailures failures)
            {
                if (target == null) return;
                try
                {
                    var node = target.Tracking;
                    if (!node.IsConnected && node.Parent == null && ReferenceEquals(node.Binding, binding))
                        node.SetMembership(null, false);
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }

            public void Clear() => _records.Clear();

            private readonly struct OwnershipRecord
            {
                internal OwnershipRecord(StateBinding binding, StateNode? parent,
                    ITrackedNode? oldNode, ITrackedNode? newNode)
                { Binding = binding; Parent = parent; OldNode = oldNode; NewNode = newNode; }
                internal StateBinding Binding { get; }
                internal StateNode? Parent { get; }
                internal ITrackedNode? OldNode { get; }
                internal ITrackedNode? NewNode { get; }
            }
        }
    }
}
