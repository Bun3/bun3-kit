using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Bun3.Unity.Telemetry.PostHog
{
    /// <summary>Posts a JSON body and returns the HTTP status and body.</summary>
    public interface IPostHogTransport
    {
        /// <summary>Posts <paramref name="json"/> to <paramref name="url"/>. A network failure returns status 0.</summary>
        UniTask<(int Status, string Body)> PostAsync(string url, string json, CancellationToken ct);
    }

    /// <summary>How the sink handles a response.</summary>
    public enum PostHogOutcome : byte
    {
        /// <summary>Accepted.</summary>
        Sent,
        /// <summary>Accepted but the project is over quota; sending stops for the run.</summary>
        QuotaLimited,
        /// <summary>The key was rejected; sending stops for the run.</summary>
        Unauthorized,
        /// <summary>Rejected for good; the batch is discarded.</summary>
        Drop,
        /// <summary>Failed transiently; the batch is requeued.</summary>
        Retry,
    }

    /// <summary>Transport backed by UnityWebRequest.</summary>
    public sealed class UnityWebRequestPostHogTransport : IPostHogTransport
    {
        /// <inheritdoc />
        public async UniTask<(int Status, string Body)> PostAsync(string url, string json, CancellationToken ct)
        {
            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 10;
            try
            {
                await req.SendWebRequest().ToUniTask(cancellationToken: ct);
            }
            catch (UnityWebRequestException e)
            {
                return ((int)e.ResponseCode, e.Text ?? string.Empty);
            }
            return ((int)req.responseCode, req.downloadHandler.text ?? string.Empty);
        }
    }

    /// <summary>Batches records and posts them to PostHog <c>/batch/</c>, one request at a time. Engine log records are never sent.</summary>
    public sealed class PostHogSink : ITelemetrySink
    {
        readonly struct Entry
        {
            public readonly TelemetryRecord Record;
            public readonly string Uuid;
            public Entry(TelemetryRecord record) { Record = record; Uuid = Guid.NewGuid().ToString("D"); }
        }

        readonly string url, apiKey;
        readonly int batchSize, maxQueue;
        readonly float flushSeconds;
        readonly IPostHogTransport transport;
        readonly List<Entry> queue = new();
        bool timerStarted, inFlight, stopped, retryWait;
        double nextDue;

        /// <summary>Creates a sink.</summary>
        public PostHogSink(string host, string apiKey, int batchSize, float flushSeconds, int maxQueue, IPostHogTransport transport)
        {
            url = ZString.Concat(host.TrimEnd('/'), "/batch/");
            this.apiKey = apiKey;
            this.batchSize = Math.Max(1, batchSize);
            this.flushSeconds = flushSeconds;
            this.maxQueue = Math.Max(1, maxQueue);
            this.transport = transport;
        }

        /// <inheritdoc />
        public bool Remote => true;
        internal int QueuedCount => queue.Count;

        /// <inheritdoc />
        public void Write(TelemetryRecord record)
        {
            if (stopped || record.Category == TelemetryCategory.EngineLog) return;
            queue.Add(new Entry(record));
            if (queue.Count > maxQueue) queue.RemoveRange(0, queue.Count - maxQueue);
        }

        /// <inheritdoc />
        public void Tick(double now)
        {
            if (!timerStarted) { timerStarted = true; nextDue = now + flushSeconds; }
            if (stopped || inFlight || queue.Count == 0) return;
            var due = now >= nextDue;
            // After a failure, wait for the next interval even when a full batch is queued.
            if (retryWait ? !due : (queue.Count < batchSize && !due)) return;
            SendOnce(CancellationToken.None, now).Forget();
        }

        /// <inheritdoc />
        public async UniTask FlushAsync(TimeSpan timeout)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                while (inFlight && !stopped) await UniTask.Yield(cts.Token);
                while (!stopped && queue.Count > 0)
                {
                    if (await SendOnce(cts.Token, 0) != PostHogOutcome.Sent) break;
                }
            }
            catch (Exception) { /* Shutdown delivery is best effort. */ }
        }

        async UniTask<PostHogOutcome> SendOnce(CancellationToken ct, double now)
        {
            var count = Math.Min(batchSize, queue.Count);
            var batch = queue.GetRange(0, count);
            queue.RemoveRange(0, count);
            inFlight = true;
            PostHogOutcome outcome;
            var status = 0;
            try
            {
                var (s, body) = await transport.PostAsync(url, BuildBody(batch), ct);
                status = s;
                outcome = Classify(s, body);
            }
            catch (Exception) { outcome = PostHogOutcome.Retry; }
            inFlight = false;

            nextDue = now + flushSeconds;
            retryWait = outcome == PostHogOutcome.Retry;
            switch (outcome)
            {
                case PostHogOutcome.Retry:
                    queue.InsertRange(0, batch);
                    if (queue.Count > maxQueue) queue.RemoveRange(0, queue.Count - maxQueue);
                    break;
                case PostHogOutcome.QuotaLimited:
                case PostHogOutcome.Unauthorized:
                    // The next batch would fail the same way.
                    stopped = true;
                    queue.Clear();
                    Debug.LogWarning(outcome == PostHogOutcome.QuotaLimited
                        ? "[Bun3.Telemetry] PostHog quota limited, sending stopped for this run"
                        : "[Bun3.Telemetry] PostHog 401 unauthorized, sending stopped for this run");
                    break;
                case PostHogOutcome.Drop:
                    Debug.LogWarning(ZString.Format("[Bun3.Telemetry] PostHog dropped a batch of {0} records, status={1}", count, status));
                    break;
            }
            return outcome;
        }

        internal static PostHogOutcome Classify(int status, string body)
        {
            if (status == 200)
                return body != null && body.Contains("\"quota_limited\"") ? PostHogOutcome.QuotaLimited : PostHogOutcome.Sent;
            if (status == 401) return PostHogOutcome.Unauthorized;
            if (status == 400 || status == 413) return PostHogOutcome.Drop;
            if (status == 0 || status == 408 || status == 429 || (status >= 500 && status <= 599)) return PostHogOutcome.Retry;
            return PostHogOutcome.Drop;
        }

        string BuildBody(List<Entry> batch)
        {
            var sb = ZString.CreateStringBuilder();
            try
            {
                sb.Append("{\"api_key\":");
                TelemetryJson.WriteString(ref sb, apiKey);
                sb.Append(",\"batch\":[");
                for (var i = 0; i < batch.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendEvent(ref sb, batch[i]);
                }
                sb.Append("]}");
                return sb.ToString();
            }
            finally { sb.Dispose(); }
        }

        static void Prop(ref Utf16ValueStringBuilder sb, string key, string value)
        {
            sb.Append(",\"");
            sb.Append(key);
            sb.Append("\":");
            TelemetryJson.WriteString(ref sb, value);
        }

        static void AppendEvent(ref Utf16ValueStringBuilder sb, in Entry e)
        {
            var r = e.Record;
            var c = r.Context;
            sb.Append("{\"event\":");
            TelemetryJson.WriteString(ref sb, r.Name);
            sb.Append(",\"timestamp\":\"");
            sb.Append(r.Utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            sb.Append("\",\"uuid\":\"");
            sb.Append(e.Uuid);
            sb.Append("\",\"properties\":{\"distinct_id\":");
            TelemetryJson.WriteString(ref sb, c.DistinctId);
            sb.Append(",\"$process_person_profile\":false");
            Prop(ref sb, "run_id", c.RunId);
            Prop(ref sb, "build", c.Build);
            Prop(ref sb, "environment", c.Environment);
            Prop(ref sb, "scene", c.Scene);
            Prop(ref sb, "language", c.Language);
            if (c.Extra != null)
            {
                for (var i = 0; i < c.Extra.Length; i++)
                {
                    if (c.Extra[i].Value == null) continue;
                    sb.Append(',');
                    TelemetryJson.WriteString(ref sb, c.Extra[i].Key);
                    sb.Append(':');
                    TelemetryJson.WriteValue(ref sb, c.Extra[i].Value);
                }
            }
            sb.Append(",\"seq\":");
            sb.Append(r.Seq.ToString(CultureInfo.InvariantCulture));
            if (r.Props != null)
            {
                for (var i = 0; i < r.Props.Length; i++)
                {
                    sb.Append(',');
                    TelemetryJson.WriteString(ref sb, r.Props[i].Key);
                    sb.Append(':');
                    TelemetryJson.WriteValue(ref sb, r.Props[i].Value);
                }
            }
            sb.Append("}}");
        }
    }
}
