# Dissonance Netcode Adapter

Optional binding for the official Dissonance Netcode for GameObjects transport. Install the licensed Dissonance SDK and its official NGO integration separately. This package does not redistribute either SDK or implement a voice wire protocol.

The imported integration needs an explicit runtime assembly named `Dissonance.Integrations.UnityNfgo`, referencing `DissonanceVoip`, `Unity.Netcode.Runtime`, and `Unity.Collections`. Its Editor and Demo folders need separate assembly definitions. Original SDK source files remain unchanged. Disable adapters before removing the integration as described below; the core audio package remains independent.

Create `DissonanceNfgoSessionScope(manager, comms, transport)` before starting NGO. The SDK comms and official `NfgoCommsNetwork` must share a GameObject. The scope binds existing objects and owns player binding lifetime; it does not start, stop, create, or destroy the network or SDK objects. Dispose it before tearing those objects down. `IsReady` describes initialized active transport, not microphone permission, PCM delivery, or audible playback.

Add `DissonanceNfgoPlayer` to player NetworkObjects instead of the original `NfgoPlayer`. Trackers resolve their manager's registered scope and replicate an atomic NGO owner ID and SDK name record. A record only applies to its current owner, including when ownership notifications and identity replication arrive in different orders. An explicit `Bind(comms)` is available for applications which own binding directly. Tracking uses an immutable identity registration, unregisters before changing identity, and releases on disable, despawn, destroy, or SDK destruction. Session lookup maps an active owner's NGO ID to the tracked SDK ID. Do not register another SDK tracker for the same identity.

This adapter requires exclusive position-tracker ownership for its `DissonanceComms`. Remove the original `NfgoPlayer` from those avatars and do not mix other position-tracking implementations into the same comms context. Duplicate registrations owned by this adapter are rejected without allocations. The SDK's public API cannot reveal an external unlinked tracker, so the adapter cannot protect or restore such a registration when implementations are mixed.

Identity RPC validation checks sender ownership, nonblank valid UTF-8 size, and duplicate names. The claimed name is not authenticated against the official transport's internal peer/name association. Applications must not use this mapping for gameplay authority; authoritative gameplay messages must validate their NGO sender separately.

The official transport uses `NetworkManager.Singleton`. One active local NGO manager and one Dissonance transport per process are supported. Multiple clients must be tested in separate processes. Game room membership, speech permission, proximity configuration, and match rules belong to the application.

Tests include an actual NGO host with injected silent microphone capture. This avoids hardware access and does not prove microphone capture quality or remote voice delivery. Separate-process encoded delivery and positional playback require additional integration tests.

## Optional SDK activation

Adapters are disabled by default. Import the required SDKs and let Unity finish compiling, then run **Tools > Bun3 > Audio > Sync Installed Adapters**. The SDK-independent `Bun3.Unity.Audio.Editor.SoundSdkSetup.SyncInstalledAdapters` method is also available through Unity `-executeMethod` or Editor automation. It checks SDK assembly-definition assets and required loaded types before enabling adapters on Standalone, Android, iOS and WebGL, preserving unrelated scripting defines.

Dissonance requires `BUN3_DISSONANCE`; the official NGO binding additionally requires `BUN3_DISSONANCE_NFGO`. Steam Audio requires both `BUN3_STEAMAUDIO` and the SDK's `STEAMAUDIO_ENABLED`. Combined voice path playback requires both SDKs. A leftover `STEAMAUDIO_ENABLED` alone does not activate Bun3 adapters. Runtime, Editor and test assemblies share these gates.

Before removing SDK assets, remove or guard application references to adapter types, then run **Tools > Bun3 > Audio > Disable Adapters** (`Bun3.Unity.Audio.Editor.SoundSdkSetup.DisableAdapters`) and let compilation finish. This removes the three Bun3 symbols on the four supported targets and preserves the SDK-owned `STEAMAUDIO_ENABLED` flag; optional packages can remain installed. Synchronize again after installing or removing SDK components. Activation is explicit and does not run automatically on domain reload. Deleting SDK files outside the Editor while old activation symbols remain is not an automatically recoverable cold-start workflow; restore the SDK or clear the managed symbols before reopening.
