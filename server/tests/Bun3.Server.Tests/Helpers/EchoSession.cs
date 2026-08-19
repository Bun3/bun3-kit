using Bun3.Server.Abstractions;
using Bun3.Server.Core;

namespace Bun3.Server.Tests.Helpers;

/// <summary>Shared test session that echoes received packets back verbatim.</summary>
public sealed class EchoSession : Session
{
    public EchoSession(IConnection connection) : base(connection) { }

    protected override ValueTask OnPacketAsync(ReadOnlyMemory<byte> packet) => SendAsync(packet);
}
