using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Unity.Acoustics
{
    /// <summary>Builds cold-path acoustic surfaces and conservative grid-visible probe candidates.</summary>
    public static class AcousticGridBuilder
    {
        /// <summary>Builds from stable matching masks; outside the rectangle remains open.</summary>
        public static AcousticGridData Build(bool[,] blocked, bool[,] roofed, AcousticGridFrame frame, AcousticGridSettings settings)
        {
            if (blocked == null) throw new ArgumentNullException(nameof(blocked));
            if (roofed == null) throw new ArgumentNullException(nameof(roofed));
            var width = blocked.GetLength(0);
            var height = blocked.GetLength(1);
            if (width == 0 || height == 0 || roofed.GetLength(0) != width || roofed.GetLength(1) != height ||
                (long)width * height * 36 > int.MaxValue)
                throw new ArgumentException("Masks must have matching positive dimensions within mesh index capacity.");
            frame.Validate();
            settings.Validate();
            for (var cornerY = 0; cornerY <= 1; cornerY++)
                for (var cornerX = 0; cornerX <= 1; cornerX++)
                    if (!AcousticGridFrame.Finite(frame.Point(cornerX * width, cornerY * height, settings.CeilingHeight)) ||
                        !AcousticGridFrame.Finite(frame.Point(cornerX * width, cornerY * height, settings.FloorHeight)))
                        throw new ArgumentException("Grid bounds exceed finite world coordinates.");

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    if (blocked[x, y]) continue;
                    var a = frame.Point(x, y, settings.FloorHeight);
                    var b = frame.Point(x + 1, y, settings.FloorHeight);
                    var c = frame.Point(x + 1, y + 1, settings.FloorHeight);
                    var d = frame.Point(x, y + 1, settings.FloorHeight);
                    var rise = frame.HeightAxis * (settings.CeilingHeight - settings.FloorHeight);
                    AddQuad(vertices, triangles, a, b, c, d, frame.HeightAxis);
                    if (roofed[x, y]) AddQuad(vertices, triangles, a + rise, b + rise, c + rise, d + rise, -frame.HeightAxis);
                    if (x > 0 && blocked[x - 1, y]) AddQuad(vertices, triangles, a, d, d + rise, a + rise, frame.CellX);
                    if (x + 1 < width && blocked[x + 1, y]) AddQuad(vertices, triangles, b, c, c + rise, b + rise, -frame.CellX);
                    if (y > 0 && blocked[x, y - 1]) AddQuad(vertices, triangles, a, b, b + rise, a + rise, frame.CellY);
                    if (y + 1 < height && blocked[x, y + 1]) AddQuad(vertices, triangles, d, c, c + rise, d + rise, -frame.CellY);
                }
            var probes = new ProbeBuilder(blocked, frame, settings).Build();
            return new AcousticGridData(vertices.ToArray(), triangles.ToArray(), probes);
        }

        private static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            var start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            var forward = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0;
            triangles.Add(start); triangles.Add(start + (forward ? 1 : 2)); triangles.Add(start + (forward ? 2 : 1));
            triangles.Add(start); triangles.Add(start + (forward ? 2 : 3)); triangles.Add(start + (forward ? 3 : 2));
        }

        private sealed class ProbeBuilder
        {
            private readonly bool[,] _blocked;
            private readonly AcousticGridFrame _frame;
            private readonly AcousticGridSettings _settings;
            private readonly int _width, _height;
            private readonly bool[] _covered;
            private readonly int[] _seen, _heap, _positions;
            private readonly float[] _distance;
            private readonly float _stepX, _stepY;
            private int _generation, _count;

            internal ProbeBuilder(bool[,] blocked, AcousticGridFrame frame, AcousticGridSettings settings)
            {
                _blocked = blocked; _frame = frame; _settings = settings;
                _width = blocked.GetLength(0); _height = blocked.GetLength(1);
                var size = _width * _height;
                _covered = new bool[size]; _seen = new int[size]; _heap = new int[size];
                _positions = new int[size]; _distance = new float[size];
                _stepX = frame.CellX.magnitude; _stepY = frame.CellY.magnitude;
            }

            internal Vector3[] Build()
            {
                var probes = new List<Vector3>();
                for (var y = 0; y < _height; y++)
                    for (var x = 0; x < _width; x++)
                    {
                        if (_blocked[x, y] || _covered[y * _width + x]) continue;
                        probes.Add(_frame.Point(x + .5f, y + .5f, _settings.EarHeight));
                        Cover(x, y);
                    }
                return probes.ToArray();
            }

            private void Cover(int sourceX, int sourceY)
            {
                _generation++;
                _count = 0;
                Visit(sourceX, sourceY, 0);
                while (_count > 0)
                {
                    var id = Pop();
                    var x = id % _width;
                    var y = id / _width;
                    if (!_covered[id] && Visible(sourceX, sourceY, x, y)) _covered[id] = true;
                    Visit(x - 1, y, _distance[id] + _stepX);
                    Visit(x + 1, y, _distance[id] + _stepX);
                    Visit(x, y - 1, _distance[id] + _stepY);
                    Visit(x, y + 1, _distance[id] + _stepY);
                }
            }

            private void Visit(int x, int y, float distance)
            {
                if (x < 0 || y < 0 || x >= _width || y >= _height || _blocked[x, y] || distance > _settings.ProbeSpacing) return;
                var id = y * _width + x;
                int position;
                if (_seen[id] == _generation)
                {
                    if (distance >= _distance[id]) return;
                    position = _positions[id];
                }
                else
                {
                    _seen[id] = _generation;
                    position = _count++;
                }
                _distance[id] = distance;
                while (position > 0)
                {
                    var parent = (position - 1) / 2;
                    if (!Less(id, _heap[parent])) break;
                    _heap[position] = _heap[parent];
                    _positions[_heap[position]] = position;
                    position = parent;
                }
                _heap[position] = id;
                _positions[id] = position;
            }

            private int Pop()
            {
                var result = _heap[0];
                var last = _heap[--_count];
                var position = 0;
                while (position * 2 + 1 < _count)
                {
                    var child = position * 2 + 1;
                    if (child + 1 < _count && Less(_heap[child + 1], _heap[child])) child++;
                    if (!Less(_heap[child], last)) break;
                    _heap[position] = _heap[child];
                    _positions[_heap[position]] = position;
                    position = child;
                }
                if (_count > 0) { _heap[position] = last; _positions[last] = position; }
                return result;
            }

            private bool Less(int a, int b) => _distance[a] < _distance[b] || (_distance[a] == _distance[b] && a < b);

            private bool Visible(int x, int y, int targetX, int targetY)
            {
                var dx = Math.Abs(targetX - x);
                var dy = Math.Abs(targetY - y);
                var sx = Math.Sign(targetX - x);
                var sy = Math.Sign(targetY - y);
                var ix = 0;
                var iy = 0;
                while (ix < dx || iy < dy)
                {
                    var crossing = (long)(1 + 2 * ix) * dy - (long)(1 + 2 * iy) * dx;
                    if (crossing == 0)
                    {
                        if (_blocked[x + sx, y] || _blocked[x, y + sy]) return false;
                        x += sx; y += sy; ix++; iy++;
                    }
                    else if (crossing < 0) { x += sx; ix++; }
                    else { y += sy; iy++; }
                    if (_blocked[x, y]) return false;
                }
                return true;
            }
        }
    }
}
