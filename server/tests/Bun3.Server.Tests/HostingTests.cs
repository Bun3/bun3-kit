using System.Net;
using System.Net.Sockets;
using System.Text;
using Bun3.Common.Network;
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
            await PacketFormat.WritePacketAsync(stream, payload);
            var echoed = await PacketFormat.ReadPacketAsync(stream, 1024 * 1024).AsTask().WaitAsync(Timeout);

            Assert.That(echoed, Is.EqualTo(payload));
        }
        finally
        {
            await host.StopAsync().WaitAsync(Timeout);
        }
    }

    [Test]
    public async Task MaxPacketSize_reaches_the_transport()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddServer<EchoSession>(options =>
        {
            options.Port = 0;
            options.MaxPacketSize = 64;
        });
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            var port = host.Services.GetRequiredService<TcpTransportListener>().BoundPort!.Value;
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();

            await PacketFormat.WritePacketAsync(stream, new byte[65]); // oversized packet

            // server closes the connection on protocol violation — EOF (null) or IO exception
            try
            {
                var packet = await PacketFormat.ReadPacketAsync(stream, 1024).AsTask().WaitAsync(Timeout);
                Assert.That(packet, Is.Null);
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
    public void Duplicate_server_registration_fails_at_registration_time()
    {
        var services = new ServiceCollection();
        services.AddServer<EchoSession>();

        Assert.Throws<InvalidOperationException>(() => services.AddServer<EchoSession>());
    }

    [Test]
    public void Options_bind_from_Bun3_Server_configuration_section()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bun3:Server:Port"] = "0",
            ["Bun3:Server:MaxPacketSize"] = "2048",
            ["Bun3:Server:MaxQueuedPacketsPerSession"] = "32",
        });
        builder.Services.AddServer<EchoSession>();
        using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<ServerOptions>>().Value;

        Assert.That(options.Port, Is.EqualTo(0));
        Assert.That(options.MaxPacketSize, Is.EqualTo(2048));
        Assert.That(options.MaxQueuedPacketsPerSession, Is.EqualTo(32));
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

        Assert.That(options.Port, Is.EqualTo(0)); // the lambda applies later and wins
    }
}
