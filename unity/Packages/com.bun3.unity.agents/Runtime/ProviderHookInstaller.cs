using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Bun3.Unity.Agents
{
    /// <summary>
    /// First-run hook setup for provider manifests with a "hooks" rule: copies the
    /// hook script (the manifest's own, or the generic beat writer) to a stable
    /// location and merges hook entries into the target CLI's settings.json (backed
    /// up once). Every provider — Claude included — goes through this same path.
    /// Runs in standalone builds only — the editor must never mutate user settings.
    /// </summary>
    public static class ProviderHookInstaller
    {
        private const string GenericScript = "bun3-agent-beat.ps1";

        /// <summary>Every command we install carries this; uninstall removes by it.</summary>
        public const string CommandMarker = "bun3-agent";

#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            foreach (var def in ProviderRegistry.All)
            {
                if (def.InstallsHooks && !IsDisabled(def.provider))
                    TryInstall(def);
            }
        }
#endif

        /// <summary>Installs one provider's hooks using its manifest paths. Safe to call
        /// from UI; failures only log.</summary>
        public static bool TryInstall(ProviderDef def)
        {
            var script = string.IsNullOrEmpty(def.hooks.script) ? GenericScript : def.hooks.script;
            var scriptDest = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "bun3-agents", "hooks", script);
            try
            {
                var content = ResolveScript(def, script);
                if (content == null)
                    return false;
                return Install(def, ExpandHome(def.hooks.settingsPath), content, scriptDest);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProviderHookInstaller] {def.provider} install failed: {e.Message}");
                return false;
            }
        }

        /// <summary>Script lookup order: an add-on's own file beside its manifest, a
        /// consumer override in StreamingAssets/hooks, then the copy bundled in this
        /// package's Resources (as .ps1.txt TextAssets).</summary>
        private static string ResolveScript(ProviderDef def, string script)
        {
            if (!string.IsNullOrEmpty(def.SourceDir))
            {
                var beside = Path.Combine(def.SourceDir, script);
                if (File.Exists(beside))
                    return File.ReadAllText(beside);
            }

            var streaming = Path.Combine(Application.streamingAssetsPath, "hooks", script);
            if (File.Exists(streaming))
                return File.ReadAllText(streaming);

            var bundled = Resources.Load<TextAsset>("Bun3AgentHooks/" + script);
            return bundled != null ? bundled.text : null;
        }

        private static string CodexOriginalPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "bun3-agents", "hooks", "codex-notify-original.json");

        /// <summary>Codex has one notify slot in config.toml, not hook events; a foreign
        /// occupant is captured so the wrapper script chains it instead of evicting it.</summary>
        private static bool InstallCodexNotify(string settingsPath, string scriptDest)
        {
            var existing = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : "";
            var ourLine = "\"powershell\", \"-NoProfile\", \"-ExecutionPolicy\", \"Bypass\", \"-File\", \""
                          + scriptDest.Replace("\\", "/") + "\"";
            var patched = CodexNotifyPatch.Install(existing, ourLine, CommandMarker);
            if (patched == null)
                return false;

            if (patched.PreviousCommand is { Length: > 0 })
            {
                File.WriteAllText(CodexOriginalPath,
                    Newtonsoft.Json.JsonConvert.SerializeObject(patched.PreviousCommand));
            }

            if (!string.IsNullOrEmpty(existing))
            {
                var backupPath = settingsPath + ".bun3-agents.bak";
                if (!File.Exists(backupPath))
                    File.Copy(settingsPath, backupPath);
            }

            File.WriteAllText(settingsPath, patched.Toml);
            Debug.Log($"[ProviderHookInstaller] codex notify installed into {settingsPath}");
            return true;
        }

        private const string CodexLauncher = "bun3-agent-codex-launcher.ps1";

        /// <summary>Codex proper hooks: appends our [[hooks.*]] block, migrating any
        /// legacy notify install away first (its slot occupant is restored and the old
        /// merged codex-cli worker dropped). Commands point at a fixed-path launcher
        /// with no arguments: Codex trust-hashes the command string, so pointing at
        /// the heartbeat script directly would cost the user a re-approval every time
        /// the script name or its arguments change.</summary>
        private static bool InstallCodexToml(ProviderDef def, string settingsPath, string scriptDest)
        {
            var launcher = Resources.Load<TextAsset>("Bun3AgentHooks/" + CodexLauncher);
            var launcherDest = Path.Combine(Path.GetDirectoryName(scriptDest), CodexLauncher);
            if (launcher != null)
                File.WriteAllText(launcherDest, launcher.text);

            var existing = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : "";

            string[] previous = null;
            if (File.Exists(CodexOriginalPath))
                previous = Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(File.ReadAllText(CodexOriginalPath));
            var withoutNotify = CodexNotifyPatch.Remove(existing, CommandMarker, previous);
            if (withoutNotify != null)
            {
                existing = withoutNotify;
                File.Delete(CodexOriginalPath);
                AgentHeartbeatStore.Remove("codex-cli");
            }

            // a pre-launcher block points commands at the heartbeat script directly:
            // strip and rewrite (costs the user one final trust approval)
            var migratedDirect = false;
            if (existing.Contains(CodexHooksPatch.Begin(CommandMarker)) && !existing.Contains(CodexLauncher))
            {
                existing = CodexHooksPatch.Remove(existing, CommandMarker) ?? existing;
                migratedDirect = true;
            }

            var launcherCommand = $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{launcherDest}\"";
            var entries = new List<(string evt, string matcher, string command)>();
            foreach (var rule in def.hooks.events)
                entries.Add((rule.@event, rule.matcher, launcherCommand));

            var merged = CodexHooksPatch.Install(existing, entries, CommandMarker);
            if (merged == null && withoutNotify == null && !migratedDirect)
                return false; // already installed

            var backupPath = settingsPath + ".bun3-agents.bak";
            if (File.Exists(settingsPath) && !File.Exists(backupPath))
                File.Copy(settingsPath, backupPath);

            File.WriteAllText(settingsPath, merged ?? existing);
            Debug.Log($"[ProviderHookInstaller] codex hooks installed into {settingsPath}");
            return true;
        }

        /// <summary>Removes every hook entry of ours from the CLI's settings file,
        /// leaving foreign hooks untouched. Returns true when something was removed.</summary>
        public static bool Uninstall(string settingsPath)
        {
            if (!File.Exists(settingsPath))
                return false;

            var content = File.ReadAllText(settingsPath);
            string removed;
            if (settingsPath.EndsWith(".toml"))
            {
                var withoutHooks = CodexHooksPatch.Remove(content, CommandMarker);
                string[] previous = null;
                if (File.Exists(CodexOriginalPath))
                    previous = Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(File.ReadAllText(CodexOriginalPath));
                var withoutNotify = CodexNotifyPatch.Remove(withoutHooks ?? content, CommandMarker, previous);
                removed = withoutNotify ?? withoutHooks;
            }
            else
            {
                removed = HookSettingsMerger.RemoveByMarker(content, CommandMarker);
            }

            if (removed == null)
                return false;
            File.WriteAllText(settingsPath, removed);
            return true;
        }

        /// <summary>Cheap installed-state probe for UI: the settings file carries our marker.</summary>
        public static bool LooksInstalled(ProviderDef def)
        {
            var path = ExpandHome(def.hooks.settingsPath);
            return File.Exists(path) && File.ReadAllText(path).Contains(CommandMarker);
        }

        private const string DisabledKey = "HooksDisabled";

        /// <summary>User opt-out per provider — survives restarts so the bootstrap does
        /// not reinstall what the user removed.</summary>
        public static bool IsDisabled(string provider) =>
            PlayerPrefs.GetString(DisabledKey, "").Contains(provider + ";");

        public static void SetDisabled(string provider, bool disabled)
        {
            var current = PlayerPrefs.GetString(DisabledKey, "");
            var token = provider + ";";
            if (disabled && !current.Contains(token))
                PlayerPrefs.SetString(DisabledKey, current + token);
            else if (!disabled)
                PlayerPrefs.SetString(DisabledKey, current.Replace(token, ""));
            PlayerPrefs.Save();
        }

        public static string ExpandHome(string path) =>
            path.StartsWith("~")
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1)
                : path;

        /// <summary>Hook entries a manifest's rule produces. A custom script gets the raw
        /// command (it reads the CLI's own payload); the generic beat writer gets
        /// -Provider/-State arguments per event.</summary>
        public static List<(string evt, string matcher, string command)> BuildEntries(ProviderDef def, string scriptDest)
        {
            var raw = !string.IsNullOrEmpty(def.hooks.script);
            var baseCommand = $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{scriptDest}\"";
            if (raw && !string.IsNullOrEmpty(def.hooks.scriptArgs))
                baseCommand += " " + def.hooks.scriptArgs;
            var entries = new List<(string, string, string)>();
            foreach (var rule in def.hooks.events)
                entries.Add((rule.@event, rule.matcher,
                    raw ? baseCommand : $"{baseCommand} -Provider {def.provider} -State {rule.state}"));
            return entries;
        }

        /// <summary>Paths are injected so tests can run against temp directories.</summary>
        public static bool Install(ProviderDef def, string settingsPath, string scriptContent, string scriptDest)
        {
            // target CLI absent on this machine
            if (!Directory.Exists(Path.GetDirectoryName(settingsPath)) || scriptContent == null)
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(scriptDest));
            File.WriteAllText(scriptDest, scriptContent);

            if (def.hooks.schema == "codex-notify")
                return InstallCodexNotify(settingsPath, scriptDest);
            if (def.hooks.schema == "codex-toml")
                return InstallCodexToml(def, settingsPath, scriptDest);

            var existing = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
            var entries = BuildEntries(def, scriptDest);
            var merged = def.hooks.schema == "cursor"
                ? HookSettingsMerger.MergeCursor(existing, entries)
                : HookSettingsMerger.Merge(existing, entries);
            if (merged == null)
                return false; // already installed

            if (existing != null)
            {
                var backupPath = settingsPath + ".bun3-agents.bak";
                if (!File.Exists(backupPath))
                    File.Copy(settingsPath, backupPath);
            }

            File.WriteAllText(settingsPath, merged);
            Debug.Log($"[ProviderHookInstaller] {def.provider} hooks installed into {settingsPath}");
            return true;
        }
    }
}
