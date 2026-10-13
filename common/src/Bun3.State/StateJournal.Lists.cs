// Typed inverse records for tracked list mutations.
using System;
using System.Collections.Generic;

namespace Bun3.State
{
    public sealed partial class StateJournal
    {
        private readonly Dictionary<Type, IUndoBuffer> _listBuffers = new Dictionary<Type, IUndoBuffer>();

        internal void RecordList<T>(TrackedList<T> target, ListChange change, int position, T oldValue)
        {
            BeginRecord();
            if (!_listBuffers.TryGetValue(typeof(T), out var untyped))
            {
                untyped = new ListBuffer<T>();
                _listBuffers.Add(typeof(T), untyped);
                _buffers.Add(untyped);
            }
            var buffer = (ListBuffer<T>)untyped;
            var index = buffer.Add(target, change, position, oldValue);
            _entries.Add(new UndoEntry(buffer, index));
        }

        private sealed class ListBuffer<T> : IUndoBuffer
        {
            private readonly List<ListRecord> _records = new List<ListRecord>();

            internal int Add(TrackedList<T> target, ListChange change, int position, T value)
            {
                var index = _records.Count;
                _records.Add(new ListRecord(target, change, position, value));
                return index;
            }

            public void Restore(int index)
            {
                var record = _records[index];
                record.Target.Restore(record.Change, record.Position, record.Value);
            }

            public void Clear() => _records.Clear();

            private readonly struct ListRecord
            {
                internal ListRecord(TrackedList<T> target, ListChange change, int position, T value)
                { Target = target; Change = change; Position = position; Value = value; }
                internal TrackedList<T> Target { get; }
                internal ListChange Change { get; }
                internal int Position { get; }
                internal T Value { get; }
            }
        }
    }
}
