using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Hosting;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddServer<EchoSession>(options => options.Port = 20000);
await builder.Build().RunAsync();

/// <summary>Minimal session that echoes received packets back — smallest Bun3.Server assembly example.</summary>
public sealed class EchoSession : Session
{
    public EchoSession(IConnection connection) : base(connection) { }

    protected override ValueTask OnPacketAsync(ReadOnlyMemory<byte> packet) => SendAsync(packet);
}
