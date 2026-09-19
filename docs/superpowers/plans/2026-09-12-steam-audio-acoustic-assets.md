# Acoustic asset bake/load implementation plan

**Goal:** Persist generated native scene/pathing data and load it with explicit reusable ownership.

**Architecture:** Generic ScriptableObject data, immutable serializable settings, cold synchronous baker and a creating-thread native load scope. The controller owns Unity execution.

**Execution status:** complete. Controller verified `acoustic-asset-red.xml` (5 intended failures) and `acoustic-asset-green.xml` (all 5 asset tests plus 1 factory test passed). Runtime compilation with actual SDK references, C#9/netstandard2.1 and XML documentation passed with zero warnings/errors. No commits or SDK edits were made.

**Files:** new Runtime/SteamAudioAcousticAsset.cs, SteamAudioPathBakeSettings.cs, SteamAudioAcousticBaker.cs, SteamAudioAcousticSceneScope.cs; new Tests/Editor/SteamAudioAcousticAssetTests.cs.

1. Add reflection-first tests requiring the public asset/baker/load contract and native roundtrip behavior. Capture intended RED through the controller.
2. Implement copied settings/metadata/probe spheres and checksum validation, then cold native bake/serialization with try/finally cleanup.
3. Implement supported SDK wrapper loading with retained context, borrowed wrappers and idempotent disposal. Document SDK-internal exceptional-load temporary finalization.
4. Run generated asset save/reload and native simulation tests, caller-array independence, corrupt/version-invalid rejection, and loader/simulation lifetime tests.
5. Compile new runtime files against the actual SDK under C#9/netstandard2.1 with XML documentation and zero warnings. Record actual Unity evidence and update package docs/version with other owners coordinated.
