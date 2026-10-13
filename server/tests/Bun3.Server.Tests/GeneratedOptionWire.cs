using ProtoBuf;

namespace Bun3.Server.Tests;

[ProtoContract]
internal sealed class OptionWire : Extensible
{
    [ProtoMember(1)] public List<OptionEntryWire> Entries { get; } = new();
    private int? _version;
    [ProtoMember(2)] public int Version { get => _version ?? 0; set => _version = value; }
    public bool ShouldSerializeVersion() => _version.HasValue;
    public void ResetVersion() => _version = null;
    private int? _mainOptionLevel;
    [ProtoMember(3)] public int MainOptionLevel { get => _mainOptionLevel ?? 0; set => _mainOptionLevel = value; }
    public bool ShouldSerializeMainOptionLevel() => _mainOptionLevel.HasValue;
    public void ResetMainOptionLevel() => _mainOptionLevel = null;
    [ProtoMember(5)] public OptionEntryWire? PetOption { get; set; }
}

[ProtoContract]
internal sealed class OptionEntryWire : Extensible
{
    [ProtoMember(1)] public int Id { get; set; }
    private int? _level;
    [ProtoMember(2)] public int Level { get => _level ?? 0; set => _level = value; }
    public bool ShouldSerializeLevel() => _level.HasValue;
    public void ResetLevel() => _level = null;
}
