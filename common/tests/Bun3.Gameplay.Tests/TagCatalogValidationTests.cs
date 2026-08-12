using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TagCatalogValidationTests
{
    [TestCase("", false)]
    [TestCase("State", true)]
    [TestCase("State.123", true)]
    [TestCase("State.01", true)]
    [TestCase("A0.B9", true)]
    [TestCase(".State", false)]
    [TestCase("State.", false)]
    [TestCase("State..Dead", false)]
    [TestCase("State_Dead", false)]
    [TestCase("State-Dead", false)]
    [TestCase("상태.Dead", false)]
    [TestCase("State Dead", false)]
    public void Name_grammar_is_ascii_alphanumeric_segments_only(string name, bool valid)
    {
        var json = $$"""{ "schemaVersion": 1, "tags": [{ "name": "{{name}}" }] }""";
        if (valid)
            Assert.DoesNotThrow(() => TagCatalogTestData.Load(json));
        else
            Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));
    }

    [Test]
    public void Numeric_text_is_not_normalized()
    {
        var catalog = TagCatalogTestData.Load(
            """{ "schemaVersion": 1, "tags": [{"name":"State.01"},{"name":"State.1"}] }""");
        Assert.That(catalog.GetRequired("State.01"), Is.Not.EqualTo(catalog.GetRequired("State.1")));
    }

    [Test]
    public void Display_case_is_preserved_and_implicit_parent_case_is_order_independent()
    {
        var first = TagCatalogTestData.Load(
            """{"schemaVersion":1,"tags":[{"name":"state.Dead.Ghost"},{"name":"State.Alive"}]}""");
        var reversed = TagCatalogTestData.Load(
            """{"schemaVersion":1,"tags":[{"name":"State.Alive"},{"name":"state.Dead.Ghost"}]}""");

        Assert.That(first.GetDisplayName(first.GetRequired("state.dead.ghost")),
            Is.EqualTo("state.Dead.Ghost"));
        Assert.That(first.GetDisplayName(first.GetRequired("state")), Is.EqualTo("State"));
        Assert.That(first.GetDisplayName(first.GetRequired("state.dead")), Is.EqualTo("state.Dead"));
        Assert.That(first.GetDisplayName(first.GetRequired("state")),
            Is.EqualTo(reversed.GetDisplayName(reversed.GetRequired("STATE"))));
    }

    [TestCase("{ \"schemaVersion\": 2, \"tags\": [] }")]
    [TestCase("{ \"schemaVersion\": 1 }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [], \"unknown\": true }")]
    [TestCase("{ \"schemaVersion\": 1, \"schemaVersion\": 1, \"tags\": [] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\",\"name\":\"B\"}] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [], }")]
    [TestCase("{ 'schemaVersion': 1, 'tags': [] }")]
    [TestCase("{ schemaVersion: 1, tags: [] }")]
    [TestCase("/*comment*/ { \"schemaVersion\": 1, \"tags\": [] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [] } true")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [] } { \"schemaVersion\": 1, \"tags\": [] }")]
    [TestCase("[]")]
    [TestCase("null")]
    [TestCase("{ \"schemaVersion\": \"1\", \"tags\": [] }")]
    [TestCase("{ \"schemaVersion\": 1.0, \"tags\": [] }")]
    [TestCase("{ \"schemaVersion\": null, \"tags\": [] }")]
    [TestCase("{ \"schemaVersion\": 01, \"tags\": [] }")]
    [TestCase("{ \"schemaVersion\": 0x1, \"tags\": [] }")]
    [TestCase("{ \"schemaVersion\": NaN, \"tags\": [] }")]
    [TestCase("{ \"schemaVersion\": Infinity, \"tags\": [] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": null }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": {} }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [null] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [[]] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [{}] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":1}] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\",\"comment\":1}] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\",\"extra\":true}] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [], \"redirects\": null }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [], \"redirects\": {} }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [], \"redirects\": [null] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\"}], \"redirects\": [{\"to\":\"A\"}] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\"}], \"redirects\": [{\"from\":\"Old\"}] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\"}], \"redirects\": [{\"from\":1,\"to\":\"A\"}] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\"}], \"redirects\": [{\"from\":\"Old\",\"to\":1}] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\"}], \"redirects\": [{\"from\":\"Old\",\"to\":\"A\",\"extra\":1}] }")]
    [TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\"}], \"redirects\": [{\"from\":\"Old_Tag\",\"to\":\"A\"}] }")]
    public void Schema_is_strict(string json)
    {
        var error = Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));
        Assert.That(error!.LineNumber, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Semantic_name_error_preserves_json_path_and_source_location()
    {
        const string json = "{\n  \"schemaVersion\": 1,\n  \"tags\": [\n" +
            "    { \"name\": \"State_Dead\" }\n  ]\n}";
        var error = Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));
        Assert.That(error!.JsonPath, Is.EqualTo("tags[0].name"));
        Assert.That(error.LineNumber, Is.EqualTo(4));
        Assert.That(error.LinePosition, Is.GreaterThan(0));
    }

    [Test]
    public void Case_only_duplicate_is_rejected_instead_of_merged()
    {
        const string json =
            """{ "schemaVersion": 1, "tags": [{"name":"State.Dead"},{"name":"state.dead"}] }""";
        Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));
    }

    [Test]
    public void Schema_version_beyond_int64_reports_its_token_location()
    {
        const string json = "{\n  \"schemaVersion\": 9223372036854775808,\n  \"tags\": []\n}";

        var error = Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));

        Assert.That(error!.JsonPath, Is.EqualTo("schemaVersion"));
        Assert.That(error.LineNumber, Is.EqualTo(2));
    }

    [Test]
    public void Strict_scanner_rejects_nesting_beyond_eight_before_json_reader()
    {
        var json = "{\"schemaVersion\":1,\"tags\":"
            + new string('[', 8) + new string(']', 8) + "}";

        var error = Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));

        Assert.That(error!.Message, Does.StartWith("JSON 중첩 깊이는 8을 넘을 수 없습니다."));
        Assert.That(error.LineNumber, Is.EqualTo(1));
    }

    [TestCase("\r")]
    [TestCase("\n")]
    [TestCase("\r\n")]
    public void Strict_scanner_counts_each_newline_style_once(string newline)
    {
        var json = "{" + newline + "\"schemaVersion\":1," + newline
            + "\"tags\":[]" + newline + "!}";

        var error = Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));

        Assert.That(error!.LineNumber, Is.EqualTo(4));
    }

    [Test]
    public void Strict_scanner_accepts_all_json_string_escapes()
    {
        const string json = """{"schemaVersion":1,"tags":[{"name":"\u0041","comment":"\"\\\/\b\f\n\r\t"}]}""";

        var catalog = TagCatalogTestData.Load(json);

        Assert.That(catalog.GetRequired("A").IsValid, Is.True);
    }

    [TestCase(@"\x")]
    [TestCase(@"\u123")]
    public void Strict_scanner_rejects_invalid_string_escapes(string escape)
    {
        var json = "{\"schemaVersion\":1,\"tags\":[{\"name\":\"A" + escape + "\"}]}";

        var error = Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));

        Assert.That(error!.Message, Does.Not.StartWith("ASCII 영숫자 이외의 문자는 사용할 수 없습니다."));
    }

    [Test]
    public void Strict_scanner_rejects_raw_string_controls()
    {
        var json = "{\"schemaVersion\":1,\"tags\":[{\"name\":\"A\u001F\"}]}";

        Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));
    }

    [TestCase("1e+2")]
    [TestCase("-0.1E-2")]
    public void Strict_scanner_accepts_json_number_and_exponent_grammar(string number)
    {
        var json = "{\"schemaVersion\":1,\"tags\":[],\"unknown\":" + number + "}";

        var error = Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));

        Assert.That(error!.Message, Does.StartWith("허용되지 않은 필드입니다:"));
    }

    [TestCase("1.")]
    [TestCase("1e")]
    [TestCase("1e+")]
    public void Strict_scanner_rejects_incomplete_number_and_exponent_grammar(string number)
    {
        var json = "{\"schemaVersion\":1,\"tags\":[],\"unknown\":" + number + "}";

        var error = Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));

        Assert.That(error!.Message, Does.Not.StartWith("허용되지 않은 필드입니다:"));
    }

    [Test]
    public void Path_length_limit_is_inclusive_at_255_characters()
    {
        Assert.DoesNotThrow(() => TagCatalogTestData.Load(
            $$"""{"schemaVersion":1,"tags":[{"name":"{{TagCatalogTestData.BuildPath(255)}}"}]}"""));
        Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(
            $$"""{"schemaVersion":1,"tags":[{"name":"{{TagCatalogTestData.BuildPath(256)}}"}]}"""));
    }

    [Test]
    public void Depth_limit_is_inclusive_at_16_segments()
    {
        Assert.DoesNotThrow(() => TagCatalogTestData.Load(
            $$"""{"schemaVersion":1,"tags":[{"name":"{{TagCatalogTestData.BuildDepth(16)}}"}]}"""));
        Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(
            $$"""{"schemaVersion":1,"tags":[{"name":"{{TagCatalogTestData.BuildDepth(17)}}"}]}"""));
    }

    [Test]
    public void Active_node_limit_includes_implicit_parents()
    {
        var maximum = TagCatalogTestData.Load(
            TagCatalogTestData.BuildTwoLevelCatalog(32_767, includeExtraRoot: true));
        Assert.That(maximum.Count, Is.EqualTo(65_535));
        Assert.That(maximum.GetRequiredByIndex(65_535).Index, Is.EqualTo(65_535));
        Assert.Throws<TagCatalogException>(
            () => TagCatalogTestData.Load(
                TagCatalogTestData.BuildTwoLevelCatalog(32_768, includeExtraRoot: false)));
    }
}
