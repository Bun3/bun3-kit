# Audio readability and expected test errors

## Intent and scope

Improve the existing audio package's reading flow without changing public APIs, serialized profiles, gain curves, playback ordering, or hot-path allocation. The reported red Console messages originate from deliberate failing-load integration tests; unexpected errors must remain visible.

## Design

- Capture only a test's exact expected Addressables exception, assert it, forward unrelated failures, and restore the previous handler on exit.
- Make preload ownership explicit: a local batch is released in one finally block unless ownership transfers to the system.
- Read Tick top-to-bottom as completion polling, state advancement, retirement, notifications, source updates, pitch updates, and music.
- Keep gain calculation pure, name validation and intermediate values, and expand compressed control flow. Do not add delegate chains or per-frame allocations.
- Start with SoundSystem.Addressables, SoundSystem.Tick, DistanceAttenuationProfile. Keep native-thread synchronization outside this bounded refactor.

## Execution and verification

1. Reproduce passing tests that nevertheless leave two Console errors (confirmed 6/6, Console errors=2).
2. Independently fix test error capture and refactor runtime flow.
3. Review behavior and ownership equivalence, run automated audio/full suites, verify Console error count for the failure fixture.
4. Publish the changed package version, restore JP Git dependencies, verify the installed package and record results.

## Decisions

- User has authorized readability improvements and fixing reported errors; proceed with the existing API and performance constraints.
- Deliberate load failures remain tested. Production Addressables logging is unchanged.

## Results

- Before fix: six preload tests passed while the Console displayed two intentional errors.
- After fix: eight preload tests passed, with zero new error logs and Console error count zero for that run.
- Full JP-hosted Unity suites: PlayMode 269/269, EditMode 1211/1211 passed; no skips.
- Independent review found no behavior-order, gain-equivalence, hot-path allocation, or ownership issue in the scoped changes.
- Added coverage for cleanup before propagating an unexpected invalid-reference exception.
