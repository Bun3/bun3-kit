using System;
using System.Collections.Generic;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.Telemetry.GameAnalytics
{
    /// <summary>
    /// Sends records to GameAnalytics. The SDK is initialized on the first record so the user id is the record's
    /// <see cref="TelemetryContext.DistinctId"/>. Each record goes through the mapping; unmapped records become a design
    /// event named after the record. An SDK failure stops the sink for the run.
    /// </summary>
    public sealed class GameAnalyticsSink : ITelemetrySink
    {
        const int DesignSegments = 5, ProgressionSegments = 3;

        readonly string gameKey, secretKey;
        readonly GameAnalyticsMap map;
        readonly IGameAnalyticsApi api;
        readonly Func<bool> consent;
        readonly List<GameAnalyticsEvent> events = new();
        bool initialized, stopped, warnedName, warnedMap;

        /// <summary>Creates a sink.</summary>
        /// <param name="gameKey">GameAnalytics game key.</param>
        /// <param name="secretKey">GameAnalytics secret key.</param>
        /// <param name="map">The game's mapping, or null to send every record as a design event named after it.</param>
        /// <param name="api">The SDK calls.</param>
        /// <param name="consent">Answers whether remote sending is allowed; read when consent changes.</param>
        public GameAnalyticsSink(string gameKey, string secretKey, GameAnalyticsMap map, IGameAnalyticsApi api, Func<bool> consent)
        {
            this.gameKey = gameKey;
            this.secretKey = secretKey;
            this.map = map;
            this.api = api;
            this.consent = consent;
        }

        /// <inheritdoc />
        public bool Remote => true;

        /// <inheritdoc />
        public void Write(TelemetryRecord record)
        {
            if (stopped) return;
            try
            {
                if (!initialized) Initialize(record.Context);
                if (!Map(record)) return;
                for (var i = 0; i < events.Count; i++) Send(events[i]);
            }
            catch (Exception e) { Stop(e); }
        }

        /// <inheritdoc />
        public void Tick(double now) { }

        /// <inheritdoc />
        public UniTask FlushAsync(TimeSpan timeout) => UniTask.CompletedTask;

        void Initialize(in TelemetryContext c)
        {
            if (!string.IsNullOrEmpty(c.DistinctId)) api.SetCustomId(c.DistinctId);
            api.SetBuild(c.Build);
            api.SetEnvironment(c.Environment);
            api.SetKeys(gameKey, secretKey);
            api.DisableErrorReporting();
            api.Initialize();
            initialized = true;
            Telemetry.ConsentChanged += OnConsentChanged;
        }

        // False when the record is dropped because the mapping threw.
        bool Map(TelemetryRecord record)
        {
            events.Clear();
            bool mapped;
            try { mapped = map != null && map(record, events); }
            catch (Exception e)
            {
                events.Clear();
                if (!warnedMap)
                {
                    warnedMap = true;
                    Debug.LogWarning(ZString.Format("[Bun3.Telemetry] GameAnalytics mapping failed for {0}, record dropped: {1}", record.Name, e.Message));
                }
                return false;
            }
            if (!mapped)
            {
                events.Clear();
                events.Add(GameAnalyticsEvent.Design(record.Name));
            }
            return true;
        }

        void Send(in GameAnalyticsEvent e)
        {
            if (e.Kind == GameAnalyticsEventKind.Design)
            {
                api.Design(Clean(e.Id, DesignSegments), Finite(e.Value));
                return;
            }
            var parts = Clean(e.Id, ProgressionSegments).Split(':');
            api.Progression(e.Kind, parts[0], parts.Length > 1 ? parts[1] : null, parts.Length > 2 ? parts[2] : null);
        }

        string Clean(string id, int maxSegments)
        {
            var cleaned = GameAnalyticsNames.Clean(id, maxSegments, out var changed);
            if (changed && !warnedName)
            {
                warnedName = true;
                Debug.LogWarning(ZString.Format("[Bun3.Telemetry] GameAnalytics id \"{0}\" was sent as \"{1}\"", id, cleaned));
            }
            return cleaned;
        }

        static float? Finite(double? value)
        {
            if (value is not double d) return null;
            var f = (float)d;
            return float.IsFinite(f) ? f : null;
        }

        void OnConsentChanged()
        {
            if (stopped) return;
            bool allowed;
            try { allowed = consent != null && consent(); }
            catch { allowed = false; }
            try { api.SetEnabled(allowed); }
            catch (Exception e) { Stop(e); }
        }

        void Stop(Exception e)
        {
            stopped = true;
            Telemetry.ConsentChanged -= OnConsentChanged;
            Debug.LogWarning(ZString.Format("[Bun3.Telemetry] GameAnalytics failed, sending stopped for this run: {0}", e.Message));
        }
    }
}
