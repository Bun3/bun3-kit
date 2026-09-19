using System;
using System.Collections.Generic;
using Bun3.Unity.Acoustics;
using Bun3.Unity.Audio.SteamAudio;
using UnityEngine;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>A generation-checked source handle owned by one planar world.</summary>
    public readonly struct PlanarAcousticSourceHandle
    {
        internal readonly PlanarAcousticWorld Owner;
        internal readonly int Index;
        internal readonly uint Generation;
        internal PlanarAcousticSourceHandle(PlanarAcousticWorld owner, int index, uint generation)
        { Owner = owner; Index = index; Generation = generation; }
        /// <summary>Whether the owning world still holds this source generation.</summary>
        public bool IsValid => Owner != null && Owner.Contains(this);
    }

    /// <summary>A validated native path with its listener frame and obstruction gain.</summary>
    public readonly struct PlanarAcousticPath
    {
        /// <summary>Native listener coordinate frame for rendering.</summary>
        public SA.CoordinateSpace3 Listener { get; }
        /// <summary>Current native path and obstruction result.</summary>
        public SteamAudioPathSimulationResult SimulationResult { get; }
        /// <summary>Additional obstruction gain, separate from native distance attenuation.</summary>
        public float Gain { get; }
        internal PlanarAcousticPath(SA.CoordinateSpace3 listener, SteamAudioPathSimulationResult result, float gain)
        { Listener = listener; SimulationResult = result; Gain = gain; }
    }

    /// <summary>One control-thread owner for a baked map, live door geometry, and bounded path sources.</summary>
    public class PlanarAcousticWorld : IDisposable
    {
        struct Source
        {
            public bool Active;
            public uint Generation;
            public Vector2 Position;
            public SteamAudioSimulationSourceHandle Native;
        }
        struct Quad
        {
            public Vector2 A, B, C, D;
            public bool Overlaps(Quad other) => Axis(other, B - A) && Axis(other, D - A) && other.Axis(this, other.B - other.A) && other.Axis(this, other.D - other.A);
            bool Axis(Quad other, Vector2 edge)
            {
                var axis = new Vector2(-edge.y, edge.x);
                float a = Vector2.Dot(A, axis), b = Vector2.Dot(B, axis), c = Vector2.Dot(C, axis), d = Vector2.Dot(D, axis);
                float e = Vector2.Dot(other.A, axis), f = Vector2.Dot(other.B, axis), g = Vector2.Dot(other.C, axis), h = Vector2.Dot(other.D, axis);
                // Boundary contact alone must not close an adjacent cell.
                return Mathf.Min(Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d)), Mathf.Max(Mathf.Max(e, f), Mathf.Max(g, h))) >
                    Mathf.Max(Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d)), Mathf.Min(Mathf.Min(e, f), Mathf.Min(g, h))) + 0.000001f * axis.magnitude;
            }
        }
        sealed class Door
        {
            public Matrix4x4 Matrix;
            public Vector2 Offset, Size;
            public Quad Polygon;
            public SteamAudioMeshScope Mesh;
            public bool Closed;
        }
        readonly int thread = Environment.CurrentManagedThreadId;
        readonly PlanarAcousticMap map;
        readonly PlanarAcousticSettings config;
        readonly AcousticGridQuery query;
        readonly Source[] sources;
        readonly Dictionary<int, Door> doors = new Dictionary<int, Door>();
        SA.Context context;
        SteamAudioAcousticSceneScope scene;
        SteamAudioPathSimulationScope simulation;
        SA.CoordinateSpace3 listener;
        Vector2 listenerPosition;
        ulong geometryRevision;
        double lastTime = -1;
        bool disposed;
        /// <summary>Whether native ownership has ended.</summary>
        public bool IsDisposed => disposed;
        /// <summary>Last simulated listener position in the planar input coordinates.</summary>
        public Vector2 ListenerPosition => listenerPosition;

        /// <summary>Creates independent native simulation ownership for the supplied map and settings.</summary>
        public PlanarAcousticWorld(PlanarAcousticMap map, PlanarAcousticSettings config)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.SourceCapacity <= 0 || !Finite(config.OcclusionRadius) || config.OcclusionRadius < 0 ||
                config.OcclusionSamples < 1 || config.OcclusionSamples > 32) throw new ArgumentException("Acoustic source settings are invalid.", nameof(config));
            this.map = map; this.config = config;
            query = map.CreateGridQuery();
            sources = new Source[config.SourceCapacity];
            try
            {
                context = new SA.Context();
                scene = new SteamAudioAcousticSceneScope(context, map.Baked);
                AudioSettings.GetDSPBufferSize(out int frameSize, out _);
                var settings = map.Baked.BakeSettings;
                simulation = new SteamAudioPathSimulationScope(context, scene.Scene, scene.Probes, sources.Length,
                    AudioSettings.outputSampleRate, frameSize, 1, settings.VisibilityRadius, settings.VisibilityThreshold, settings.VisibilityRange, 32);
            }
            catch { Dispose(); throw; }
        }

        /// <summary>Registers a bounded source, returning false when capacity is exhausted.</summary>
        public bool TryRegisterSource(Vector2 position, out PlanarAcousticSourceHandle handle)
        {
            Ensure(); Validate(position); handle = default;
            for (int i = 0; i < sources.Length; i++)
            {
                ref var source = ref sources[i];
                if (source.Active || source.Generation == uint.MaxValue) continue;
                var coordinates = Coordinates(position);
                if (!simulation.TryRegisterSource(coordinates, out var native)) return false;
                try { simulation.SetOcclusion(native, SA.OcclusionType.Volumetric, config.OcclusionRadius, config.OcclusionSamples); }
                catch { simulation.RemoveSource(native); throw; }
                source.Generation++; source.Active = true; source.Position = position; source.Native = native;
                handle = new PlanarAcousticSourceHandle(this, i, source.Generation);
                return true;
            }
            return false;
        }

        /// <summary>Updates a live source position; stale handles return false.</summary>
        public bool SetSourcePosition(PlanarAcousticSourceHandle handle, Vector2 position)
        {
            Ensure(); Validate(position);
            if (!Contains(handle)) return false;
            ref var source = ref sources[handle.Index];
            if (source.Position.Equals(position)) return true;
            var coordinates = Coordinates(position);
            simulation.SetSource(source.Native, coordinates); source.Position = position;
            return true;
        }

        /// <summary>Updates the native path distance model; unity gain extends to this authored minimum.</summary>
        public bool SetSourceMinimumDistance(PlanarAcousticSourceHandle handle, float minimumDistance)
        {
            Ensure();
            if (!Finite(minimumDistance)) throw new ArgumentOutOfRangeException(nameof(minimumDistance));
            return Contains(handle) && simulation.SetSourceMinimumDistance(sources[handle.Index].Native, minimumDistance);
        }

        /// <summary>Releases a source generation; stale handles return false.</summary>
        public bool RemoveSource(PlanarAcousticSourceHandle handle)
        {
            Ensure();
            if (!Contains(handle)) return false;
            ref var source = ref sources[handle.Index];
            simulation.RemoveSource(source.Native); source.Active = false;
            return true;
        }

        /// <summary>Simulates at a nondecreasing control-thread timestamp.</summary>
        public void Tick(Vector2 position, double time)
        {
            Ensure(); Validate(position);
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0 || time < lastTime) throw new ArgumentOutOfRangeException(nameof(time));
            var coordinates = Coordinates(position);
            simulation.SetListener(coordinates);
            simulation.Simulate(time);
            listener = coordinates; listenerPosition = position; lastTime = time;
        }

        /// <summary>Queries current grid connectivity and probe attachment without running or changing native simulation.</summary>
        public bool IsRouteCovered(Vector2 source, Vector2 target)
        {
            Ensure(); Validate(source); Validate(target);
            var coverage = query.Query(Point(source), Point(target));
            return coverage.Reachability == AcousticGridReachability.Connected &&
                coverage.SourceCoverage.Status == AcousticEndpointCoverageStatus.GridVisible &&
                coverage.ListenerCoverage.Status == AcousticEndpointCoverageStatus.GridVisible;
        }

        /// <summary>Copies a current, covered native path; stale or silent results return false.</summary>
        public bool TryGetPath(PlanarAcousticSourceHandle handle, float[] coefficients, out PlanarAcousticPath path)
        {
            Ensure(); path = default;
            if (coefficients == null) throw new ArgumentNullException(nameof(coefficients));
            Array.Clear(coefficients, 0, coefficients.Length);
            if (coefficients.Length != 4) throw new ArgumentException("Order-one paths require four coefficients.", nameof(coefficients));
            if (!Contains(handle) || lastTime < 0) return false;
            var source = sources[handle.Index];
            if (!IsRouteCovered(source.Position, listenerPosition)) return false;
            if (!simulation.TryCopyResult(source.Native, coefficients, out var result) || result.Status != SteamAudioPathSimulationStatus.Valid ||
                result.GeometryRevision != geometryRevision || !result.HasPathSignal)
            { Array.Clear(coefficients, 0, coefficients.Length); return false; }
            float obstruction = config.ObstructedPathGain;
            float gain = Finite(obstruction) ? Mathf.Lerp(Mathf.Clamp01(obstruction), 1, Mathf.Clamp01(result.DirectOcclusion)) : 0;
            if (!(gain > 0)) { Array.Clear(coefficients, 0, coefficients.Length); return false; }
            path = new PlanarAcousticPath(listener, result, gain);
            return true;
        }

        /// <summary>Sets the immutable native distance curve for a live source.</summary>
        public bool SetSourceDistanceCurve(PlanarAcousticSourceHandle handle, Bun3.Unity.Audio.DistanceAttenuationCurve curve)
        {
            Ensure();
            return Contains(handle) && simulation.SetSourceDistanceCurve(sources[handle.Index].Native, curve);
        }

        /// <summary>Enables or disables native distance gain for a live source.</summary>
        public bool SetSourceDistanceAttenuationEnabled(PlanarAcousticSourceHandle handle, bool enabled)
        {
            Ensure();
            return Contains(handle) && simulation.SetSourceDistanceAttenuationEnabled(sources[handle.Index].Native, enabled);
        }

        /// <summary>Updates the fallback attenuation endpoint and edge fade.</summary>
        public bool SetSourceDistanceRange(PlanarAcousticSourceHandle handle, float maximum, float fadeFraction)
        {
            Ensure();
            return Contains(handle) && simulation.SetSourceDistanceRange(sources[handle.Index].Native, maximum, fadeFraction);
        }

        /// <summary>Reads native evaluated-distance bounds and evaluation counts.</summary>
        public bool TryGetNativePathDistances(PlanarAcousticSourceHandle handle, out float shortest, out float longest,
            out int evaluatedCount, out int nonzeroGainCount)
        {
            Ensure(); shortest = longest = 0; evaluatedCount = nonzeroGainCount = 0;
            return Contains(handle) && simulation.TryGetPathDistanceRange(sources[handle.Index].Native,
                out shortest, out longest, out evaluatedCount, out nonzeroGainCount);
        }

        /// <summary>
        /// Gets output stereo width from the shortest native evaluated path. Missing path measurements retain full width;
        /// this never substitutes straight-line/grid distance or changes audibility, path EQ or attenuation.
        /// </summary>
        public float GetSpatialBlend(PlanarAcousticSourceHandle handle)
        {
            return TryGetNativePathDistances(handle, out float shortest, out _, out _, out _)
                ? config.EvaluateSpatialBlend(shortest) : 1;
        }

        /// <summary>Evaluates an output-specific shared or inline stereo-width range using only native path measurements.</summary>
        public float GetSpatialBlend(PlanarAcousticSourceHandle handle, Bun3.Unity.Audio.SpatialBlendProfile profile,
            float monoDistance, float fullSpatialDistance)
        {
            if (!TryGetNativePathDistances(handle, out float shortest, out _, out _, out _)) return 1;
            return profile != null ? profile.Evaluate(shortest) :
                Bun3.Unity.Audio.SpatialBlendProfile.Evaluate(shortest, monoDistance, fullSpatialDistance);
        }

        /// <summary>Whether the loaded native SDK exposes path diagnostic extensions.</summary>
        public bool SupportsNativePathDiagnostics => simulation.SupportsPathDiagnostics;

        /// <summary>Enables opt-in native path capture when supported by the loaded SDK.</summary>
        public bool SetNativePathDiagnosticsEnabled(bool enabled)
        {
            Ensure();
            return simulation.SetPathDiagnosticsEnabled(enabled);
        }

        /// <summary>Copies captured paths into caller-owned storage and reports dropped entries.</summary>
        public int CopyNativePathDiagnostics(PlanarAcousticSourceHandle handle, Span<SteamAudioPathDiagnostic> destination, out int dropped)
        {
            Ensure(); dropped = 0;
            return Contains(handle) ? simulation.CopyPathDiagnostics(sources[handle.Index].Native, destination, out dropped) : 0;
        }

        /// <summary>Copies one captured path polyline into caller-owned storage.</summary>
        public int CopyNativePathDiagnosticPoints(PlanarAcousticSourceHandle handle, int index, Span<SA.Vector3> destination, out bool truncated)
        {
            Ensure(); truncated = false;
            return Contains(handle) ? simulation.CopyPathDiagnosticPoints(sources[handle.Index].Native, index, destination, out truncated) : 0;
        }

        /// <summary>Updates an extruded planar box obstacle and invalidates prior paths before committing geometry.</summary>
        public void SetObstacle(int id, BoxCollider2D collider, bool closed)
        {
            Ensure();
            if (collider == null) throw new ArgumentNullException(nameof(collider));
            var matrix = collider.transform.localToWorldMatrix;
            var offset = collider.offset; var size = collider.size;
            Validate(offset); Validate(size);
            if (size.x <= 0 || size.y <= 0) throw new ArgumentException("Door dimensions must be positive.", nameof(collider));
            for (int i = 0; i < 16; i++) if (!Finite(matrix[i])) throw new ArgumentException("Door transform must be finite.", nameof(collider));
            bool exists = doors.TryGetValue(id, out var door);
            bool shapeChanged = !exists || !door.Matrix.Equals(matrix) || !door.Offset.Equals(offset) || !door.Size.Equals(size);
            if (!shapeChanged && door.Closed == closed) return;
            Door replacement = null;
            if (shapeChanged)
            {
                var half = size * .5f;
                var polygon = new Quad
                {
                    A = matrix.MultiplyPoint3x4(offset + new Vector2(-half.x, -half.y)),
                    B = matrix.MultiplyPoint3x4(offset + new Vector2(half.x, -half.y)),
                    C = matrix.MultiplyPoint3x4(offset + new Vector2(half.x, half.y)),
                    D = matrix.MultiplyPoint3x4(offset + new Vector2(-half.x, half.y))
                };
                if (Mathf.Abs(Cross(polygon.B - polygon.A, polygon.D - polygon.A)) < 0.0000001f) throw new ArgumentException("Door world shape is degenerate.", nameof(collider));
                replacement = new Door { Matrix = matrix, Offset = offset, Size = size, Polygon = polygon };
                replacement.Mesh = CreateDoorMesh(polygon);
            }
            try { Invalidate(); }
            catch { if (replacement != null) replacement.Mesh.Dispose(); throw; }
            if (shapeChanged)
            {
                if (exists)
                {
                    if (door.Closed) door.Mesh.Remove();
                    door.Mesh.Dispose();
                }
                door = replacement; doors[id] = door;
            }
            else if (door.Closed) door.Mesh.Remove();
            door.Closed = closed;
            if (closed) door.Mesh.Add();
            CommitDoors();
        }

        /// <summary>Removes an obstacle and invalidates prior paths.</summary>
        public bool RemoveObstacle(int id)
        {
            Ensure();
            if (!doors.TryGetValue(id, out var door)) return false;
            Invalidate();
            if (door.Closed) door.Mesh.Remove();
            door.Mesh.Dispose(); doors.Remove(id);
            CommitDoors(); return true;
        }

        void Invalidate()
        {
            if (geometryRevision == ulong.MaxValue) throw new InvalidOperationException("Geometry revision cannot wrap.");
            simulation.InvalidateGeometry(++geometryRevision);
        }
        void CommitDoors()
        {
            scene.Scene.Commit();
            var frame = map.Frame;
            for (int y = 0; y < map.Height; y++) for (int x = 0; x < map.Width; x++)
            {
                bool blocked = map.IsBlocked(x, y);
                if (!blocked)
                {
                    Vector3 corner = frame.Origin + frame.CellX * x + frame.CellY * y;
                    var cell = new Quad { A = GamePoint(corner), B = GamePoint(corner + frame.CellX),
                        C = GamePoint(corner + frame.CellX + frame.CellY), D = GamePoint(corner + frame.CellY) };
                    foreach (var pair in doors) if (pair.Value.Closed && cell.Overlaps(pair.Value.Polygon)) { blocked = true; break; }
                }
                query.SetBlocked(x, y, blocked);
            }
        }

        SteamAudioMeshScope CreateDoorMesh(Quad polygon)
        {
            var vertices = new SA.Vector3[8];
            var corners = new[] { polygon.A, polygon.B, polygon.C, polygon.D };
            for (int i = 0; i < 4; i++)
            {
                vertices[i] = new SA.Vector3 { x = corners[i].x, y = map.Frame.Origin.y + map.FloorHeight, z = -corners[i].y };
                vertices[i + 4] = new SA.Vector3 { x = corners[i].x, y = map.Frame.Origin.y + map.CeilingHeight, z = -corners[i].y };
            }
            int[] topology = { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0 };
            var triangles = new SA.Triangle[12];
            for (int i = 0; i < 12; i++) triangles[i] = new SA.Triangle { index0 = topology[3 * i], index1 = topology[3 * i + 1], index2 = topology[3 * i + 2] };
            return new SteamAudioMeshScope(scene.Scene, vertices, triangles, new int[12],
                new[] { new SA.Material { absorptionLow = .1f, absorptionMid = .1f, absorptionHigh = .1f, scattering = .5f } });
        }
        internal bool Contains(PlanarAcousticSourceHandle handle) => !disposed && ReferenceEquals(handle.Owner, this) && handle.Index >= 0 &&
            handle.Index < sources.Length && sources[handle.Index].Active && sources[handle.Index].Generation == handle.Generation;
        Vector3 Point(Vector2 value) => new Vector3(value.x, map.Frame.Origin.y + map.EarHeight, -value.y);
        SA.CoordinateSpace3 Coordinates(Vector2 value)
        {
            var point = Point(value);
            return new SA.CoordinateSpace3 { origin = new SA.Vector3 { x = point.x, y = point.y, z = point.z },
                right = new SA.Vector3 { x = 1 }, up = new SA.Vector3 { y = 1 }, ahead = new SA.Vector3 { z = -1 } };
        }
        static Vector2 GamePoint(Vector3 point) => new Vector2(point.x, -point.z);
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void Validate(Vector2 value) { if (!Finite(value.x) || !Finite(value.y)) throw new ArgumentException("Position must be finite.", nameof(value)); }
        void EnsureThread() { if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Acoustic world operations belong to the creating thread."); }
        void Ensure() { EnsureThread(); if (disposed) throw new ObjectDisposedException(nameof(PlanarAcousticWorld)); }
        /// <summary>Releases retained resources. Repeated disposal is harmless.</summary>
        public void Dispose()
        {
            EnsureThread(); if (disposed) return; disposed = true;
            simulation?.Dispose(); simulation = null;
            foreach (var pair in doors)
            {
                var door = pair.Value;
                if (door.Closed) door.Mesh.Remove();
                door.Mesh.Dispose();
            }
            doors.Clear(); scene?.Dispose(); scene = null; context?.Release(); context = null;
        }
    }
}
