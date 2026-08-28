using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Bun3.Unity.Agents
{
    /// <summary>
    /// Merges ai-office hook entries into a CLI's settings.json, preserving everything
    /// already there. Works for any CLI using the hooks.&lt;Event&gt;[] → {hooks:[{type,command}]}
    /// settings shape (Claude, Gemini). Which events/commands to merge comes from the
    /// provider manifest — no provider knowledge lives here. Pure string-in/string-out.
    /// </summary>
    public static class HookSettingsMerger
    {
        /// <summary>
        /// Returns the merged settings JSON, or null when every event already carries
        /// its command (nothing to change).
        /// </summary>
        public static string Merge(string existingJson, IEnumerable<(string evt, string matcher, string command)> hookEntries)
        {
            var root = string.IsNullOrWhiteSpace(existingJson) ? new JObject() : JObject.Parse(existingJson);
            if (root["hooks"] is not JObject hooks)
                root["hooks"] = hooks = new JObject();

            var changed = false;
            foreach (var (evt, matcher, command) in hookEntries)
            {
                if (hooks[evt] is not JArray entries)
                    hooks[evt] = entries = new JArray();

                if (ContainsCommand(entries, command))
                    continue;

                var entry = new JObject();
                if (!string.IsNullOrEmpty(matcher))
                    entry["matcher"] = matcher;
                entry["hooks"] = new JArray(new JObject
                {
                    ["type"] = "command",
                    ["command"] = command,
                });
                entries.Add(entry);
                changed = true;
            }

            return changed ? root.ToString() : null;
        }

        /// <summary>
        /// Cursor-shaped merge: hooks.&lt;event&gt;[] holds flat {"command": ...} entries
        /// and the file carries a version field ("~/.cursor/hooks.json").
        /// </summary>
        public static string MergeCursor(string existingJson, IEnumerable<(string evt, string matcher, string command)> hookEntries)
        {
            var root = string.IsNullOrWhiteSpace(existingJson) ? new JObject() : JObject.Parse(existingJson);
            root["version"] ??= 1;
            if (root["hooks"] is not JObject hooks)
                root["hooks"] = hooks = new JObject();

            var changed = false;
            foreach (var (evt, _, command) in hookEntries)
            {
                if (hooks[evt] is not JArray entries)
                    hooks[evt] = entries = new JArray();

                var present = false;
                foreach (var entry in entries)
                {
                    if ((string)entry["command"] == command)
                    {
                        present = true;
                        break;
                    }
                }

                if (present)
                    continue;
                entries.Add(new JObject { ["command"] = command });
                changed = true;
            }

            return changed ? root.ToString() : null;
        }

        /// <summary>
        /// Removes every hook entry whose command contains the marker (our install
        /// namespace), leaving foreign hooks untouched. Handles both the nested
        /// (Claude/Gemini) and flat (Cursor) shapes. Empty leftovers are pruned.
        /// Returns the updated JSON, or null when nothing of ours was found.
        /// </summary>
        public static string RemoveByMarker(string existingJson, string marker)
        {
            if (string.IsNullOrWhiteSpace(existingJson))
                return null;
            var root = JObject.Parse(existingJson);
            if (root["hooks"] is not JObject hooks)
                return null;

            var changed = false;
            foreach (var eventProperty in new List<JProperty>(hooks.Properties()))
            {
                if (eventProperty.Value is not JArray entries)
                    continue;
                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries[i]["hooks"] is JArray inner)
                    {
                        for (var j = inner.Count - 1; j >= 0; j--)
                        {
                            var command = (string)inner[j]["command"];
                            if (command != null && command.Contains(marker))
                            {
                                inner.RemoveAt(j);
                                changed = true;
                            }
                        }

                        if (inner.Count == 0)
                            entries.RemoveAt(i);
                    }
                    else if (((string)entries[i]["command"])?.Contains(marker) == true)
                    {
                        entries.RemoveAt(i); // flat Cursor-style entry
                        changed = true;
                    }
                }

                if (entries.Count == 0)
                    eventProperty.Remove();
            }

            return changed ? root.ToString() : null;
        }

        public static bool IsInstalled(string existingJson, IEnumerable<(string evt, string matcher, string command)> hookEntries)
        {
            if (string.IsNullOrWhiteSpace(existingJson))
                return false;
            var root = JObject.Parse(existingJson);
            if (root["hooks"] is not JObject hooks)
                return false;
            foreach (var (evt, _, command) in hookEntries)
            {
                if (hooks[evt] is not JArray entries || !ContainsCommand(entries, command))
                    return false;
            }

            return true;
        }

        private static bool ContainsCommand(JArray entries, string command)
        {
            foreach (var entry in entries)
            {
                if (entry["hooks"] is not JArray inner)
                    continue;
                foreach (var hook in inner)
                {
                    if ((string)hook["command"] == command)
                        return true;
                }
            }

            return false;
        }
    }
}
