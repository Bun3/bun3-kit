#nullable enable
using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

/// <summary>B3DK reader가 손상 단계별로 입력을 거부하는지 검증합니다.</summary>
[TestFixture]
public sealed class TagCatalogBinaryCorruptionTests
{
    private const int HeaderSize = 78;
    private byte[] _valid = null!;

    [SetUp]
    public void SetUp() => _valid = TagCatalogBinaryTests.WriteBinary(
        TagCatalogBinaryTests.Compile("game-a", "1.4.0", false).Catalog!);

    [TestCase(0, TestName = "Magic_is_rejected")]
    [TestCase(4, TestName = "Unsupported_schema_is_rejected")]
    public void Fixed_header_fields_are_rejected(int offset)
    {
        var bytes = Copy();
        bytes[offset] ^= 0x01;

        AssertFormat(bytes);
    }

    [TestCase(6, TestName = "Catalog_id_length_out_of_bounds_is_rejected")]
    [TestCase(8, TestName = "Catalog_version_length_out_of_bounds_is_rejected")]
    public void Header_string_lengths_out_of_bounds_are_rejected(int offset)
    {
        var bytes = Copy();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), ushort.MaxValue);

        AssertFormat(bytes);
    }

    [Test]
    public void Payload_length_mismatch_is_rejected()
    {
        var bytes = Copy();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10, 4), 0);

        AssertFormat(bytes);
    }

    [Test]
    public void Trailing_byte_is_rejected()
    {
        var bytes = new byte[_valid.Length + 1];
        Array.Copy(_valid, bytes, _valid.Length);

        AssertFormat(bytes);
    }

    [Test]
    public void Invalid_catalog_id_utf8_is_rejected()
    {
        var bytes = Copy();
        bytes[HeaderSize] = 0xff;
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Invalid_catalog_version_utf8_is_rejected()
    {
        var bytes = Copy();
        bytes[HeaderSize + ReadUInt16(bytes, 6)] = 0xff;
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Checksum_mismatch_is_rejected()
    {
        var bytes = Copy();
        bytes[^1] ^= 0x01;

        AssertFormat(bytes);
    }

    [Test]
    public void Catalog_id_mismatch_is_a_compatibility_error() =>
        AssertCompatibility(_valid, TagCatalogExpectations.ForPublished("game-b", "1.4.0", Fingerprint));

    [Test]
    public void Catalog_version_mismatch_is_a_compatibility_error() =>
        AssertCompatibility(_valid, TagCatalogExpectations.ForPublished("game-a", "1.4.1", Fingerprint));

    [Test]
    public void Expected_fingerprint_mismatch_is_a_compatibility_error()
    {
        var expected = Fingerprint;
        expected[0] ^= 0x01;

        AssertCompatibility(_valid, TagCatalogExpectations.ForPublished("game-a", "1.4.0", expected));
    }

    [Test]
    public void Tag_count_exceeding_file_derived_bound_is_rejected_before_allocation()
    {
        var bytes = Copy();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(PayloadOffset, 4), uint.MaxValue);
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Truncated_tag_name_length_is_rejected()
    {
        var bytes = Copy();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(FirstTagOffset + 2, 2), ushort.MaxValue);
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Invalid_tag_name_utf8_is_rejected()
    {
        var bytes = Copy();
        bytes[FirstTagOffset + 4] = 0xff;
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Duplicate_tag_index_is_rejected()
    {
        var bytes = Copy();
        WriteUInt16(bytes, TagOffset(1), 1);
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Out_of_order_tag_index_is_rejected()
    {
        var bytes = Copy();
        WriteUInt16(bytes, TagOffset(1), 4);
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Duplicate_tag_name_is_rejected()
    {
        var bytes = Copy();
        ReplaceAscii(bytes, TagNameOffset(2), "alpha");
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Out_of_order_tag_name_is_rejected()
    {
        var bytes = Copy();
        ReplaceAscii(bytes, TagNameOffset(0), "zebra");
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Non_canonical_tag_name_is_rejected()
    {
        var bytes = Copy();
        bytes[TagNameOffset(0)] = (byte)'A';
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Wrong_parent_index_is_rejected()
    {
        var bytes = Copy();
        WriteUInt16(bytes, TagParentOffset(1), 0);
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Wrong_subtree_end_index_is_rejected()
    {
        var bytes = Copy();
        WriteUInt16(bytes, TagSubtreeEndOffset(0), 1);
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Redirect_count_exceeding_file_derived_bound_is_rejected_before_allocation()
    {
        var bytes = Copy();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(RedirectCountOffset, 4), uint.MaxValue);
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Truncated_redirect_name_length_is_rejected()
    {
        var bytes = Copy();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(FirstRedirectOffset, 2), ushort.MaxValue);
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Invalid_redirect_name_utf8_is_rejected()
    {
        var bytes = Copy();
        bytes[FirstRedirectOffset + 2] = 0xff;
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Duplicate_redirect_source_is_rejected()
    {
        var bytes = Copy();
        ReplaceAscii(bytes, RedirectNameOffset(1), "legacy.one");
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Out_of_order_redirect_source_is_rejected()
    {
        var bytes = Copy();
        ReplaceAscii(bytes, RedirectNameOffset(0), "zegacy.one");
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Non_canonical_redirect_source_is_rejected()
    {
        var bytes = Copy();
        bytes[RedirectNameOffset(0)] = (byte)'L';
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [TestCase(0, TestName = "None_redirect_target_is_rejected")]
    [TestCase(5, TestName = "Out_of_range_redirect_target_is_rejected")]
    public void Invalid_redirect_target_is_rejected(int target)
    {
        var bytes = Copy();
        WriteUInt16(bytes, RedirectTargetOffset(0), checked((ushort)target));
        Rechecksum(bytes);

        AssertFormat(bytes);
    }

    [Test]
    public void Payload_fingerprint_mismatch_is_rejected_even_in_development_mode()
    {
        var development = TagCatalogBinaryTests.WriteBinary(
            TagCatalogBinaryTests.Compile("game-a", "0.0.0-dev", false).Catalog!);
        development[14] ^= 0x01;
        Rechecksum(development);

        using var input = new MemoryStream(development);
        Assert.Throws<TagCatalogFormatException>(() =>
            TagCatalogBinary.Load(input, TagCatalogExpectations.ForDevelopment("game-a")));
    }

    private byte[] Fingerprint => _valid.AsSpan(14, 32).ToArray();
    private int PayloadOffset => HeaderSize + ReadUInt16(_valid, 6) + ReadUInt16(_valid, 8);
    private int FirstTagOffset => PayloadOffset + 4;

    private int RedirectCountOffset
    {
        get
        {
            var offset = FirstTagOffset;
            var count = ReadUInt32(_valid, PayloadOffset);
            for (var i = 0; i < count; i++) offset = NextTagOffset(_valid, offset);
            return offset;
        }
    }

    private int FirstRedirectOffset => RedirectCountOffset + 4;

    private int TagOffset(int ordinal)
    {
        var offset = FirstTagOffset;
        for (var i = 0; i < ordinal; i++) offset = NextTagOffset(_valid, offset);
        return offset;
    }

    private int TagNameOffset(int ordinal) => TagOffset(ordinal) + 4;
    private int TagParentOffset(int ordinal) => TagNameOffset(ordinal) + ReadUInt16(_valid, TagOffset(ordinal) + 2);
    private int TagSubtreeEndOffset(int ordinal) => TagParentOffset(ordinal) + 2;

    private int RedirectOffset(int ordinal)
    {
        var offset = FirstRedirectOffset;
        for (var i = 0; i < ordinal; i++)
        {
            offset += 2 + ReadUInt16(_valid, offset) + 2;
        }

        return offset;
    }

    private int RedirectNameOffset(int ordinal) => RedirectOffset(ordinal) + 2;
    private int RedirectTargetOffset(int ordinal) =>
        RedirectNameOffset(ordinal) + ReadUInt16(_valid, RedirectOffset(ordinal));

    private byte[] Copy() => (byte[])_valid.Clone();

    private static int NextTagOffset(byte[] bytes, int offset) =>
        offset + 2 + 2 + ReadUInt16(bytes, offset + 2) + 2 + 2;

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);

    private static void ReplaceAscii(byte[] bytes, int offset, string value)
    {
        for (var i = 0; i < value.Length; i++) bytes[offset + i] = checked((byte)value[i]);
    }

    private static void Rechecksum(byte[] bytes)
    {
        bytes.AsSpan(46, 32).Clear();
        using var sha256 = SHA256.Create();
        var checksum = sha256.ComputeHash(bytes);
        checksum.CopyTo(bytes, 46);
    }

    private static void AssertFormat(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        Assert.Throws<TagCatalogFormatException>(() => TagCatalogBinary.Load(
            input,
            TagCatalogExpectations.ForPublished("game-a", "1.4.0", bytes.AsSpan(14, 32))));
    }

    private static void AssertCompatibility(byte[] bytes, TagCatalogExpectations expectations)
    {
        using var input = new MemoryStream(bytes);
        Assert.Throws<TagCatalogCompatibilityException>(() => TagCatalogBinary.Load(input, expectations));
    }
}
