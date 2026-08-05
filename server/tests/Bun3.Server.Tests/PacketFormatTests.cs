using System.Text;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PacketFormatTests
{
    private const int MaxPacketSize = 1024;

    private static async Task<byte[]> DumpPacketAsync(byte[] body)
    {
        using var ms = new MemoryStream();
        await PacketFormat.WritePacketAsync(ms, body);
        return ms.ToArray();
    }

    [Test]
    public async Task Roundtrip_preserves_payload()
    {
        var payload = Encoding.UTF8.GetBytes("hello bun3");
        var wire = await DumpPacketAsync(payload);
        using var ms = new MemoryStream(wire);

        var packet = await PacketFormat.ReadPacketAsync(ms, MaxPacketSize);

        Assert.That(packet, Is.EqualTo(payload));
    }

    [Test]
    public async Task Header_is_4_byte_little_endian_length()
    {
        var wire = await DumpPacketAsync(new byte[300]);

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
        var wire = await DumpPacketAsync(payload);
        using var stream = new ChunkedReadStream(wire, chunkSize: 3);

        var packet = await PacketFormat.ReadPacketAsync(stream, MaxPacketSize);

        Assert.That(packet, Is.EqualTo(payload));
    }

    [Test]
    public async Task Merged_arrival_yields_two_packets()
    {
        using var ms = new MemoryStream();
        await PacketFormat.WritePacketAsync(ms, Encoding.UTF8.GetBytes("one"));
        await PacketFormat.WritePacketAsync(ms, Encoding.UTF8.GetBytes("two"));
        ms.Position = 0;

        var first = await PacketFormat.ReadPacketAsync(ms, MaxPacketSize);
        var second = await PacketFormat.ReadPacketAsync(ms, MaxPacketSize);
        var third = await PacketFormat.ReadPacketAsync(ms, MaxPacketSize);

        Assert.That(first, Is.EqualTo(Encoding.UTF8.GetBytes("one")));
        Assert.That(second, Is.EqualTo(Encoding.UTF8.GetBytes("two")));
        Assert.That(third, Is.Null); // 패킷 경계의 깨끗한 EOF
    }

    [Test]
    public async Task Zero_length_packet_is_valid_and_empty()
    {
        var wire = await DumpPacketAsync(Array.Empty<byte>());
        using var ms = new MemoryStream(wire);

        var packet = await PacketFormat.ReadPacketAsync(ms, MaxPacketSize);

        Assert.That(packet, Is.Not.Null);
        Assert.That(packet, Is.Empty);
    }

    [Test]
    public async Task Packet_at_exactly_max_size_is_accepted()
    {
        var wire = await DumpPacketAsync(new byte[MaxPacketSize]);
        using var ms = new MemoryStream(wire);

        var packet = await PacketFormat.ReadPacketAsync(ms, MaxPacketSize);

        Assert.That(packet, Has.Length.EqualTo(MaxPacketSize));
    }

    [Test]
    public async Task Packet_over_max_size_throws_InvalidDataException()
    {
        var wire = await DumpPacketAsync(new byte[MaxPacketSize + 1]);
        using var ms = new MemoryStream(wire);

        Assert.ThrowsAsync<InvalidDataException>(async () => await PacketFormat.ReadPacketAsync(ms, MaxPacketSize));
    }

    [Test]
    public void Negative_length_header_throws_InvalidDataException()
    {
        using var ms = new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }); // -1
        Assert.ThrowsAsync<InvalidDataException>(async () => await PacketFormat.ReadPacketAsync(ms, MaxPacketSize));
    }

    [Test]
    public void Eof_mid_header_throws_EndOfStreamException()
    {
        using var ms = new MemoryStream(new byte[] { 0x05, 0x00 }); // 헤더 2바이트만 도착
        Assert.ThrowsAsync<EndOfStreamException>(async () => await PacketFormat.ReadPacketAsync(ms, MaxPacketSize));
    }

    [Test]
    public void Eof_mid_body_throws_EndOfStreamException()
    {
        using var ms = new MemoryStream(new byte[] { 0x0A, 0x00, 0x00, 0x00, 1, 2, 3 }); // 길이 10, 본문 3바이트만
        Assert.ThrowsAsync<EndOfStreamException>(async () => await PacketFormat.ReadPacketAsync(ms, MaxPacketSize));
    }

    [Test]
    public async Task Empty_stream_returns_null()
    {
        using var ms = new MemoryStream();
        var packet = await PacketFormat.ReadPacketAsync(ms, MaxPacketSize);
        Assert.That(packet, Is.Null);
    }
}
