# Bun3 Steam

Steamworks lifetime for Unity: initialization, relaunch through Steam, callback pumping and shutdown. Adapter packages (`com.bun3.unity.steam.ngo`, `com.bun3.unity.rooms.steam`, `com.bun3.unity.rooms.steam.ngo`) build on it.

## Installation and activation

Steamworks.NET is not a package dependency. Install it in the game:

```json
{
  "com.rlabrecque.steamworks.net": "https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net",
  "com.bun3.unity.steam": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.steam#Bun3/jp-steam-networking"
}
```

`com.bun3.unity.core` must also be installed. The Bun3 Steam runtime assemblies and their runtime tests require the `BUN3_STEAMWORKS` define and are limited to the Editor and Standalone platforms; `Bun3.Unity.Steam.Editor` and `Bun3.Unity.Steam.Editor.Tests` are ungated. After Steamworks.NET finishes compiling, run **Tools > Bun3 > Steam > Sync Installed Adapters** (or `-executeMethod Bun3.Unity.Steam.Editor.SteamSdkSetup.SyncInstalledAdapters`). It adds the define to Standalone only when the Steamworks.NET assembly definition and its types are present, and keeps other defines in place. Before removing Steamworks.NET, run **Tools > Bun3 > Steam > Disable Adapters**. Nothing runs automatically on domain reload.

## Usage

```csharp
var result = SteamRuntime.Initialize(new SteamRuntimeOptions { AppId = 480, RestartThroughSteam = !Debug.isDebugBuild }, out string error);
if (result == SteamInitResult.RestartingThroughSteam) Application.Quit();
else if (result == SteamInitResult.Unavailable) SteamRuntime.QuitWithError(Application.productName, "Start Steam, sign in, and launch the game again.");
string defaultNickname = SteamRuntime.PersonaName; // empty before initialization; apply your own name rules
```

- `RestartThroughSteam` calls `SteamAPI.RestartAppIfNecessary` outside the Editor. Use it for release builds.
- The App ID is then published through the `SteamAppId`/`SteamGameId` environment variables, so the Editor and development builds started outside Steam need no `steam_appid.txt`.
- `Unavailable` means Steam is not running, not signed in, or the native library is missing; `error` carries Steam's English message.
- `QuitWithError(title, message)` logs the message, shows a native error dialog on Windows players (topmost, foreground) or an Editor dialog, then quits (the Editor leaves Play Mode). Batch mode and other platforms skip the dialog. The text is yours, so localize it.
- `PersonaName` is the signed-in user's Steam profile name, or empty before initialization.
- When ready, `SteamAPI.RunCallbacks` runs every frame before `Update` scripts, and Steamworks shuts down on `Application.quitting`. Main thread only.
