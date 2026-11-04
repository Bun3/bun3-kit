using System;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.Telemetry
{
    /// <summary>Prints each record as a JSON line to the console. Engine logs are skipped because the console already shows them.</summary>
    public sealed class ConsoleSink : ITelemetrySink
    {
        /// <inheritdoc />
        public bool Remote => false;

        /// <inheritdoc />
        public void Write(TelemetryRecord record)
        {
            if (record.Category == TelemetryCategory.EngineLog) return;
            Debug.Log(ZString.Concat("[Bun3.Telemetry] ", TelemetryJson.RecordLine(record)));
        }

        /// <inheritdoc />
        public void Tick(double now) { }

        /// <inheritdoc />
        public UniTask FlushAsync(TimeSpan timeout) => UniTask.CompletedTask;
    }
}
