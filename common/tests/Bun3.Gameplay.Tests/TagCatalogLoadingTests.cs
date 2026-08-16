using System;
using System.IO;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TagCatalogLoadingTests
{
    [Test]
    public void Load_builds_implicit_parents_and_deterministic_preorder()
    {
        var catalog = TagCatalogTestData.Load();

        Assert.That(catalog.Count, Is.EqualTo(7));
        Assert.That(catalog.GetRequired("Ability").Index, Is.EqualTo(1));
        Assert.That(catalog.GetRequired("ABILITY.MOVEMENT").Index, Is.EqualTo(2));
        Assert.That(catalog.GetRequired("ability.movement.jump").Index, Is.EqualTo(3));
        Assert.That(catalog.GetRequired("State").Index, Is.EqualTo(4));
        Assert.That(catalog.GetRequired("state.dead").Index, Is.EqualTo(5));
        Assert.That(catalog.GetRequired("STATE.DEAD.GHOST").Index, Is.EqualTo(6));
        Assert.That(catalog.GetRequired("state.rooted").Index, Is.EqualTo(7));
    }

    [Test]
    public void Parent_and_subtree_queries_use_catalog_arrays()
    {
        var catalog = TagCatalogTestData.Load();
        var state = catalog.GetRequired("State");
        var dead = catalog.GetRequired("State.Dead");
        var ghost = catalog.GetRequired("State.Dead.Ghost");

        Assert.That(catalog.GetParent(ghost), Is.EqualTo(dead));
        Assert.That(catalog.GetParent(state), Is.EqualTo(GameplayTag.None));
        Assert.That(catalog.IsAncestorOrSelf(state, ghost), Is.True);
        Assert.That(catalog.IsAncestorOrSelf(ghost, state), Is.False);
        Assert.That(catalog.IsAncestorOrSelf(GameplayTag.None, ghost), Is.False);
    }

    [Test]
    public void Wire_index_is_restored_only_through_catalog_range_check()
    {
        var catalog = TagCatalogTestData.Load();

        Assert.That(catalog.TryGetByIndex(0, out var none), Is.True);
        Assert.That(none, Is.EqualTo(GameplayTag.None));
        Assert.That(catalog.GetRequiredByIndex(0), Is.EqualTo(GameplayTag.None));
        Assert.That(catalog.TryGetByIndex(7, out var last), Is.True);
        Assert.That(last.Index, Is.EqualTo(7));
        Assert.That(catalog.TryGetByIndex(8, out _), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => catalog.GetRequiredByIndex(8));
    }

    [Test]
    public void Load_leaves_the_input_stream_open()
    {
        var prefix = System.Text.Encoding.UTF8.GetBytes("ignored-prefix");
        var json = System.Text.Encoding.UTF8.GetBytes(TagCatalogTestData.CanonicalJson);
        using var stream = new MemoryStream(prefix.Length + json.Length);
        stream.Write(prefix, 0, prefix.Length);
        stream.Write(json, 0, json.Length);
        stream.Position = prefix.Length;
        Assert.That(TagCatalogJson.Load(stream).Count, Is.EqualTo(7));
        Assert.That(stream.CanRead, Is.True);
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    [Test]
    public void Load_reads_from_the_current_position_of_a_nonseekable_stream_and_leaves_it_open()
    {
        var prefix = System.Text.Encoding.UTF8.GetBytes("ignored-prefix");
        var json = System.Text.Encoding.UTF8.GetBytes(TagCatalogTestData.CanonicalJson);
        var source = new MemoryStream(prefix.Length + json.Length);
        source.Write(prefix, 0, prefix.Length);
        source.Write(json, 0, json.Length);
        source.Position = prefix.Length;
        using var stream = new NonSeekableReadStream(source);

        Assert.That(TagCatalogJson.Load(stream).Count, Is.EqualTo(7));
        Assert.That(stream.CanRead, Is.True);
        Assert.That(stream.ReadByte(), Is.EqualTo(-1));
    }

    [Test]
    public void Load_rejects_invalid_utf8_as_a_located_catalog_error()
    {
        using var stream = new MemoryStream(new byte[] { 0xFF });

        var error = Assert.Throws<TagCatalogException>(() => TagCatalogJson.Load(stream));

        Assert.That(error!.LineNumber, Is.EqualTo(1));
        Assert.That(error.LinePosition, Is.EqualTo(1));
    }

    [Test]
    public void Unregistered_and_malformed_lookups_have_distinct_contracts()
    {
        var catalog = TagCatalogTestData.Load();
        Assert.That(catalog.TryGet("State.Missing", out var missing), Is.False);
        Assert.That(missing, Is.EqualTo(GameplayTag.None));
        var required = Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
            () => catalog.GetRequired("State.Missing"));
        Assert.That(required!.Message, Does.Contain("State.Missing"));
        Assert.Throws<ArgumentException>(() => catalog.TryGet("State_Bad", out _));
        Assert.Throws<ArgumentException>(() => catalog.GetRequired("State_Bad"));
    }

    [Test]
    public void Frozen_catalog_supports_concurrent_reads()
    {
        var catalog = TagCatalogTestData.Load();
        var failures = 0;
        System.Threading.Tasks.Parallel.For(0, 10_000, i =>
        {
            if (!catalog.TryGet((i & 1) == 0 ? "STATE.DEAD" : "ability.movement.jump", out var tag)
                || !tag.IsValid
                || catalog.GetDisplayName(tag).Length == 0)
                System.Threading.Interlocked.Increment(ref failures);
        });
        Assert.That(failures, Is.Zero);
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly Stream _inner;

        internal NonSeekableReadStream(Stream inner) => _inner = inner;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
