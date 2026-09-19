# Bun3 Unity Acoustics

Cold-build grid geometry and deterministic probe candidates without a simulation SDK dependency.

## Installation and assembly

Requires Unity 6000.3 or later as declared in `package.json`. Add this development branch URL through Package Manager **Add package from Git URL**:

```text
https://github.com/Bun3/bun3-kit.git?path=/unity/Packages/com.bun3.unity.acoustics#Bun3/jp-sound-integration
```

The branch is a moving development reference, not a released tag. Private repository access requires working Git authentication. Reference `Bun3.Unity.Acoustics` from your consuming assembly definition; the namespace is also `Bun3.Unity.Acoustics`. This package declares no package dependencies. When installing it alongside adapters, Git URLs do not recursively resolve unpublished sibling packages from this repository: add each required sibling explicitly at a compatible revision, or use an appropriate registry.

## Scope and first use

This package builds geometry arrays and answers conservative grid questions. It does not create Unity meshes, own native simulation resources, bake probes, play audio, apply occlusion, or decide gameplay hearing. Grid connectivity, a debug route and endpoint coverage are diagnostics; none is proof of an audible native path. The consumer owns upload, baking, materials, dynamic geometry synchronization and native path validation.

This complete example builds a one-cell-wide corridor, then demonstrates connectivity changing independently of probe coverage. Call `Run` from an editor command or test. No scene components or simulation SDK are required.

```csharp
using System;
using Bun3.Unity.Acoustics;
using UnityEngine;

public static class AcousticGridExample
{
    public static AcousticGridQueryResult Run()
    {
        var blocked = new bool[3, 1];
        var roofed = new bool[3, 1];
        var frame = new AcousticGridFrame(Vector3.zero,
            Vector3.right, Vector3.forward, Vector3.up);
        var settings = new AcousticGridSettings(
            floorHeight: 0f, ceilingHeight: 3f, earHeight: 1.5f,
            probeSpacing: 2f);
        AcousticGridData geometry = AcousticGridBuilder.Build(
            blocked, roofed, frame, settings);
        Debug.Assert(geometry.Vertices.Length > 0);

        // Candidate positions are not baked influence spheres.
        var query = new AcousticGridQuery(blocked, frame, 0f, 3f,
            Array.Empty<AcousticProbe>(), probeSelectionLimit: 1);
        var source = new Vector3(0.5f, 1.5f, 0.5f);
        var listener = new Vector3(2.5f, 1.5f, 0.5f);
        var before = query.Query(source, listener);
        Debug.Assert(before.Reachability == AcousticGridReachability.Connected);
        Debug.Assert(before.SourceCoverage.Status == AcousticEndpointCoverageStatus.None);
        query.SetBlocked(1, 0, true);
        return query.Query(source, listener); // Disconnected, revision 1.
    }
}
```

For baked coverage, replace the empty array with `AcousticProbe` values copied from the actual provider's world-space centers and influence radii. Supply the provider's actual selection limit. Inventing radii from `ProbeSpacing` would change the meaning of the result.

## Build contract

`AcousticGridBuilder.Build(blocked, roofed, frame, settings)` returns owned vertex, triangle-index and probe arrays. Masks use `[x, y]`; the frame origin is the lower corner of cell `(0, 0)`. Cell vectors encode world spacing and orientation. The height axis is a perpendicular unit vector. Settings specify floor, ceiling and ear offsets along that axis, plus positive probe coverage distance.

Geometry includes walkable-cell floors, explicitly flagged walkable-cell ceilings and walls between blocked and walkable cells. Adjacent blocked cells produce no internal faces. Outside the input rectangle is open; represent an enclosing boundary with blocked cells. Dynamic obstacles must be supplied separately by the consumer.

Probe placement scans rows then columns. A probe covers a cell only when both the four-neighbor walkable path distance is within the configured distance and a conservative grid-supercover segment is clear. Crossing a blocked corner is not clear. Separate regions and blind corners can therefore receive probes closer together than the configured distance. This setting controls conservative coverage, not minimum separation or a fixed probe count.

Every walkable cell center is covered by at least one candidate under this discrete model. This does not prove native endpoint visibility, baked path availability, arbitrary continuous-position coverage or audio quality. Consumers must validate those properties using their simulation SDK and actual geometry. Generation allocates output and build scratch memory and belongs in authoring or loading, not a frame loop.

## Mutable topology and endpoint queries

`AcousticGridQuery(blocked, frame, floorHeight, ceilingHeight, bakedProbes, probeSelectionLimit)` copies occupancy and actual baked `AcousticProbe` influence spheres and prepares fixed query storage. It is bound to its creating thread. `SetBlocked(x, y, value)` changes final occupancy and increments `Revision` only on a change. Callers combine overlapping obstacle states, synchronize native geometry independently, and discard earlier query snapshots when revisions differ. This class neither owns doors nor updates a native scene.

`Query(source, listener)` returns a value snapshot with independent `Reachability`, `SourceCoverage`, `ListenerCoverage` and `Revision`. Connectivity uses four-neighbor components within the finite input rectangle. Disconnected grid regions do not prove a sealed native space: routes outside the rectangle, over unroofed walls or through geometry absent from the mask are outside this model.

Endpoint height must lie strictly between the configured floor and ceiling. World mapping uses the supplied grid frame. Probe attachment checks the actual three-dimensional influence sphere, including its boundary, and a conservative continuous-position supercover segment through current walkable cells. Contact with a blocked cell edge or corner is rejected. Coordinates within one millionth of a cell of a grid line are snapped conservatively for boundary checks. The first grid-visible probe index is returned for diagnostics; it is not a native selected-probe handle.

Coverage states are separate from connectivity:

| Status | Meaning |
| --- | --- |
| `InvalidEndpoint` | Position is nonfinite, outside the domain/height range, or touches blocked space. |
| `None` | No containing supplied probe is visible in the grid. |
| `GridVisible` | At least one candidate survives any selection of `min(containingCount, probeSelectionLimit)` containing probes. This is grid evidence only. |
| `SelectionUncertain` | Visible candidates exist, but bounded selection could exclude all of them. |

The selection statement assumes the consumer actually selects that many containing candidates before its visibility checks. A consumer with a different selection rule gets no such guarantee. Supply its actual limit explicitly; no provider constants are embedded. Even `GridVisible` cannot prove visibility against native geometry or that the baked probe graph contains a route. Production native publication must preserve that distinction.

After one or more edits, the next valid connectivity query rebuilds components in O(cell count) using the preallocated flood queue. Subsequent connectivity comparison is O(1). Endpoint coverage scans the fixed probe set and bounds every traversed ray by crossed grid cells; total query cost therefore depends on probe count and ray length. No query, edit or component rebuild allocates managed memory. Consumers should schedule queries at their simulation cadence and profile their authored map.

## API and settings reference

All constructor settings are explicit; there is no default frame, height profile or probe-selection limit. Default-initialized frame, settings and probe structs fail validation when consumed.

| API | Contract |
| --- | --- |
| `AcousticGridFrame(origin, cellX, cellY, heightAxis)` | Finite origin; nonzero mutually orthogonal cell vectors; perpendicular unit height axis. Cell lengths set world spacing. |
| `AcousticGridSettings(floorHeight, ceilingHeight, earHeight, probeSpacing)` | Finite values, `floor < ear < ceiling`, positive spacing. Spacing is coverage path distance, not minimum separation. |
| `AcousticGridBuilder.Build(blocked, roofed, frame, settings)` | Matching positive `[x, y]` dimensions; produces `AcousticGridData`. Does not retain input masks. |
| `AcousticGridData.Vertices / Triangles / Probes` | Owned mutable arrays; triangle winding faces walkable space; probes are candidate `Vector3` positions. |
| `AcousticProbe(center, radius)` | Finite world center and strictly positive finite radius; no native handle. |
| `AcousticGridQuery(...)` | Copies occupancy and probe spheres once; finite ordered floor/ceiling; positive selection limit. Probe set is fixed for its lifetime. |
| `SetBlocked(x, y, blocked)` | Returns whether occupancy changed; only changes increment `Revision`; out-of-bounds indices throw. Revision exhaustion throws instead of wrapping. |
| `Query(source, listener)` | Independent connectivity and endpoint-coverage value snapshots at one revision. Invalid endpoint geometry returns statuses rather than throwing. |
| `CreateDebugRoute(source, listener)` | Allocates a four-neighbor connectivity witness, including source, cell centers and listener; empty for invalid/disconnected endpoints. Not a metric shortest route or native sound path. |
| `GetDebugProbe(index)` | Returns the copied sphere at an index from coverage diagnostics. Only use a valid nonnegative index. |

## Ownership, performance and threading

Build during loading or authoring with stable input masks; the builder allocates lists, output arrays and scratch storage. The caller owns the returned arrays and any derived Unity/native resources. Query construction copies inputs and allocates scratch storage; later changes to the original mask or probe array do not affect it. Construct a new query when the frame, height range or baked probe set changes.

`Query`, `SetBlocked`, `CreateDebugRoute` and `GetDebugProbe` must run on the thread that constructed the query; another thread throws `InvalidOperationException`. Query results contain values, not borrowed scratch buffers. No disposal is required because the package owns no native objects. Retain snapshots only with their `Revision` and invalidate cached decisions after topology changes. Use debug-route generation on demand because it allocates; ordinary queries, edits and component rebuilds do not allocate managed memory.

## Troubleshooting and validation

| Symptom | Check |
| --- | --- |
| Connected endpoints have `None` coverage | Connectivity does not require probes. An empty supplied probe array gives `None` for valid endpoints while reachability still works. Check actual influence radii and grid visibility. |
| `InvalidEndpoint` at a wall or floor | Heights are strictly interior; blocked edges/corners are rejected. Check frame orientation, world units and conservative grid-line snapping. |
| `SelectionUncertain` | Some containing spheres are blocked from the endpoint and bounded native selection could omit every visible candidate. This is not a successful native attachment. |
| Query says disconnected but native sound travels | The finite grid does not model paths outside the rectangle, over open walls, or absent geometry. Do not use it as a native sealed-space proof. |
| Door edit does not affect audio | `SetBlocked` changes only query occupancy. Update native geometry separately and aggregate overlapping obstacles before changing the cell. |
| Generated room has no outer walls | Outside the input rectangle is open. Add blocked boundary cells to author enclosure. |

Add `"com.bun3.unity.acoustics"` to the consuming project's `Packages/manifest.json` `testables` array and run `Bun3.Unity.Acoustics.Tests` in Unity Test Runner (EditMode). Tests cover geometry, frames, conservative probe placement, topology edits, endpoint boundaries, coverage ambiguity, debug diagnostics and allocation-free queries. These tests validate the grid model; native baking and audible output require consumer integration tests.
