using System;
using System.Globalization;
using Cysharp.Text;

namespace Bun3.Unity.Telemetry
{
    /// <summary>Minimal JSON writer for telemetry lines.</summary>
    public static class TelemetryJson
    {
        const string Hex = "0123456789abcdef";

        /// <summary>Appends a quoted, escaped JSON string, or null.</summary>
        public static void WriteString(ref Utf16ValueStringBuilder sb, string value)
        {
            if (value == null) { sb.Append("null"); return; }
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u00");
                            sb.Append(Hex[c >> 4]);
                            sb.Append(Hex[c & 15]);
                        }
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>Appends a JSON value; unknown types are written as strings.</summary>
        public static void WriteValue(ref Utf16ValueStringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case float f:
                    if (float.IsNaN(f) || float.IsInfinity(f)) sb.Append("null");
                    else sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
                    else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case string s: WriteString(ref sb, s); break;
                default: WriteString(ref sb, Convert.ToString(value, CultureInfo.InvariantCulture)); break;
            }
        }

        static void Field(ref Utf16ValueStringBuilder sb, string key, string value)
        {
            sb.Append(",\"");
            sb.Append(key);
            sb.Append("\":");
            WriteString(ref sb, value);
        }

        /// <summary>Builds the JSON line of a record: fixed keys, then context extras in order, then <c>props</c>.</summary>
        public static string RecordLine(TelemetryRecord r)
        {
            var sb = ZString.CreateStringBuilder();
            try
            {
                var c = r.Context;
                sb.Append("{\"ts\":\"");
                sb.Append(r.Utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
                sb.Append("\",\"seq\":");
                sb.Append(r.Seq.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"tier\":\"");
                sb.Append(TelemetryCategories.TierName(TelemetryCategories.Tier(r.Category)));
                sb.Append('"');
                Field(ref sb, "category", TelemetryCategories.Name(r.Category));
                Field(ref sb, "event", r.Name);
                Field(ref sb, "distinct_id", c.DistinctId);
                Field(ref sb, "run_id", c.RunId);
                Field(ref sb, "build", c.Build);
                Field(ref sb, "environment", c.Environment);
                Field(ref sb, "scene", c.Scene);
                Field(ref sb, "language", c.Language);
                if (c.Extra != null)
                {
                    foreach (var (key, value) in c.Extra)
                    {
                        if (value == null) continue;
                        sb.Append(',');
                        WriteString(ref sb, key);
                        sb.Append(':');
                        WriteValue(ref sb, value);
                    }
                }
                sb.Append(",\"props\":{");
                if (r.Props != null)
                {
                    for (var i = 0; i < r.Props.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        WriteString(ref sb, r.Props[i].Key);
                        sb.Append(':');
                        WriteValue(ref sb, r.Props[i].Value);
                    }
                }
                sb.Append("}}");
                return sb.ToString();
            }
            finally { sb.Dispose(); }
        }
    }
}
