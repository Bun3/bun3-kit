using Bun3.State;

namespace Bun3.Server.Tests;

[TrackedState]
public sealed partial class GeneratedOptionState
{
    [Tracked] private int _level = 1;
    [Tracked] private GeneratedOptionChild _option = new();
    [Tracked] private readonly TrackedList<GeneratedOptionChild> _options = new();
}

[TrackedState]
public sealed partial class GeneratedOptionChild
{
    [Tracked] private int _level = 2;
}

[TrackedState]
public sealed partial class EquipmentOptionState
{
    [Tracked] private readonly TrackedList<EquipmentOptionEntry> _entries = new();
    [Tracked] private int? _version;
    [Tracked] private int? _mainOptionLevel;
    [Tracked] private EquipmentOptionEntry? _specialOption;
}

[TrackedState]
public sealed partial class EquipmentOptionEntry
{
    [Tracked] private int _id;
    [Tracked] private int _poolId;
    [Tracked] private int? _level;
}
