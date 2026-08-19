using System;
using System.IO;

namespace Bun3.Server.Tests.Helpers;

/// <summary>Read-only stream where a single Read returns at most chunkSize bytes. Simulates fragmented TCP arrival.</summary>
public sealed class ChunkedReadStream : Stream
{
    private readonly byte[] _data;
    private readonly int _chunkSize;
    private int _position;

    public ChunkedReadStream(byte[] data, int chunkSize)
    {
        _data = data;
        _chunkSize = chunkSize;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _data.Length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = Math.Min(Math.Min(count, _chunkSize), _data.Length - _position);
        Array.Copy(_data, _position, buffer, offset, n);
        _position += n;
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
