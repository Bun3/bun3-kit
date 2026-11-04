using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.Telemetry
{
    /// <summary>Keeps records in memory while open so they can be read back by name. Never leaves the device.</summary>
    public sealed class HistorySink : ITelemetrySink
    {
        /// <summary>Default maximum number of kept records.</summary>
        public const int DefaultCapacity = 4000;

        readonly int capacity;
        readonly List<TelemetryRecord> records = new();
        bool open;

        /// <summary>Creates a closed sink.</summary>
        /// <param name="capacity">Maximum number of kept records. Debug-tier records are dropped first, then the oldest.</param>
        public HistorySink(int capacity = DefaultCapacity) => this.capacity = capacity;

        /// <inheritdoc />
        public bool Remote => false;

        /// <summary>True while records are being kept.</summary>
        public bool IsOpen => open;

        /// <summary>Clears the history and starts keeping records. Clears even when already open.</summary>
        public void Open()
        {
            records.Clear();
            open = true;
        }

        /// <summary>Clears the history and stops keeping records.</summary>
        public void Close()
        {
            records.Clear();
            open = false;
        }

        /// <inheritdoc />
        public void Write(TelemetryRecord record)
        {
            if (!open) return;
            records.Add(record);
            if (records.Count > capacity) DropOne();
        }

        // ponytail: linear scan and RemoveAt; cost only appears once the capacity is exceeded.
        void DropOne()
        {
            for (var i = 0; i < records.Count; i++)
                if (TelemetryCategories.Tier(records[i].Category) == TelemetryTier.Debug) { records.RemoveAt(i); return; }
            records.RemoveAt(0);
        }

        /// <summary>Kept records with one of the given names, ordered by sequence number. All records when no name is given.</summary>
        /// <param name="names">Event names to match.</param>
        public IReadOnlyList<TelemetryRecord> Find(params string[] names)
        {
            var found = new List<TelemetryRecord>();
            foreach (var r in records)
                if (names == null || names.Length == 0 || Array.IndexOf(names, r.Name) >= 0) found.Add(r);
            found.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            return found;
        }

        /// <inheritdoc />
        public void Tick(double now) { }

        /// <inheritdoc />
        public UniTask FlushAsync(TimeSpan timeout) => UniTask.CompletedTask;
    }
}
