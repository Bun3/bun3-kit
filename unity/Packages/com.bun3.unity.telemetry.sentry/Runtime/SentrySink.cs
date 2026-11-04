using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.Telemetry.Sentry
{
    /// <summary>Handles a record itself. Returns true to skip the default breadcrumb.</summary>
    public delegate bool SentryRecordHook(TelemetryRecord record, ISentryApi api);

    /// <summary>Turns records into Sentry breadcrumbs and keeps tags and the user in step with the common properties. Main thread only.</summary>
    public sealed class SentrySink : ITelemetrySink
    {
        readonly ISentryApi api;
        readonly string[] tagKeys;
        readonly string[] tagValues;
        readonly SentryRecordHook hook;
        bool staticTags;
        string distinctId;

        /// <summary>Creates the sink.</summary>
        /// <param name="api">The Sentry API.</param>
        /// <param name="tagKeys">Keys of <see cref="TelemetryContext.Extra"/> that are mirrored as tags, or null.</param>
        /// <param name="hook">Optional per-record handler, or null.</param>
        public SentrySink(ISentryApi api, IReadOnlyList<string> tagKeys, SentryRecordHook hook)
        {
            this.api = api;
            this.hook = hook;
            this.tagKeys = tagKeys?.ToArray() ?? Array.Empty<string>();
            tagValues = new string[this.tagKeys.Length];
        }

        /// <inheritdoc />
        public bool Remote => true;

        /// <inheritdoc />
        public void Write(TelemetryRecord record)
        {
            var c = record.Context;
            if (!staticTags)
            {
                staticTags = true;
                api.SetTag("run_id", c.RunId);
                api.SetTag("build", c.Build);
                api.SetTag("environment", c.Environment);
            }
            for (var i = 0; i < tagKeys.Length; i++)
            {
                var value = Text(c.Get(tagKeys[i]));
                if (value != tagValues[i]) api.SetTag(tagKeys[i], tagValues[i] = value);
            }
            if (c.DistinctId != distinctId) api.SetUser(distinctId = c.DistinctId);

            if (hook != null && hook(record, api)) return;
            api.AddBreadcrumb(record.Name, TelemetryCategories.Name(record.Category), Data(record.Props),
                record.Category == TelemetryCategory.Analytics);
        }

        static string Text(object value) => value switch
        {
            null => null,
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        static Dictionary<string, string> Data((string Key, object Value)[] props)
        {
            if (props == null || props.Length == 0) return null;
            var data = new Dictionary<string, string>(props.Length);
            foreach (var (key, value) in props)
            {
                var text = Text(value);
                if (text != null) data[key] = text;
            }
            return data;
        }

        /// <inheritdoc />
        public void Tick(double now) { }

        /// <inheritdoc />
        public UniTask FlushAsync(TimeSpan timeout) => UniTask.CompletedTask;
    }
}
