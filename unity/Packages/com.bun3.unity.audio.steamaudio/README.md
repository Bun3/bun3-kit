# Bun3 Unity Audio - Steam Audio Adapter

Optional adapter that wires [`com.bun3.unity.audio`](../com.bun3.unity.audio)
to [Steam Audio](https://valvesoftware.github.io/steam-audio/). It turns off
the core package's built-in linecast occlusion/low-pass pipeline and lets
Steam Audio own spatialization and occlusion instead.

## Install

Steam Audio ships as a legacy `.unitypackage`, not a UPM package, so it is
installed once per game project (not referenced from `manifest.json`):

1. Download the Unity integration zip for the Steam Audio release you want
   (this adapter was built and tested against **v4.8.1**) from
   [github.com/ValveSoftware/steam-audio/releases](https://github.com/ValveSoftware/steam-audio/releases) —
   grab `steamaudio_unity_<version>.zip` and extract `SteamAudio.unitypackage`
   from it (the zip also has FMOD/Wwise variants — not needed for plain Unity
   audio).
2. In the Unity Editor: **Assets > Import Package > Custom Package...**,
   select `SteamAudio.unitypackage`, keep everything selected, **Import**.
   This adds `Assets/Plugins/SteamAudio/` and auto-sets the
   `STEAMAUDIO_ENABLED` scripting define via the package's own installer —
   this adapter's assemblies use `defineConstraints: ["STEAMAUDIO_ENABLED"]`,
   so they compile out entirely until Steam Audio is imported (safe to keep
   this adapter package installed either way).
3. **Edit > Project Settings > Audio**, set **Spatializer Plugin** to
   **Steam Audio Spatializer**. The adapter's editor validator
   (`SteamAudioSetupValidator`) logs a warning on domain load if this isn't
   set, since the binder's occlusion/spatialization mapping is a no-op
   without it.

This package itself is a normal embedded UPM package
(`unity/Packages/com.bun3.unity.audio.steamaudio`) depending on
`com.bun3.unity.audio`; it does not declare a Steam Audio UPM dependency
because none exists. (This repo's own dev-project fetch of the
`.unitypackage` — used to build/test the adapter itself — is documented
separately in `unity/Vendor/README.md`.)

## Usage

```csharp
using Bun3.Unity.Audio;
using Bun3.Unity.Audio.SteamAudio;

var config = SteamAudioSoundSetup.Apply(new SoundSystemConfig
{
    SfxVoices = 24,
});
var sound = new SoundSystem(config);
```

`Apply`:

- Sets `SoundSystemConfig.OcclusionChecksPerFrame = 0` — the framework's off
  switch for the core occlusion/low-pass pipeline: no raycasts run and no
  `LowPassFilter` components are attached, since Steam Audio's spatializer
  owns spatialization and occlusion instead. `SoundDef.Occlusion` and
  `SoundDef.Spatial` remain the only game-facing knobs either way.
- Chains a per-voice binder onto `SoundSystemConfig.OnVoiceConfigured`
  (running after any hook already set on the config). Per SFX play, the
  binder:
  - Adds a `SteamAudio.SteamAudioSource` to the voice's `AudioSource`
    GameObject the first time it's used (pooled voices keep it afterward).
  - Sets `AudioSource.spatialize` from `SoundDef.Spatial` (`!= None`).
  - For 3D sounds (`Positional`/`Follow`): enables the `SteamAudioSource` and
    sets its `occlusion` field from `SoundDef.Occlusion`.
  - For 2D sounds (`None`): disables the `SteamAudioSource`.
  - Leaves `occlusionType`, `occlusionInput`, and the `transmission*` fields
    at Steam Audio's own defaults — this adapter maps spatialization/occlusion
    on/off only, not per-def transmission tuning.
- Is idempotent: calling `Apply` again on the same config does not
  double-register the binder.

`Apply` returns the same `config` it was given, for chaining into
`SoundSystem`'s constructor.
