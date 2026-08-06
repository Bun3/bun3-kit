using Bun3.Server.Abstractions;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Tests.PlayersProtocol;
using NUnit.Framework;
using static Bun3.Server.Tests.Helpers.PacketTestHelper;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayersTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class TestPlayer : Player
    {
        public long Gold = 100;
        public readonly List<bool> AttachedReconnectFlags = new();
        public int DetachedCalls;
        public readonly TaskCompletionSource<bool> Detached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Retired = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override ValueTask OnAttachedAsync(bool isReconnect)
        {
            AttachedReconnectFlags.Add(isReconnect);
            return default;
        }

        protected override ValueTask OnDetachedAsync()
        {
            DetachedCalls++;
            Detached.TrySetResult(true);
            return default;
        }

        protected override ValueTask OnRetiredAsync()
        {
            Retired.TrySetResult(true);
            return default;
        }
    }

    private sealed class TestPlayersSession : PlayerSession<TestPlayer>
    {
        public TestPlayersSession(IConnection connection) : base(connection) { }
    }

    private sealed class Harness
    {
        public readonly FakeTransport Transport = new();
        public readonly PlayerRegistry<TestPlayer> Registry;
        public readonly RpcServer<TestPlayersSession, PlayersRequest, PlayersResponse, PlayersUpdate> Server;
        public int LoaderCalls;

        public Harness(PlayersOptions? options = null)
        {
            Registry = new PlayerRegistry<TestPlayer>(key =>
            {
                Interlocked.Increment(ref LoaderCalls);
                return new ValueTask<TestPlayer>(new TestPlayer());
            }, options);

            var config = new PlayersConfig<TestPlayersSession>();
            config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
            {
                try
                {
                    var result = await s.SignInAsync($"guest:{req.DeviceId}");
                    if (req.DeviceId == "double")
                    {
                        await s.SignInAsync($"guest:{req.DeviceId}");   // 이중 SignIn → 예외 → status 2
                    }
                    return new LoginResponse { Gold = result.Player.Gold, IsReconnect = result.IsReconnect };
                }
                catch (DuplicateLoginException)
                {
                    return Reply.Fail(-77);   // RejectNew 정책 테스트용
                }
            });
            config.OnRequest<AddGoldRequest, AddGoldResponse>((s, req) =>
            {
                s.Player!.Gold += req.Amount;
                return new ValueTask<Reply<AddGoldResponse>>(new AddGoldResponse { Gold = s.Player.Gold });
            });
            config.OnRequest<GetGoldRequest, GetGoldResponse>((s, req) =>
                new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse { Gold = s.Player!.Gold }));

            Server = new RpcServer<TestPlayersSession, PlayersRequest, PlayersResponse, PlayersUpdate>(
                Transport,
                Registry.Wrap(config, conn => new TestPlayersSession(conn)),
                config.Rpc);
        }
    }

    private static async Task<PlayersResponse> RoundtripAsync(FakeConnection conn, PlayersRequest request)
    {
        conn.ReceivePacket(Wrap(Channels.Request, request));
        await conn.SentSignal.WaitAsync(Timeout);
        Assert.That(conn.SentPackets.TryDequeue(out var packet), Is.True);
        Assert.That(packet![0], Is.EqualTo(Channels.Response));
        return PlayersResponse.Parser.ParseFrom(packet.AsSpan(1).ToArray());
    }

    private static Task<PlayersResponse> LoginAsync(FakeConnection conn, string device, long requestId = 1) =>
        RoundtripAsync(conn, new PlayersRequest { RequestId = requestId, Login = new LoginRequest { DeviceId = device } });

    [Test]
    public async Task New_sign_in_loads_player_once()
    {
        var h = new Harness();
        await h.Server.StartAsync();
        var conn = h.Transport.Connect(1);

        var response = await LoginAsync(conn, "a");

        Assert.That(response.Status, Is.EqualTo(RpcStatus.Ok));
        Assert.That(response.Login.Gold, Is.EqualTo(100));
        Assert.That(response.Login.IsReconnect, Is.False);
        Assert.That(h.LoaderCalls, Is.EqualTo(1));
        Assert.That(h.Registry.TryGet("guest:a"), Is.Not.Null);
        await h.Server.StopAsync();
    }

    [Test]
    public async Task Unauthenticated_request_is_gated_with_status_3()
    {
        var h = new Harness();
        await h.Server.StartAsync();
        var conn = h.Transport.Connect(1);

        var gated = await RoundtripAsync(conn, new PlayersRequest { RequestId = 1, GetGold = new GetGoldRequest() });
        Assert.That(gated.Status, Is.EqualTo(RpcStatus.Unauthenticated));
        Assert.That(conn.IsOpen, Is.True);

        await LoginAsync(conn, "a", 2);
        var afterLogin = await RoundtripAsync(conn, new PlayersRequest { RequestId = 3, GetGold = new GetGoldRequest() });
        Assert.That(afterLogin.Status, Is.EqualTo(RpcStatus.Ok));
        Assert.That(afterLogin.GetGold.Gold, Is.EqualTo(100));
        await h.Server.StopAsync();
    }

    [Test]
    public async Task Grace_rebind_keeps_state_without_reloading()
    {
        var h = new Harness();
        await h.Server.StartAsync();
        var conn1 = h.Transport.Connect(1);
        await LoginAsync(conn1, "a");
        var added = await RoundtripAsync(conn1, new PlayersRequest { RequestId = 2, AddGold = new AddGoldRequest { Amount = 5 } });
        Assert.That(added.AddGold.Gold, Is.EqualTo(105));
        var player = h.Registry.TryGet("guest:a")!;

        conn1.Close();
        await player.Detached.Task.WaitAsync(Timeout);

        var conn2 = h.Transport.Connect(2);
        var relogin = await LoginAsync(conn2, "a");

        Assert.That(relogin.Login.IsReconnect, Is.True);
        Assert.That(relogin.Login.Gold, Is.EqualTo(105));
        Assert.That(h.LoaderCalls, Is.EqualTo(1));
        Assert.That(player.AttachedReconnectFlags, Is.EqualTo(new[] { false, true }));
        Assert.That(player.DetachedCalls, Is.EqualTo(1));
        await h.Server.StopAsync();
    }

    [Test]
    public async Task Grace_expiry_retires_and_next_login_reloads()
    {
        var h = new Harness(new PlayersOptions { GracePeriod = TimeSpan.FromMilliseconds(200) });
        await h.Server.StartAsync();
        var conn1 = h.Transport.Connect(1);
        await LoginAsync(conn1, "a");
        var player = h.Registry.TryGet("guest:a")!;

        conn1.Close();
        await player.Retired.Task.WaitAsync(Timeout);
        Assert.That(h.Registry.TryGet("guest:a"), Is.Null);

        var conn2 = h.Transport.Connect(2);
        var relogin = await LoginAsync(conn2, "a");
        Assert.That(relogin.Login.IsReconnect, Is.False);
        Assert.That(h.LoaderCalls, Is.EqualTo(2));
        await h.Server.StopAsync();
    }

    [Test]
    public async Task Zero_grace_retires_immediately_on_disconnect()
    {
        var h = new Harness(new PlayersOptions { GracePeriod = TimeSpan.Zero });
        await h.Server.StartAsync();
        var conn = h.Transport.Connect(1);
        await LoginAsync(conn, "a");
        var player = h.Registry.TryGet("guest:a")!;

        conn.Close();

        await player.Retired.Task.WaitAsync(Timeout);
        Assert.That(h.Registry.TryGet("guest:a"), Is.Null);
        await h.Server.StopAsync();
    }

    [Test]
    public async Task Duplicate_login_new_wins_by_default()
    {
        var h = new Harness();
        await h.Server.StartAsync();
        var connA = h.Transport.Connect(1);
        await LoginAsync(connA, "a");
        await RoundtripAsync(connA, new PlayersRequest { RequestId = 2, AddGold = new AddGoldRequest { Amount = 5 } });
        var player = h.Registry.TryGet("guest:a")!;

        var connB = h.Transport.Connect(2);
        var loginB = await LoginAsync(connB, "a");

        Assert.That(loginB.Login.IsReconnect, Is.True);
        Assert.That(loginB.Login.Gold, Is.EqualTo(105));   // 같은 Player
        Assert.That(connA.IsOpen, Is.False);               // 옛 연결 킥
        Assert.That(h.LoaderCalls, Is.EqualTo(1));
        Assert.That(ReferenceEquals(h.Registry.TryGet("guest:a"), player), Is.True);
        await h.Server.StopAsync();
    }

    [Test]
    public async Task Duplicate_login_reject_policy_fails_new_and_keeps_old()
    {
        var h = new Harness(new PlayersOptions { DuplicatePolicy = DuplicateLoginPolicy.RejectNew });
        await h.Server.StartAsync();
        var connA = h.Transport.Connect(1);
        await LoginAsync(connA, "a");

        var connB = h.Transport.Connect(2);
        var loginB = await LoginAsync(connB, "a");

        Assert.That(loginB.Status, Is.EqualTo(-77));   // 핸들러가 DuplicateLoginException을 잡아 변환
        Assert.That(connA.IsOpen, Is.True);
        await h.Server.StopAsync();
    }

    [Test]
    public async Task Concurrent_same_key_logins_load_exactly_once()
    {
        var h = new Harness();
        await h.Server.StartAsync();
        var connA = h.Transport.Connect(1);
        var connB = h.Transport.Connect(2);

        var taskA = LoginAsync(connA, "a");
        var taskB = LoginAsync(connB, "a");
        await Task.WhenAll(taskA, taskB).WaitAsync(Timeout);

        Assert.That(h.LoaderCalls, Is.EqualTo(1));
        // 새 연결 승리 정책상 정확히 한 연결만 살아남는다 (승자는 순서에 따라 다름)
        for (var i = 0; i < 50 && connA.IsOpen && connB.IsOpen; i++) await Task.Delay(20);
        Assert.That(connA.IsOpen ^ connB.IsOpen, Is.True);
        await h.Server.StopAsync();
    }

    [Test]
    public async Task Double_sign_in_on_same_session_surfaces_as_status_2()
    {
        var h = new Harness();
        await h.Server.StartAsync();
        var conn = h.Transport.Connect(1);

        var response = await LoginAsync(conn, "double");

        Assert.That(response.Status, Is.EqualTo(RpcStatus.HandlerException));
        await h.Server.StopAsync();
    }

    [Test]
    public async Task RetireAll_flushes_every_player_and_clears_registry()
    {
        var h = new Harness();
        await h.Server.StartAsync();
        var connA = h.Transport.Connect(1);
        var connB = h.Transport.Connect(2);
        await LoginAsync(connA, "a");
        await LoginAsync(connB, "b");
        var playerA = h.Registry.TryGet("guest:a")!;
        var playerB = h.Registry.TryGet("guest:b")!;

        await h.Registry.RetireAllAsync();

        await playerA.Retired.Task.WaitAsync(Timeout);
        await playerB.Retired.Task.WaitAsync(Timeout);
        Assert.That(h.Registry.Players, Is.Empty);
        await h.Server.StopAsync();
    }

    [Test]
    public async Task PushUpdate_routes_to_session_when_attached_and_noops_when_detached()
    {
        var h = new Harness();
        await h.Server.StartAsync();
        var conn = h.Transport.Connect(1);
        await LoginAsync(conn, "a");
        var player = h.Registry.TryGet("guest:a")!;

        Assert.That(await player.PushUpdateAsync(new NoticeUpdate { Text = "hi" }), Is.True);
        await conn.SentSignal.WaitAsync(Timeout);
        Assert.That(conn.SentPackets.TryDequeue(out var packet), Is.True);
        Assert.That(packet![0], Is.EqualTo(Channels.Update));
        Assert.That(PlayersUpdate.Parser.ParseFrom(packet.AsSpan(1).ToArray()).Notice.Text, Is.EqualTo("hi"));

        conn.Close();
        await player.Detached.Task.WaitAsync(Timeout);
        Assert.That(await player.PushUpdateAsync(new NoticeUpdate { Text = "gone" }), Is.False);
        await h.Server.StopAsync();
    }
}
