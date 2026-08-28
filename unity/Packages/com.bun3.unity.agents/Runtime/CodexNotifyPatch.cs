using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Bun3.Unity.Agents
{
    /// <summary>
    /// Codex has a single top-level <c>notify = [...]</c> slot in config.toml instead
    /// of hook events. Installing must not evict whatever already owns the slot, so
    /// the previous command is captured for the wrapper script to chain. Pure
    /// string-in/string-out for testability.
    /// </summary>
    public static class CodexNotifyPatch
    {
        private static readonly Regex NotifyLine =
            new(@"^\s*notify\s*=\s*\[(?<items>[^\]]*)\]\s*$", RegexOptions.Multiline);

        public sealed class Result
        {
            public string Toml;               // updated file content
            public string[] PreviousCommand;  // non-null when a foreign notify was replaced
        }

        /// <summary>Installs our notify command. Null result = already installed. A line
        /// carrying the legacy marker is an older install of ours — replaced without
        /// capturing it as a foreign command (chaining our own old wrapper would double
        /// every heartbeat).</summary>
        public static Result Install(string toml, string ourCommandLine, string marker, string legacyMarker = null)
        {
            toml ??= "";
            var match = NotifyLine.Match(toml);
            if (match.Success && match.Value.Contains(marker))
                return null;

            var line = "notify = [" + ourCommandLine + "]";
            if (match.Success)
            {
                var legacyOurs = legacyMarker != null && match.Value.Contains(legacyMarker);
                return new Result
                {
                    Toml = toml.Remove(match.Index, match.Length).Insert(match.Index, line),
                    PreviousCommand = legacyOurs ? null : ParseItems(match.Groups["items"].Value),
                };
            }

            // top-level keys must precede the first [section]
            var sectionAt = toml.IndexOf("\n[", System.StringComparison.Ordinal);
            var insert = "\n# ai-office\n" + line + "\n";
            return new Result
            {
                Toml = sectionAt < 0 ? toml.TrimEnd() + "\n" + insert : toml.Insert(sectionAt, "\n" + insert),
            };
        }

        /// <summary>Removes our notify line, restoring the previous command when one was
        /// captured. Null = nothing of ours found.</summary>
        public static string Remove(string toml, string marker, string[] previousCommand)
        {
            if (string.IsNullOrEmpty(toml))
                return null;
            var match = NotifyLine.Match(toml);
            if (!match.Success || !match.Value.Contains(marker))
                return null;

            if (previousCommand is { Length: > 0 })
            {
                var items = new List<string>();
                foreach (var item in previousCommand)
                    items.Add("\"" + item.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
                var restored = "notify = [" + string.Join(", ", items) + "]";
                return toml.Remove(match.Index, match.Length).Insert(match.Index, restored);
            }

            return toml.Remove(match.Index, match.Length).Replace("# ai-office\n", "");
        }

        public static string[] ParseItems(string items)
        {
            var result = new List<string>();
            foreach (Match m in Regex.Matches(items, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                result.Add(Regex.Unescape(m.Groups[1].Value));
            return result.ToArray();
        }
    }
}
