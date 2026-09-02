# Vendor: Steam Audio Unity plugin

Steam Audio does not ship a UPM (`.tgz`) package — only a legacy `.unitypackage`,
imported via Unity's `Assets > Import Package > Custom Package` menu. It is NOT
referenced from `unity/Packages/manifest.json`; there is no UPM entry for it.

The extracted file is 122 MB (over the repo's 80 MB commit threshold), so it is
gitignored (`unity/Vendor/*.unitypackage`) instead of committed.

## Download + placement (do this once per machine)

1. Download the Unity integration zip for the release you need (this repo was set
   up against **v4.8.1**):
   `https://github.com/ValveSoftware/steam-audio/releases/download/v4.8.1/steamaudio_unity_4.8.1.zip`
   (138,708,392 bytes at time of writing). For a newer release, browse
   `https://github.com/ValveSoftware/steam-audio/releases` and grab that release's
   `steamaudio_unity_<version>.zip` asset instead.
2. From the zip, extract only `steamaudio_unity/unity/SteamAudio.unitypackage`
   (122,379,594 bytes for v4.8.1) into this folder as:
   `unity/Vendor/SteamAudio.unitypackage`
   (The zip also contains `SteamAudioFMODStudio.unitypackage` and
   `SteamAudioWwise.unitypackage` — not needed; this project uses plain Unity
   audio, not FMOD/Wwise. `doc/` and `symbols/` in the zip are also not needed.)
3. Open the project in the Unity Editor, then **Assets > Import Package > Custom
   Package...**, select `unity/Vendor/SteamAudio.unitypackage`, keep everything
   selected, click **Import**. This lands the plugin under
   `Assets/Plugins/SteamAudio/` (native binaries, runtime + editor scripts,
   default material presets).
4. After import, set the spatializer plugin: **Edit > Project Settings >
   Audio**, set **Spatializer Plugin** to **Steam Audio Spatializer**. (The
   dev project already has this baked into `ProjectSettings/AudioManager.asset`
   as a committed, intentional setting — this step is only needed if that asset
   is reverted or you are setting up a fresh clone without pulling it.)

## What's already committed for you

- `ProjectSettings/AudioManager.asset` — `m_SpatializerPlugin` set to
  `Steam Audio Spatializer` (verified round-trip via SerializedObject after
  import; confirmed live via `AudioSettings.GetSpatializerPluginNames()`).
- This README + the `.gitignore` rule for the binary itself.

The `.unitypackage` binary and the source zip are **not** committed — re-fetch
per the steps above after a fresh clone.
