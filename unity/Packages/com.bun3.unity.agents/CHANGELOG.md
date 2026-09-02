# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.9.0] - 2026-09-03

### Changed

- Codex hook commands point at a fixed-path argumentless launcher
  (bun3-agent-codex-launcher.ps1) that dispatches in-process to the shared
  heartbeat script. Codex trust-hashes the command string, so script renames or
  argument changes no longer cost the user a re-approval - only this one
  migration does. Pre-launcher blocks are stripped and rewritten on install.
- The heartbeat script caches the resolved owner pid: the previous event's pid is
  reused while that process is alive and still looks like a CLI, so the multi-query
  WMI ancestor walk runs only on the first event of a session (or after a miss).
  Fallback worker name with no cwd is now the provider name, not always Claude.

## [0.8.0] - 2026-08-31

### Changed

- Codex detection moved from the legacy single-slot notify command to Codex's proper
  hooks (config.toml [[hooks.*]] tables, stable since the hooks feature landed). Codex
  sessions now get per-session workers with project names, live states, and pid-based
  pruning - notify only fired at turn boundaries and merged every session into one
  "Codex CLI" worker that never appeared until a first turn completed.
- bun3-agent-heartbeat.ps1 is provider-parameterized (-Provider codex): Codex hooks
  pipe the same payload shape as Claude's (session_id / cwd / hook_event_name /
  tool_name / agent_id), so both CLIs share one script. HookRule gains `scriptArgs`.
- Install migrates a legacy notify entry away automatically: the previous notify
  occupant is restored and the merged codex-cli worker dropped.
- NOTE: seeded manifests are never overwritten - delete
  %LOCALAPPDATA%/bun3-agents/providers/codex.json once so the new manifest reseeds.
  Codex asks to trust the new hooks on its next launch; approve them there.

## [0.7.0] - 2026-08-30

### Removed

- Automatic transcript adoption (`SessionSync.SyncNow`, `SessionDiscovery`,
  `AgentHeartbeatStore.WriteProvisional`, tombstones, the manifest `discovery` rule).
  Heartbeats are written by the hooks whether or not any consumer app runs, so a live
  session always resurfaces on its own next hook event - the only gap adoption ever
  closed was sessions predating hook install. Meanwhile process-count guessing kept
  minting ghost workers: the CLI's exe is shared by pty hosts, daemons, nested
  wrappers, and utility subcommands (attach, agents), and no count survives that.
  Three ghost bugs in, the feature loses. `SessionSync.KillSessionTree` stays.
- Consumers: drop `SyncNow` calls; expired pid -1 placeholders are still cleaned,
  just no longer tombstoned.

## [0.6.0] - 2026-08-30

### Added

- `SessionSync.KillSessionTree(pid)` - kills a session's whole CLI process tree
  (topmost same-named ancestor plus every descendant: pty hosts, wrappers, MCP
  servers, shells). Headless leftover sessions are invisible outside Task Manager,
  so consumers can offer "fire this worker" instead of asking users to hunt pids.
- `AgentHeartbeatStore.Remove(id)` - deletes an agent's heartbeat and watch flag,
  the consumer-initiated exit. A session still alive recreates its file on its next
  hook event, so removing a live one self-heals.

## [0.5.3] - 2026-08-30

### Fixed

- Sync counts unaccounted session process TREES, not processes: the CLI runs as a
  small tree (wrapper → session, daemon → pty host → session) while hooks record one
  pid inside it, so per-process counting minted one ghost adoption per helper spawn.
  Parent links come from a Toolhelp32 snapshot (Windows); elsewhere grouping degrades
  to the old per-process behavior.
- Unaccounted trees with no member younger than the transcript window are not counted:
  a long-idle heartbeat-less process (pre-hook session, forgotten shell) has no recent
  transcript of its own, so counting it only ghosted other projects' corpses.
- Adoption's one-per-project dedup keys on the transcript directory (Candidate.DirName)
  instead of the display name, which falls back to the path-encoded dir when the
  transcript head has no cwd - one project could slip through twice under two spellings.
- ReadAndPrune keeps only the newest session heartbeat per live pid: /clear and resume
  start a new session id in the same process and the replaced session never gets a
  SessionEnd, so its heartbeat survived the dead-pid check forever as a sleeping ghost.
  Chicks (p set) are exempt - they share their parent's pid by design.

## [0.5.2] - 2026-08-30

### Fixed

- Expired provisional registrations are tombstoned (adoption-tombstones.txt in the
  heartbeat directory) and sync never re-adopts them - infrastructure processes that
  inflate the live-CLI count (daemons, pty hosts) can no longer cycle the same corpse
  transcript into a ghost every sync. A session that later proves alive registers
  itself through its hooks regardless.

## [0.5.1] - 2026-08-30

### Fixed

- SyncNow is idempotent: pending provisional registrations (pid -1) count against the
  unaccounted-process gap, and each pass adopts at most one candidate per project -
  repeated syncs no longer pile up corpse registrations from the same project.
- ProjectLeaf scans up to 64KB of the transcript head (the cwd field can sit past a
  huge first record).

## [0.5.0] - 2026-08-29

### Added

- `SessionSync.SyncNow()` - reconciles the store with reality instead of listing
  "traces": counts the provider's live CLI processes unaccounted for by any heartbeat
  pid (desktop-app processes excluded via path hints) and provisionally registers that
  many of the newest unregistered transcript sessions. Zero unaccounted processes =
  zero registrations, so corpses never enter.
- `AgentHeartbeatStore.WriteProvisional(id, name)` - the pid -1 adoption writer.
- `SessionDiscovery.ProjectLeaf` - candidates now carry the session's real folder name
  read from the transcript cwd, not the path-encoded directory mush.

## [0.4.2] - 2026-08-29

### Fixed

- Adoption placeholders (pid -1) expire after the deep-quiet threshold when no hook
  event ever replaces them - adopting a session that turned out to be dead no longer
  leaves an immortal sleeping zombie. Consumers should adopt with pid -1.

## [0.4.1] - 2026-08-29

### Fixed

- The Claude heartbeat script sweeps its session chick files on Stop/StopFailure/
  SessionEnd - a subagent killed without SubagentStop no longer lingers as an orphan
  duckling (chick pids point at the living parent, so pid pruning cannot catch them).

## [0.4.0] - 2026-08-29

### Changed

- The default provider manifests (Claude, Gemini, Cursor, Codex, ChatGPT) now ship
  inside the package and are SEEDED into the add-on directory
  (`%LOCALAPPDATA%/bun3-agents/providers/`) on first run - the defaults are add-ons,
  sitting exactly where a community manifest goes, as live examples users can copy or
  edit. An existing file of the same name is never overwritten (edits win; delete to
  restore the bundled version). Consumers no longer need their own
  StreamingAssets/providers, though that layer still works as an app-level override.

## [0.3.0] - 2026-08-29

### Changed

- Protocol namespace is `%LOCALAPPDATA%/bun3-agents/` with `bun3-agent-*.ps1` scripts
  and the `bun3-agent` install marker. All pre-rename (`ai-office`) migration shims
  were removed - nothing had shipped, so there is nothing to migrate.

## [0.1.0] - 2026-08-28

### Added

- Initial extraction from ai-office: heartbeat protocol + store (dead-pid pruning),
  provider manifest registry with community add-on support, hook auto-install and
  clean uninstall for Claude / Gemini / Cursor / Codex (three settings schemas,
  scripts bundled as Resources TextAssets), transcript-based session discovery,
  desktop-app process watcher.
