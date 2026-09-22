# Changelog

## [0.2.0] - 2026-09-12

- Added `SoundActivityLease` for strictly sequenced, finite activity owned by one source in one session.
- Added authoritative `SoundActivityData` and explicit report outcomes, including consumed capacity rejection.
- Added tests for renewal, replay, policy denial, expiry, source cleanup, callback recovery, clocks, capacity, disposal, and allocation-free heartbeats.

## [0.1.0] - 2026-09-11

- Added fixed-capacity event delivery, immutable snapshots, generation-safe handles, replaceable reach policy, and deterministic explicit clock.
