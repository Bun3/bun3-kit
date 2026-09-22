using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Bun3.Unity.Audio.Editor
{
    /// <summary>Explicitly enables installed optional audio adapters or disables them before SDK removal.</summary>
    public static class SoundSdkSetup
    {
        private static readonly NamedBuildTarget[] Targets =
        {
            NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.WebGL
        };

        /// <summary>Enables adapters whose SDK assembly assets and required loaded types are available.</summary>
        [MenuItem("Tools/Bun3/Audio/Sync Installed Adapters")]
        public static void SyncInstalledAdapters()
        {
            var assemblies = FindAssemblyAssets();
            bool dissonance = HasSdk(assemblies, "DissonanceVoip", "Dissonance.DissonanceComms",
                "Dissonance.VAD.IVoiceActivationListener", "Dissonance.Audio.Playback.VoicePlayback");
            bool netcode = dissonance && HasSdk(assemblies, "Dissonance.Integrations.UnityNfgo",
                "Dissonance.Integrations.Unity_NFGO.NfgoCommsNetwork") &&
                HasSdk(assemblies, "Unity.Netcode.Runtime", "Unity.Netcode.NetworkManager");
            bool steamAudio = HasSdk(assemblies, "SteamAudioUnity", "SteamAudio.SteamAudioSource",
                "SteamAudio.Source", "SteamAudio.SteamAudioManager", "SteamAudio.API");
            Apply(dissonance, netcode, steamAudio);
            Debug.Log($"Bun3 audio adapters: Dissonance={dissonance}, NGO={netcode}, Steam Audio={steamAudio}. " +
                "SDK imports must finish compiling before synchronization. Applied to Standalone, Android, iOS and WebGL.");
        }

        /// <summary>Disables every optional audio adapter on supported targets before removing SDK assets.</summary>
        [MenuItem("Tools/Bun3/Audio/Disable Adapters")]
        public static void DisableAdapters()
        {
            Apply(false, false, false);
            Debug.Log("Bun3 optional audio adapters disabled on Standalone, Android, iOS and WebGL.");
        }

        private static void Apply(bool dissonance, bool netcode, bool steamAudio)
        {
            foreach (var target in Targets)
            {
                PlayerSettings.GetScriptingDefineSymbols(target, out string[] current);
                var updated = new List<string>(current);
                Set(updated, "BUN3_DISSONANCE", dissonance);
                Set(updated, "BUN3_DISSONANCE_NFGO", netcode);
                Set(updated, "BUN3_STEAMAUDIO", steamAudio);
                if (steamAudio) Set(updated, "STEAMAUDIO_ENABLED", true);
                if (!Same(current, updated))
                    PlayerSettings.SetScriptingDefineSymbols(target, updated.ToArray());
            }
            AssetDatabase.SaveAssets();
        }

        private static void Set(List<string> symbols, string symbol, bool enabled)
        {
            if (!enabled)
                symbols.RemoveAll(value => value == symbol);
            else if (!symbols.Contains(symbol))
                symbols.Add(symbol);
        }

        private static bool Same(string[] current, List<string> updated)
        {
            if (current.Length != updated.Count) return false;
            for (int i = 0; i < current.Length; i++)
                if (current[i] != updated[i]) return false;
            return true;
        }

        private static HashSet<string> FindAssemblyAssets()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)) continue;
                var asset = AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>(path);
                if (asset == null) continue;
                var definition = JsonUtility.FromJson<AssemblyDefinition>(asset.text);
                if (definition != null && !string.IsNullOrEmpty(definition.name)) names.Add(definition.name);
            }
            return names;
        }

        private static bool HasSdk(HashSet<string> assets, string assemblyName, params string[] requiredTypes)
        {
            if (!assets.Contains(assemblyName)) return false;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != assemblyName) continue;
                foreach (string typeName in requiredTypes)
                    if (assembly.GetType(typeName, false) == null) return false;
                return true;
            }
            return false;
        }

        [Serializable]
        private sealed class AssemblyDefinition
        {
            public string name = string.Empty;
        }
    }
}
