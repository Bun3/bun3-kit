using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Hosting;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddServer<EchoSession>(options => options.Port = 20000);
await builder.Build().RunAsync();

/// <summary>받은 프레임을 그대로 돌려주는 최소 세션 — Bun3.Server 조립의 최소 예제.</summary>
public sealed class EchoSession : Session
{
    public EchoSession(IConnection connection) : base(connection) { }

    protected override ValueTask OnFrameAsync(ReadOnlyMemory<byte> frame) => SendAsync(frame);
}
