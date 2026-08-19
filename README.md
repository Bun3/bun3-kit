# bun3-kit

Bun3's personal framework monorepo.

| Folder | Contents |
|---|---|
| `common/` | Cross-platform .NET libraries (`com.bun3.common` = dual UPM+NuGet packaging) |
| `server/` | Reusable server module libraries (`Bun3.Server.*`, published to NuGet) |
| `unity/` | Unity package development project (`com.bun3.unity.*`) |

- Design doc: `docs/superpowers/specs/2026-07-26-monorepo-structure-design.md`
- Solutions: root `Bun3.sln` (common+server). `unity/unity.sln` is Unity-generated.
- Consumption: server/.NET via NuGet, Unity via `?path=` UPM git URLs. The server app
  entry point lives in the separate `bun3-server-template` repo.
