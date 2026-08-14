using System;
using System.IO;
using System.Linq;
using System.Text;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TagSourceJsonTests
{
    [Test]
    public void Game_source_is_fixed_editable_and_normalizes_names_on_write()
    {
        using var input = Utf8("{\"schemaVersion\":1,\"tags\":[{\"name\":\"Ability.Jump\",\"comment\":\"jump\"}],\"redirects\":[]}");
        var source = TagSourceJson.LoadGame(input, "ProjectSettings/GameplayTags.json");

        Assert.That(source.Descriptor.SourceId, Is.EqualTo("game"));
        Assert.That(source.Descriptor.IsReadOnly, Is.False);
        using var output = new MemoryStream();
        TagSourceJson.WriteGame(output, source);
        Assert.That(Utf8Text(output), Does.Contain("ability.jump"));
        Assert.That(Utf8Text(output), Does.Not.Contain("Ability.Jump"));
    }

    [Test]
    public void Metadata_requires_source_identity_and_preserves_per_source_comment()
    {
        using var input = Utf8("{\"schemaVersion\":1,\"source\":{\"id\":\"bun3.gameplay\",\"displayName\":\"Bun3.Gameplay\",\"kind\":\"packageJson\"},\"tags\":[{\"name\":\"ability.jump\",\"comment\":\"framework\"}],\"redirects\":[]}");
        var source = TagSourceJson.LoadMetadata(input, "Packages/com.bun3.gameplay/Bun3/GameplayTags/TagSource.json");

        Assert.That(source.Tags.Single().Comment, Is.EqualTo("framework"));
        Assert.That(source.Descriptor.IsReadOnly, Is.True);
    }

    [TestCase("{\"schemaVersion\":2,\"tags\":[],\"redirects\":[]}")]
    [TestCase("{\"schemaVersion\":1,\"tags\":[],\"tags\":[],\"redirects\":[]}")]
    [TestCase("{\"schemaVersion\":1,\"extra\":true,\"tags\":[],\"redirects\":[]}")]
    [TestCase("{\"schemaVersion\":1,\"redirects\":[]}")]
    [TestCase("{\"schemaVersion\":1,\"tags\":[{\"name\":\"ability.jump\"},{\"name\":\"Ability.Jump\"}],\"redirects\":[]}")]
    [TestCase("{\"schemaVersion\":1,\"tags\":[],\"redirects\":[{\"from\":\"ability.old\",\"to\":\"ability.new\"},{\"from\":\"Ability.Old\",\"to\":\"ability.other\"}]}")]
    [TestCase("{\"schemaVersion\":1,\"tags\":[{\"name\":\"ability_bad\"}],\"redirects\":[]}")]
    [TestCase("{\"schemaVersion\":1,\"tags\":[],\"redirects\":[]} true")]
    public void Game_rejects_strict_schema_violations(string json)
    {
        using var input = Utf8(json);

        Assert.Throws<TagCatalogException>(() => TagSourceJson.LoadGame(input, "game.json"));
    }

    [Test]
    public void Metadata_rejects_editable_source_descriptors()
    {
        using var input = Utf8("{\"schemaVersion\":1,\"source\":{\"id\":\"package\",\"displayName\":\"Package\",\"kind\":\"packageJson\",\"isReadOnly\":false},\"tags\":[],\"redirects\":[]}");

        Assert.Throws<TagCatalogException>(() => TagSourceJson.LoadMetadata(input, "package.json"));
    }

    [Test]
    public void Game_rejects_invalid_utf8()
    {
        using var input = new MemoryStream(new byte[] { 0xff });

        Assert.Throws<TagCatalogException>(() => TagSourceJson.LoadGame(input, "game.json"));
    }

    [Test]
    public void Legacy_game_without_redirects_reads_empty_and_writer_emits_redirects()
    {
        using var input = Utf8("{\"schemaVersion\":1,\"tags\":[]}");
        var source = TagSourceJson.LoadGame(input, "game.json");

        Assert.That(source.Redirects, Is.Empty);
        using var output = new MemoryStream();
        TagSourceJson.WriteGame(output, source);
        Assert.That(Utf8Text(output), Does.Contain("\"redirects\": []"));
    }

    [Test]
    public void Metadata_requires_a_source_object()
    {
        using var input = Utf8("{\"schemaVersion\":1,\"tags\":[],\"redirects\":[]}");

        Assert.Throws<TagCatalogException>(() => TagSourceJson.LoadMetadata(input, "package.json"));
    }

    private static MemoryStream Utf8(string value) => new MemoryStream(new UTF8Encoding(false).GetBytes(value));

    private static string Utf8Text(MemoryStream stream) => new UTF8Encoding(false, true).GetString(stream.ToArray());
}
