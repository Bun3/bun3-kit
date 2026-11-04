using System;
using System.Collections.Generic;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.Telemetry
{
    /// <summary>Routes records to the sinks that subscribed to their tier. Created by <see cref="TelemetryBuilder"/>. Failures in the game callbacks or in a sink never reach the caller.</summary>
    public sealed class TelemetryHub
    {
        sealed class Entry
        {
            public ITelemetrySink Sink;
            public TelemetryTier Tiers;
            public bool Warned;
        }

        readonly string runId, build, environment;
        readonly Func<string> sceneName;
        readonly TelemetryContextFill fill;
        readonly Func<bool> consent;
        readonly Func<DateTime> utcNow;
        readonly string[] dropKeys;
        readonly Entry[] sinks;
        readonly HashSet<string> warnedKeys = new();
        long seq;
        bool recordWarned, fillWarned;

        internal TelemetryHub(string runId, string build, string environment, Func<string> sceneName, TelemetryContextFill fill,
            Func<bool> consent, Func<DateTime> utcNow, string[] dropKeys, List<(ITelemetrySink Sink, TelemetryTier Tiers)> sinks)
        {
            this.runId = runId;
            this.build = build;
            this.environment = environment;
            this.sceneName = sceneName;
            this.fill = fill;
            this.consent = consent;
            this.utcNow = utcNow;
            this.dropKeys = dropKeys;
            this.sinks = new Entry[sinks.Count];
            for (var i = 0; i < this.sinks.Length; i++)
                this.sinks[i] = new Entry { Sink = sinks[i].Sink, Tiers = sinks[i].Tiers };
        }

        /// <summary>The first attached sink of this type, or null.</summary>
        public T Sink<T>() where T : class, ITelemetrySink
        {
            foreach (var e in sinks)
                if (e.Sink is T t) return t;
            return null;
        }

        bool Allowed(Entry e)
        {
            if (!e.Sink.Remote) return true;
            try { return consent == null || consent(); }
            catch { return false; }
        }

        void Fail(Entry e, Exception ex)
        {
            if (e.Warned) return;
            e.Warned = true;
            Debug.LogWarning(ZString.Concat("[Bun3.Telemetry] sink failed: ", e.Sink.GetType().Name, " ", ex.GetType().Name, ": ", ex.Message));
        }

        string ReadScene()
        {
            try { return sceneName(); }
            catch { return null; }
        }

        TelemetryContext BuildContext()
        {
            var context = new TelemetryContext { RunId = runId, Build = build, Environment = environment, Scene = ReadScene() };
            if (fill != null)
            {
                var filled = context;
                try { fill(ref filled); context = filled; }
                catch (Exception ex)
                {
                    // A warning logged here can be routed back into Track, so warn only once.
                    if (!fillWarned)
                    {
                        fillWarned = true;
                        Debug.LogWarning(ZString.Concat("[Bun3.Telemetry] context fill failed: ", ex.GetType().Name, ": ", ex.Message));
                    }
                }
            }
            if (context.Extra != null) context.Extra = Filter(context.Extra);
            return context;
        }

        /// <summary>Records an event and writes it to every sink that subscribed to its tier and is allowed by consent.</summary>
        public void Track(TelemetryCategory category, string name, params (string Key, object Value)[] props)
        {
            TelemetryRecord record;
            try
            {
                record = new TelemetryRecord
                {
                    Name = name,
                    Category = category,
                    Utc = utcNow(),
                    Seq = ++seq,
                    Context = BuildContext(),
                    Props = Filter(props),
                };
            }
            catch (Exception ex)
            {
                // A warning logged here can be routed back into Track, so warn only once.
                if (!recordWarned)
                {
                    recordWarned = true;
                    Debug.LogWarning(ZString.Concat("[Bun3.Telemetry] record failed: ", ex.GetType().Name, ": ", ex.Message));
                }
                return;
            }

            var tier = TelemetryCategories.Tier(category);
            foreach (var e in sinks)
            {
                if ((e.Tiers & tier) == 0 || !Allowed(e)) continue;
                try { e.Sink.Write(record); }
                catch (Exception ex) { Fail(e, ex); }
            }
        }

        (string Key, object Value)[] Filter((string Key, object Value)[] props)
        {
            if (props == null) return Array.Empty<(string, object)>();
            var bad = 0;
            foreach (var p in props)
                if (Array.IndexOf(dropKeys, p.Key) >= 0) bad++;
            if (bad == 0) return props;

            var kept = new (string Key, object Value)[props.Length - bad];
            var n = 0;
            foreach (var p in props)
            {
                if (Array.IndexOf(dropKeys, p.Key) < 0) { kept[n++] = p; continue; }
                if (warnedKeys.Add(p.Key))
                    Debug.LogWarning(ZString.Concat("[Bun3.Telemetry] dropped prop: ", p.Key));
            }
            return kept;
        }

        /// <summary>Gives every allowed sink a periodic update.</summary>
        public void Tick(double now)
        {
            foreach (var e in sinks)
            {
                if (!Allowed(e)) continue;
                try { e.Sink.Tick(now); }
                catch (Exception ex) { Fail(e, ex); }
            }
        }

        /// <summary>Flushes every allowed sink at once, so a slow sink does not hold up the others.</summary>
        public UniTask FlushAsync(TimeSpan timeout)
        {
            var flushes = new List<UniTask>(sinks.Length);
            foreach (var e in sinks)
                if (Allowed(e)) flushes.Add(Flush(e, timeout));
            return UniTask.WhenAll(flushes);
        }

        async UniTask Flush(Entry e, TimeSpan timeout)
        {
            try { await e.Sink.FlushAsync(timeout); }
            catch (Exception ex) { Fail(e, ex); }
        }
    }
}
