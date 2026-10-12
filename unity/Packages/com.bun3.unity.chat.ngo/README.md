# Bun3 Chat NGO Adapter

Relays [`com.bun3.chat`](../../../common/src/com.bun3.chat) over Netcode for GameObjects named messages.

- `Bun3.Chat.Submit` (client → server), `Bun3.Chat.Message` (server → recipients), `Bun3.Chat.History` (server → one new client) all use `ReliableFragmentedSequenced`, so a replay always arrives before later live messages.
- The server runs `ChatRelay.Submit`, sends to `CollectRecipients`, and replays `CollectReplay` in chunks of at most 4 KB when a client connects. Rejections are dropped silently.
- `Send` never displays optimistically: only messages the server relays back raise `Received`.
- Handlers are registered per session and released on stop; stopping the server clears the relay. `MaxLength` must be at most 896.

```csharp
var relay = new ChatRelay(new MyPolicy(), new ChatRelayOptions { HistoryCapacity = 1000, HistoryReplayCount = 100 });
var scope = new NgoChatScope(networkManager, relay);
var log = new ChatLog(1000);
scope.Received += m => log.Add(m);
scope.HistoryReceived += m => log.Add(m);   // skip this when the peer already has a log (reconnect)
networkManager.OnServerStarted += () => relay.SeedHistory(log);   // a new host inherits its own log
scope.Send(input.text);
```

Dispose the scope when the owner goes away.
