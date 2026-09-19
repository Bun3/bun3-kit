# Bun3 Unity Sound Events

Deterministic sound-event lifetimes and spatial delivery. This package uses `Vector3` but does not depend on audio playback, networking, SDKs, or game-domain types.

```csharp
var world = new SoundEventWorld(eventCapacity: 128, listenerCapacity: 32);
var listener = world.RegisterListener(position, hearingRadius, receiver);
var data = new SoundEventData(sessionId, eventId, sourceId, kindId,
    position, intensity, propagationRadius, hostTick, revision);
if (world.TryStart(data, expiresAt: 10, out var sound))
{
    world.Tick(0); // Enter for listeners in reach.
    sound.Update(data, expiresAt: 20);
    world.Tick(1); // Update, Exit, or Enter based on current reach.
    sound.Stop(); // End only for listeners still in reach.
}
listener.Release();
world.Dispose();
```

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

```csharp
var lease = new SoundActivityLease(world, sessionId, sourceId, leaseSeconds: 1,
    allocateEventId: AllocateSessionEventId);
var data = new SoundActivityData(kindId, hostPosition, intensity, radius, hostTick);
var result = lease.Report(sequence, active: true, allowed: policyAllowsActivity,
    now: hostTime, hostData: data);
lease.Tick(hostTime, allowed: policyAllowsActivity);
world.Tick(hostTime);
// Disconnect or end the session after processing has stopped.
lease.Dispose();
```

The first report may use sequence zero. Later reports must strictly increase; sequence wrap is unsupported, so create a new lease for a new session. A fresh active report starts an identity or renews the current one. Heartbeats advance its revision and expiry without allocating managed memory or calling the identity allocator. Expiry before a fresh report, external event removal, or revision exhaustion causes a fresh identity. The allocator must return unique IDs and must not mutate the world or lease; it is invoked only on attempted starts, including attempts rejected for capacity.

Host policy denial immediately ends activity **even when the report sequence is stale** and returns `Denied`. Other stale reports return `Stale` without event changes. Fresh denied and inactive reports consume their sequence. Capacity rejection returns `CapacityUnavailable` and consumes the sequence; neither `Tick` nor replay retries the start. Only a fresh active report can retry, avoiding retained pending work and retry loops. `Tick(now, allowed)` independently enforces expiry and policy loss when no report arrives. Call it for every live lease, then tick the shared world to deliver the resulting event state.

Times must be finite, nonnegative, and no earlier than the lease's latest accepted operation or the world's latest tick. Active reports require a finite expiry strictly greater than their report time. Stop and policy denial do not require a representable future expiry. Input validation precedes all state mutation. Listener callback exceptions propagate after event cleanup; accepted sequences remain consumed. Mutations from callbacks or the identity allocator are rejected. Disposal permanently invalidates the lease and works even after world disposal.

`IsActive` reflects the current generation-safe registration; clock expiry is applied by `Tick`, `Report`, or the world's tick. Use it alongside `CurrentEventId`, since zero is also a valid caller-allocated identity. This is a local activity lifetime primitive, not a network validator or voice-activity detector.


## Session and activity orchestration

`SoundEventSession` owns session identity, event ID allocation, listener registration and live listener-position refresh. Implement `ISoundEventSessionListener` to supply current position and receive events. Interpretation remains application policy.

`ValidatedSoundActivitySource` binds reports to one owner and session, validates sequence and report rate, and manages an expiring activity lease. Pass host-authoritative activity data and an `allowed` decision. `OutgoingSoundActivityState` preserves short activations between network polls and emits ordered activity/heartbeat/stop reports without per-poll allocation. Applications own transport, group membership and host permission rules. Mutations from delivery callbacks are rejected before report state is consumed; retry after delivery completes.
