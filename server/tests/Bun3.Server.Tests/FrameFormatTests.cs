using System.Text;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class FrameFormatTests
{
    private const int MaxFrameSize = 1024;

    private static async Task<byte[]> DumpFrameAsync(byte[] body)
    {
        using var ms = new MemoryStream();
        await FrameFormat.WriteFrameAsync(ms, body);
        return ms.ToArray();
    }

    [Test]
    public async Task Roundtrip_preserves_payload()
    {
        var payload = Encoding.UTF8.GetBytes("hello bun3");
        var wire = await DumpFrameAsync(payload);
        using var ms = new MemoryStream(wire);

        var frame = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);

        Assert.That(frame, Is.EqualTo(payload));
    }

    [Test]
    public async Task Header_is_4_byte_little_endian_length()
    {
        var wire = await DumpFrameAsync(new byte[300]);

        Assert.That(wire.Length, Is.EqualTo(4 + 300));
        Assert.That(wire[0], Is.EqualTo(0x2C)); // 300 = 0x012C
        Assert.That(wire[1], Is.EqualTo(0x01));
        Assert.That(wire[2], Is.EqualTo(0x00));
        Assert.That(wire[3], Is.EqualTo(0x00));
    }

    [Test]
    public async Task Partial_arrival_is_reassembled()
    {
        var payload = Encoding.UTF8.GetBytes("split into tiny chunks");
        var wire = await DumpFrameAsync(payload);
        using var stream = new ChunkedReadStream(wire, chunkSize: 3);

        var frame = await FrameFormat.ReadFrameAsync(stream, MaxFrameSize);

        Assert.That(frame, Is.EqualTo(payload));
    }

    [Test]
    public async Task Merged_arrival_yields_two_frames()
    {
        using var ms = new MemoryStream();
        await FrameFormat.WriteFrameAsync(ms, Encoding.UTF8.GetBytes("one"));
        await FrameFormat.WriteFrameAsync(ms, Encoding.UTF8.GetBytes("two"));
        ms.Position = 0;

        var first = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);
        var second = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);
        var third = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);

        Assert.That(first, Is.EqualTo(Encoding.UTF8.GetBytes("one")));
        Assert.That(second, Is.EqualTo(Encoding.UTF8.GetBytes("two")));
        Assert.That(third, Is.Null); // 프레임 경계의 깨끗한 EOF
    }

    [Test]
    public async Task Zero_length_frame_is_valid_and_empty()
    {
        var wire = await DumpFrameAsync(Array.Empty<byte>());
        using var ms = new MemoryStream(wire);

        var frame = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);

        Assert.That(frame, Is.Not.Null);
        Assert.That(frame, Is.Empty);
    }

    [Test]
    public async Task Frame_at_exactly_max_size_is_accepted()
    {
        var wire = await DumpFrameAsync(new byte[MaxFrameSize]);
        using var ms = new MemoryStream(wire);

        var frame = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);

        Assert.That(frame, Has.Length.EqualTo(MaxFrameSize));
    }

    [Test]
    public async Task Frame_over_max_size_throws_InvalidDataException()
    {
        var wire = await DumpFrameAsync(new byte[MaxFrameSize + 1]);
        using var ms = new MemoryStream(wire);

        Assert.ThrowsAsync<InvalidDataException>(async () => await FrameFormat.ReadFrameAsync(ms, MaxFrameSize));
    }

    [Test]
    public void Negative_length_header_throws_InvalidDataException()
    {
        using var ms = new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }); // -1
        Assert.ThrowsAsync<InvalidDataException>(async () => await FrameFormat.ReadFrameAsync(ms, MaxFrameSize));
    }

    [Test]
    public void Eof_mid_header_throws_EndOfStreamException()
    {
        using var ms = new MemoryStream(new byte[] { 0x05, 0x00 }); // 헤더 2바이트만 도착
        Assert.ThrowsAsync<EndOfStreamException>(async () => await FrameFormat.ReadFrameAsync(ms, MaxFrameSize));
    }

    [Test]
    public void Eof_mid_body_throws_EndOfStreamException()
    {
        using var ms = new MemoryStream(new byte[] { 0x0A, 0x00, 0x00, 0x00, 1, 2, 3 }); // 길이 10, 본문 3바이트만
        Assert.ThrowsAsync<EndOfStreamException>(async () => await FrameFormat.ReadFrameAsync(ms, MaxFrameSize));
    }

    [Test]
    public async Task Empty_stream_returns_null()
    {
        using var ms = new MemoryStream();
        var frame = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);
        Assert.That(frame, Is.Null);
    }
}
