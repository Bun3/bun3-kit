# Bun3 Unity Acoustics

Cold-build grid geometry and deterministic probe candidates without a simulation SDK dependency.

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
