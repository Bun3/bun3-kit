using System.Net;
using System.Net.Sockets;
using System.Text;
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Hosting;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class HostingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Host_boots_serves_echo_and_stops_gracefully()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddServer<EchoSession>(options => options.Port = 0);
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            var port = host.Services.GetRequiredService<TcpTransportListener>().BoundPort!.Value;
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();

            var payload = Encoding.UTF8.GetBytes("ping over host");
            await FrameFormat.WriteFrameAsync(stream, payload);
            var echoed = await FrameFormat.ReadFrameAsync(stream, 1024 * 1024).AsTask().WaitAsync(Timeout);

            Assert.That(echoed, Is.EqualTo(payload));
        }
        finally
        {
            await host.StopAsync().WaitAsync(Timeout);
        }
    }

    [Test]
    public async Task MaxFrameSize_reaches_the_transport()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddServer<EchoSession>(options =>
        {
            options.Port = 0;
            options.MaxFrameSize = 64;
        });
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            var port = host.Services.GetRequiredService<TcpTransportListener>().BoundPort!.Value;
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();

            await FrameFormat.WriteFrameAsync(stream, new byte[65]); // 초과 프레임

            // 서버가 프로토콜 위반으로 연결을 닫는다 — EOF(null) 또는 IO 예외
            try
            {
                var frame = await FrameFormat.ReadFrameAsync(stream, 1024).AsTask().WaitAsync(Timeout);
                Assert.That(frame, Is.Null);
            }
            catch (IOException)
            {
            }
        }
        finally
        {
            await host.StopAsync().WaitAsync(Timeout);
        }
    }

    [Test]
    public void Options_bind_from_Bun3_Server_configuration_section()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bun3:Server:Port"] = "0",
            ["Bun3:Server:MaxFrameSize"] = "2048",
            ["Bun3:Server:MaxQueuedFramesPerSession"] = "32",
        });
        builder.Services.AddServer<EchoSession>();
        using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<ServerOptions>>().Value;

        Assert.That(options.Port, Is.EqualTo(0));
        Assert.That(options.MaxFrameSize, Is.EqualTo(2048));
        Assert.That(options.MaxQueuedFramesPerSession, Is.EqualTo(32));
    }

    [Test]
    public void Configure_lambda_overrides_configuration_section()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bun3:Server:Port"] = "12345",
        });
        builder.Services.AddServer<EchoSession>(options => options.Port = 0);
        using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<ServerOptions>>().Value;

        Assert.That(options.Port, Is.EqualTo(0)); // 람다가 나중에 적용되어 우선한다
    }
}
