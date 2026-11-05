# Bun3 Unity Telemetry GameAnalytics

GameAnalytics sink for `com.bun3.unity.telemetry`. Same requirements as the core package (Unity 6000.5 or newer, ZString and UniTask in the project manifest).

## Setup

1. Add the SDK to the project `manifest.json`: `"com.gameanalytics.sdk": "https://github.com/GameAnalytics/GA-SDK-UNITY.git"`. `packages-lock.json` pins the commit.
2. Create the SDK settings asset at `Assets/Resources/GameAnalytics/Settings.asset` (the SDK's settings window creates it). Keys may stay empty. Listing the environment names under Custom Dimensions 01 is optional; the sink adds the current one at runtime.
3. Add `GameAnalyticsSinkOptions` (`GameKey`, `SecretKey`, `Tiers` defaulting to Analytics) to each environment bundle that should send.
4. Call `builder.AddGameAnalytics(map)` while building the hub.

## Behavior

- The SDK is initialized on the first record: custom user id from `TelemetryContext.DistinctId` (skipped when empty), build from `Build`, custom dimension 01 from `Environment`, keys from the options. The SDK's own error, FPS, memory and hardware reporting and its info logs are turned off.
- `map` returning false sends a design event named after the record; true with no events skips the record; otherwise the events are sent in order.
- Ids are cleaned to GameAnalytics rules (allowed characters, 64 per segment, 5 segments for design and 3 for progression events). The first changed id logs a warning.
- The hub only hands records to remote sinks with consent, so a run without consent never initializes the SDK. Later consent changes turn event submission on or off.
- An SDK exception stops the sink for the run with one warning. A mapping exception drops only that record.
- On Windows standalone builds the SDK waits 1.5 seconds in `OnApplicationQuit` to end its session.
