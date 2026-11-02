using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.Telemetry.Tests
{
    /// <summary>Test sink that exposes the records it received and its tick count.</summary>
    public class ListSink : ITelemetrySink
    {
        public readonly List<TelemetryRecord> Written = new();
        public int Ticks;
        public bool Remote { get; set; }

        public virtual void Write(TelemetryRecord record) => Written.Add(record);
        public void Tick(double now) => Ticks++;
        public UniTask FlushAsync(TimeSpan timeout) => UniTask.CompletedTask;
    }
}
