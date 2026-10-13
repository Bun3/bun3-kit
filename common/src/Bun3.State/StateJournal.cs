// Global undo order and completion; Values, Ownership, and Lists partials provide typed records.
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Bun3.State
{
    /// <summary>Records ordered state changes. Values, Ownership, and Lists partials implement record storage.</summary>
    public sealed partial class StateJournal
    {
        private readonly List<UndoEntry> _entries = new List<UndoEntry>();
        private readonly Dictionary<Type, IUndoBuffer> _buffersByType = new Dictionary<Type, IUndoBuffer>();
        private readonly List<IUndoBuffer> _buffers = new List<IUndoBuffer>();
        private bool _completed;
        private bool _completionFailed;

        /// <summary>Commits all changes and releases retained undo values.</summary>
        public void Commit()
        {
            Complete(false);
        }

        /// <summary>Restores all changes in reverse order and releases retained undo values.</summary>
        public void Rollback()
        {
            Complete(true);
        }

        private void Complete(bool rollback)
        {
            if (_completed)
            {
                if (_completionFailed)
                {
                    throw new InvalidOperationException(
                        "The previous state completion failed; record a new change to start a fresh cycle.");
                }

                return;
            }

            _completed = true;
            _completionFailed = true;
            var failures = new CompletionFailures();
            try
            {
                if (rollback)
                {
                    for (var i = _entries.Count - 1; i >= 0; i--)
                    {
                        var entry = _entries[i];
                        try
                        {
                            entry.Buffer.Restore(entry.Index);
                        }
                        catch (Exception error)
                        {
                            failures.Add(error);
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    _ownership?.ReleaseDetached(ref failures);
                }
                finally
                {
                    _entries.Clear();
                    foreach (var buffer in _buffers)
                    {
                        buffer.Clear();
                    }
                }
            }

            _completionFailed = failures.HasFailure;
            failures.Throw();
        }

        private struct CompletionFailures
        {
            private ExceptionDispatchInfo? _first;
            private List<Exception>? _multiple;

            internal bool HasFailure => _first != null;

            internal void Add(Exception error)
            {
                if (_first == null) _first = ExceptionDispatchInfo.Capture(error);
                else
                {
                    _multiple ??= new List<Exception> { _first.SourceException };
                    _multiple.Add(error);
                }
            }

            internal void Throw()
            {
                if (_multiple != null)
                    throw new AggregateException("Multiple state completion operations failed.", _multiple);
                _first?.Throw();
            }
        }

        private void BeginRecord()
        {
            if (!_completed) return;
            _completed = false;
            _completionFailed = false;
        }

        private interface IUndoBuffer
        {
            void Restore(int index);
            void Clear();
        }

        private readonly struct UndoEntry
        {
            internal UndoEntry(IUndoBuffer buffer, int index)
            {
                Buffer = buffer;
                Index = index;
            }

            internal IUndoBuffer Buffer { get; }
            internal int Index { get; }
        }
    }
}
