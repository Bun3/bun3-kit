#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

/// <summary>B3DK 카탈로그의 결정성과 정상 로딩 계약을 검증합니다.</summary>
[TestFixture]
public sealed class TagCatalogBinaryTests
{
    [Test]
    public void Same_semantic_input_writes_identical_b3dk_bytes()
    {
        var first = WriteBinary(Compile("game-a", "1.4.0", false).Catalog!);
        var second = WriteBinary(Compile("game-a", "1.4.0", true).Catalog!);

        Assert.Multiple(() =>
        {
            Assert.That(first.Take(4).ToArray(), Is.EqualTo(Encoding.ASCII.GetBytes("B3DK")));
            Assert.That(second, Is.EqualTo(first));
        });
    }

    [Test]
    public void Published_round_trip_requires_external_expected_fingerprint()
    {
        var original = Compile("game-a", "1.4.0", false).Catalog!;
        using var bytes = new MemoryStream(WriteBinary(original));

        var loaded = TagCatalogBinary.Load(bytes,
            TagCatalogExpectations.ForPublished("game-a", "1.4.0", original.Fingerprint));

        Assert.Multiple(() =>
        {
            Assert.That(loaded.GetRequired("alpha.child").Index,
                Is.EqualTo(original.GetRequired("alpha.child").Index));
            Assert.That(loaded.GetParent(loaded.GetRequired("alpha.child")),
                Is.EqualTo(loaded.GetRequired("alpha")));
            Assert.That(loaded.IsAncestorOrSelf(
                loaded.GetRequired("alpha"), loaded.GetRequired("alpha.child")), Is.True);
            Assert.That(loaded.GetRequired("legacy.one"), Is.EqualTo(loaded.GetRequired("alpha.child")));
            Assert.That(loaded.Fingerprint.ToArray(), Is.EqualTo(original.Fingerprint.ToArray()));
            Assert.That(loaded.CatalogId, Is.EqualTo("game-a"));
            Assert.That(loaded.CatalogVersion, Is.EqualTo("1.4.0"));
        });
    }

    [Test]
    public void Development_round_trip_requires_exact_development_identity()
    {
        var original = Compile("game-a", "0.0.0-dev", false).Catalog!;
        using var bytes = new MemoryStream(WriteBinary(original));

        var loaded = TagCatalogBinary.Load(bytes, TagCatalogExpectations.ForDevelopment("game-a"));

        Assert.That(loaded.Fingerprint.ToArray(), Is.EqualTo(original.Fingerprint.ToArray()));

        var published = Compile("game-a", "1.4.0", false).Catalog!;
        using var publishedBytes = new MemoryStream(WriteBinary(published));
        Assert.Throws<TagCatalogCompatibilityException>(() =>
            TagCatalogBinary.Load(publishedBytes, TagCatalogExpectations.ForDevelopment("game-a")));
    }

    [Test]
    public void Published_expectations_copy_the_external_fingerprint()
    {
        var original = Compile("game-a", "1.4.0", false).Catalog!;
        var expected = original.Fingerprint.ToArray();
        var expectations = TagCatalogExpectations.ForPublished("game-a", "1.4.0", expected);
        expected[0] ^= 0xff;
        using var bytes = new MemoryStream(WriteBinary(original));

        Assert.DoesNotThrow(() => TagCatalogBinary.Load(bytes, expectations));
    }

    [Test]
    public void Readable_non_seekable_stream_loads_successfully()
    {
        var original = Compile("game-a", "0.0.0-dev", false).Catalog!;
        using var input = new NonSeekableReadStream(WriteBinary(original));

        var loaded = TagCatalogBinary.Load(input, TagCatalogExpectations.ForDevelopment("game-a"));

        Assert.That(loaded.GetRequired("bravo.child"), Is.EqualTo(original.GetRequired("bravo.child")));
    }

    [Test]
    public void Binary_and_legacy_json_loaders_do_not_auto_detect_formats()
    {
        using var json = new MemoryStream(Encoding.UTF8.GetBytes(TagCatalogTestData.CanonicalJson));
        Assert.Throws<TagCatalogFormatException>(() =>
            TagCatalogBinary.Load(json, TagCatalogExpectations.ForDevelopment("game-a")));

        var original = Compile("game-a", "0.0.0-dev", false).Catalog!;
        using var binary = new MemoryStream(WriteBinary(original));
#pragma warning disable CS0618 // 레거시 JSON 로더가 binary를 자동 감지하지 않는 계약을 검증합니다.
        Assert.Throws<TagCatalogException>(() => TagCatalog.Load(binary));
#pragma warning restore CS0618
    }

    [Test]
    public void Writer_rejects_identity_less_legacy_json_catalog()
    {
#pragma warning disable CS0618 // 레거시 JSON Catalog의 배포 차단 계약을 검증합니다.
        var legacy = TagCatalogTestData.Load();
#pragma warning restore CS0618
        using var output = new MemoryStream();

        Assert.Throws<InvalidOperationException>(() => TagCatalogBinaryWriter.Write(output, legacy));
    }

    [Test]
    public void Published_expectations_require_a_32_byte_fingerprint()
    {
        Assert.Throws<ArgumentException>(() =>
            TagCatalogExpectations.ForPublished("game-a", "1.4.0", new byte[31]));
    }

    [Test]
    public void Published_expectations_reject_the_reserved_development_version()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            TagCatalogExpectations.ForPublished("game-a", "0.0.0-dev", new byte[32]));

        Assert.That(exception!.ParamName, Is.EqualTo("catalogVersion"));
    }

    internal static TagCatalogCompilation Compile(string catalogId, string version, bool reverseSources)
    {
        var first = Source("source-a",
            new[] { new TagSourceTag("alpha.child", "alpha") },
            new[] { new TagSourceRedirect("legacy.one", "alpha.child") });
        var second = Source("source-b",
            new[] { new TagSourceTag("bravo.child", "bravo") },
            new[] { new TagSourceRedirect("legacy.two", "bravo.child") });
        return TagCatalogCompiler.Compile(
            reverseSources ? new[] { second, first } : new[] { first, second },
            new TagCatalogIdentity(catalogId, version));
    }

    internal static byte[] WriteBinary(TagCatalog catalog)
    {
        using var output = new MemoryStream();
        TagCatalogBinaryWriter.Write(output, catalog);
        return output.ToArray();
    }

    private static TagSourceDocument Source(
        string id,
        TagSourceTag[] tags,
        TagSourceRedirect[] redirects) =>
        new TagSourceDocument(
            new TagSourceDescriptor(id, id, TagSourceKind.PackageJson, true),
            id + ".json",
            tags,
            redirects);

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;

        internal NonSeekableReadStream(byte[] bytes) => _inner = new MemoryStream(bytes, false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
