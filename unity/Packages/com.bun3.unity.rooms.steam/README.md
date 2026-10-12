# Bun3 Rooms Steam Backend

`IRoomBackend` on Steam lobbies. Rooms are invisible lobbies (found by search, hidden from friends lists) whose six-character code lives in lobby data, so players keep typing short codes while friends can also join from the Steam overlay.

## Installation

Install and activate `com.bun3.unity.steam` first (Steamworks.NET plus `BUN3_STEAMWORKS`), plus `com.bun3.unity.rooms`, then:

```json
{
  "com.bun3.unity.rooms.steam": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.rooms.steam#Bun3/jp-steam-networking"
}
```

The runtime assembly is `Bun3.Unity.Rooms.Steam` (Editor and Standalone, `BUN3_STEAMWORKS`).

## Behaviour

- Construct `SteamLobbyBackend` after `SteamRuntime.Initialize` returns `Ready`, and dispose it on shutdown. While Steam is not initialized every operation fails with `ServiceUnavailable`.
- Lobby data: `bun3.code`, `bun3.ver`, `bun3.name`, `bun3.open` (owner; `"0"` while locked); member data `bun3.joined` (each member's Steam server time, used as `RoomMember.JoinedAt`). Room and member ids are decimal SteamIDs. `MaxMembers` is the lobby member limit.
- Create: a new code is checked against existing joinable lobbies (up to five attempts), then an invisible lobby is created.
- Join by code: worldwide search on `bun3.code` with a free slot. No match → `NotFound`; enter response Full → `Full`, Error → `ServiceUnavailable`, others → `NotFound`. A different `bun3.ver` leaves the lobby and reports `VersionMismatch`. Steam requests time out after 15 seconds (`ServiceUnavailable`).
- `SetJoinableAsync(false)` hides the lobby from search and stores `bun3.open = "0"`. Every member reads it into `IsJoinable` and clears its own `connect` rich presence, so no friend sees "Join Game" during a match. Locked rooms report `NotFound`.
- `DataChanged` fires on each lobby data update (not on member data updates); re-read the keys you need.
- Invites: while in an open room each member publishes rich presence `connect = "+connect_lobby <lobby id>"`. Overlay invites (`GameLobbyJoinRequested_t`), "Join Game" (`GameRichPresenceJoinRequested_t`) and a `+connect_lobby <id>` launch argument raise `JoinRequested` with the lobby id; the launch argument is raised on the next frame. `TryOpenInviteOverlay` opens the Steam invite dialog.
- A member leaving or disconnecting, and Steam handing lobby ownership to another member, refresh the member list and raise `OwnerChanged`; the room's owner is the Steam lobby owner. Kicks and the local member's own leave notification raise `Closed`.

Known limit: members write their own `bun3.joined`, so a lobby member could claim an early time to win waiting-room host election.
