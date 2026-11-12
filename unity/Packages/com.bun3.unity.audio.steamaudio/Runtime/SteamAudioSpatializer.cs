using UnityEngine;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>How far the project's spatializer supports native positional output.</summary>
    public enum SteamAudioSpatializerSupport
    {
        /// <summary>The Steam Audio spatializer is not selected; positional sound plays unspatialized.</summary>
        Missing,

        /// <summary>The stock Steam Audio spatializer; everything except near-field mono works.</summary>
        Stock,

        /// <summary>The Bun3 build with the pathing stereo width parameter.</summary>
        Patched
    }

    /// <summary>Detects the spatializer that native SFX and voice output render through.</summary>
    public static class SteamAudioSpatializer
    {
        /// <summary>Spatializer plugin name of the Steam Audio Unity integration.</summary>
        public const string PluginName = "Steam Audio Spatializer";

        /// <summary>Classifies the selected spatializer plugin. Main thread.</summary>
        public static SteamAudioSpatializerSupport Detect() =>
            Classify(AudioSettings.GetSpatializerPluginName(), SteamAudioSpatializerPatch.IsPatched);

        /// <summary>Classifies a spatializer plugin name and patch presence.</summary>
        public static SteamAudioSpatializerSupport Classify(string pluginName, bool patched) =>
            pluginName != PluginName ? SteamAudioSpatializerSupport.Missing
            : patched ? SteamAudioSpatializerSupport.Patched
            : SteamAudioSpatializerSupport.Stock;
    }

    /// <summary>Parameter indices of the Steam Audio Unity spatializer used by native output.</summary>
    public static class SteamAudioSpatializerParams
    {
        /// <summary>First of the six direct-path feature flags (distance attenuation to reflections).</summary>
        public const int FirstDirectFlag = 0;

        /// <summary>Number of direct-path feature flags native output disables.</summary>
        public const int DirectFlagCount = 6;

        /// <summary>Enables pathing.</summary>
        public const int ApplyPathing = 6;

        /// <summary>Direct path mix level.</summary>
        public const int DirectMixLevel = 23;

        /// <summary>Reflections mix level.</summary>
        public const int ReflectionsMixLevel = 25;

        /// <summary>Applies HRTF to pathing.</summary>
        public const int PathingBinaural = 26;

        /// <summary>Pathing mix level.</summary>
        public const int PathingMixLevel = 27;

        /// <summary>Handle returned by iplUnityAddSource, or -1.</summary>
        public const int SimulationOutputsHandle = 31;

        /// <summary>Normalizes pathing EQ.</summary>
        public const int PathingNormalizeEq = 33;

        /// <summary>Bun3 patch: pathing stereo width, 0 dual mono to 1 full binaural.</summary>
        public const int PathingStereoWidth = SteamAudioSpatializerPatch.PathingStereoWidthParam;

        /// <summary>Configures a source to render only the pathing output of its registered simulation source.</summary>
        public static void ConfigurePathingOnly(AudioSource source)
        {
            for (int i = FirstDirectFlag; i < FirstDirectFlag + DirectFlagCount; i++) source.SetSpatializerFloat(i, 0);
            source.SetSpatializerFloat(ApplyPathing, 1);
            source.SetSpatializerFloat(DirectMixLevel, 0);
            source.SetSpatializerFloat(ReflectionsMixLevel, 0);
            source.SetSpatializerFloat(PathingBinaural, 1);
        }
    }
}
