# Bun3 Steam NGO Transport

Netcode for GameObjects transport over Steam Datagram Relay (`ISteamNetworkingSockets` peer-to-peer). Relay traffic is routed through Valve's network, so players never exchange IP addresses.

## Installation

Install and activate `com.bun3.unity.steam` first (Steamworks.NET plus `BUN3_STEAMWORKS`), then:

```json
{
  "com.bun3.unity.steam.ngo": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.steam.ngo#Bun3/jp-steam-networking"
}
```

Requires NGO 2.13.1. The runtime assembly is `Bun3.Unity.Steam.Ngo` (Editor and Standalone, `BUN3_STEAMWORKS`).

## Usage

Add `SteamSocketsTransport` and assign it as `NetworkConfig.NetworkTransport` before starting NGO (`com.bun3.unity.rooms.steam.ngo` does this per room).

- Host: `StartHost()` opens a P2P listen socket on `VirtualPort`. `AcceptFilter` decides which SteamIDs may connect; null accepts everyone.
- Client: set `TargetSteamId` to the host's SteamID, then `StartClient()`.
- `TryGetSteamId(transportId, out steamId)` maps NGO transport ids to SteamIDs. A client's id for its host is 0.
- Reliable deliveries use Steam reliable messages; `Unreliable` and `UnreliableSequenced` use unreliable messages (Steam does not drop older unreliable messages). All sends disable Nagle delay because NGO already batches per frame.
- Steam may hand over a connection's first message before its `Connected` callback; the transport reports `Connect` first so NGO's connection request is never lost.
- `SteamRuntime` must be initialized; otherwise `StartHost`/`StartClient` return false. Receiving reuses one buffer and releases Steam messages immediately.
