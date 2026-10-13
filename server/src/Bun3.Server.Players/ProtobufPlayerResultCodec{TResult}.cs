using System;
using Google.Protobuf;

namespace Bun3.Server.Players
{
    /// <summary>Encodes protobuf results using a single explicitly supported schema version.</summary>
    public sealed class ProtobufPlayerResultCodec<TResult> : IPlayerResultCodec<TResult> where TResult : class, IMessage<TResult>
    {
        private readonly MessageParser<TResult> _parser;
        /// <summary>Creates a codec using the supplied parser and schema version.</summary>
        public ProtobufPlayerResultCodec(MessageParser<TResult> parser, int schemaVersion = 1)
        { _parser = parser ?? throw new ArgumentNullException(nameof(parser)); SchemaVersion = schemaVersion; }
        /// <summary>Gets the supported schema version.</summary>
        public int SchemaVersion { get; }
        /// <summary>Copies the protobuf message into an owned payload.</summary>
        public ReadOnlyMemory<byte> Encode(TResult result) => (result ?? throw new ArgumentNullException(nameof(result))).ToByteArray();
        /// <summary>Parses a result, rejecting a different schema version.</summary>
        public TResult Decode(int schemaVersion, ReadOnlyMemory<byte> payload)
        {
            if (schemaVersion != SchemaVersion) throw new InvalidOperationException("Unsupported player result schema version.");
            return _parser.ParseFrom(payload.ToArray());
        }
    }
}
