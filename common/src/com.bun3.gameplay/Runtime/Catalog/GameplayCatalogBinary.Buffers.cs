// GameplayCatalogBinary partial — byte writer and bounds-checked payload reader.
#nullable enable
using System;
using System.Buffers.Binary;
using System.Text;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Catalog
{
    public static partial class GameplayCatalogBinary
    {
        private sealed class BufferWriter
        {
            private byte[] _buffer;

            internal BufferWriter(int capacity) => _buffer = new byte[capacity];

            internal int Length { get; private set; }

            internal void WriteByte(byte value)
            {
                Ensure(1);
                _buffer[Length++] = value;
            }

            internal void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

            internal void WriteUInt16(ushort value)
            {
                Ensure(2);
                BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(Length, 2), value);
                Length += 2;
            }

            internal void WriteUInt32(uint value)
            {
                Ensure(4);
                BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(Length, 4), value);
                Length += 4;
            }

            internal void WriteInt32(int value)
            {
                Ensure(4);
                BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(Length, 4), value);
                Length += 4;
            }

            internal void WriteInt64(long value)
            {
                Ensure(8);
                BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(Length, 8), value);
                Length += 8;
            }

            internal void WriteTag(GameplayTag tag) => WriteUInt16(tag.Index);

            internal void WriteBigNum(BigNum value)
            {
                WriteInt64(value.Mantissa);
                WriteInt32(value.Exponent);
            }

            internal void WriteOperand(Operand operand)
            {
                WriteByte((byte)operand.Kind);
                WriteUInt16(operand.AttributeId);
                WriteBigNum(operand.Value);
            }

            internal void WriteNullableOperand(Operand? operand)
            {
                WriteBool(operand.HasValue);
                if (operand.HasValue) WriteOperand(operand.Value);
            }

            internal void WriteBigNums(BigNum[] values)
            {
                WriteInt32(values.Length);
                foreach (var value in values) WriteBigNum(value);
            }

            internal void WriteNullableBigNums(BigNum[]? values)
            {
                if (values is null)
                {
                    WriteInt32(-1);
                    return;
                }

                WriteBigNums(values);
            }

            internal void WriteString(string? value)
            {
                if (value is null)
                {
                    WriteInt32(-1);
                    return;
                }

                var bytes = StrictUtf8.GetBytes(value);
                WriteInt32(bytes.Length);
                WriteBytes(bytes);
            }

            internal void WriteBytes(ReadOnlySpan<byte> value)
            {
                Ensure(value.Length);
                value.CopyTo(_buffer.AsSpan(Length));
                Length += value.Length;
            }

            internal void WriteZeroes(int count)
            {
                Ensure(count);
                _buffer.AsSpan(Length, count).Clear();
                Length += count;
            }

            internal void PatchUInt32(int offset, uint value) =>
                BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(offset, 4), value);

            internal byte[] ToArray()
            {
                var result = new byte[Length];
                Array.Copy(_buffer, result, Length);
                return result;
            }

            private void Ensure(int additional)
            {
                var required = checked(Length + additional);
                if (required <= _buffer.Length) return;
                var capacity = _buffer.Length;
                while (capacity < required) capacity = checked(capacity * 2);
                Array.Resize(ref _buffer, capacity);
            }
        }

        private sealed class PayloadReader
        {
            private readonly byte[] _bytes;
            private readonly int _end;
            private int _offset;

            internal PayloadReader(byte[] bytes, int offset, int length)
            {
                _bytes = bytes;
                _offset = offset;
                _end = checked(offset + length);
            }

            internal int Remaining => _end - _offset;

            internal byte ReadByte(string field)
            {
                Require(1, field);
                return _bytes[_offset++];
            }

            internal bool ReadBool(string field)
            {
                var value = ReadByte(field);
                if (value > 1) throw Fail(field, "is not a boolean");
                return value == 1;
            }

            internal ushort ReadUInt16(string field)
            {
                Require(2, field);
                var value = BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(_offset, 2));
                _offset += 2;
                return value;
            }

            internal int ReadInt32(string field)
            {
                Require(4, field);
                var value = BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(_offset, 4));
                _offset += 4;
                return value;
            }

            internal long ReadInt64(string field)
            {
                Require(8, field);
                var value = BinaryPrimitives.ReadInt64LittleEndian(_bytes.AsSpan(_offset, 8));
                _offset += 8;
                return value;
            }

            internal GameplayTag ReadTag(string field) => new GameplayTag(ReadUInt16(field));

            internal BigNum ReadBigNum(string field)
            {
                var mantissa = ReadInt64(field);
                var exponent = ReadInt32(field);
                try
                {
                    return BigNum.FromParts(mantissa, exponent);
                }
                catch (Exception exception) when (exception is ArgumentOutOfRangeException
                                                  || exception is BigNumOverflowException)
                {
                    throw new TagCatalogFormatException(At(field, "is not a representable BigNum"), exception);
                }
            }

            internal Operand ReadOperand(string field)
            {
                var kind = (OperandKind)ReadByte(field);
                var attributeId = ReadUInt16(field);
                var value = ReadBigNum(field);
                switch (kind)
                {
                    case OperandKind.Constant: return Operand.Constant(value);
                    case OperandKind.Attribute: return Operand.Attribute(attributeId, value);
                    case OperandKind.SourceAttribute: return Operand.SourceAttribute(attributeId, value);
                    case OperandKind.EventMagnitude: return Operand.EventMagnitude(value);
                    default: throw Fail(field, "has an unknown operand kind");
                }
            }

            internal Operand? ReadNullableOperand(string field) =>
                ReadBool(field) ? ReadOperand(field) : (Operand?)null;

            internal BigNum[] ReadBigNums(string field)
            {
                var values = new BigNum[ReadLength(field)];
                for (var i = 0; i < values.Length; i++) values[i] = ReadBigNum(field);
                return values;
            }

            internal BigNum[]? ReadNullableBigNums(string field)
            {
                var count = ReadInt32(field);
                if (count == -1) return null;
                if (count < 0 || count > Remaining) throw Fail(field, "count exceeds the payload boundary");
                var values = new BigNum[count];
                for (var i = 0; i < count; i++) values[i] = ReadBigNum(field);
                return values;
            }

            internal string? ReadString(string field)
            {
                var length = ReadInt32(field);
                if (length == -1) return null;
                Require(length, field);
                string value;
                try
                {
                    value = StrictUtf8.GetString(_bytes, _offset, length);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new TagCatalogFormatException(At(field, "is not strict UTF-8"), exception);
                }

                _offset += length;
                return value;
            }

            // Element counts are bounded by the remaining bytes (every element costs at least one byte),
            // so a corrupted count cannot make the reader allocate an arbitrary array.
            internal int ReadLength(string field)
            {
                var count = ReadInt32(field);
                if (count < 0 || count > Remaining) throw Fail(field, "exceeds the payload boundary");
                return count;
            }

            private void Require(int length, string field)
            {
                if (length < 0 || length > Remaining)
                {
                    throw Fail(field, "extends past the payload boundary");
                }
            }

            // Labels name only the caller-level field; the byte offset locates the failure, so the
            // success path never builds a label string.
            private string At(string field, string problem) => $"{field} {problem} (payload byte {_offset}).";

            private TagCatalogFormatException Fail(string field, string problem) => Format(At(field, problem));
        }
    }
}
