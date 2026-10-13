// Root identity and coordination; graph validation is in StateBinding.Ownership.cs.
using System;

namespace Bun3.State
{
    /// <summary>Represents a stable item owner. Graph validation is implemented in the Ownership partial.</summary>
    public sealed partial class StateBinding
    {
        private readonly GraphValidation _validation = new GraphValidation();
        /// <summary>Creates a binding for one item owner.</summary>
        /// <param name="owner">The owner that prepares and observes state changes.</param>
        public StateBinding(IStateOwner owner)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        internal IStateOwner Owner { get; }

        /// <summary>Gets the state currently attached as this binding's root.</summary>
        public ITrackedState? Root { get; private set; }

        /// <summary>Attaches a state root to this item.</summary>
        /// <param name="root">The state root to attach.</param>
        public void AttachRoot(ITrackedState root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (Root != null)
            {
                throw new InvalidOperationException("A state root is already attached.");
            }

            ReplaceRoot(root, null);
        }
    }
}
