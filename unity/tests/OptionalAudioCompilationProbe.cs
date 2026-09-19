using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEngine;

// Copy into a validation project's Assets/Editor directory, never a runtime assembly.
/// <summary>Verifies compiled optional adapters in a disposable Unity host.</summary>
public static class OptionalAudioCompilationProbe
{
    [Serializable]
    private sealed class Report
    {
        public bool passed;
        public int expectedMask;
        public string error;
        public string[] activeAssemblies;
        public string standaloneDefines;
    }

    /// <summary>Checks assembly inclusion and loadability against the command-line SDK mask.</summary>
    public static void Verify()
    {
        var arguments = Environment.GetCommandLineArgs();
        int mask = int.Parse(Argument(arguments, "-expectedAudioAdapters"));
        string output = Path.GetFullPath(Argument(arguments, "-audioProbeResult"));
        var report = new Report { expectedMask = mask };
        try
        {
            if (EditorApplication.isCompiling) throw new InvalidOperationException("Compilation is still running.");
            var active = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.Editor)) active.Add(assembly.name);
            report.activeAssemblies = new List<string>(active).ToArray();
            report.standaloneDefines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            Require(active.Contains("Bun3.Unity.Audio"), "Common audio must compile in every configuration.");
            Require(active.Contains("Bun3.Unity.SoundEvents"), "Sound events must compile in every configuration.");
            Check(active, "Bun3.Unity.Audio.Dissonance", "Bun3.Unity.Audio.Dissonance.DissonanceRoomScope", (mask & 1) != 0);
            Check(active, "Bun3.Unity.Audio.SteamAudio", "Bun3.Unity.Audio.SteamAudio.SteamAudioPathRenderer", (mask & 2) != 0);
            Check(active, "Bun3.Unity.Audio.Dissonance.Netcode", "Bun3.Unity.Audio.Dissonance.Netcode.DissonanceNfgoSessionScope", (mask & 4) != 0);
            Check(active, "Bun3.Unity.Audio.Dissonance.SteamAudio", "Bun3.Unity.Audio.Dissonance.SteamAudio.DissonanceSteamAudioPlayback", (mask & 3) == 3);
            foreach (string name in active)
            {
                if (name.StartsWith("Bun3.Unity.Audio.Dissonance.SteamAudio", StringComparison.Ordinal))
                    Require((mask & 3) == 3, "Combined adapter tests/editor must require both SDKs.");
                else if (name.StartsWith("Bun3.Unity.Audio.Dissonance.Netcode", StringComparison.Ordinal))
                    Require((mask & 4) != 0, "NGO adapter tests must require the official integration.");
                else if (name.StartsWith("Bun3.Unity.Audio.Dissonance", StringComparison.Ordinal))
                    Require((mask & 1) != 0, "Dissonance tests must remain disabled without its SDK.");
                else if (name.StartsWith("Bun3.Unity.Audio.SteamAudio", StringComparison.Ordinal))
                    Require((mask & 2) != 0, "Steam Audio tests/editor must remain disabled without its SDK.");
            }
            report.passed = true;
        }
        catch (Exception error) { report.error = error.ToString(); throw; }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("Optional audio compilation probe: " + report.passed);
        }
    }

    /// <summary>Checks explicit disable and synchronization without changing unrelated or SDK-owned symbols.</summary>
    public static void VerifySymbolLifecycle()
    {
        var targets = new[] { NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.WebGL };
        var snapshots = new string[targets.Length];
        for (int i = 0; i < targets.Length; i++) snapshots[i] = PlayerSettings.GetScriptingDefineSymbols(targets[i]);
        try
        {
            Bun3.Unity.Audio.Editor.SoundSdkSetup.DisableAdapters();
            for (int i = 0; i < targets.Length; i++)
            {
                var expected = new HashSet<string>(snapshots[i].Split(';'));
                expected.Remove("BUN3_DISSONANCE");
                expected.Remove("BUN3_DISSONANCE_NFGO");
                expected.Remove("BUN3_STEAMAUDIO");
                Require(expected.SetEquals(PlayerSettings.GetScriptingDefineSymbols(targets[i]).Split(';')),
                    "Disabling adapters must preserve every unrelated and vendor symbol.");
            }
            Bun3.Unity.Audio.Editor.SoundSdkSetup.SyncInstalledAdapters();
            for (int i = 0; i < targets.Length; i++)
                Require(new HashSet<string>(snapshots[i].Split(';')).SetEquals(
                    PlayerSettings.GetScriptingDefineSymbols(targets[i]).Split(';')),
                    "Synchronization must restore exactly the installed adapter symbols on every target.");
            Debug.Log("Optional audio symbol lifecycle: PASS");
        }
        finally
        {
            for (int i = 0; i < targets.Length; i++) PlayerSettings.SetScriptingDefineSymbols(targets[i], snapshots[i]);
            AssetDatabase.SaveAssets();
        }
    }

    private static void Check(HashSet<string> assemblies, string assembly, string type, bool expected)
    {
        Require(assemblies.Contains(assembly) == expected, "Unexpected compilation state: " + assembly);
        Require((Type.GetType(type + ", " + assembly, false) != null) == expected, "Unexpected loadable type: " + type);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string Argument(string[] arguments, string name)
    {
        int index = Array.IndexOf(arguments, name);
        if (index < 0 || index + 1 == arguments.Length) throw new ArgumentException("Missing argument: " + name);
        return arguments[index + 1];
    }
}
