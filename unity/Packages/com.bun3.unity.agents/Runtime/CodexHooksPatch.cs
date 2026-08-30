using System.Collections.Generic;
using System.Text;

namespace Bun3.Unity.Agents
{
    /// <summary>
    /// Codex hooks live in config.toml as [[hooks.&lt;Event&gt;]] tables (same event
    /// names and stdin payload as Claude's hooks). Our block is bracketed by marker
    /// comments so uninstall strips exactly what install wrote, leaving foreign hooks
    /// and the rest of the file untouched. Pure string-in/string-out for testability.
    /// </summary>
    public static class CodexHooksPatch
    {
        public static string Begin(string marker) => "# " + marker + " hooks begin";
        public static string End(string marker) => "# " + marker + " hooks end";

        /// <summary>Appends our hook tables at the end of the file. Null = already installed.</summary>
        public static string Install(string toml, List<(string evt, string matcher, string command)> entries, string marker)
        {
            toml ??= "";
            if (toml.Contains(Begin(marker)))
                return null;

            var sb = new StringBuilder(toml.TrimEnd());
            sb.Append("\n\n").Append(Begin(marker)).Append('\n');
            foreach (var (evt, matcher, command) in entries)
            {
                sb.Append("[[hooks.").Append(evt).Append("]]\n");
                if (!string.IsNullOrEmpty(matcher))
                    sb.Append("matcher = '").Append(matcher).Append("'\n");
                sb.Append("[[hooks.").Append(evt).Append(".hooks]]\n");
                sb.Append("type = \"command\"\n");
                // TOML literal string: no escapes, so the command must not contain single quotes
                sb.Append("command = '").Append(command).Append("'\n");
                sb.Append("async = true\n");
            }

            sb.Append(End(marker)).Append('\n');
            return sb.ToString();
        }

        /// <summary>Strips our marker block. Null = nothing of ours found.</summary>
        public static string Remove(string toml, string marker)
        {
            if (string.IsNullOrEmpty(toml))
                return null;
            var begin = toml.IndexOf(Begin(marker), System.StringComparison.Ordinal);
            if (begin < 0)
                return null;
            var endMark = toml.IndexOf(End(marker), begin, System.StringComparison.Ordinal);
            var end = endMark < 0 ? toml.Length : endMark + End(marker).Length;
            while (end < toml.Length && (toml[end] == '\n' || toml[end] == '\r'))
                end++;
            return toml.Remove(begin, end - begin).TrimEnd() + "\n";
        }
    }
}
