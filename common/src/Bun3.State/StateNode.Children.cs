// Child ownership, attachment metadata, and subtree lifecycle.
using System;

namespace Bun3.State
{
    public sealed partial class StateNode
    {
        /// <summary>Atomically initializes parent ownership for this node's existing child graph.</summary>
        /// <remarks>
        /// Call before exposing initialized fields through generated properties. Repeated calls do no work.
        /// Metadata access and child visitation must not invoke initialization recursively.
        /// This operation does not replace fields, record undo entries, or invoke owner callbacks.
        /// </remarks>
        public void InitializeChildren()
        {
            if (ChildrenInitialized) return;
            var validation = Validation;
            try
            {
                validation.Validate(_target, null, Binding);
                validation.Apply(Binding, IsConnected);
            }
            finally
            {
                validation.Clear();
            }
        }

        /// <summary>Validates and replaces a child reference, recording its value and ownership.</summary>
        /// <typeparam name="T">The nullable or nonnullable tracked child type.</typeparam>
        /// <param name="slot">The generated field slot.</param>
        /// <param name="storage">The backing field to set.</param>
        /// <param name="value">The new child, or null.</param>
        /// <returns>Whether the child reference changed.</returns>
        public bool SetChild<T>(int slot, ref T storage, T value) where T : class?, ITrackedNode?
        {
            if (ReferenceEquals(storage, value)) return false;
            if (storage != null)
            {
                var oldNode = storage.Tracking;
                if ((oldNode.Parent != null && !ReferenceEquals(oldNode.Parent, this)) ||
                    (oldNode.Binding != null && !ReferenceEquals(oldNode.Binding, Binding)) ||
                    (oldNode.IsConnected && oldNode.Parent == null))
                    throw new InvalidOperationException("The existing child belongs to another tree position.");
            }
            var validation = Validation;
            try
            {
                validation.Validate(value, this, Binding);
                var target = (IStateValueTarget<T>)_target;
                var journal = Binding?.Owner.BeforeChange();
                journal?.RecordValue(target, slot, storage);
                journal?.RecordOwnership(Binding!, this, storage, value);
                ReplaceChild(storage, journal, validation);
                storage = value;
                if (IsConnected) Binding!.Owner.OnChanged();
                return true;
            }
            finally
            {
                validation.Clear();
            }
        }

        internal void ReplaceChild(ITrackedNode? oldChild, StateJournal? journal,
            StateBinding.GraphValidation validation)
        {
            oldChild?.Tracking.Detach(journal != null);
            validation.Apply(Binding, IsConnected);
        }

        internal void SetParent(StateNode? parent)
        {
            if (ReferenceEquals(Parent, parent)) return;
            if (_previousSibling != null) _previousSibling._nextSibling = _nextSibling;
            else if (Parent != null) Parent.FirstChild = _nextSibling;
            if (_nextSibling != null) _nextSibling._previousSibling = _previousSibling;
            Parent = parent;
            _previousSibling = null;
            _nextSibling = parent?.FirstChild;
            if (_nextSibling != null) _nextSibling._previousSibling = this;
            if (parent != null) parent.FirstChild = this;
        }

        internal void Detach(bool retainBinding)
        {
            SetParent(null);
            SetMembership(retainBinding ? Binding : null, false);
        }

        internal void SetMembership(StateBinding? binding, bool connected)
        {
            Binding = binding;
            IsConnected = connected;
            for (var child = FirstChild; child != null; child = child._nextSibling)
            {
                child.SetMembership(binding, connected);
            }
        }

        internal void RestoreChild(ITrackedNode? oldChild, ITrackedNode? newChild)
        {
            newChild?.Tracking.Detach(true);
            if (oldChild != null)
            {
                oldChild.Tracking.SetParent(this);
                oldChild.Tracking.SetMembership(Binding, IsConnected);
            }
        }
    }
}
