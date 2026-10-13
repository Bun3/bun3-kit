// Typed scalar and child-reference value records.
using System;
using System.Collections.Generic;

namespace Bun3.State
{
    public sealed partial class StateJournal
    {
        /// <summary>Records one scalar field's previous value.</summary>
        /// <typeparam name="T">The scalar field type.</typeparam>
        /// <param name="target">The generated state that owns the field.</param>
        /// <param name="slot">The generated field slot.</param>
        /// <param name="oldValue">The value before the change.</param>
        public void RecordValue<T>(IStateValueTarget<T> target, int slot, T oldValue)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            BeginRecord();

            var type = typeof(T);
            if (!_buffersByType.TryGetValue(type, out var untypedBuffer))
            {
                untypedBuffer = new ValueBuffer<T>();
                _buffersByType.Add(type, untypedBuffer);
                _buffers.Add(untypedBuffer);
            }

            var buffer = (ValueBuffer<T>)untypedBuffer;
            var index = buffer.Add(target, slot, oldValue);
            _entries.Add(new UndoEntry(buffer, index));
        }

        private sealed class ValueBuffer<T> : IUndoBuffer
        {
            private readonly List<ValueRecord> _records = new List<ValueRecord>();

            internal int Add(IStateValueTarget<T> target, int slot, T value)
            {
                var index = _records.Count;
                _records.Add(new ValueRecord(target, slot, value));
                return index;
            }

            public void Restore(int index)
            {
                var record = _records[index];
                record.Target.RestoreValue(record.Slot, record.Value);
            }

            public void Clear()
            {
                _records.Clear();
            }

            private readonly struct ValueRecord
            {
                internal ValueRecord(IStateValueTarget<T> target, int slot, T value)
                {
                    Target = target;
                    Slot = slot;
                    Value = value;
                }

                internal IStateValueTarget<T> Target { get; }
                internal int Slot { get; }
                internal T Value { get; }
            }
        }
    }
}
