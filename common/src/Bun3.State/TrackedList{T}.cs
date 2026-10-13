using System;
using System.Collections;
using System.Collections.Generic;

namespace Bun3.State
{
    /// <summary>A mutable list whose values and owned child nodes participate in state tracking.</summary>
    /// <typeparam name="T">A supported scalar or tracked node type.</typeparam>
    public sealed class TrackedList<T> : IList<T>, IReadOnlyList<T>, ITrackedNode
    {
        private static readonly bool IsNode = typeof(ITrackedNode).IsAssignableFrom(typeof(T));
        private readonly List<T> _items;
        private readonly StateNode _tracking;

        /// <summary>Creates an empty tracked list.</summary>
        public TrackedList() : this(0) { }

        /// <summary>Creates an empty tracked list with initial capacity.</summary>
        /// <param name="capacity">The initial capacity.</param>
        public TrackedList(int capacity)
        {
            _items = new List<T>(capacity);
            _tracking = new StateNode(this);
        }

        StateNode ITrackedNode.Tracking => _tracking;

        void ITrackedNode.VisitChildren(IStateChildVisitor visitor)
        {
            if (!IsNode) return;
            foreach (var item in _items)
            {
                var child = AsNode(item);
                if (child != null) visitor.Visit(child);
            }
        }

        /// <summary>Gets the number of elements.</summary>
        public int Count => _items.Count;

        /// <summary>Gets whether mutation is prohibited; always false.</summary>
        public bool IsReadOnly => false;

        /// <summary>Gets or replaces an element, comparing tracked nodes by identity.</summary>
        /// <param name="index">The zero-based element index.</param>
        public T this[int index]
        {
            get => _items[index];
            set
            {
                var oldValue = _items[index];
                if (Same(oldValue, value)) return;
                Change(ListChange.Replace, index, oldValue, value);
            }
        }

        /// <summary>Appends an element.</summary>
        /// <param name="item">The element to append.</param>
        public void Add(T item) => Insert(Count, item);

        /// <summary>Inserts an element at the specified position.</summary>
        /// <param name="index">The insertion position, including the end of the list.</param>
        /// <param name="item">The element to insert.</param>
        public void Insert(int index, T item)
        {
            if ((uint)index > (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            Change(ListChange.Insert, index, default!, item);
        }

        /// <summary>Removes the first matching scalar value or exact tracked-node reference.</summary>
        /// <param name="item">The element to remove.</param>
        /// <returns>Whether an element was removed.</returns>
        public bool Remove(T item)
        {
            var index = IndexOf(item);
            if (index < 0) return false;
            RemoveAt(index);
            return true;
        }

        /// <summary>Removes the element at the specified index.</summary>
        /// <param name="index">The zero-based element index.</param>
        public void RemoveAt(int index) => Change(ListChange.Remove, index, _items[index], default!);

        /// <summary>Removes all elements, recording removals from the end toward the beginning.</summary>
        public void Clear()
        {
            for (var index = Count - 1; index >= 0; index--) RemoveAt(index);
        }

        /// <summary>Finds a scalar value or exact tracked-node reference.</summary>
        /// <param name="item">The element to find.</param>
        /// <returns>The first matching index, or -1.</returns>
        public int IndexOf(T item)
        {
            if (!IsNode) return _items.IndexOf(item);
            for (var index = 0; index < Count; index++)
                if (Same(_items[index], item)) return index;
            return -1;
        }

        /// <summary>Checks for a scalar value or exact tracked-node reference.</summary>
        /// <param name="item">The element to find.</param>
        /// <returns>Whether the element exists.</returns>
        public bool Contains(T item) => IndexOf(item) >= 0;

        /// <summary>Copies the elements to an array.</summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The destination starting index.</param>
        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        /// <summary>Returns an allocation-free enumerator for direct iteration.</summary>
        /// <returns>A version-checked enumerator.</returns>
        public Enumerator GetEnumerator() => new Enumerator(_items.GetEnumerator());

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private static bool Same(T left, T right) => IsNode
            ? ReferenceEquals(left, right) : EqualityComparer<T>.Default.Equals(left, right);

        private static ITrackedNode? AsNode(T value) => (ITrackedNode?)(object?)value;

        private void Change(ListChange change, int index, T oldValue, T newValue)
        {
            var oldNode = IsNode && change != ListChange.Insert ? AsNode(oldValue) : null;
            var newNode = IsNode && change != ListChange.Remove ? AsNode(newValue) : null;
            var validation = IsNode ? _tracking.Validation : null;
            try
            {
                validation?.Validate(newNode, _tracking, _tracking.Binding);
                var binding = _tracking.Binding;
                var journal = binding?.Owner.BeforeChange();
                journal?.RecordList(this, change, index, oldValue);
                if (IsNode)
                {
                    journal?.RecordOwnership(binding!, _tracking, oldNode, newNode);
                    _tracking.ReplaceChild(oldNode, journal, validation!);
                }
                if (change == ListChange.Insert) _items.Insert(index, newValue);
                else if (change == ListChange.Remove) _items.RemoveAt(index);
                else _items[index] = newValue;
                if (_tracking.IsConnected) binding!.Owner.OnChanged();
            }
            finally { validation?.Clear(); }
        }

        internal void Restore(ListChange change, int index, T oldValue)
        {
            if (change == ListChange.Insert) _items.RemoveAt(index);
            else if (change == ListChange.Remove) _items.Insert(index, oldValue);
            else _items[index] = oldValue;
        }

        /// <summary>Enumerates the list without allocation and rejects mutation during iteration.</summary>
        public struct Enumerator : IEnumerator<T>
        {
            private List<T>.Enumerator _inner;
            internal Enumerator(List<T>.Enumerator inner) { _inner = inner; }

            /// <summary>Gets the current element.</summary>
            public T Current => _inner.Current;
            object? IEnumerator.Current => ((IEnumerator)_inner).Current;

            /// <summary>Advances to the next element.</summary>
            /// <returns>Whether another element exists.</returns>
            public bool MoveNext() => _inner.MoveNext();

            /// <summary>Releases the enumerator.</summary>
            public void Dispose() => _inner.Dispose();

            void IEnumerator.Reset()
            {
                IEnumerator boxed = _inner;
                boxed.Reset();
                _inner = (List<T>.Enumerator)boxed;
            }
        }
    }

    internal enum ListChange { Insert, Remove, Replace }
}
