# Bun3 Rooms Steam NGO Transport

Joins `com.bun3.unity.rooms.ngo` sessions to the Steam Datagram Relay transport from `com.bun3.unity.steam.ngo`.

## Installation

```json
{
  "com.bun3.unity.rooms.steam.ngo": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.rooms.steam.ngo#Bun3/jp-steam-networking"
}
```

Requires the rooms, rooms NGO, Steam and Steam NGO packages with `BUN3_STEAMWORKS` active. The runtime assembly is `Bun3.Unity.Rooms.Steam.Ngo`.

## Usage

```csharp
var sockets = manager.GetComponent<SteamSocketsTransport>();
var session = new NgoRoomSession(manager, room, new SteamRoomTransport(sockets), options);
```

- `PrepareHost` admits only SteamIDs that are current members of the room and publishes the host's SteamID as the endpoint.
- `PrepareClient` connects to the SteamID in the endpoint and throws `FormatException` for anything else.
- Both assign the Steam transport as `NetworkConfig.NetworkTransport`.
