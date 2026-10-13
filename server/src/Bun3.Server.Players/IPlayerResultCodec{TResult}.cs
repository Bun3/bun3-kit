using System;

namespace Bun3.Server.Players
{
    /// <summary>Serializes immutable operation receipts and reconstructs their domain results.</summary>
    public interface IPlayerResultCodec<TResult> where TResult : class
    {
        /// <summary>Gets the schema version written with new results.</summary>
        int SchemaVersion { get; }
        /// <summary>Serializes a result before its transaction commits.</summary>
        ReadOnlyMemory<byte> Encode(TResult result);
        /// <summary>Reconstructs a result, rejecting unsupported schema versions.</summary>
        TResult Decode(int schemaVersion, ReadOnlyMemory<byte> payload);
    }
}
