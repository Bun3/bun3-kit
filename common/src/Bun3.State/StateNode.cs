// Scalar tracking and shared node metadata; child ownership is in StateNode.Children.cs.
using System;
using System.Collections.Generic;

namespace Bun3.State
{
    /// <summary>Tracks one mutable state node. Child ownership is implemented in the Children partial.</summary>
    public sealed partial class StateNode
    {
        private readonly ITrackedNode _target;

        /// <summary>Creates tracking metadata for a state node.</summary>
        /// <param name="target">The node to track.</param>
        public StateNode(ITrackedNode target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        internal ITrackedNode Target => _target;

        internal StateBinding? Binding { get; set; }

        internal bool IsConnected { get; set; }

        internal bool ChildrenInitialized { get; set; }

        internal StateNode? Parent { get; private set; }
        internal StateNode? FirstChild { get; private set; }
        private StateNode? _previousSibling;
        private StateNode? _nextSibling;
        private StateBinding.GraphValidation? _validation;

        internal StateBinding.GraphValidation Validation => _validation ??= new StateBinding.GraphValidation();

        /// <summary>Sets a scalar field and records its prior value when a transaction is active.</summary>
        /// <typeparam name="T">The scalar field type.</typeparam>
        /// <param name="slot">The generated field slot.</param>
        /// <param name="storage">The backing field to set.</param>
        /// <param name="value">The new value.</param>
        /// <returns><see langword="true"/> if the field changed; otherwise <see langword="false"/>.</returns>
        public bool SetValue<T>(int slot, ref T storage, T value)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            var target = (IStateValueTarget<T>)_target;
            var binding = Binding;
            if (binding != null)
            {
                var journal = binding.Owner.BeforeChange();
                journal?.RecordValue(target, slot, storage);
            }

            storage = value;
            if (IsConnected)
            {
                binding!.Owner.OnChanged();
            }

            return true;
        }
    }
}
