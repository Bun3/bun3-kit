# Sound foundation validation

`Invoke-SoundFoundationTests.ps1` runs the sound foundation suites sequentially
in an existing validation project. It fails on Unity process errors, timeouts,
missing reports, zero discovered tests, failures, or skipped/inconclusive cases.
Each execution writes fresh XML reports and Editor logs to a unique directory.
Do not open the same project in another Editor while running it.

```powershell
./unity/tests/Invoke-SoundFoundationTests.ps1 `
    -ProjectPath 'E:/Temp/bun3-sound-validation-20260911' `
    -UnityPath 'E:/Unitys/6000.5.5f1/Editor/Unity.exe' `
    -ResultsDirectory 'E:/Temp/bun3-sound-validation-results'
```

Prepare the project before running the script:

- Install a licensed Unity Editor compatible with the SDKs. The initial
  validation host uses Unity 6000.5.5f1; the package repository targets 6000.3.14f1.
- Reference the local `com.bun3.unity.audio`, `com.bun3.unity.sound-events`,
  `com.bun3.unity.audio.dissonance`, and `com.bun3.unity.audio.steamaudio` package
  directories in `Packages/manifest.json`, and list all four in `testables`.
- Supply the core/common dependencies, UniTask, FixedMathSharp.Lean,
  Addressables, Unity Test Framework, and required Unity modules. The current
  audio asmdefs require Addressables even when its APIs are not exercised.
  The initial host uses Addressables 2.9.1 and Test Framework 1.7.0.
- Install SerializeReference Extensions from
  `https://github.com/Bun3/Unity-SerializeReferenceExtensions.git?path=Assets/MackySoft/MackySoft.SerializeReferenceExtensions`.
  This separate upstream fork fixes the obsolete `AdvancedDropdownItem.children`
  accessor on Unity 6000.5 while retaining it for older Editors. Commit the
  host's `Packages/packages-lock.json` to record its Git revision. The toolkit
  no longer supplies a local copy. Historical validation hosts used embedded
  copies; new hosts should use this Git dependency.
- Install the actual Dissonance and Steam Audio SDKs, their managed assembly
  definitions, and native libraries for the Editor platform. After import and compilation, run **Tools > Bun3 > Audio > Sync Installed
  Adapters**. The runner requires `BUN3_DISSONANCE`, `BUN3_STEAMAUDIO` and
  `STEAMAUDIO_ENABLED` for Standalone and discovered tests from both SDK adapters.
  Unactivated adapters are excluded from compilation, including their tests.

EditMode runs the SDK adapter test namespaces. PlayMode runs external-source
ownership, pooled-handle reentrancy, sound-event lifetimes, and SDK adapter
PlayMode tests. The script does not install dependencies, copy SDK files,
create scenes, change project settings, or launch an interactive Editor window.
Tests that need scenes create their own fixtures.

The full preexisting audio suite is broader than this foundation run. In
particular, its Addressables round-trip tests require the repository's
`Assets/Bun3AudioTestAssets` and matching Addressables setup in the host.

## Optional acoustics, NGO and combined native playback tests

Add `-IncludeExtensions` to run the original foundation suites together with
the grid geometry/coverage tests, NGO binding/host tests and combined
Dissonance/Steam Audio facade and sample-pump tests, plus the core
`SoundVoiceOutputTests` ownership suite. The default invocation above remains
unchanged.

Before enabling this mode, install these additional dependencies through your
normal Unity package workflow:

- `com.bun3.unity.audio.dissonance.netcode`,
  `com.bun3.unity.audio.dissonance.steamaudio`, and `com.bun3.unity.acoustics`,
  using local `file:` references to the same checkout as this script.
  Do **not** add these three packages to
  `testables`; retain the original four foundation `testables` entries.
- `com.unity.netcode.gameobjects`, plus the official Dissonance NGO integration.
  Its runtime assembly must be named `Dissonance.Integrations.UnityNfgo`;
  give its Editor and Demo folders separate assembly definitions.
- The actual SDKs and native libraries for the Editor platform. Preflight
  requires `DissonanceVoip`, `Dissonance.Integrations.UnityNfgo`, and
  `SteamAudioUnity` assembly definitions under the host's `Assets`, and
  `BUN3_DISSONANCE`, `BUN3_DISSONANCE_NFGO`, `BUN3_STEAMAUDIO` and
  `STEAMAUDIO_ENABLED` in its Standalone scripting defines. These checks do
  not establish native-library compatibility; the Unity tests do that.

```powershell
./unity/tests/Invoke-SoundFoundationTests.ps1 `
    -ProjectPath 'E:/Temp/fresh-sound-extension-host' `
    -UnityPath 'E:/Unitys/6000.5.5f1/Editor/Unity.exe' `
    -ResultsDirectory 'E:/Temp/bun3-sound-validation-results' `
    -IncludeExtensions
```

Extension mode copies all three packages' `Tests` trees into
`Assets/Bun3SoundFoundationValidation`. Test namespaces and assembly references
remain unchanged; assembly names and filenames receive `.ValidationHost`.
Source `.meta` files are excluded so Unity generates distinct metadata. The
script validates dependencies and SDK assembly presence before writing, then
replaces only its marker-owned copy folder on each run so removed tests cannot
linger. It rejects reparse points and refuses to replace an existing unmarked
folder.

Use a fresh validation host without earlier manual copies of these tests. The
runner rejects matching original or `.ValidationHost` test assembly names
elsewhere under `Assets`. It never cleans `Assets/NetcodeAdapterTests`,
`Assets/NativePlaybackTests`, or any other manual test folder. Resolve a
reported conflict yourself or choose another prepared host.

Add `-PrepareOnly` together with `-IncludeExtensions` to perform preflight and
refresh the owned test copies without starting Unity. No invocation installs
dependencies, edits manifests or `testables`, copies SDK files, or changes
project settings. Normal extension runs require discovered cases from the
grid geometry and coverage suites, NGO EditMode suite, NGO host suite, sample
pump, and playback facade, so a passing foundation suite cannot hide a missing
optional test assembly. It also requires at least eleven discovered
`SoundVoiceOutputTests` cases in PlayMode. The acoustics package itself has no SDK dependency;
this combined runner mode still requires both SDKs for the other extensions.

## Recorded validation

The validation host's completed reports below are separate controller runs,
not fixed test-count expectations for this script. Its default selection is
still narrower than the complete audio regression suite.

| Report | Passed | Coverage |
| --- | ---: | --- |
| `sound-production-play.xml` | 185/185 | Audio/event regression, native output and voice playback, including strengthened mailbox admission and native gain/pitch checks |
| `acoustic-query-unity-green.xml` | 26/26 | Grid geometry, endpoint coverage, topology changes and query allocation checks |
| `acoustic-asset-green.xml` | 6/6 | Native acoustic asset bake/load validation |

Reports were produced in `E:/Temp/bun3-sound-validation-20260911/Results`.
They do not establish complete game-scene or physical microphone validation.

## Optional SDK compilation matrix

Copy `OptionalAudioCompilationProbe.cs` into a disposable host's `Assets/Editor`.
Reference all optional packages and include their tests in `testables`. Run Unity
with `-batchmode -quit -executeMethod OptionalAudioCompilationProbe.Verify`,
`-expectedAudioAdapters <mask>` and `-audioProbeResult <absolute-json-path>`.
Mask bits are Dissonance=1, Steam Audio=2, official Dissonance NGO=4; combined
playback requires bits 1 and 2. Probe 0 must pass without SDK assets, including
when the legacy `STEAMAUDIO_ENABLED` symbol remains. For installed cases, import
and compile SDKs, run `SoundSdkSetup.SyncInstalledAdapters`, then probe in a new
Editor process so updated symbols have compiled. Before SDK removal, disable
adapters and remove application references to their types. This explicit
workflow does not automatically repair stale Bun3 symbols after external SDK deletion.

With installed adapters already synchronized, run
`-executeMethod OptionalAudioCompilationProbe.VerifySymbolLifecycle` to check
that disabling preserves all unrelated and SDK-owned symbols and synchronization
restores the installed adapter configuration on all four supported targets.
The probe restores its original settings even if an assertion fails.
