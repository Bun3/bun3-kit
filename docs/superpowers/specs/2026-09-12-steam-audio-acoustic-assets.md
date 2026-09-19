# Native acoustic bake and load assets

Status: implemented and native-validated on 2026-09-12. `acoustic-asset-green.xml` passed all 5 asset tests plus the separately owned renderer-factory test (6/6 total).

`SteamAudioAcousticBaker.Bake` accepts an existing SDK context, mesh vertices/triangles/material indices/materials, exact probe spheres, immutable bake settings and a nonblank geometry fingerprint. It creates a default native scene, commits the static mesh/probes, performs a synchronous dynamic-pathing bake with a rooted nonnull callback, serializes scene/probe data and returns a new generic `SteamAudioAcousticAsset`. The caller owns asset persistence and chooses geometry; no map or door knowledge enters the package.

The ScriptableObject stores format version 1, Steam Audio version 4.8.1, fingerprint, copied settings, copied scene/probe blobs with SHA-256 checksums, and exact supplied probe centers/radii. Public properties and `GetProbe(index)` expose immutable values; blob arrays are not public. Settings expose visibility samples/radius/threshold/range, path range and thread count. Defaults are one sample, 0.05m radius, 0.99 threshold, 4.5m visibility range, 100m path range and one bake thread.

`SteamAudioAcousticSceneScope(Context, asset)` validates metadata, checksums and sizes before native loading, retains a context wrapper, and owns the loaded Scene/ProbeBatch. Borrowed `Context`, `Scene`, and `Probes` properties are creating-thread-only. Geometry may be added/removed through the borrowed Scene on that thread; callers commit edits and invalidate their simulation revision. The normal disposal order is simulation, scene scope, caller context, though an existing simulation scope's independently retained native handles remain valid after the loader releases its wrappers.

Supported SDK deserialization constructors are used without source edits or private reflection. They contain an internal SerializedObject temporary released on success; exceptional native scene-load failure can defer that SDK-internal temporary to its finalizer. All successfully returned scope-owned wrappers and temporary SerializedData Unity objects are released deterministically. A zero probe-batch handle is detected even when the SDK logs rather than throws. Metadata/checksums detect accidental corruption; they do not prove arbitrary native blob contents safe or authenticate externally supplied assets.

Actual spheres are preserved for coverage consumers. This layer does not claim that a nonzero old SH result proves current endpoint coverage; callers must gate simulation publication using their coverage query. No geometry topology or complete sealed-room validator is included.

Validation uses generated native geometry and probes, an actual Unity asset save/reload, checksummed corruption/version rejection, copied caller arrays, deterministic normal load/disposal and a retained simulation after loader teardown. Native path outputs must survive serialization. No game assets, SDK edits or commits are part of this task.

## Verification record

`E:/Temp/bun3-sound-validation-20260911/Results/acoustic-asset-red.xml` recorded five intended missing-contract failures. `acoustic-asset-green.xml` passed on the actual Windows x64 Steam Audio 4.8.1 library, including asset persistence followed by nonzero native indirect signal, exact independent probe metadata, pre-native checksum/version rejection, and simulation after loader/context release. Both bake and load require a nonempty native dynamic-pathing data layer. The baker owns native mesh upload buffers in try/finally and uses explicit serialized-object cleanup; it does not inherit the SDK mesh constructor's exceptional upload-buffer leak.

The actual-SDK netstandard2.1/C#9 documentation build completed with zero warnings and errors (`E:/Temp/steam-path-simulation-harness/Runtime.csproj`). The remaining SDK-internal exceptional deserialization finalizer limitation above is unchanged. Game scene authoring, runtime door synchronization and endpoint coverage are separate integrations.
