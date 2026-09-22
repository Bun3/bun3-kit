# Dissonance Netcode Adapter

Optional binding for the official Dissonance Netcode for GameObjects transport. Install the licensed Dissonance SDK and its official NGO integration separately. This package does not redistribute either SDK or implement a voice wire protocol.

## Installation and assembly setup

The package declares Unity **6000.3** as its minimum. Import the third-party SDK assets separately; Git installation does not install licensed SDK files or native binaries. For a Git-based project, merge these entries into `Packages/manifest.json`'s existing `dependencies` object:

```json
{
  "com.bun3.unity.audio": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.audio#Bun3/jp-sound-integration",
  "com.bun3.unity.audio.dissonance": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.audio.dissonance#Bun3/jp-sound-integration",
  "com.bun3.unity.audio.dissonance.netcode": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.audio.dissonance.netcode#Bun3/jp-sound-integration"
}
```

These are dependency entries, not a complete project manifest. The fragment `Bun3/jp-sound-integration` selects the integration branch; it is a moving branch, not a release tag. Package dependencies containing Bun3 version numbers do not resolve sibling packages from the same Git checkout automatically. Add the Bun3 transitive packages explicitly, including `com.bun3.unity.core` and its `com.bun3.common` dependency, or provide a registry that actually serves the declared versions. Follow the [audio installation guide](../com.bun3.unity.audio/README.md) for that dependency chain, UniTask, and SerializeReferenceExtensions. Do not assume a single Git URL is a self-contained install.

For a local checkout, use the same package set as embedded packages or project-manifest `file:` dependencies pointing to each package directory. Do not copy the third-party SDK into these packages.

The runtime assembly is `Bun3.Unity.Audio.Dissonance.Netcode`. An application with its own assembly definition must reference the assemblies whose types it uses: `Bun3.Unity.Audio.Dissonance.Netcode`, `DissonanceVoip`, `Dissonance.Integrations.UnityNfgo`, `Unity.Netcode.Runtime`, `Unity.Collections`. Match the adapter's define constraints (`BUN3_DISSONANCE`, `BUN3_DISSONANCE_NFGO`) on application adapter assemblies, so SDK removal can disable those assemblies cleanly. See **Optional SDK activation** below before adding symbols manually.

The package declares `com.bun3.unity.audio.dissonance` **0.2.0** and `com.unity.netcode.gameobjects` **2.13.1** dependencies. Install NGO through Unity's registry and the official Dissonance NGO integration through its licensed distribution. `Unity.Collections` is also an assembly dependency. Transport configuration such as Unity Transport, connection addresses, and host/client startup remains application setup.

## Transport and identity ownership

The imported integration needs an explicit runtime assembly named `Dissonance.Integrations.UnityNfgo`, referencing `DissonanceVoip`, `Unity.Netcode.Runtime`, and `Unity.Collections`. Its Editor and Demo folders need separate assembly definitions. Original SDK source files remain unchanged. Disable adapters before removing the integration as described below; the core audio package remains independent.

Create `DissonanceNfgoSessionScope(manager, comms, transport)` before starting NGO. The SDK comms and official `NfgoCommsNetwork` must share a GameObject. The scope binds existing objects and owns player binding lifetime; it does not start, stop, create, or destroy the network or SDK objects. Dispose it before tearing those objects down. `IsReady` describes initialized active transport, not microphone permission, PCM delivery, or audible playback.

Add `DissonanceNfgoPlayer` to player NetworkObjects instead of the original `NfgoPlayer`. Trackers resolve their manager's registered scope and replicate an atomic NGO owner ID and SDK name record. A record only applies to its current owner, including when ownership notifications and identity replication arrive in different orders. An explicit `Bind(comms)` is available for applications which own binding directly. Tracking uses an immutable identity registration, unregisters before changing identity, and releases on disable, despawn, destroy, or SDK destruction. Session lookup maps an active owner's NGO ID to the tracked SDK ID. Do not register another SDK tracker for the same identity.

This adapter requires exclusive position-tracker ownership for its `DissonanceComms`. Remove the original `NfgoPlayer` from those avatars and do not mix other position-tracking implementations into the same comms context. Duplicate registrations owned by this adapter are rejected without allocations. The SDK's public API cannot reveal an external unlinked tracker, so the adapter cannot protect or restore such a registration when implementations are mixed.

Identity RPC validation checks sender ownership, nonblank valid UTF-8 size, and duplicate names. The claimed name is not authenticated against the official transport's internal peer/name association. Applications must not use this mapping for gameplay authority; authoritative gameplay messages must validate their NGO sender separately.

The official transport uses `NetworkManager.Singleton`. One active local NGO manager and one Dissonance transport per process are supported. Multiple clients must be tested in separate processes. Game room membership, speech permission, proximity configuration, and match rules belong to the application.

Tests include an actual NGO host with injected silent microphone capture. This avoids hardware access and does not prove microphone capture quality or remote voice delivery. Separate-process encoded delivery and positional playback require additional integration tests.

## Minimal session owner

Prepare a scene with one configured `NetworkManager`, its network transport, and existing `DissonanceComms` plus `NfgoCommsNetwork` on the same GameObject. Add `DissonanceNfgoPlayer` to the registered player prefab's `NetworkObject`, removing the SDK's original `NfgoPlayer`. Ensure the application has completed this binding before calling `StartHost`, `StartServer`, or `StartClient`.

The example intentionally leaves network startup with the caller. Call `BindBeforeNetworkStart` once, then start the desired network mode. Before shutting down or destroying the SDK/manager, call `ReleaseBeforeNetworkShutdown`; `OnDestroy` is a fallback.

```csharp
using Bun3.Unity.Audio.Dissonance.Netcode;
using Dissonance;
using Dissonance.Integrations.Unity_NFGO;
using Unity.Netcode;
using UnityEngine;

public sealed class VoiceNetworkOwner : MonoBehaviour
{
    [SerializeField] private NetworkManager manager = null;
    [SerializeField] private DissonanceComms comms = null;
    [SerializeField] private NfgoCommsNetwork voiceTransport = null;
    private DissonanceNfgoSessionScope session;

    public bool IsVoiceReady => session != null && session.IsReady;

    public void BindBeforeNetworkStart()
    {
        if (session == null)
            session = new DissonanceNfgoSessionScope(manager, comms, voiceTransport);
    }

    public bool TryGetVoiceId(ulong ownerId, out string voiceId)
    {
        voiceId = null;
        return session != null && session.TryGetPlayerId(ownerId, out voiceId);
    }

    public void ReleaseBeforeNetworkShutdown()
    {
        session?.Dispose();
        session = null;
    }

    private void OnDestroy() => ReleaseBeforeNetworkShutdown();
}
```

The official integration's C# namespace is `Dissonance.Integrations.Unity_NFGO`, while its required assembly name is `Dissonance.Integrations.UnityNfgo`. These names are intentionally different. Automatic trackers join their manager's scope; `DissonanceNfgoPlayer.Bind(comms)` is an alternative for direct application ownership. Explicitly bound players are not attached to the session's lookup list, so do not expect `TryGetPlayerId` to enumerate that independent binding path.

## Contracts, settings, and performance

Use session creation, disposal, player binding, readiness, and identity lookup only on the Unity main thread. The adapter does not process microphone/audio callback buffers. Stable player updates and duplicate-registration checks avoid creating new identity strings; identity publication/change and registration lifetimes may allocate, as may NGO serialization and the SDK. The one-second local retry interval is internal behavior, not a user-tunable profile.

Identity strings reject blanks, control characters, malformed surrogate pairs, and UTF-8 content exceeding `FixedString128Bytes.UTF8MaxLengthInBytes`. Owner changes discard registration before a replacement identity becomes valid. Session lookup requires a spawned, actively tracked player. A transport connection by itself does not guarantee a player identity is available yet.

This package has no ScriptableObject profiles. Configure connection/transport in NGO, capture and codec in Dissonance, routing through explicit application room ownership, and optional acoustic profiles in the output adapter. Session disposal releases attached trackers immediately and is idempotent; it never calls NGO shutdown.

## Troubleshooting and test coverage

| Symptom | Check |
|---|---|
| Missing integration assembly | Create the required SDK assembly definitions and let SDK compilation finish before synchronization. Merely defining `BUN3_DISSONANCE_NFGO` cannot provide the SDK types. |
| `IsReady` never becomes true | Confirm singleton identity, `manager.IsListening`, enabled comms/transport, initialized transport, and connected status. |
| Session construction rejects the setup | Use one binding per manager/comms and put comms plus voice transport on the same object. |
| No mapped identity | Wait for spawn/identity replication and verify exclusive tracker ownership; direct `Bind` does not populate session lookup. |
| Name remains untracked | Check UTF-8 length/characters, owner identity, and collisions with another player's SDK name. |
| Host passes but remote voice fails | Test separate processes; a local host handshake does not exercise remote packet routing or capture. |

Add `com.bun3.unity.audio.dissonance.netcode` to the project's top-level `testables` array, install Unity Test Framework, and activate both SDK defines. `Bun3.Unity.Audio.Dissonance.Netcode.Tests` is Editor-only and additionally requires `UNITY_INCLUDE_TESTS`; run it in EditMode for binding validation, atomic identity ownership, duplicate rejection, and warm duplicate-check allocations. Run `Bun3.Unity.Audio.Dissonance.Netcode.PlayMode.Tests` in PlayMode for the actual host handshake, despawn, and reuse scenario. Keep one manager per test process and restore singleton/network state between cases. These tests do not authenticate remote name claims or validate remote audible delivery.

## Optional SDK activation

Adapters are disabled by default. Import the required SDKs and let Unity finish compiling, then run **Tools > Bun3 > Audio > Sync Installed Adapters**. The SDK-independent `Bun3.Unity.Audio.Editor.SoundSdkSetup.SyncInstalledAdapters` method is also available through Unity `-executeMethod` or Editor automation. It checks SDK assembly-definition assets and required loaded types before enabling adapters on Standalone, Android, iOS and WebGL, preserving unrelated scripting defines.

Dissonance requires `BUN3_DISSONANCE`; the official NGO binding additionally requires `BUN3_DISSONANCE_NFGO`. Steam Audio requires both `BUN3_STEAMAUDIO` and the SDK's `STEAMAUDIO_ENABLED`. Combined voice path playback requires both SDKs. A leftover `STEAMAUDIO_ENABLED` alone does not activate Bun3 adapters. Runtime, Editor and test assemblies share these gates.

Before removing SDK assets, remove or guard application references to adapter types, then run **Tools > Bun3 > Audio > Disable Adapters** (`Bun3.Unity.Audio.Editor.SoundSdkSetup.DisableAdapters`) and let compilation finish. This removes the three Bun3 symbols on the four supported targets and preserves the SDK-owned `STEAMAUDIO_ENABLED` flag; optional packages can remain installed. Synchronize again after installing or removing SDK components. Activation is explicit and does not run automatically on domain reload. Deleting SDK files outside the Editor while old activation symbols remain is not an automatically recoverable cold-start workflow; restore the SDK or clear the managed symbols before reopening.
