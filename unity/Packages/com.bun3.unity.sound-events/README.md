# Bun3 Unity Sound Events

Deterministic sound-event lifetimes and spatial delivery. This package uses `Vector3` but does not depend on audio playback, networking, SDKs, or game-domain types.

## Installation and assembly

Requires Unity 6000.3 or later as declared in `package.json`. Add this URL through Package Manager **Add package from Git URL**:

```text
https://github.com/Bun3/bun3-kit.git?path=/unity/Packages/com.bun3.unity.sound-events#Bun3/jp-sound-integration
```

This is a moving development branch, not a released tag. Private repository access requires Git authentication. Reference `Bun3.Unity.SoundEvents` in your assembly definition and import the matching namespace. The package declares no package dependencies. For integrations that also need sibling packages, Git installation does not recursively resolve unpublished siblings from the same repository; install each required package explicitly at compatible revisions or provide a registry.

## Responsibilities

The package owns bounded event storage, spatial delivery, registration generations, activity lifetimes and report bookkeeping. The game owns sound-kind meanings, intensity/radius tuning, authoritative positions, authenticated sender identity, network transport, permissions, team/group rules, reactions and decision ranking. An event reaching a listener is a gameplay notification, not evidence that a player hears audio. No probes or native bake are required; there is no native pathfinding, occlusion, audio playback or voice-activity detection here. The default spherical reach test ignores walls.

## Minimal world example

This self-contained example can be called from a test or a Unity component. It defines all identities, timing and receiver behavior, and releases registrations deterministically.

```csharp
using System;
using Bun3.Unity.SoundEvents;
using UnityEngine;

public static class SoundEventExample
{
    private sealed class Receiver : ISoundEventListener
    {
        public int NotificationCount { get; private set; }
        public void OnSoundEvent(in SoundEventSnapshot snapshot, SoundEventPhase phase)
        {
            NotificationCount++;
        }
    }

    public static int Run()
    {
        using var world = new SoundEventWorld(eventCapacity: 128, listenerCapacity: 32);
        var receiver = new Receiver();
        var listener = world.RegisterListener(Vector3.zero, 1f, receiver);
        var data = new SoundEventData(sessionId: 1, eventId: 1, sourceId: 7,
            kindId: 10, position: Vector3.right, intensity: 0.8f,
            radius: 4f, hostTick: 0, revision: 0);
        if (!world.TryStart(data, expiresAt: 10.0, out var sound))
            throw new InvalidOperationException("Event capacity exhausted.");
        world.Tick(0.0); // Enter.
        sound.Update(data, expiresAt: 20.0);
        world.Tick(1.0); // Update.
        sound.Stop(); // End is synchronous.
        listener.Release();
        return receiver.NotificationCount; // 3.
    }
}
```

## Delivery and lifetime contract

Implement `ISoundEventListener.OnSoundEvent(in SoundEventSnapshot, SoundEventPhase)`. `TryEmitPulse` queues a single `Pulse` notification per reachable listener on the next tick; pending pulses can be stopped. `TryStart` creates a sustained event. Sustained events emit `Enter`, then `Update` on each tick in reach, `Exit` on leaving reach, and `End` on stop, source removal, expiry, or world disposal. Reentry emits another `Enter`. An already-exited listener receives no `End`. Releasing a listener emits `Exit` for its active relationships.

Call `Tick(double now)` with an absolute, finite, nonnegative monotonic time. Equal tick times are permitted and still deliver sustained updates. Expiry is checked before reach, including equality; zero-duration or already-expired events never enter. Position, intensity, and radii must be finite; intensity and radii must be nonnegative. `Update` cannot change the session, event, or source identity. Revision ordering, identifier uniqueness, session ownership, and gameplay interpretation belong to the caller.

The default reach policy detects sphere contact, including equality: center distance is at most source radius plus listener radius. Supply an `ISoundEventReachPolicy` to replace it. Arithmetic uses doubles internally to avoid overflow for finite float geometry.

Storage is allocated once: event slots, listener slots, and an event-capacity × listener-capacity relationship matrix. Normal ticks allocate no managed memory inside the package. Caller callbacks and custom policies must honor their own allocation budget. Event exhaustion returns `false`; listener exhaustion throws `InvalidOperationException`. Stale or default handles return `false` and cannot affect reused slots. Slots whose generation counter is exhausted are retired rather than wrapping.

Use `TryRegisterListener` when capacity exhaustion should return `false` without exceptions or allocations. `TryEmitPulse(data, out handle, captureReach: true)` evaluates reach at enqueue for existing listener registrations, then delivers the captured recipients at the next tick. Subsequent movement does not alter that pulse; removed or replaced listeners do not receive it. Capturing reach forbids reentrant world mutation. A policy exception rolls back the entire pending pulse and releases its capacity, without listener notifications. The default `captureReach: false` retains tick-time reach evaluation.

The world is single-threaded. Delivery uses ascending event-slot and listener-slot order. Mutations and nested ticks from listeners or reach policies throw `InvalidOperationException`; queue those actions outside the world and apply them after the current operation returns. Read-only handle validity checks are permitted. To resolve order-independent gameplay decisions, collect notifications and evaluate them after the tick.

Listener exceptions do not interrupt remaining delivery or cleanup: the first exception is rethrown after the operation completes. A reach-policy exception skips that pair, preserves an existing sustained relationship, and is likewise rethrown after the tick. Pulses are consumed even when a callback or reach policy throws. Stop, listener release, source removal, and disposal notify synchronously. Disposal invalidates all handles even if a listener throws; it is otherwise idempotent. Other world methods throw `ObjectDisposedException` after disposal.

Package tests cover contact boundaries, lifetimes, cleanup, generations, capacity, numeric validation, callback restrictions, exception recovery, snapshot contents, policy replacement, and allocation-free steady-state ticks. They run through Unity Test Runner with this package included in the project's `testables` list.

## Activity leases

`SoundActivityLease` owns one sustained event for one authoritative source and session. Construct it once with a finite positive lease duration and a session-unique `Func<ulong>` event ID allocator. Supply only host-derived geometry and tuning through `SoundActivityData`; sender identity, session authentication, report rate, and policy evaluation remain outside this package.

For session-owned identity allocation and lease setup, use the complete orchestration example below. For direct world ownership, construct `SoundActivityLease(world, sessionId, sourceId, leaseSeconds, allocateEventId)` once per source; `allocateEventId` must be a stable `Func<ulong>` supplying session-unique identities.

The first report may use sequence zero. Later reports must strictly increase; sequence wrap is unsupported, so create a new lease for a new session. A fresh active report starts an identity or renews the current one. Heartbeats advance its revision and expiry without allocating managed memory or calling the identity allocator. Expiry before a fresh report, external event removal, or revision exhaustion causes a fresh identity. The allocator must return unique IDs and must not mutate the world or lease; it is invoked only on attempted starts, including attempts rejected for capacity.

Host policy denial immediately ends activity **even when the report sequence is stale** and returns `Denied`. Other stale reports return `Stale` without event changes. Fresh denied and inactive reports consume their sequence. Capacity rejection returns `CapacityUnavailable` and consumes the sequence; neither `Tick` nor replay retries the start. Only a fresh active report can retry, avoiding retained pending work and retry loops. `Tick(now, allowed)` independently enforces expiry and policy loss when no report arrives. Call it for every live lease, then tick the shared world to deliver the resulting event state.

Times must be finite, nonnegative, and no earlier than the lease's latest accepted operation or the world's latest tick. Active reports require a finite expiry strictly greater than their report time. Stop and policy denial do not require a representable future expiry. Input validation precedes all state mutation. Listener callback exceptions propagate after event cleanup; accepted sequences remain consumed. Mutations from callbacks or the identity allocator are rejected. Disposal permanently invalidates the lease and works even after world disposal.

`IsActive` reflects the current generation-safe registration; clock expiry is applied by `Tick`, `Report`, or the world's tick. Use it alongside `CurrentEventId`, since zero is also a valid caller-allocated identity. This is a local activity lifetime primitive, not a network validator or voice-activity detector.


## Session and activity orchestration

`SoundEventSession` owns session identity, event ID allocation, listener registration and live listener-position refresh. Implement `ISoundEventSessionListener` to supply current position and receive events. Interpretation remains application policy.

`ValidatedSoundActivitySource` binds reports to one owner and session, validates sequence and report rate, and manages an expiring activity lease. Pass host-authoritative activity data and an `allowed` decision. `OutgoingSoundActivityState` preserves short activations between network polls and emits ordered activity/heartbeat/stop reports without per-poll allocation. Applications own transport, group membership and host permission rules. Mutations from delivery callbacks are rejected before report state is consumed; retry after delivery completes.


## Session and validated activity example

The following complete example stands in for an authenticated transport receiving a report. Replace the constants with host-owned identity, clock, policy and geometry. Keep one source object per owner for its session, process reports, tick every source, then tick the shared session.

```csharp
using System;
using Bun3.Unity.SoundEvents;
using UnityEngine;

public static class SoundActivityExample
{
    private sealed class Receiver : ISoundEventSessionListener
    {
        public Vector3 SoundPosition => Vector3.zero;
        public int Notifications { get; private set; }
        public void BeginSoundTick() { }
        public void EndSoundTick() { }
        public void OnSoundEvent(in SoundEventSnapshot snapshot, SoundEventPhase phase)
        {
            Notifications++;
        }
    }

    public static int Run()
    {
        using var session = new SoundEventSession(1, 128, 32);
        var receiver = new Receiver();
        var listener = session.Listen(receiver.SoundPosition, 1f, receiver);
        using var source = new ValidatedSoundActivitySource(session, ownerId: 7,
            leaseSeconds: 1.0, minimumReportInterval: 0.1);
        var outgoing = new OutgoingSoundActivityState();
        outgoing.Reset(session.SessionId);
        if (!outgoing.TryBuildReport(0.0, 0.2, active: true, hadActivation: true,
            out var report))
            throw new InvalidOperationException("Expected an initial activity report.");
        var hostData = new SoundActivityData(kindId: 10, position: Vector3.right,
            intensity: 0.8f, radius: 4f, hostTick: 0);
        bool accepted = source.Report(senderId: 7, sessionId: report.SessionId,
            sequence: report.Sequence, active: report.Active,
            hadActivation: report.HadActivation, now: 0.0,
            allowed: true, hostData: hostData);
        if (!accepted) throw new InvalidOperationException("Activity was rejected.");
        source.Tick(0.0, allowed: true);
        session.Tick(0.0); // Enter; live listener position is refreshed.
        source.Tick(1.0, allowed: true); // Expiry ends activity even without a stop report.
        session.Tick(1.0);
        listener.Release();
        return receiver.Notifications; // Enter + End.
    }
}
```

`SoundEventSession` requires a nonzero session ID and positive capacities. `AllocateEventId()` returns increasing nonzero identities and throws on exhaustion. `EmitPulse(data)` requires matching session identity, refreshes live positions and captures current recipients for next-tick delivery. `Listen`/`TryListen` return ordinary listener handles; `ISoundEventSessionListener.SoundPosition` is refreshed before ticks and session pulses. Its registration radius is retained by the session. `BeginSoundTick` and `EndSoundTick` bracket session tick delivery; successful begins receive an end even after a delivery exception. Immediate stop/release callbacks can occur outside that bracket. Do not mutate session state from getters or callbacks.

`ValidatedSoundActivitySource.Report` rejects owner/session mismatches and stale sequences. Call it only with the sender identity established by your authenticated transport. It cannot authenticate a network connection itself. Fresh reports consume their sequence even when throttled. Permission loss ends activity immediately, including stale denial reports; accepted stops and short activations can preserve activity that occurred entirely between world ticks as a pulse. A `false` result can therefore coexist with required cleanup; it does not mean no state changed. Do not replay a consumed report to retry capacity or throttling failure.

`OutgoingSoundActivityState.Reset(sessionId)` resets sequence and heartbeat state. `TryBuildReport` emits increasing sequences beginning at one, retains `hadActivation` until included in a report, reports active heartbeats, and sends a stop immediately after previously reported activity. Use a positive finite report interval and finite nonnegative time. A short activation can wait for the next report interval; `HasPendingActivation` exposes that state and `ClearPendingActivation()` discards it when policy requires. Sequence exhaustion returns `false`; reset for a new session. The caller serializes outgoing state access and supplies a consistent clock.

## API and configuration reference

| API | Configuration / result |
| --- | --- |
| `SoundEventWorld(eventCapacity, listenerCapacity, reachPolicy = null)` | Positive fixed capacities; null selects inclusive `SphereSoundEventReachPolicy`. No automatic capacity growth. |
| `TryEmitPulse(data, out handle, captureReach = false)` | Queues one pulse; default reach is evaluated on the next tick. Returns false when full. |
| `TryStart(data, expiresAt, out handle)` | Creates sustained registration with absolute finite nonnegative expiry. Returns false when full. |
| `RegisterListener` / `TryRegisterListener` | Position, radius and receiver; throwing or nonthrowing capacity behavior. |
| `SoundEventHandle.IsValid / Update / Stop` | Generation-safe registration; only sustained events can be updated. Stop is synchronous. |
| `SoundEventListenerHandle.IsValid / Update / Release` | Generation-safe listener; update position/radius; release synchronously emits exits. |
| `Tick(now)` / `RemoveSource(sourceId)` | Monotonic absolute clock delivery / bulk source cleanup returning removed event count. |
| `SoundEventData` | Session, event and source IDs; game-owned integer kind; position, intensity, radius, host tick and revision. Intensity does not alter default spatial reach. |
| `SoundEventSnapshot` | Value payload plus `IsSustained` and absolute `ExpiresAt`; pulse expiry is zero. |
| `ISoundEventReachPolicy.CanReach(...)` | Replacement spatial test passed at world construction; may inspect snapshot and listener geometry, but may not mutate the world. |
| `SoundActivityLease.Report(...)` | Returns `Started`, `Renewed`, `Stopped`, `Stale`, `Denied` or `CapacityUnavailable`; explicit sequence, active flag, policy and host data. |
| `SoundActivityLease.Tick(now, allowed = true)` | Enforces expiry and permission changes without a report. |
| `SoundActivityReportGate(minimumInterval)` | Finite nonnegative interval. `TryAccept(sequence, now, bypassInterval = false)` consumes fresh sequence watermarks even when throttled. Bypass admits cleanup without advancing normal admission timing. |
| `SoundEventSession` / `ValidatedSoundActivitySource` | Session ID allocation and live listeners / per-owner validation and activity orchestration. Lease duration must be positive finite; minimum report interval may be zero. |

All capacities, activity durations and report intervals are explicit caller choices. Example values are demonstration settings, not package defaults. A pulse and a sustained event both occupy event capacity until consumed/ended. The relationship matrix scales with the product of event and listener capacities; size both together.

## Ownership, threading and allocation

Keep world/session, listeners and sources alive across ticks. Create these objects and receiver/delegate instances during setup; dispose sources on disconnect and the session when the session ends. A session owns its world; avoid bypassing its identity and position-refresh rules through direct world calls. Release individual listener handles when the associated entity leaves. Snapshots are copied values and may be retained without borrowing package storage.

The world and orchestration objects are not thread safe. Serialize reports, ticking, registration, updates and disposal on one owner thread; marshal transport callbacks to that thread. There are no internal worker threads. Tick, report and callback operations are synchronous. Normal steady-state world delivery and activity bookkeeping avoid package-managed allocation after setup; exception paths, caller callbacks, custom reach policies and caller-created closures can allocate. Do not log or format strings in a high-frequency callback when measuring this contract.

## Troubleshooting and tests

| Symptom | Check |
| --- | --- |
| Listener receives nothing | Tick the world/session, inspect capacity return values, confirm matching session and positive lifetime, then check positions and sum of radii. No audio component or probe can substitute for ticking. |
| Event crosses a wall | Default reach is sphere contact. Supply an explicit gameplay reach policy if walls should affect event delivery; audible audio uses its own simulation. |
| Activity remains after disconnect | Dispose its source/lease or remove its world events by source ID. Continue source ticks to enforce expiry while connected. |
| Short speech disappeared | Feed observed activation edges into `hadActivation`, preserve outgoing state between polls, and use validated-source handling. The low-level world cannot reconstruct missing input edges. |
| Report retry is rejected | Sequence watermarks include rate-limited reports. Send a fresh sequence; do not replay the old one. |
| Callback mutation throws | Collect decisions during delivery; apply mutations only after the enclosing operation returns. |
| An exited listener receives no end | Expected: `End` is for relationships still in reach; `Exit` already ended that listener relationship. |

Add `"com.bun3.unity.sound-events"` to the consuming project's `Packages/manifest.json` `testables` array. Run the `Bun3.Unity.SoundEvents.Tests` assembly through Unity Test Runner. Tests cover world contracts, sessions, leases, report gates, outgoing activity, validated sources, reentrancy, exception cleanup and steady-state allocations. These are deterministic event tests; transport authentication, game policy and actual audible output need application integration tests.
