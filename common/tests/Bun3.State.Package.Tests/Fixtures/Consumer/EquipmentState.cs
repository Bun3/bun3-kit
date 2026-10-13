using Bun3.State;

namespace PackageConsumer
{
    [TrackedState]
    public sealed partial class EquipmentState
    {
        [Tracked] private int _level = 1;
    }
}
