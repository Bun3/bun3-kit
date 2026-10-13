using Bun3.Server.Items;
using Bun3.Server.Tests.Protos;
using Google.Protobuf;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ProtoBuf;

namespace Bun3.Server.Tests;

[TestFixture]
public class GeneratedOptionPersistenceTests
{
    // Root field 99 = 900; entries A/B field 99 = 101/102; special field 99 = 103.
    internal const string NetHex = "0A07080B10039806650A0708161004980666100218032A070821100598066798068407";
    internal const string GoogleHex = "1209080B1007180398066512090816100818049806667A0908211009180598066798068407";
    internal const string Json = """
        {"RerollOptions":[{"Id":11,"PoolId":7,"Level":3,"Future":{"tag":"A"}},
          {"Id":22,"PoolId":8,"Level":4,"Future":{"tag":"B"}}],
         "ProductOption":{"Id":33,"PoolId":9,"Level":5,"Future":"special"},
         "FutureRoot":{"version":900}}
        """;

    [TestCase("json")]
    [TestCase("net")]
    [TestCase("google")]
    public void Reordering_preserves_each_nodes_unknown_data(string format)
    {
        var mapping = new GeneratedOptionMapping();
        var state = Load(mapping, format);
        var first = state.Entries[0];
        state.Entries.RemoveAt(0);
        state.Entries.Add(first);
        first.Level = 31;
        AssertSaved(mapping, state, format, new[] { 22, 11 }, new[] { 4, 31 }, new[] { 102, 101 });
    }

    [TestCase("json")]
    [TestCase("net")]
    [TestCase("google")]
    public void Replacement_drops_removed_metadata_and_new_nodes_start_clean(string format)
    {
        var mapping = new GeneratedOptionMapping();
        var state = Load(mapping, format);
        state.Entries.RemoveAt(0);
        state.Entries.Add(new EquipmentOptionEntry { Id = 44, PoolId = 10, Level = 6 });
        state.SpecialOption!.Level = 15;
        AssertSaved(mapping, state, format, new[] { 22, 44 }, new[] { 4, 6 }, new[] { 102, 0 });
    }

    [Test]
    public void Json_uses_Newtonsoft_property_names_numeric_values_and_null_child()
    {
        var mapping = new GeneratedOptionMapping();
        var missing = mapping.LoadJson("{}");
        var zero = mapping.LoadJson("{\"RerollOptions\":[{\"Level\":0}],\"ProductOption\":{}}");
        Assert.That(missing.SpecialOption, Is.Null);
        Assert.That(zero.SpecialOption, Is.Not.Null);
        Assert.That(zero.Entries[0].Level, Is.Zero);
        var saved = JObject.Parse(mapping.SaveJson(zero));
        Assert.That(saved["RerollOptions"]![0]!["Level"]!.Type, Is.EqualTo(JTokenType.Integer));
        Assert.That(saved["RerollOptions"]![0]!["Id"]!.Value<int>(), Is.Zero);
        Assert.That(saved["rerollOptions"], Is.Null);
        Assert.That(JObject.Parse(mapping.SaveJson(missing))["ProductOption"]!.Type, Is.EqualTo(JTokenType.Null));
        Assert.That(mapping.LoadJson("{\"RerollOptions\":[{}]}").Entries[0].Level, Is.Zero);
    }

    [Test]
    public void Google_proto3_collapses_absent_and_zero_but_preserves_empty_message()
    {
        var mapping = new GeneratedOptionMapping();
        var absent = mapping.LoadGoogleBlob(Convert.FromHexString("1200"));
        var zero = mapping.LoadGoogleBlob(Convert.FromHexString("120218007A00"));
        Assert.That(absent.Entries[0].Level, Is.Zero);
        Assert.That(zero.Entries[0].Level, Is.Zero);
        Assert.That(absent.SpecialOption, Is.Null);
        Assert.That(zero.SpecialOption, Is.Not.Null);
        Assert.That(Convert.ToHexString(mapping.SaveGoogleBlob(absent)), Is.EqualTo("1200"));
        Assert.That(Convert.ToHexString(mapping.SaveGoogleBlob(zero)), Is.EqualTo("12007A00"));
    }

    [Test]
    public void Net_nullable_presence_preserves_unset_explicit_zero_and_empty_child()
    {
        var mapping = new GeneratedOptionMapping();
        var absent = mapping.LoadBlob(Convert.FromHexString("0A00"));
        var zero = mapping.LoadBlob(Convert.FromHexString("0A021000100018002A00"));
        Assert.That(absent.Version, Is.Null);
        Assert.That(absent.MainOptionLevel, Is.Null);
        Assert.That(absent.Entries[0].Level, Is.Null);
        Assert.That(absent.SpecialOption, Is.Null);
        Assert.That(zero.Version, Is.Zero);
        Assert.That(zero.MainOptionLevel, Is.Zero);
        Assert.That(zero.Entries[0].Level, Is.Zero);
        Assert.That(zero.SpecialOption, Is.Not.Null);
        Assert.That(Convert.ToHexString(mapping.SaveBlob(absent)), Is.EqualTo("0A00"));
        Assert.That(Convert.ToHexString(mapping.SaveBlob(zero)), Is.EqualTo("0A021000100018002A00"));
        zero.Version = null;
        zero.MainOptionLevel = null;
        zero.Entries[0].Level = null;
        zero.SpecialOption = null;
        Assert.That(Convert.ToHexString(mapping.SaveBlob(zero)), Is.EqualTo("0A00"));
    }

    [TestCase("json")]
    [TestCase("net")]
    [TestCase("google")]
    public void Loading_fills_detached_properties_before_inventory_binding(string format)
    {
        var state = Load(new GeneratedOptionMapping(), format);
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var dirty = 0;
        var inventory = new ItemInventory<EquipmentOptionState>(catalog, () => 2, _ => new(), onDirty: () => dirty++);
        Assert.That(inventory.TryLoadInstance(1, catalog.GetRequired("sword"), 1, 0, state), Is.EqualTo(InventoryError.None));
        Assert.That(inventory.HasChanges, Is.False);
        Assert.That(dirty, Is.Zero);
        state.Entries[0].Level = 8;
        Assert.That(inventory.HasChanges, Is.True);
        Assert.That(dirty, Is.EqualTo(1));
    }

    private static EquipmentOptionState Load(GeneratedOptionMapping mapping, string format) => format switch
    {
        "json" => mapping.LoadJson(Json),
        "net" => mapping.LoadBlob(Convert.FromHexString(NetHex)),
        _ => mapping.LoadGoogleBlob(Convert.FromHexString(GoogleHex))
    };

    private static void AssertSaved(GeneratedOptionMapping mapping, EquipmentOptionState state, string format,
        int[] ids, int[] levels, int[] unknowns)
    {
        EquipmentOptionState reloaded;
        if (format == "json")
        {
            var json = mapping.SaveJson(state);
            var root = JObject.Parse(json);
            Assert.That(root["FutureRoot"]!["version"]!.Value<int>(), Is.EqualTo(900));
            var entries = (JArray)root["RerollOptions"]!;
            Assert.That(entries.Select(x => (string?)x["Future"]?["tag"]),
                Is.EqualTo(unknowns.Select(x => x == 101 ? "A" : x == 102 ? "B" : null)));
            Assert.That((string?)root["ProductOption"]!["Future"], Is.EqualTo("special"));
            reloaded = mapping.LoadJson(json);
        }
        else if (format == "net")
        {
            var bytes = mapping.SaveBlob(state);
            using var stream = new MemoryStream(bytes);
            var root = Serializer.Deserialize<OptionWire>(stream);
            Assert.That(Extensible.GetValue<int>(root, 99), Is.EqualTo(900));
            Assert.That(root.Entries.Select(x => Extensible.GetValue<int>(x, 99)), Is.EqualTo(unknowns));
            Assert.That(Extensible.GetValue<int>(root.PetOption!, 99), Is.EqualTo(103));
            reloaded = mapping.LoadBlob(bytes);
        }
        else
        {
            var bytes = mapping.SaveGoogleBlob(state);
            var root = TrackedOptions.Parser.ParseFrom(bytes);
            Assert.That(Unknown99(root.ToByteArray()), Is.EqualTo(900));
            Assert.That(root.RerollOptions.Select(x => Unknown99(x.ToByteArray())), Is.EqualTo(unknowns));
            Assert.That(Unknown99(root.ProductOption.ToByteArray()), Is.EqualTo(103));
            reloaded = mapping.LoadGoogleBlob(bytes);
        }
        Assert.That(reloaded.Entries.Select(x => x.Id), Is.EqualTo(ids));
        Assert.That(reloaded.Entries.Select(x => x.Level), Is.EqualTo(levels));
        Assert.That(reloaded.SpecialOption!.Level, Is.EqualTo(state.SpecialOption!.Level));
        if (format != "net") Assert.That(reloaded.Entries.Select(x => x.PoolId), Is.EqualTo(state.Entries.Select(x => x.PoolId)));
    }

    private static int Unknown99(byte[] data)
    {
        using var input = new CodedInputStream(data);
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            if (tag == 792) return input.ReadInt32();
            input.SkipLastField();
        }
        return 0;
    }
}
