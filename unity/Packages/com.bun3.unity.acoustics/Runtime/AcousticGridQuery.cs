using System;
using UnityEngine;

namespace Bun3.Unity.Acoustics
{
    /// <summary>
    /// Owns mutable grid connectivity and conservative endpoint coverage on its creating thread.
    /// Queries and component rebuilds use fixed scratch storage. Native geometry and bake ownership stay with the caller.
    /// </summary>
    public sealed class AcousticGridQuery
    {
        private readonly int _thread, _width, _height, _selectionLimit;
        private readonly bool[] _blocked;
        private readonly int[] _labels, _queue;
        private readonly AcousticProbe[] _probes;
        private readonly AcousticGridFrame _frame;
        private readonly float _floor, _ceiling;
        private bool _dirty = true;


        /// <summary>Current topology revision, incremented only when a cell changes.</summary>
        public ulong Revision { get; private set; }

        /// <summary>
        /// Copies masks and actual baked influence spheres and preallocates query scratch.
        /// The selection limit models a consumer selecting min(containing count, limit) candidates before visibility filtering.
        /// </summary>
        public AcousticGridQuery(bool[,] blocked, AcousticGridFrame frame, float floorHeight, float ceilingHeight,
            AcousticProbe[] bakedProbes, int probeSelectionLimit)
        {
            if (blocked == null) throw new ArgumentNullException(nameof(blocked));
            if (bakedProbes == null) throw new ArgumentNullException(nameof(bakedProbes));
            _width = blocked.GetLength(0); _height = blocked.GetLength(1);
            if (_width == 0 || _height == 0) throw new ArgumentException("Grid dimensions must be positive.", nameof(blocked));
            frame.Validate();
            if (!AcousticGridFrame.Finite(floorHeight) || !AcousticGridFrame.Finite(ceilingHeight) || floorHeight >= ceilingHeight)
                throw new ArgumentException("Floor and ceiling must be finite and ordered.");
            if (probeSelectionLimit <= 0) throw new ArgumentOutOfRangeException(nameof(probeSelectionLimit));
            for (var i = 0; i < bakedProbes.Length; i++) bakedProbes[i].Validate();
            _thread = Environment.CurrentManagedThreadId;
            _frame = frame; _floor = floorHeight; _ceiling = ceilingHeight; _selectionLimit = probeSelectionLimit;
            _probes = (AcousticProbe[])bakedProbes.Clone();
            _blocked = new bool[blocked.Length]; _labels = new int[blocked.Length]; _queue = new int[blocked.Length];
            for (var y = 0; y < _height; y++)
                for (var x = 0; x < _width; x++) _blocked[y * _width + x] = blocked[x, y];
        }

        /// <summary>
        /// Sets the final occupancy of one cell and returns whether it changed. The caller aggregates overlapping obstacles
        /// and synchronizes native geometry separately; changing this grid does not update a simulation scene.
        /// </summary>
        public bool SetBlocked(int x, int y, bool blocked)
        {
            EnsureThread();
            if (x < 0 || x >= _width) throw new ArgumentOutOfRangeException(nameof(x));
            if (y < 0 || y >= _height) throw new ArgumentOutOfRangeException(nameof(y));
            var index = y * _width + x;
            if (_blocked[index] == blocked) return false;
            if (Revision == ulong.MaxValue) throw new InvalidOperationException("Grid revision cannot wrap.");
            _blocked[index] = blocked;
            Revision++;
            _dirty = true;
            return true;
        }

        /// <summary>
        /// Returns independent connectivity and probe evidence without allocation. Invalid/nonfinite endpoints return
        /// InvalidEndpoint. Connectivity is restricted to the input rectangle and is not proof of a sealed native space.
        /// </summary>
        public AcousticGridQueryResult Query(Vector3 source, Vector3 listener)
        {
            EnsureThread();
            var sourceValid = TryLocateWalkablePoint(source, out var sx, out var sy, out var sourceCell);
            var listenerValid = TryLocateWalkablePoint(listener, out var lx, out var ly, out var listenerCell);
            var sourceCoverage = EvaluateCoverage(source, sx, sy, sourceValid);
            var listenerCoverage = EvaluateCoverage(listener, lx, ly, listenerValid);
            var reach = AcousticGridReachability.InvalidEndpoint;
            if (sourceValid && listenerValid)
            {
                if (_dirty) RebuildComponents();
                reach = _labels[sourceCell] == _labels[listenerCell]
                    ? AcousticGridReachability.Connected : AcousticGridReachability.Disconnected;
            }
            return new AcousticGridQueryResult(Revision, reach, sourceCoverage, listenerCoverage);
        }

        /// <summary>
        /// Allocates a diagnostic four-neighbor connectivity witness using current occupancy.
        /// Returns an empty array for invalid or disconnected endpoints. This is not a native sound path
        /// or a metric shortest path. Use only for on-demand diagnostics, not the audio update loop.
        /// </summary>
        public Vector3[] CreateDebugRoute(Vector3 source, Vector3 listener)
        {
            EnsureThread();
            if (!TryLocateWalkablePoint(source, out _, out _, out var start) ||
                !TryLocateWalkablePoint(listener, out _, out _, out var end)) return Array.Empty<Vector3>();
            var parents = new int[_blocked.Length];
            Array.Fill(parents, -1);
            var head = 0;
            var tail = 1;
            _queue[0] = start; parents[start] = start;
            while (head < tail && parents[end] < 0)
            {
                var cell = _queue[head++];
                var x = cell % _width;
                var y = cell / _width;
                Visit(x - 1, y, cell); Visit(x + 1, y, cell);
                Visit(x, y - 1, cell); Visit(x, y + 1, cell);
            }
            if (parents[end] < 0) return Array.Empty<Vector3>();
            return BuildDebugRoute(source, listener, start, end, parents);

            void Visit(int x, int y, int parent)
            {
                if (x < 0 || y < 0 || x >= _width || y >= _height) return;
                var index = y * _width + x;
                if (_blocked[index] || parents[index] >= 0) return;
                parents[index] = parent; _queue[tail++] = index;
            }
        }

        private Vector3[] BuildDebugRoute(Vector3 source, Vector3 listener, int start, int end, int[] parents)
        {
            var count = 1;
            for (var cell = end; cell != start; cell = parents[cell]) count++;
            var route = new Vector3[count + 2];
            route[0] = source; route[route.Length - 1] = listener;
            var height = Vector3.Dot(source - _frame.Origin, _frame.HeightAxis);
            var current = end;
            for (var i = count; i > 0; i--)
            {
                route[i] = _frame.Origin + _frame.CellX * (current % _width + .5f) +
                    _frame.CellY * (current / _width + .5f) + _frame.HeightAxis * height;
                current = parents[current];
            }
            return route;
        }

        /// <summary>Returns a baked probe by index for inspecting endpoint coverage evidence.</summary>
        public AcousticProbe GetDebugProbe(int index)
        {
            EnsureThread();
            return _probes[index];
        }

        private AcousticEndpointCoverage EvaluateCoverage(Vector3 position, double x, double y, bool valid)
        {
            if (!valid) return new AcousticEndpointCoverage(AcousticEndpointCoverageStatus.InvalidEndpoint, -1, 0, 0);
            var containing = 0;
            var visible = 0;
            var index = -1;
            for (var i = 0; i < _probes.Length; i++)
            {
                var probe = _probes[i];
                if (!ContainsPoint(probe, position)) continue;
                containing++;
                if (!TryLocateWalkablePoint(probe.Center, out var px, out var py, out _) || !HasClearLineOfSight(x, y, px, py)) continue;
                visible++;
                if (index < 0) index = i;
            }
            return new AcousticEndpointCoverage(ClassifyCoverage(containing, visible), index, containing, visible);
        }

        private AcousticEndpointCoverageStatus ClassifyCoverage(int containing, int visible)
        {
            if (visible == 0) return AcousticEndpointCoverageStatus.None;
            bool selectionMustIncludeVisibleProbe = containing <= _selectionLimit || containing - visible < _selectionLimit;
            return selectionMustIncludeVisibleProbe
                ? AcousticEndpointCoverageStatus.GridVisible
                : AcousticEndpointCoverageStatus.SelectionUncertain;
        }

        private static bool ContainsPoint(AcousticProbe probe, Vector3 position)
        {
            var dx = (double)position.x - probe.Center.x;
            var dy = (double)position.y - probe.Center.y;
            var dz = (double)position.z - probe.Center.z;
            return !(dx * dx + dy * dy + dz * dz > (double)probe.Radius * probe.Radius);
        }

        private bool TryLocateWalkablePoint(Vector3 position, out double x, out double y, out int index)
        {
            x = y = 0; index = -1;
            if (!AcousticGridFrame.Finite(position)) return false;
            var dx = (double)position.x - _frame.Origin.x;
            var dy = (double)position.y - _frame.Origin.y;
            var dz = (double)position.z - _frame.Origin.z;
            var height = dx * _frame.HeightAxis.x + dy * _frame.HeightAxis.y + dz * _frame.HeightAxis.z;
            if (!(height > _floor && height < _ceiling)) return false;
            x = Snap((dx * _frame.CellX.x + dy * _frame.CellX.y + dz * _frame.CellX.z) / Dot(_frame.CellX, _frame.CellX));
            y = Snap((dx * _frame.CellY.x + dy * _frame.CellY.y + dz * _frame.CellY.z) / Dot(_frame.CellY, _frame.CellY));
            if (x < 0 || y < 0 || x >= _width || y >= _height) return false;
            var cx = (int)Math.Floor(x);
            var cy = (int)Math.Floor(y);
            if (!IsCellAndBoundaryClear(cx, cy, x == cx, y == cy)) return false;
            index = cy * _width + cx;
            return true;
        }

        private bool HasClearLineOfSight(double startX, double startY, double endX, double endY)
        {
            var x = (int)Math.Floor(startX);
            var y = (int)Math.Floor(startY);
            var targetX = (int)Math.Floor(endX);
            var targetY = (int)Math.Floor(endY);
            var deltaX = endX - startX;
            var deltaY = endY - startY;
            var directionX = Math.Sign(deltaX);
            var directionY = Math.Sign(deltaY);
            var onVerticalBoundary = directionX == 0 && startX == x;
            var onHorizontalBoundary = directionY == 0 && startY == y;
            var crossingIntervalX = directionX == 0 ? double.PositiveInfinity : 1 / Math.Abs(deltaX);
            var crossingIntervalY = directionY == 0 ? double.PositiveInfinity : 1 / Math.Abs(deltaY);
            var nextCrossingX = directionX == 0 ? double.PositiveInfinity : ((directionX > 0 ? x + 1 : x) - startX) / deltaX;
            var nextCrossingY = directionY == 0 ? double.PositiveInfinity : ((directionY > 0 ? y + 1 : y) - startY) / deltaY;
            var remainingCrossings = (long)Math.Abs(targetX - x) + Math.Abs(targetY - y) + 2;
            while (x != targetX || y != targetY)
            {
                // Endpoint boundary neighbors were checked by TryLocateWalkablePoint; never step beyond that endpoint.
                if (Math.Min(nextCrossingX, nextCrossingY) >= 1) return true;
                if (remainingCrossings-- == 0) return false;
                if (Math.Abs(nextCrossingX - nextCrossingY) <= 1e-10)
                {
                    if (IsBlocked(x + directionX, y) || IsBlocked(x, y + directionY)) return false;
                    x += directionX;
                    y += directionY;
                    nextCrossingX += crossingIntervalX;
                    nextCrossingY += crossingIntervalY;
                }
                else if (nextCrossingX < nextCrossingY)
                {
                    x += directionX;
                    nextCrossingX += crossingIntervalX;
                }
                else
                {
                    y += directionY;
                    nextCrossingY += crossingIntervalY;
                }
                if (!IsCellAndBoundaryClear(x, y, onVerticalBoundary, onHorizontalBoundary)) return false;
            }
            return true;
        }

        private bool IsCellAndBoundaryClear(int x, int y, bool onX, bool onY)
        {
            return !IsBlocked(x, y) && (!onX || !IsBlocked(x - 1, y)) && (!onY || !IsBlocked(x, y - 1)) &&
                (!onX || !onY || !IsBlocked(x - 1, y - 1));
        }

        private bool IsBlocked(int x, int y) => x >= 0 && y >= 0 && x < _width && y < _height && _blocked[y * _width + x];

        private void RebuildComponents()
        {
            Array.Clear(_labels, 0, _labels.Length);
            var label = 0;
            for (var i = 0; i < _blocked.Length; i++)
            {
                if (_blocked[i] || _labels[i] != 0) continue;
                label++;
                LabelConnectedCells(i, label);
            }
            _dirty = false;
        }

        private void LabelConnectedCells(int startCell, int label)
        {
            var head = 0;
            var tail = 1;
            _queue[0] = startCell; _labels[startCell] = label;
            while (head < tail)
            {
                var cell = _queue[head++];
                var x = cell % _width;
                var y = cell / _width;
                EnqueueUnlabeledCell(x - 1, y, label, ref tail);
                EnqueueUnlabeledCell(x + 1, y, label, ref tail);
                EnqueueUnlabeledCell(x, y - 1, label, ref tail);
                EnqueueUnlabeledCell(x, y + 1, label, ref tail);
            }
        }

        private void EnqueueUnlabeledCell(int x, int y, int label, ref int tail)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height) return;
            var cell = y * _width + x;
            if (_blocked[cell] || _labels[cell] != 0) return;
            _labels[cell] = label; _queue[tail++] = cell;
        }

        private static double Dot(Vector3 a, Vector3 b) => (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;
        private static double Snap(double value) => Math.Abs(value - Math.Round(value)) <= 1e-6 ? Math.Round(value) : value;
        private void EnsureThread()
        {
            if (Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("Grid queries and edits must remain on the creating thread.");
        }
    }
}
