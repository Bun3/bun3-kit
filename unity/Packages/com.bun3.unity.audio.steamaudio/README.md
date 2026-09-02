# Bun3 Unity Audio - Steam Audio Adapter

Optional adapter that wires [`com.bun3.unity.audio`](../com.bun3.unity.audio)
to [Steam Audio](https://valvesoftware.github.io/steam-audio/). It turns off
the core package's built-in linecast occlusion/low-pass pipeline and lets
Steam Audio own spatialization and occlusion instead.

## Install

Steam Audio ships as a legacy `.unitypackage`, not a UPM package. See
`unity/Vendor/README.md` for the download and import steps. Importing it adds
the `STEAMAUDIO_ENABLED` scripting define via Steam Audio's own installer —
this adapter's assemblies use `defineConstraints: ["STEAMAUDIO_ENABLED"]`, so
they compile out entirely until Steam Audio is imported.

This package itself is a normal embedded UPM package
(`unity/Packages/com.bun3.unity.audio.steamaudio`) depending on
`com.bun3.unity.audio`; it does not declare a Steam Audio UPM dependency
because none exists.

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
  switch for the core occlusion/low-pass pipeline, since Steam Audio replaces
  it.
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
