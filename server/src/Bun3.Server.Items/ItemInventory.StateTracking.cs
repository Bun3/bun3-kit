// ItemInventory partial — one reusable generated-state journal per inventory.
using Bun3.State;

namespace Bun3.Server.Items
{
    public sealed partial class ItemInventory<TState>
    {
        private StateJournal? _stateJournal;

        internal StateJournal? PrepareStateChange(ItemInstance<TState> instance)
        {
            if (!InTransaction) return null;
            Capture(instance);
            return _stateJournal ??= new StateJournal();
        }

        private void DetachStateTracking(ItemInstance<TState> instance)
        {
            instance.DetachStateTracking(instance.State is ITrackedState
                ? PrepareStateChange(instance)
                : null);
        }
    }
}
