using System;
using System.Collections.Generic;

namespace Bun3.Common.Collections
{
    /// <summary>
    /// Array-backed binary min-heap. Mirrors the .NET 6+
    /// <c>System.Collections.Generic.PriorityQueue</c> API subset so consumers can migrate
    /// mechanically once off netstandard2.1. Elements dequeue in ascending priority order
    /// per the comparer; ties dequeue in unspecified order. Not thread safe.
    /// </summary>
    public class PriorityQueue<TElement, TPriority>
    {
        private (TElement Element, TPriority Priority)[] _nodes;
        private int _count;
        private readonly IComparer<TPriority> _comparer;

        public PriorityQueue() : this(0, null) { }

        public PriorityQueue(int initialCapacity) : this(initialCapacity, null) { }

        public PriorityQueue(IComparer<TPriority> comparer) : this(0, comparer) { }

        public PriorityQueue(int initialCapacity, IComparer<TPriority> comparer)
        {
            if (initialCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            _nodes = initialCapacity > 0
                ? new (TElement, TPriority)[initialCapacity]
                : Array.Empty<(TElement, TPriority)>();
            _comparer = comparer ?? Comparer<TPriority>.Default;
        }

        public int Count => _count;

        public IComparer<TPriority> Comparer => _comparer;

        public void Enqueue(TElement element, TPriority priority)
        {
            if (_count == _nodes.Length)
                Array.Resize(ref _nodes, _nodes.Length == 0 ? 4 : _nodes.Length * 2);
            var index = _count++;
            _nodes[index] = (element, priority);
            SiftUp(index);
        }

        public TElement Dequeue()
        {
            if (_count == 0)
                throw new InvalidOperationException("The queue is empty.");
            var element = _nodes[0].Element;
            RemoveRoot();
            return element;
        }

        public bool TryDequeue(out TElement element, out TPriority priority)
        {
            if (_count == 0)
            {
                element = default;
                priority = default;
                return false;
            }
            (element, priority) = _nodes[0];
            RemoveRoot();
            return true;
        }

        public TElement Peek()
        {
            if (_count == 0)
                throw new InvalidOperationException("The queue is empty.");
            return _nodes[0].Element;
        }

        public bool TryPeek(out TElement element, out TPriority priority)
        {
            if (_count == 0)
            {
                element = default;
                priority = default;
                return false;
            }
            (element, priority) = _nodes[0];
            return true;
        }

        public void Clear()
        {
            Array.Clear(_nodes, 0, _count);
            _count = 0;
        }

        private void RemoveRoot()
        {
            var lastIndex = --_count;
            _nodes[0] = _nodes[lastIndex];
            _nodes[lastIndex] = default;
            if (_count > 0)
                SiftDown(0);
        }

        private void SiftUp(int index)
        {
            var node = _nodes[index];
            while (index > 0)
            {
                var parentIndex = (index - 1) >> 1;
                var parent = _nodes[parentIndex];
                if (_comparer.Compare(node.Priority, parent.Priority) >= 0)
                    break;
                _nodes[index] = parent;
                index = parentIndex;
            }
            _nodes[index] = node;
        }

        private void SiftDown(int index)
        {
            var node = _nodes[index];
            while (true)
            {
                var childIndex = (index << 1) + 1;
                if (childIndex >= _count)
                    break;
                var rightIndex = childIndex + 1;
                if (rightIndex < _count &&
                    _comparer.Compare(_nodes[rightIndex].Priority, _nodes[childIndex].Priority) < 0)
                    childIndex = rightIndex;
                if (_comparer.Compare(_nodes[childIndex].Priority, node.Priority) >= 0)
                    break;
                _nodes[index] = _nodes[childIndex];
                index = childIndex;
            }
            _nodes[index] = node;
        }
    }
}
