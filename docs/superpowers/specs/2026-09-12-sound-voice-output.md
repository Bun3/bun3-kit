# Pooled sound output ownership

Status: implemented; fake-backend RED and GREEN verified, broader regression controlled by the parent task.

`SoundSystemConfig.CreateVoiceOutput` creates one optional `ISoundVoiceOutput` per prewarmed SFX source after `OnSourceCreated`. Music remains on its existing path. The owner is prepared once and reused; neither the factory nor component creation belongs in Play or Tick.

`TryStart(definition, selectedClip, logicalPitch)` runs after ordinary stopped-source configuration and `OnVoiceConfigured`, before core calls source Play. `Unsupported` permits ordinary playback only after prior custom processing is quiescent. `Started` delegates driver configuration, DSP, logical pitch and completion to the owner. `Unavailable` stops and releases the request without playing its dry clip. Expected backend failures return Unavailable.

For Started voices, the core preserves source volume, handle volume, fades, mixer route and followed position. The backend owns spatial processing and driver pitch; built-in occlusion queries, low-pass and occlusion gain are bypassed. Clip elapsed time no longer completes the slot: backend `IsComplete` reports drained input/tails or failure. Logical pitch zero pauses backend progress without invalidating the handle. Explicit stop/fade still ends the voice.

Retirement precedes source Stop or slot reconfiguration. Retire is idempotent and nonblocking. A held audio reader prevents backend reuse through Unavailable; core never resets native processing itself. Backend reclamation must remain possible after its source is stopped/destroyed. Dispose retires and disposes every prepared owner, including partial-construction failure. Completion callbacks use the original generation and run only after stop/reconfiguration is complete.

Validation sequence: observe the new fake-backend tests fail at the missing factory contract, implement the core seam, rerun those tests, then run existing audio regression tests. Validate native PCM, envelope preservation and reader reclamation separately in the optional backend; fake-core tests do not establish device audio correctness.

RED evidence: validation host `Results/sfx-output-cache-red2.xml` contains all eleven `SoundVoiceOutputTests` failing only at the missing `CreateVoiceOutput` assertion. The runtime implementation was written after inspecting that completed result.

Latest completed validation matrix (Unity 6000.5.5f1, validation host `E:/Temp/bun3-sound-validation-20260911/Results`):

| Report | Result | Scope |
| --- | --- | --- |
| `sound-production-play.xml` | 185/185 passed | Full selected audio/event/native/voice regression after the mailbox reader-admission fix, including native gain and pitch checks |
| `acoustic-query-unity-green.xml` | 26/26 passed | Generic geometry and mutable grid reach/endpoint coverage |
| `acoustic-asset-green.xml` | 6/6 passed | Native acoustic bake/load asset checks |

The opt-in `unity/tests/Invoke-SoundFoundationTests.ps1 -IncludeExtensions` stages acoustics, NGO and combined playback tests from the installed local checkout. It preserves the original default selection and does not install dependencies or edit manifests. These recorded controller runs are broader evidence, not a claim that the runner's default invocation discovers 185 tests or that full game-scene/microphone behavior is validated.

GREEN evidence: `Results/sfx-native-output-red.xml` passes all eleven core `SoundVoiceOutputTests`, including warm allocation measurement. Other suites in that combined result contain separately owned native/cache RED cases; the combined filename is not an all-green claim.
