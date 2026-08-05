using Bun3.Server.Abstractions;
using Bun3.Server.Core;

namespace Bun3.Server.Tests.Helpers;

/// <summary>받은 패킷을 그대로 돌려주는 공용 테스트 세션.</summary>
public sealed class EchoSession : Session
{
    public EchoSession(IConnection connection) : base(connection) { }

    protected override ValueTask OnPacketAsync(ReadOnlyMemory<byte> packet) => SendAsync(packet);
}
