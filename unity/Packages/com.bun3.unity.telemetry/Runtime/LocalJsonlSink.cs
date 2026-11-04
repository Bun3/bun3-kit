using System;
using System.Globalization;
using System.IO;
using System.Text;
using Cysharp.Text;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.Telemetry
{
    /// <summary>Writes one JSONL file per run. Lines are buffered and written on a timer and on flush.</summary>
    public sealed class LocalJsonlSink : ITelemetrySink
    {
        const string TruncatedLine = "{\"event\":\"log_truncated\"}";

        readonly StringBuilder buffer = new();
        // ReadTail may run on any thread; the buffer and file writes share this lock.
        readonly object gate = new();
        readonly long maxFileBytes;
        readonly float flushSeconds;
        bool disabled, truncated;
        long bytes;
        double lastFlush = -1;

        /// <inheritdoc />
        public bool Remote => false;

        /// <summary>Path of this run's file.</summary>
        public string FilePath { get; }

        /// <summary>Creates the run file and deletes the oldest run files so at most <paramref name="keepRuns"/> remain.</summary>
        /// <param name="directory">Folder for run files.</param>
        /// <param name="runId">Id of this run, used in the file name.</param>
        /// <param name="startedUtc">Start time, used in the file name.</param>
        /// <param name="keepRuns">How many run files to keep, including this one.</param>
        /// <param name="maxFileBytes">Size cap; after it a truncation marker is written and later records are dropped.</param>
        /// <param name="flushSeconds">Seconds between timed writes.</param>
        public LocalJsonlSink(string directory, string runId, DateTime startedUtc, int keepRuns,
            long maxFileBytes, float flushSeconds = 2f)
        {
            this.maxFileBytes = maxFileBytes;
            this.flushSeconds = flushSeconds;
            FilePath = Path.Combine(directory,
                ZString.Concat(startedUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture),
                    "-", runId, ".jsonl"));
            try
            {
                Directory.CreateDirectory(directory);
                var old = Array.FindAll(Directory.GetFiles(directory, "*.jsonl"), IsRunFile);
                Array.Sort(old, StringComparer.Ordinal);
                var keep = Math.Max(keepRuns - 1, 0);
                var excess = old.Length - (Array.IndexOf(old, FilePath) >= 0 ? 1 : 0) - keep;
                for (var i = 0; i < old.Length && excess > 0; i++)
                {
                    if (old[i] == FilePath) continue;
                    try { File.Delete(old[i]); } catch (Exception) { }
                    excess--;
                }
                File.WriteAllText(FilePath, string.Empty);
            }
            catch (Exception) { disabled = true; }
        }

        // Only yyyyMMdd'T'HHmmss'Z'-<runId>.jsonl names are cleaned up; other files are left alone.
        static bool IsRunFile(string path)
        {
            var n = Path.GetFileName(path);
            if (n.Length < 18 || n[8] != 'T' || n[15] != 'Z' || n[16] != '-') return false;
            for (var i = 0; i < 15; i++)
                if (i != 8 && (n[i] < '0' || n[i] > '9')) return false;
            return true;
        }

        /// <inheritdoc />
        public void Write(TelemetryRecord record)
        {
            if (disabled || truncated) return;
            var line = TelemetryJson.RecordLine(record);
            lock (gate)
            {
                buffer.Append(line).Append('\n');
                bytes += Encoding.UTF8.GetByteCount(line) + 1;
                if (bytes > maxFileBytes)
                {
                    truncated = true;
                    buffer.Append(TruncatedLine).Append('\n');
                }
            }
        }

        /// <inheritdoc />
        public void Tick(double now)
        {
            if (lastFlush < 0) { lastFlush = now; return; }
            if (now - lastFlush < flushSeconds) return;
            lastFlush = now;
            Flush();
        }

        /// <inheritdoc />
        public UniTask FlushAsync(TimeSpan timeout)
        {
            Flush();
            return UniTask.CompletedTask;
        }

        void Flush()
        {
            lock (gate)
            {
                if (disabled || buffer.Length == 0) return;
                try { File.AppendAllText(FilePath, buffer.ToString(), new UTF8Encoding(false)); }
                catch (Exception) { disabled = true; }
                buffer.Clear();
            }
        }

        /// <summary>The end of the file plus lines not yet written. Past <paramref name="maxBytes"/> the front is cut at a line boundary. Safe from any thread.</summary>
        /// <param name="maxBytes">Maximum number of bytes to return.</param>
        public byte[] ReadTail(int maxBytes)
        {
            lock (gate)
            {
                byte[] head = Array.Empty<byte>();
                var cut = false;
                try
                {
                    using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var start = Math.Max(0, fs.Length - maxBytes);
                    fs.Seek(start, SeekOrigin.Begin);
                    head = new BinaryReader(fs).ReadBytes((int)(fs.Length - start));
                    cut = start > 0;
                }
                catch (Exception) { }
                var tail = Encoding.UTF8.GetBytes(buffer.ToString());
                var all = new byte[head.Length + tail.Length];
                Buffer.BlockCopy(head, 0, all, 0, head.Length);
                Buffer.BlockCopy(tail, 0, all, head.Length, tail.Length);
                var begin = Math.Max(0, all.Length - maxBytes);
                if (begin > 0) cut = true;
                if (cut)
                {
                    var nl = Array.IndexOf(all, (byte)'\n', begin > 0 ? begin - 1 : 0);
                    begin = nl < 0 ? all.Length : nl + 1;
                }
                return all.AsSpan(begin).ToArray();
            }
        }
    }
}
