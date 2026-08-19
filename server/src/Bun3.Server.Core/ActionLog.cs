using System;

namespace Bun3.Server.Core
{
    /// <summary>Kind of a log entry.</summary>
    public enum ActionLogEntryKind : byte
    {
        /// <summary>Scope start — <see cref="ActionLogEntry.Text"/> holds the action name and context.</summary>
        ScopeStart,

        /// <summary>Free-form note left by a system (achievement cleared, draw result, pity counter, etc.).</summary>
        Note,

        /// <summary>Structured entry — <see cref="ActionLogEntry.Data"/> holds a system-defined payload
        /// (e.g. InventoryChange from Items), <see cref="ActionLogEntry.Source"/> a source label.</summary>
        Data,
    }

    /// <summary>One entry of the CS audit ledger — indenting by Depth yields an "action trace".</summary>
    public readonly struct ActionLogEntry
    {
        internal ActionLogEntry(ActionLogEntryKind kind, int depth, string? text, string? source, object? data)
        {
            Kind = kind;
            Depth = depth;
            Text = text;
            Source = source;
            Data = data;
        }

        /// <summary>Entry kind.</summary>
        public ActionLogEntryKind Kind { get; }

        /// <summary>Nesting depth (root scope = 0).</summary>
        public int Depth { get; }

        /// <summary>Scope name or note content. Null for Data entries.</summary>
        public string? Text { get; }

        /// <summary>Source label of a structured entry (e.g. to distinguish multiple inventories). Null if unspecified.</summary>
        public string? Source { get; }

        /// <summary>System-defined payload — sinks interpret it by type matching
        /// (e.g. <c>entry.Data is InventoryChange c</c>). Valid only for Data entries.</summary>
        public object? Data { get; }
    }

    /// <summary>Ledger sink — receives the completed batch when the root scope closes (entries
    /// outside any scope are delivered immediately). The span is only valid during the call —
    /// copy or serialize to retain.</summary>
    /// <param name="entries">Completed log entries (in occurrence order, indented by Depth).</param>
    public delegate void ActionLogHandler(ReadOnlySpan<ActionLogEntry> entries);

    /// <summary>Log scope handle — close with using. Scopes must close in reverse order of opening.</summary>
    public readonly struct ActionLogScope : IDisposable
    {
        private readonly ActionLog? _log;
        private readonly int _token;

        internal ActionLogScope(ActionLog? log, int token)
        {
            _log = log;
            _token = token;
        }

        /// <summary>Closes the scope — when the root closes, the batch is delivered to the sink.</summary>
        public void Dispose() => _log?.EndScope(_token);
    }

    /// <summary>
    /// Action log (CS audit ledger) — a general-purpose context, one per session/player. When a
    /// handler opens a root scope, everything happening beneath it — structured entries from each
    /// module (inventory changes attached automatically, etc.) and free-form notes (achievement
    /// cleared, etc.) — lands in one ordered tree, and lower-level logic never needs to know what
    /// initiated it. Scopes nest freely; the completed batch goes to the sink when the root
    /// closes, and out-of-scope entries are delivered immediately so the ledger has no gaps.
    /// Low-frequency audit path, so string/boxing allocations are acceptable. Use only inside a
    /// single session actor (no locks).
    /// </summary>
    public sealed class ActionLog
    {
        private readonly ActionLogHandler _sink;
        private ActionLogEntry[] _entries = Array.Empty<ActionLogEntry>();
        private int _count;
        private int _depth;

        /// <summary>Creates a log with the given ledger sink — a game persists (player, time,
        /// action tree, payload) with one sink adapter and live CS tracing is complete.</summary>
        public ActionLog(ActionLogHandler sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        /// <summary>Opens a scope — put the action and its context in the name (e.g. "BuyItem product=1021 x3").</summary>
        public ActionLogScope BeginScope(string name)
        {
            AppendEntry(ActionLogEntryKind.ScopeStart, name, null, null);
            _depth++;
            return new ActionLogScope(this, _depth);
        }

        /// <summary>Adds a free-form note to the current scope — from any system.
        /// Outside a scope it is delivered immediately as a single-entry batch.</summary>
        public void Log(string note)
        {
            AppendEntry(ActionLogEntryKind.Note, note, null, null);
            FlushIfRoot();
        }

        /// <summary>Adds a structured entry to the current scope — a module ships its own type
        /// as-is and the sink interprets it by type matching. Delivered immediately outside a scope.</summary>
        /// <param name="data">System-defined payload.</param>
        /// <param name="source">Source label (e.g. to distinguish multiple containers).</param>
        public void Append(object data, string? source = null)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            AppendEntry(ActionLogEntryKind.Data, null, source, data);
            FlushIfRoot();
        }

        internal void EndScope(int token)
        {
            if (token != _depth)
            {
                throw new InvalidOperationException("Log scopes must be closed in reverse order of opening.");
            }

            _depth--;
            FlushIfRoot();
        }

        private void AppendEntry(ActionLogEntryKind kind, string? text, string? source, object? data)
        {
            if (_count == _entries.Length)
            {
                Array.Resize(ref _entries, _entries.Length == 0 ? 16 : _entries.Length * 2);
            }

            _entries[_count++] = new ActionLogEntry(kind, _depth, text, source, data);
        }

        private void FlushIfRoot()
        {
            if (_depth != 0 || _count == 0)
            {
                return;
            }

            var count = _count;
            _count = 0;
            _sink(new ReadOnlySpan<ActionLogEntry>(_entries, 0, count));
        }
    }
}
