// ItemInstance partial — stable generated-state ownership and typed root undo.
using System;
using Bun3.State;

namespace Bun3.Server.Items
{
    public sealed partial class ItemInstance<TState> : IStateOwner, IStateValueTarget<TState>
    {
        private StateBinding? _stateBinding;
        private bool _stateScopeUsed;
        internal ItemInventory<TState>? StateTransactionOwner;

        internal void InitializeStateTracking()
        {
            if (_state is ITrackedState root)
            {
                var binding = new StateBinding(this);
                binding.AttachRoot(root);
                _stateBinding = binding;
            }
        }

        internal void DetachStateTracking(StateJournal? journal) => _stateBinding?.DetachRoot(journal);

        internal void FinalizeStateTracking()
        {
            try
            {
                if (_owner == null) DetachStateTracking(null);
            }
            finally
            {
                StateTransactionOwner = null;
                TxToken = 0;
                TxSnapshotIndex = -1;
            }
        }

        private void ThrowIfGeneratedStateTracking()
        {
            if (_stateBinding != null || _state is ITrackedState)
            {
                throw new InvalidOperationException(
                    "Generated state and BeginStateChange cannot be mixed on one item instance. Edit generated properties directly.");
            }
        }

        private void SetState(TState value)
        {
            var root = value as ITrackedState;
            if (root != null && _stateScopeUsed)
            {
                throw new InvalidOperationException(
                    "An item instance that has used BeginStateChange cannot accept generated state. Load a new instance instead.");
            }

            if ((_state is ITrackedState || root != null) && ReferenceEquals(_state, value)) return;

            var owner = _owner ?? StateTransactionOwner;
            if (owner != null && (_stateBinding != null || root != null))
            {
                var binding = _stateBinding ?? new StateBinding(this);
                binding.ValidateRoot(root);
                var journal = owner.PrepareStateChange(this);
                journal?.RecordValue(this, 0, _state);
                binding.ReplaceRoot(root, journal);
                _stateBinding = binding;
            }
            else
            {
                _owner?.CaptureForRollback(this);
            }

            _state = value;
            MarkChangedInternal();
        }

        StateJournal? IStateOwner.BeforeChange() =>
            (_owner ?? StateTransactionOwner)?.PrepareStateChange(this);

        void IStateOwner.OnChanged()
        {
            if (_owner != null) MarkChangedInternal();
        }

        void IStateValueTarget<TState>.RestoreValue(int slot, TState value) => _state = value;
    }
}
