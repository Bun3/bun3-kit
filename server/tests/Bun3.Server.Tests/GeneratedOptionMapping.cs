using System.Runtime.CompilerServices;
using Bun3.Server.Tests.Protos;
using Google.Protobuf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProtoBuf;

namespace Bun3.Server.Tests;

internal sealed class GeneratedOptionMapping
{
    private readonly ConditionalWeakTable<object, JObject> _json = new();
    private readonly ConditionalWeakTable<EquipmentOptionState, TrackedOptions> _googleRoots = new();
    private readonly ConditionalWeakTable<EquipmentOptionEntry, TrackedOptionEntry> _googleEntries = new();
    private readonly ConditionalWeakTable<EquipmentOptionState, OptionWire> _netRoots = new();
    private readonly ConditionalWeakTable<EquipmentOptionEntry, OptionEntryWire> _netEntries = new();

    public EquipmentOptionState LoadJson(string json)
    {
        var original = JObject.Parse(json);
        var state = LoadGoogle(original.ToObject<TrackedOptions>()!);
        _json.Add(state, (JObject)original.DeepClone());
        var entries = original["RerollOptions"] as JArray;
        for (var i = 0; i < state.Entries.Count; i++)
            if (entries?[i] is JObject entry) _json.Add(state.Entries[i], (JObject)entry.DeepClone());
        if (state.SpecialOption != null && original["ProductOption"] is JObject child)
            _json.Add(state.SpecialOption, (JObject)child.DeepClone());
        return state;
    }

    public string SaveJson(EquipmentOptionState state)
    {
        var root = JsonTemplate(state);
        var known = JObject.FromObject(SaveGoogle(state));
        foreach (var property in known.Properties()) root[property.Name] = property.Value.DeepClone();
        root["RerollOptions"] = new JArray(state.Entries.Select(SaveJsonEntry));
        root["ProductOption"] = state.SpecialOption == null ? JValue.CreateNull() : SaveJsonEntry(state.SpecialOption);
        return root.ToString(Formatting.None);
    }

    private JObject SaveJsonEntry(EquipmentOptionEntry state)
    {
        var result = JsonTemplate(state);
        foreach (var property in JObject.FromObject(SaveGoogleEntry(state)).Properties())
            result[property.Name] = property.Value.DeepClone();
        return result;
    }

    private JObject JsonTemplate(object state) => _json.TryGetValue(state, out var original)
        ? (JObject)original.DeepClone() : new JObject();

    public EquipmentOptionState LoadBlob(byte[] data)
    {
        using var stream = new MemoryStream(data);
        var dto = Serializer.Deserialize<OptionWire>(stream);
        var state = new EquipmentOptionState
        {
            Version = dto.ShouldSerializeVersion() ? dto.Version : null,
            MainOptionLevel = dto.ShouldSerializeMainOptionLevel() ? dto.MainOptionLevel : null,
            SpecialOption = dto.PetOption == null ? null : LoadNetEntry(dto.PetOption)
        };
        foreach (var entry in dto.Entries) state.Entries.Add(LoadNetEntry(entry));
        _netRoots.Add(state, Serializer.DeepClone(dto));
        return state;
    }

    private EquipmentOptionEntry LoadNetEntry(OptionEntryWire dto)
    {
        var state = new EquipmentOptionEntry { Id = dto.Id, Level = dto.ShouldSerializeLevel() ? dto.Level : null };
        _netEntries.Add(state, Serializer.DeepClone(dto));
        return state;
    }

    public byte[] SaveBlob(EquipmentOptionState state)
    {
        var dto = _netRoots.TryGetValue(state, out var original) ? Serializer.DeepClone(original) : new OptionWire();
        if (state.Version.HasValue) dto.Version = state.Version.Value;
        else dto.ResetVersion();
        if (state.MainOptionLevel.HasValue) dto.MainOptionLevel = state.MainOptionLevel.Value;
        else dto.ResetMainOptionLevel();
        dto.Entries.Clear();
        foreach (var entry in state.Entries) dto.Entries.Add(SaveNetEntry(entry));
        dto.PetOption = state.SpecialOption == null ? null : SaveNetEntry(state.SpecialOption);
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, dto);
        return stream.ToArray();
    }

    private OptionEntryWire SaveNetEntry(EquipmentOptionEntry state)
    {
        var dto = _netEntries.TryGetValue(state, out var original) ? Serializer.DeepClone(original) : new OptionEntryWire();
        dto.Id = state.Id;
        if (state.Level.HasValue) dto.Level = state.Level.Value;
        else dto.ResetLevel();
        return dto;
    }

    public EquipmentOptionState LoadGoogleBlob(byte[] data) => LoadGoogle(TrackedOptions.Parser.ParseFrom(data));
    public byte[] SaveGoogleBlob(EquipmentOptionState state) => SaveGoogle(state).ToByteArray();

    private EquipmentOptionState LoadGoogle(TrackedOptions dto)
    {
        var state = new EquipmentOptionState
        {
            SpecialOption = dto.ProductOption == null ? null : LoadGoogleEntry(dto.ProductOption)
        };
        foreach (var entry in dto.RerollOptions) state.Entries.Add(LoadGoogleEntry(entry));
        _googleRoots.Add(state, dto.Clone());
        return state;
    }

    private EquipmentOptionEntry LoadGoogleEntry(TrackedOptionEntry dto)
    {
        var state = new EquipmentOptionEntry { Id = dto.Id, PoolId = dto.PoolId, Level = dto.Level };
        _googleEntries.Add(state, dto.Clone());
        return state;
    }

    private TrackedOptions SaveGoogle(EquipmentOptionState state)
    {
        var dto = _googleRoots.TryGetValue(state, out var original) ? original.Clone() : new TrackedOptions();
        dto.RerollOptions.Clear();
        foreach (var entry in state.Entries) dto.RerollOptions.Add(SaveGoogleEntry(entry));
        dto.ProductOption = state.SpecialOption == null ? null : SaveGoogleEntry(state.SpecialOption);
        return dto;
    }

    private TrackedOptionEntry SaveGoogleEntry(EquipmentOptionEntry state)
    {
        var dto = _googleEntries.TryGetValue(state, out var original) ? original.Clone() : new TrackedOptionEntry();
        dto.Id = state.Id;
        dto.PoolId = state.PoolId;
        dto.Level = state.Level ?? 0;
        return dto;
    }
}
