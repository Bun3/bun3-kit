// Side-effect-free graph validation and root ownership transitions.
using System;
using System.Collections.Generic;

namespace Bun3.State
{
    public sealed partial class StateBinding
    {
        /// <summary>Checks whether a root can be attached without changing any node or invoking the owner.</summary>
        /// <param name="root">The proposed root, or null to detach.</param>
        public void ValidateRoot(ITrackedState? root)
        {
            if (ReferenceEquals(Root, root)) return;
            try { _validation.Validate(root, null, this); }
            finally { _validation.Clear(); }
        }

        /// <summary>Validates, records, and replaces the root without invoking owner callbacks.</summary>
        /// <param name="root">The new root, or null to detach.</param>
        /// <param name="journal">The active transaction journal, or null.</param>
        public void ReplaceRoot(ITrackedState? root, StateJournal? journal)
        {
            if (ReferenceEquals(Root, root)) return;
            try
            {
                _validation.Validate(root, null, this);
                journal?.RecordOwnership(this, null, Root, root);
                Root?.Tracking.Detach(journal != null);
                _validation.Apply(this, true);
                Root = root;
            }
            finally { _validation.Clear(); }
        }

        /// <summary>Detaches the current root, retaining its transaction ownership when a journal is supplied.</summary>
        /// <param name="journal">The active transaction journal, or null.</param>
        public void DetachRoot(StateJournal? journal) => ReplaceRoot(null, journal);

        internal void RestoreRoot(ITrackedNode? oldRoot, ITrackedNode? newRoot)
        {
            newRoot?.Tracking.Detach(true);
            Root = (ITrackedState?)oldRoot;
            oldRoot?.Tracking.SetMembership(this, true);
        }

        internal sealed class GraphValidation : IStateChildVisitor
        {
            private readonly HashSet<StateNode> _visited = new HashSet<StateNode>();
            private readonly List<NodeLink> _nodes = new List<NodeLink>();
            private StateNode? _parent;
            private StateBinding? _binding;

            internal void Validate(ITrackedNode? root, StateNode? parent, StateBinding? binding)
            {
                Clear();
                _binding = binding;
                _parent = parent;
                for (var ancestor = parent; ancestor != null; ancestor = ancestor.Parent)
                    _visited.Add(ancestor);
                if (root == null) return;
                var node = root.Tracking;
                if (node.Parent != null || node.IsConnected)
                    throw new InvalidOperationException("The node already occupies a position in a state tree.");
                Visit(root);
            }

            public void Visit(ITrackedNode child)
            {
                var node = child.Tracking;
                if (node.IsConnected)
                    throw new InvalidOperationException("The node already occupies a position in a state tree.");
                if (!_visited.Add(node))
                    throw new InvalidOperationException("State trees cannot contain shared nodes or cycles.");
                if (node.Binding != null && !ReferenceEquals(node.Binding, _binding))
                    throw new InvalidOperationException("The node belongs to another state owner.");
                if (node.Parent != null && !ReferenceEquals(node.Parent, _parent))
                    throw new InvalidOperationException("The node already belongs to another parent.");
                _nodes.Add(new NodeLink(node, _parent));
                var previous = _parent;
                _parent = node;
                child.VisitChildren(this);
                _parent = previous;
            }

            internal void Apply(StateBinding? binding, bool connected)
            {
                foreach (var link in _nodes)
                {
                    link.Node.SetParent(link.Parent);
                    link.Node.Binding = binding;
                    link.Node.IsConnected = connected;
                    link.Node.ChildrenInitialized = true;
                }
            }

            internal void Clear()
            {
                _visited.Clear();
                _nodes.Clear();
                _parent = null;
                _binding = null;
            }

            private readonly struct NodeLink
            {
                internal NodeLink(StateNode node, StateNode? parent) { Node = node; Parent = parent; }
                internal StateNode Node { get; }
                internal StateNode? Parent { get; }
            }
        }
    }
}
