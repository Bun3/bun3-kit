using System;
using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.SteamAudio.Editor
{
    /// <summary>
    /// Warns once per domain load if the project's configured Audio spatializer plugin
    /// is not Steam Audio, since the adapter's occlusion/spatialization binding is a
    /// no-op without it. Log-only: never opens a dialog or blocks, so it is safe under
    /// batchmode/CI Unity invocations.
    /// </summary>
    internal static class SteamAudioSetupValidator
    {
        private const string ExpectedSpatializerName = "Steam Audio Spatializer";

        [InitializeOnLoadMethod]
        private static void Validate()
        {
            var configured = ReadConfiguredSpatializerName();
            if (string.IsNullOrEmpty(configured) || configured == ExpectedSpatializerName)
            {
                return;
            }

            Debug.LogWarning(
                $"Bun3.Unity.Audio.SteamAudio: project's Audio spatializer plugin is " +
                $"\"{configured}\", not \"{ExpectedSpatializerName}\". Set it in " +
                "Project Settings > Audio > Spatializer Plugin for the Steam Audio adapter to work.");
        }

        /// <summary>
        /// Prefers the runtime API; falls back to reading the serialized project asset
        /// directly if that API throws in this editor context (observed to be
        /// unavailable outside Play Mode on some Editor versions).
        /// </summary>
        private static string ReadConfiguredSpatializerName()
        {
            try
            {
                return AudioSettings.GetSpatializerPluginName();
            }
            catch (Exception)
            {
                return ReadSpatializerFromProjectSettingsAsset();
            }
        }

        private static string ReadSpatializerFromProjectSettingsAsset()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
            if (assets == null || assets.Length == 0)
            {
                return null;
            }

            var serialized = new SerializedObject(assets[0]);
            var property = serialized.FindProperty("m_SpatializerPlugin");
            return property != null ? property.stringValue : null;
        }
    }
}
