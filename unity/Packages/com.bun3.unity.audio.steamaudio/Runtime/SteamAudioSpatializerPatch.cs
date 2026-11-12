using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>
    /// Detects the Bun3 build of the Steam Audio Unity spatializer that adds a pathing stereo width parameter.
    /// The stock plugin lacks the exports and reports unpatched.
    /// </summary>
    internal static class SteamAudioSpatializerPatch
    {
        private const string Library = "audioplugin_phonon";
        private const int SupportedVersion = 1;

        /// <summary>Spatializer parameter index of the pathing stereo width (0 dual mono, 1 full binaural).</summary>
        internal const int PathingStereoWidthParam = 34;

        private static int _detected;

        /// <summary>Whether the loaded spatializer carries the supported patch. Never throws.</summary>
        internal static bool IsPatched
        {
            get
            {
                if (_detected == 0) _detected = Detect() ? 1 : 2;
                return _detected == 1;
            }
        }

        /// <summary>Runs the native pathing width on caller buffers. Test seam; requires <see cref="IsPatched"/>.</summary>
        internal static void ApplyPathingWidthForTest(float[] left, float[] right, ref float current, float target, int sampleRate)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));
            if (left.Length != right.Length) throw new ArgumentException("Channel buffers must have equal length.", nameof(right));
            ApplyPathingWidth(left, right, left.Length, ref current, target, sampleRate);
        }

        private static bool Detect()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
                return false;
            try { return GetPatchVersion() == SupportedVersion; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        [DllImport(Library, EntryPoint = "bun3UnityPluginGetPatchVersion", CallingConvention = CallingConvention.Winapi)]
        private static extern int GetPatchVersion();

        [DllImport(Library, EntryPoint = "bun3UnityPluginApplyPathingWidthForTest", CallingConvention = CallingConvention.Winapi)]
        private static extern void ApplyPathingWidth(float[] left, float[] right, int frames, ref float current, float target, int sampleRate);
    }
}
