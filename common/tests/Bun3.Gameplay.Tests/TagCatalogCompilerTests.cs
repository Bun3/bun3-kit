#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

/// <summary>여러 GameplayTag Source의 결정적 병합 계약을 검증합니다.</summary>
[TestFixture]
public sealed class TagCatalogCompilerTests
{
    private static readonly TagCatalogIdentity Identity = new TagCatalogIdentity("test-game", "0.0.0-dev");

    [Test]
    public void Same_tag_from_two_sources_has_one_runtime_identity_and_two_comments()
    {
        var result = TagCatalogCompiler.Compile(new[]
        {
            Source("game", false, Tag("Ability.Jump", "game")),
            Source("bun3.gameplay", true, Tag("ability.jump", "framework"))
        }, Identity);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Catalog!.Count, Is.EqualTo(2));
        Assert.That(result.Catalog.GetDisplayName(result.Catalog.GetRequired("ABILITY.JUMP")),
            Is.EqualTo("ability.jump"));
        Assert.That(result.Provenance!.GetContributions("ability.jump").Select(x => x.Comment),
            Is.EqualTo(new[] { "framework", "game" }));
    }

    [Test]
    public void Removing_one_source_keeps_a_tag_contributed_by_another_source()
    {
        var both = Compile(Source("a", true, Tag("state.dead")), Source("b", true, Tag("state.dead")));
        var one = Compile(Source("b", true, Tag("state.dead")));

        Assert.That(both.Catalog!.TryGet("state.dead", out _), Is.True);
        Assert.That(one.Catalog!.TryGet("state.dead", out _), Is.True);
    }

    [Test]
    public void Implicit_parent_has_source_scoped_implicit_provenance()
    {
        var result = Compile(Source("package", true, Tag("state.dead.ghost", "leaf")));

        var parent = result.Provenance!.GetContributions("state.dead").Single();
        Assert.Multiple(() =>
        {
            Assert.That(parent.SourceId, Is.EqualTo("package"));
            Assert.That(parent.DisplayName, Is.EqualTo("package"));
            Assert.That(parent.Origin, Is.EqualTo("package.json"));
            Assert.That(parent.Comment, Is.EqualTo(string.Empty));
            Assert.That(parent.IsExplicit, Is.False);
            Assert.That(parent.IsReadOnly, Is.True);
        });
    }

    [Test]
    public void Source_order_and_comments_do_not_change_indices_or_fingerprint()
    {
        var first = Compile(
            Source("z", true, Tag("state.dead", "first"), Tag("ability.jump", "jump")),
            Source("a", true, Tag("state.alive", "alive")));
        var reversed = Compile(
            Source("a", true, Tag("state.alive", "changed")),
            Source("z", true, Tag("ability.jump", "changed"), Tag("state.dead", "changed")));

        Assert.Multiple(() =>
        {
            Assert.That(reversed.Catalog!.Fingerprint.ToArray(), Is.EqualTo(first.Catalog!.Fingerprint.ToArray()));
            Assert.That(reversed.Catalog.GetRequired("ability.jump").Index,
                Is.EqualTo(first.Catalog.GetRequired("ability.jump").Index));
            Assert.That(reversed.Catalog.GetRequired("state.dead").Index,
                Is.EqualTo(first.Catalog.GetRequired("state.dead").Index));
        });
    }

    [Test]
    public void Identical_redirects_from_two_sources_are_merged()
    {
        var result = Compile(
            Source("a", true, new[] { Tag("state.dead") }, Redirect("state.old", "state.dead")),
            Source("b", true, Array.Empty<TagSourceTag>(), Redirect("STATE.OLD", "STATE.DEAD")));

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Catalog!.GetRequired("state.old"), Is.EqualTo(result.Catalog.GetRequired("state.dead")));
    }

    [Test]
    public void Divergent_redirect_targets_are_a_stable_error()
    {
        var result = Compile(
            Source("b", true, new[] { Tag("state.alive"), Tag("state.dead") }, Redirect("state.old", "state.alive")),
            Source("a", true, Array.Empty<TagSourceTag>(), Redirect("state.old", "state.dead")));

        AssertFailed(result, "B3TAG2002", "a", "state.old");
    }

    [Test]
    public void Redirect_chain_is_flattened_to_the_active_target()
    {
        var chained = Compile(Source("a", true,
            new[] { Tag("state.dead") },
            Redirect("state.legacy", "state.old"),
            Redirect("state.old", "state.dead")));
        var direct = Compile(Source("a", true,
            new[] { Tag("state.dead") },
            Redirect("state.legacy", "state.dead"),
            Redirect("state.old", "state.dead")));

        Assert.Multiple(() =>
        {
            Assert.That(chained.Catalog!.GetRequired("state.legacy"),
                Is.EqualTo(chained.Catalog.GetRequired("state.dead")));
            Assert.That(chained.Catalog.Fingerprint.ToArray(), Is.EqualTo(direct.Catalog!.Fingerprint.ToArray()));
        });
    }

    [Test]
    public void Redirect_cycle_is_a_stable_error()
    {
        var result = Compile(Source("a", true,
            Array.Empty<TagSourceTag>(),
            Redirect("state.one", "state.two"),
            Redirect("state.two", "state.one")));

        AssertFailed(result, "B3TAG2003", "a", "state.one");
    }

    [Test]
    public void Redirect_self_reference_is_a_stable_error()
    {
        var result = Compile(Source("a", true,
            Array.Empty<TagSourceTag>(), Redirect("state.old", "state.old")));

        AssertFailed(result, "B3TAG2003", "a", "state.old");
    }

    [Test]
    public void Active_redirect_self_reference_is_still_a_cycle_error()
    {
        var result = Compile(Source("a", true,
            new[] { Tag("state.old") }, Redirect("state.old", "state.old")));

        AssertFailed(result, "B3TAG2003", "a", "state.old");
    }

    [Test]
    public void Redirect_cycle_between_active_names_is_still_a_cycle_error()
    {
        var result = Compile(Source("a", true,
            new[] { Tag("state.one"), Tag("state.two") },
            Redirect("state.one", "state.two"),
            Redirect("state.two", "state.one")));

        Assert.Multiple(() =>
        {
            AssertFailed(result, "B3TAG2003", "a", "state.one");
            AssertFailed(result, "B3TAG2003", "a", "state.two");
        });
    }

    [Test]
    public void Redirect_missing_final_target_is_a_stable_error()
    {
        var result = Compile(Source("a", true,
            Array.Empty<TagSourceTag>(), Redirect("state.old", "state.missing")));

        AssertFailed(result, "B3TAG2004", "a", "state.old");
    }

    [Test]
    public void Active_old_name_shadows_redirect_until_the_declaration_is_removed()
    {
        var withActiveOldName = Compile(
            Source("a", true, Tag("state.old")),
            Source("b", true, new[] { Tag("state.dead") }, Redirect("state.old", "state.dead")));
        var withoutActiveOldName = Compile(
            Source("b", true, new[] { Tag("state.dead") }, Redirect("state.old", "state.dead")));

        Assert.Multiple(() =>
        {
            Assert.That(withActiveOldName.Succeeded, Is.True);
            Assert.That(withActiveOldName.Diagnostics.Single().Code, Is.EqualTo("B3TAG2001"));
            Assert.That(withActiveOldName.Diagnostics.Single().Severity, Is.EqualTo(TagCatalogDiagnosticSeverity.Warning));
            Assert.That(withActiveOldName.Catalog!.GetRequired("state.old"),
                Is.Not.EqualTo(withActiveOldName.Catalog.GetRequired("state.dead")));
            Assert.That(withoutActiveOldName.Catalog!.GetRequired("state.old"),
                Is.EqualTo(withoutActiveOldName.Catalog.GetRequired("state.dead")));
        });
    }

    [Test]
    public void Duplicate_source_id_is_rejected_before_runtime_allocation()
    {
        var result = Compile(Source("a", true, Tag("one")), Source("a", true, Tag("two")));

        AssertFailed(result, "B3TAG1001", "a", string.Empty);
    }

    [Test]
    public void Implicit_parents_that_exceed_runtime_capacity_return_a_diagnostic_without_a_catalog()
    {
        var tags = new TagSourceTag[32_768];
        for (var i = 0; i < tags.Length; i++)
        {
            tags[i] = Tag("root" + i + ".leaf");
        }

        var result = Compile(Source("a", true, tags));

        AssertFailed(result, "B3TAG1002", string.Empty, string.Empty);
    }

    [Test]
    public void Diagnostics_are_sorted_by_source_path_and_code()
    {
        var result = Compile(
            Source("z", true, Array.Empty<TagSourceTag>(), Redirect("z.old", "missing")),
            Source("a", true, Array.Empty<TagSourceTag>(), Redirect("a.old", "missing")));

        Assert.That(result.Diagnostics.Select(x => x.SourceId), Is.EqualTo(new[] { "a", "z" }));
    }

    [Test]
    public void Build_context_enforces_development_and_published_versions()
    {
        var sources = new[] { Source("a", true, Tag("state.dead")) };

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => new GameCatalogBuildContext(
                new TagCatalogIdentity("game", "0.0.0-dev"), CatalogBuildMode.Development, sources));
            Assert.Throws<ArgumentException>(() => new GameCatalogBuildContext(
                new TagCatalogIdentity("game", "1.0.0"), CatalogBuildMode.Development, sources));
            Assert.DoesNotThrow(() => new GameCatalogBuildContext(
                new TagCatalogIdentity("game", "1.0.0"), CatalogBuildMode.Published, sources));
            Assert.Throws<ArgumentException>(() => new GameCatalogBuildContext(
                new TagCatalogIdentity("game", "0.0.0-dev"), CatalogBuildMode.Published, sources));
        });
    }

    [Test]
    public void Build_context_sources_cannot_be_replaced_after_validation()
    {
        var context = new GameCatalogBuildContext(
            Identity,
            CatalogBuildMode.Development,
            new[] { Source("a", true, Tag("state.dead")) });
        var exposedSources = (IList<TagSourceDocument>)context.Sources;

        Assert.Throws<NotSupportedException>(() => exposedSources[0] = null!);
        Assert.That(context.Sources[0], Is.Not.Null);
    }

    [Test]
    public void Api_misuse_throws_argument_exceptions()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => TagCatalogCompiler.Compile(null!, Identity));
            Assert.Throws<ArgumentNullException>(() => TagCatalogCompiler.Compile(
                new TagSourceDocument[] { null! }, Identity));
            Assert.Throws<ArgumentNullException>(() => TagCatalogCompiler.Compile(Array.Empty<TagSourceDocument>(), null!));
            Assert.Throws<ArgumentException>(() => new TagCatalogIdentity("", "0.0.0-dev"));
            Assert.Throws<ArgumentException>(() => new TagCatalogIdentity("game", ""));
        });
    }

    [Test]
    public void Compilation_diagnostics_and_provenance_lists_are_immutable()
    {
        var result = Compile(
            Source("a", true, Tag("state.old")),
            Source("b", true, new[] { Tag("state.dead") }, Redirect("state.old", "state.dead")));
        var diagnostics = (IList<TagCatalogDiagnostic>)result.Diagnostics;
        var contributions = (IList<TagSourceContribution>)result.Provenance!.GetContributions("state.old");

        Assert.Multiple(() =>
        {
            Assert.Throws<NotSupportedException>(() => diagnostics[0] = diagnostics[0]);
            Assert.Throws<NotSupportedException>(() => contributions[0] = contributions[0]);
        });
    }

    private static TagCatalogCompilation Compile(params TagSourceDocument[] sources) =>
        TagCatalogCompiler.Compile(sources, Identity);

    private static TagSourceDocument Source(string id, bool readOnly, params TagSourceTag[] tags) =>
        Source(id, readOnly, tags, Array.Empty<TagSourceRedirect>());

    private static TagSourceDocument Source(
        string id,
        bool readOnly,
        IReadOnlyList<TagSourceTag> tags,
        params TagSourceRedirect[] redirects)
    {
        var kind = readOnly ? TagSourceKind.PackageJson : TagSourceKind.GameJson;
        return new TagSourceDocument(
            new TagSourceDescriptor(id, id, kind, readOnly),
            id + ".json",
            tags,
            redirects);
    }

    private static TagSourceTag Tag(string name, string comment = "") => new TagSourceTag(name, comment);

    private static TagSourceRedirect Redirect(string from, string to) => new TagSourceRedirect(from, to);

    private static void AssertFailed(
        TagCatalogCompilation result,
        string code,
        string sourceId,
        string canonicalPath)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Catalog, Is.Null);
            Assert.That(result.Provenance, Is.Null);
            Assert.That(result.Diagnostics.Any(x => x.Code == code
                && x.SourceId == sourceId
                && x.CanonicalPath == canonicalPath), Is.True);
        });
    }
}
