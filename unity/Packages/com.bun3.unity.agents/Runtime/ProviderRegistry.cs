using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Bun3.Unity.Agents
{
    /// <summary>
    /// One AI agent kind the game knows how to display (and optionally watch as a
    /// desktop process). Loaded from JSON manifests — built-ins ship in
    /// StreamingAssets/providers, community add-ons drop into the user directory.
    /// </summary>
    [Serializable]
    public sealed class ProviderDef
    {
        public string provider;      // worker id prefix ("grok" → grok-… workers)
        public string displayName;
        public string icon;          // optional, relative to the manifest file
        public ProcessRule process;  // optional — presence turns on desktop watching

        public HookRule hooks;         // optional — auto-installs hook calls into the CLI's settings

        [Serializable]
        public sealed class ProcessRule
        {
            public string processName;
            public string[] pathHints;
        }

        [Serializable]
        public sealed class HookRule
        {
            public string settingsPath;  // "~/.gemini/settings.json" — ~ expands to the user profile
            public string schema;        // settings shape: "" = Claude/Gemini nested hooks, "cursor" = flat {command} entries
            public string script;        // optional custom hook script (parses the CLI's payload itself);
                                         // empty = the generic beat writer called with -Provider/-State
            public string scriptArgs;    // optional extra arguments appended to a custom script's command
            public EventRule[] events;

            [Serializable]
            public sealed class EventRule
            {
                public string @event;    // hook event name in the target CLI
                public int state;        // AgentState written when it fires (ignored with a custom script)
                public string matcher;   // optional event filter
            }
        }

        [NonSerialized] public string SourceDir;

        public bool WatchesProcess => process != null && !string.IsNullOrEmpty(process.processName);

        public bool InstallsHooks =>
            hooks != null && !string.IsNullOrEmpty(hooks.settingsPath) && hooks.events != null && hooks.events.Length > 0;
    }

    /// <summary>Loads and indexes provider manifests; user add-ons override built-ins.</summary>
    public static class ProviderRegistry
    {
        private static List<ProviderDef> _all;

        public static string BuiltinDirectory => Path.Combine(Application.streamingAssetsPath, "providers");

        public static string UserDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "bun3-agents", "providers");

        public static IReadOnlyList<ProviderDef> All
        {
            get
            {
                if (_all == null)
                {
                    SeedDefaults(UserDirectory);
                    _all = Load(BuiltinDirectory, UserDirectory);
                }

                return _all;
            }
        }

        /// <summary>
        /// Writes the bundled default manifests (Claude, Gemini, Cursor, Codex, ChatGPT)
        /// into the add-on directory unless a file of that name already exists — the
        /// defaults ARE add-ons, sitting right where a community manifest would go, as
        /// live examples users can copy or edit. Edits win; delete a file to get the
        /// bundled version back on next run.
        /// </summary>
        public static void SeedDefaults(string directory)
        {
            Directory.CreateDirectory(directory);
            foreach (var asset in Resources.LoadAll<TextAsset>("Bun3AgentProviders"))
            {
                var path = Path.Combine(directory, asset.name); // "claude.json" (.txt stripped by the importer)
                if (!File.Exists(path))
                    File.WriteAllText(path, asset.text);
            }
        }

        public static ProviderDef Find(string provider)
        {
            if (string.IsNullOrEmpty(provider))
                return null;
            foreach (var def in All)
            {
                if (def.provider == provider)
                    return def;
            }

            return null;
        }

        /// <summary>Later directories win per provider key, so user add-ons can override built-ins.</summary>
        public static List<ProviderDef> Load(params string[] directories)
        {
            var byProvider = new Dictionary<string, ProviderDef>();
            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir))
                    continue;
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                {
                    try
                    {
                        var def = JsonUtility.FromJson<ProviderDef>(File.ReadAllText(file));
                        if (def == null || string.IsNullOrEmpty(def.provider))
                            continue;
                        def.SourceDir = dir;
                        byProvider[def.provider] = def;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[ProviderRegistry] bad manifest {file}: {e.Message}");
                    }
                }
            }

            return new List<ProviderDef>(byProvider.Values);
        }
    }
}
