using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Bun3.Unity.Steam.Editor
{
    /// <summary>
    /// Explicitly enables the Bun3 Steam adapters on Standalone once Steamworks.NET is compiled, or disables
    /// them before removing it. Never runs on domain reload.
    /// </summary>
    public static class SteamSdkSetup
    {
        /// <summary>Scripting define that gates every Bun3 Steam assembly.</summary>
        public const string Symbol = "BUN3_STEAMWORKS";

        private const string SdkAssembly = "com.rlabrecque.steamworks.net";

        /// <summary>Adds <see cref="Symbol"/> when the Steamworks.NET assembly definition and its loaded types exist; removes it otherwise.</summary>
        [MenuItem("Tools/Bun3/Steam/Sync Installed Adapters")]
        public static void SyncInstalledAdapters()
        {
            bool installed = IsSteamworksInstalled();
            Apply(installed);
            Debug.Log(installed
                ? "Bun3 Steam adapters enabled on Standalone."
                : "Steamworks.NET was not found; Bun3 Steam adapters are disabled on Standalone. Let the SDK finish compiling before synchronizing.");
        }

        /// <summary>Removes <see cref="Symbol"/> from Standalone before Steamworks.NET is removed.</summary>
        [MenuItem("Tools/Bun3/Steam/Disable Adapters")]
        public static void DisableAdapters()
        {
            Apply(false);
            Debug.Log("Bun3 Steam adapters disabled on Standalone.");
        }

        internal static string[] WithSymbol(string[] current, bool enabled)
        {
            if (enabled == (Array.IndexOf(current, Symbol) >= 0)) return current;
            var updated = new List<string>(current);
            if (enabled) updated.Add(Symbol);
            else updated.RemoveAll(symbol => symbol == Symbol);
            return updated.ToArray();
        }

        private static void Apply(bool enabled)
        {
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone, out string[] current);
            string[] updated = WithSymbol(current, enabled);
            if (ReferenceEquals(updated, current)) return;
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, updated);
            AssetDatabase.SaveAssets();
        }

        private static bool IsSteamworksInstalled()
        {
            bool definition = false;
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)) continue;
                var asset = AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>(path);
                if (asset != null && JsonUtility.FromJson<AssemblyDefinitionName>(asset.text)?.name == SdkAssembly)
                {
                    definition = true;
                    break;
                }
            }
            return definition && Type.GetType("Steamworks.SteamAPI, " + SdkAssembly, false) != null;
        }

        [Serializable]
        private sealed class AssemblyDefinitionName
        {
            public string name = string.Empty;
        }
    }
}
