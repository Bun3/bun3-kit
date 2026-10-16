# Bun3 Chat

Transport-agnostic text chat core. netstandard2.1, no UnityEngine dependency.

## What it does

- `ChatText.TryNormalize` turns typed text into one line: control and line-separator characters (including U+2028 and U+2029) become spaces, format characters of any plane are removed, the text is trimmed and composed to NFC. It rejects invisible text (whitespace, combining marks, format characters, variation selectors and Hangul fillers only), more than `maxLength` text elements, and more than `maxLength × 4` UTF-8 bytes.
- `ChatRelay` runs on the host: sender identity → text → channel → optional `TextFilter` → burst limit. Rejected messages do not spend the burst budget. `TextFilter` rewrites the normalized text, for example to mask bad words with `com.bun3.badwords`; its result is normalized again. Names and player ids come only from the policy, never from the client.
- `ChatLog` is a fixed-capacity ring with an unread counter. `TotalAdded` survives `Clear`, so a view can diff appended and evicted counts.
- Optional host history: `HistoryCapacity` keeps accepted messages in memory; `HistoryReplayCount` replays the newest receivable ones to new peers. `0` turns each off.

## Policy contract

Implement `IChatPolicy` with your game's rules:

- `TryGetSender` returns a stable, non-empty player id and a display name; together at most `ChatMessage.MaxIdentityBytes` (448) UTF-8 bytes.
- `TryGetChannel` returns false when the sender may not chat now.
- `CanReceive` decides delivery. Replay uses it with the recipient's current state, so decide by `channel` (state at send time), not by the sender's current state. The sender id in history can be stale after reconnects; use `SenderPlayerId` for "mine", blocking and reporting.

## Example

```csharp
var relay = new ChatRelay(new MyPolicy(), new ChatRelayOptions { HistoryCapacity = 1000, HistoryReplayCount = 100 });
var log = new ChatLog(1000);
// A transport adapter calls relay.Submit on the host and delivers the message to CollectRecipients.
```

Persistence, read cursors, whispers and blocking are out of scope. Transport adapters (for example `com.bun3.unity.chat.ngo`) live in separate packages.
