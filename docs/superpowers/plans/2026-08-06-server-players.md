# Bun3.Server.Players 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 재접속에 살아남는 Player 계층(`Bun3.Server.Players`) — SignInAsync 상태기계, 유예/중복 로그인, 미인증 게이트 — 를 구현하고 게스트 로그인 수직 슬라이스 E2E로 완료한다.

**Architecture:** `PlayerRegistry`(accountKey→Player, 스트라이프 락 256, 유예 스윕)는 서버와 독립 객체로 세션 팩토리 래핑으로 부착. v1 Rpc 변경은 `OnGateRequest` 훅 + 상태코드 3 예약뿐. 스펙: `docs/superpowers/specs/2026-08-06-server-players-design.md`.

**Tech Stack:** netstandard2.1 + C# 9 (Players), 기존 v1 스택 위. 테스트 NUnit 4.

## Global Constraints

- **TFM/언어**: `Bun3.Server.Players`는 netstandard2.1 + `<LangVersion>9.0</LangVersion>` + `<Nullable>enable</Nullable>`, ImplicitUsings 금지(명시 using, 블록 네임스페이스). 라이브러리 await 전부 `.ConfigureAwait(false)`. `Task.WaitAsync`/`IReadOnlySet` 등 net5+ API 금지.
- **v1 변경 최소**: Rpc 패키지 변경은 Task 1의 게이트 훅·`RpcStatus` 상수·스펙 문서 갱신이 전부. `RpcServer`/`RpcClient`/와이어 무변경. v0 Core 무변경.
- **기본값(스펙 §1)**: 유예 60초(0=즉시 정리), 중복 로그인 = 새 연결 승리(옵션 `RejectNew`), 상태코드 3=미인증.
- **신원**: accountKey는 불투명 문자열. Players에 제공자(스팀 등) 지식 유입 금지.
- **DB 금지**: 로드/저장은 게임 훅(로더 델리게이트, `OnRetiredAsync`)으로만.
- **훅 순서 보장**: 같은 계정 키에 대해 OnDetachedAsync → OnAttachedAsync(재바인딩) → OnRetiredAsync 순서가 뒤섞이지 않는다(스트라이프 락 안에서 실행).
- **doc 주석**: public 멤버 전부(0 경고, GenerateDocumentationFile). internal은 불필요.
- **커밋**: gitmoji + 두 번째 `-m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"`. 레포 루트에서 실행.
- **테스트**: `dotnet test server/tests/Bun3.Server.Tests` (시작 시점 86개 — 태스크마다 전체 초록 유지).

## 파일 구조 (전체 조감)

```
server/src/Bun3.Server.Rpc/
├── RpcStatus.cs                       [Task 1] 상태코드 상수 (0/1/2/3)
├── RpcSession.cs                      [Task 1: OnGateRequest 훅 추가]
└── RpcRuntime.cs                      [Task 1: 디스패치 직전 게이트 호출 + 상수 치환]
server/src/Bun3.Server.Players/        [Task 2 신규 패키지]
├── Bun3.Server.Players.csproj
├── Player.cs                          게임 상속 베이스 + 훅 3종 + PushUpdateAsync
├── SignInResult.cs                    readonly struct { TPlayer Player; bool IsReconnect; }
├── PlayersOptions.cs                  GracePeriod=60s, DuplicatePolicy=NewWins
├── DuplicateLoginException.cs         RejectNew 정책에서 SignInAsync가 던짐
├── PlayersConfig.cs                   RpcConfig 래퍼 + 미인증 허용 목록
├── PlayerSession.cs                   PlayerSession<TPlayer> : RpcSession
└── PlayerRegistry.cs                  상태기계 + 스트라이프 락 + 유예 스윕 + RetireAll
server/src/Bun3.Server.Hosting/
└── PlayersServiceCollectionExtensions.cs [Task 3] AddPlayerServer + 정지 시 RetireAll
server/tests/Bun3.Server.Tests/
├── Protos/players_game.proto          [Task 2] Players 전용 루트(기존 game.proto 불변)
├── RpcGateTests.cs                    [Task 1]
├── PlayersTests.cs                    [Task 2] 상태기계/훅/게이트 단위 (FakeTransport)
├── PlayersHostingTests.cs             [Task 3]
└── PlayersE2ETests.cs                 [Task 4] 수직 슬라이스 (실 TCP)
```

의존: Players → Rpc + Core + Abstractions. 기존 game.proto는 건드리지 않는다(케이스 추가 시 기존 검증 테스트가 깨지므로 Players 테스트는 전용 루트 사용).

---

### Task 1: Rpc 게이트 훅 + RpcStatus 상수

**Files:**
- Create: `server/src/Bun3.Server.Rpc/RpcStatus.cs`
- Modify: `server/src/Bun3.Server.Rpc/RpcSession.cs` (훅 추가)
- Modify: `server/src/Bun3.Server.Rpc/RpcRuntime.cs` (게이트 호출 + 리터럴 1/2 상수 치환)
- Modify: `docs/superpowers/specs/2026-08-05-server-messaging-design.md` (§3 상태코드 표에 3 추가)
- Test: `server/tests/Bun3.Server.Tests/RpcGateTests.cs`

**Interfaces:**
- Consumes: 기존 `RpcSession`/`RpcRuntime`/테스트 하니스(FakeTransport, GameProtocol, PacketTestHelper)
- Produces:
  - `public static class RpcStatus { public const int Ok = 0; public const int UnregisteredHandler = 1; public const int HandlerException = 2; public const int Unauthenticated = 3; }`
  - `RpcSession`: `protected internal virtual int OnGateRequest(Type requestType) => RpcStatus.Ok;` — 디스패치 직전 호출, 비0 반환 시 그 상태코드로 즉시 응답(핸들러 미도달). Control 채널(Ping)은 게이트 대상 아님.
  - Task 2의 `PlayerSession`이 이 훅을 sealed 구현

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/RpcGateTests.cs`:

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;
using Bun3.Server.Rpc.ControlMessages;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Tests.Helpers;
using Google.Protobuf;
using NUnit.Framework;
using static Bun3.Server.Tests.Helpers.PacketTestHelper;

namespace Bun3.Server.Tests;

[TestFixture]
public class RpcGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>BuyItemRequest만 상태 7로 거부하는 게이트.</summary>
    private sealed class GatedSession : RpcSession
    {
        public int HandlerCalls;

        public GatedSession(IConnection connection) : base(connection) { }

        protected internal override int OnGateRequest(Type requestType) =>
            requestType == typeof(BuyItemRequest) ? 7 : RpcStatus.Ok;
    }

    private static async Task<(RpcServer<GatedSession, Request, Response, Update> server, FakeTransport transport)>
        StartAsync()
    {
        var config = new RpcConfig<GatedSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>((s, req) =>
        {
            s.HandlerCalls++;
            return new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse { UnixMs = 1 });
        });
        config.OnRequest<BuyItemRequest, BuyItemResponse>((s, req) =>
        {
            s.HandlerCalls++;
            return new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse());
        });
        var transport = new FakeTransport();
        var server = new RpcServer<GatedSession, Request, Response, Update>(
            transport, conn => new GatedSession(conn), config);
        await server.StartAsync();
        return (server, transport);
    }

    private static async Task<Response> NextResponseAsync(FakeConnection conn)
    {
        await conn.SentSignal.WaitAsync(Timeout);
        Assert.That(conn.SentPackets.TryDequeue(out var packet), Is.True);
        Assert.That(packet![0], Is.EqualTo(Channels.Response));
        return Response.Parser.ParseFrom(packet.AsSpan(1).ToArray());
    }

    [Test]
    public async Task Gated_request_is_rejected_without_reaching_the_handler()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);
        var session = server.Sessions.Single();

        conn.ReceivePacket(Wrap(Channels.Request, new Request { RequestId = 1, BuyItem = new BuyItemRequest { ItemId = 5 } }));

        var response = await NextResponseAsync(conn);
        Assert.That(response.Status, Is.EqualTo(7));
        Assert.That(response.RequestId, Is.EqualTo(1));
        Assert.That(response.BodyCase, Is.EqualTo(Response.BodyOneofCase.None));
        Assert.That(session.HandlerCalls, Is.EqualTo(0));
        Assert.That(conn.IsOpen, Is.True);   // 게이트 거부는 위반이 아니다 — 세션 유지
        await server.StopAsync();
    }

    [Test]
    public async Task Ungated_request_and_control_ping_pass_through()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);
        var session = server.Sessions.Single();

        conn.ReceivePacket(Wrap(Channels.Request, new Request { RequestId = 2, GetServerTime = new GetServerTimeRequest() }));
        var response = await NextResponseAsync(conn);
        Assert.That(response.Status, Is.EqualTo(RpcStatus.Ok));
        Assert.That(session.HandlerCalls, Is.EqualTo(1));

        conn.ReceivePacket(Wrap(Channels.Control, new Control { Ping = new Ping { ClientTimeUnixMs = 9 } }));
        await conn.SentSignal.WaitAsync(Timeout);
        Assert.That(conn.SentPackets.TryDequeue(out var pong), Is.True);
        Assert.That(pong![0], Is.EqualTo(Channels.Control));   // Ping은 게이트 무관
        await server.StopAsync();
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~RpcGateTests"`
Expected: 컴파일 에러 — `RpcStatus`/`OnGateRequest` 미정의

- [ ] **Step 3: 구현**

`server/src/Bun3.Server.Rpc/RpcStatus.cs`:

```csharp
namespace Bun3.Server.Rpc
{
    /// <summary>프레임워크 예약 상태코드(1~99). 음수는 게임 정의.</summary>
    public static class RpcStatus
    {
        public const int Ok = 0;

        /// <summary>핸들러 미등록 — 기동 검증상 불가, 방어용.</summary>
        public const int UnregisteredHandler = 1;

        /// <summary>핸들러 예외 (OnHandlerError 기본 정책).</summary>
        public const int HandlerException = 2;

        /// <summary>미인증 — OnGateRequest 게이트 거부 (Players 모듈 등).</summary>
        public const int Unauthenticated = 3;
    }
}
```

`RpcSession.cs`에 추가 (기존 훅들 근처):

```csharp
        /// <summary>
        /// 요청 디스패치 직전 호출되는 게이트. RpcStatus.Ok(0)이면 진행,
        /// 비0이면 그 상태코드로 즉시 응답하고 핸들러에 도달하지 않는다.
        /// Control 채널(Ping)은 게이트 대상이 아니다.
        /// </summary>
        protected internal virtual int OnGateRequest(Type requestType) => RpcStatus.Ok;
```

`RpcRuntime.HandleRequestAsync`: `requestCase` 해석 직후·등록 조회 이전에 게이트 삽입, 기존 리터럴 1/2는 `RpcStatus.UnregisteredHandler`/`RpcStatus.HandlerException`으로 치환:

```csharp
            var gate = session.OnGateRequest(requestCase.PayloadType);
            if (gate != RpcStatus.Ok)
            {
                var gatedResponse = new TResponse();
                _schema.RequestIdOfResponse.Accessor.SetValue(gatedResponse, requestId);
                _schema.StatusOfResponse.Accessor.SetValue(gatedResponse, gate);
                await SendAsync(session, Channels.Response, gatedResponse).ConfigureAwait(false);
                return;
            }
```

스펙 문서 §3 상태코드 문단의 `1~99 = 프레임워크 예약(...)` 괄호에 `3=미인증(게이트)` 추가.

- [ ] **Step 4: 테스트 통과 확인**

Run: 필터 → 2/2. 전체 `dotnet test server/tests/Bun3.Server.Tests` → 88/88 (기존 86 회귀 없음 — 게이트 기본값 Ok이므로 기존 경로 무변경).

- [ ] **Step 5: Commit**

```
git add server/ docs/
git commit -m "✨ Add request gate hook and RpcStatus constants to Rpc" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Players 패키지 — Player/Registry/Session 상태기계

**Files:**
- Create: `server/src/Bun3.Server.Players/Bun3.Server.Players.csproj`
- Create: `server/src/Bun3.Server.Players/Player.cs`
- Create: `server/src/Bun3.Server.Players/SignInResult.cs`
- Create: `server/src/Bun3.Server.Players/PlayersOptions.cs`
- Create: `server/src/Bun3.Server.Players/DuplicateLoginException.cs`
- Create: `server/src/Bun3.Server.Players/PlayersConfig.cs`
- Create: `server/src/Bun3.Server.Players/PlayerSession.cs`
- Create: `server/src/Bun3.Server.Players/PlayerRegistry.cs`
- Create: `server/tests/Bun3.Server.Tests/Protos/players_game.proto`
- Modify: `server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj` (Players 참조 + proto 항목)
- Modify: `Bun3.sln` (`dotnet sln add`)
- Test: `server/tests/Bun3.Server.Tests/PlayersTests.cs`

**Interfaces:**
- Consumes: Task 1의 `RpcStatus`/`OnGateRequest`, 기존 `RpcSession`(`OnSessionClosedAsync` virtual, `SendUpdateAsync`), `RpcConfig<TSession>`, `RpcServer`, FakeTransport 하니스
- Produces (전부 namespace `Bun3.Server.Players`):
  - `abstract class Player` — `string AccountKey`, `RpcSession? CurrentSession`, `bool IsConnected`, 훅 `OnAttachedAsync(bool isReconnect)`/`OnDetachedAsync()`/`OnRetiredAsync()` (protected internal virtual; **훅 안에서 SignInAsync/Kick 재호출 금지** — 스트라이프 락 안에서 실행됨), `ValueTask<bool> PushUpdateAsync(IMessage)`
  - `readonly struct SignInResult<TPlayer> { TPlayer Player; bool IsReconnect; }`
  - `enum DuplicateLoginPolicy { NewWins, RejectNew }`; `PlayersOptions { TimeSpan GracePeriod = 60초(Zero=즉시); DuplicateLoginPolicy DuplicatePolicy = NewWins; }`
  - `DuplicateLoginException(string accountKey) : Exception`
  - `PlayersConfig<TSession> where TSession : Session` — `RpcConfig<TSession> Rpc`, `OnRequest<TReq,TRes>(...)`(위임), `OnRequestUnauthenticated<TReq,TRes>(...)`(위임+허용목록), internal `HashSet<Type> UnauthenticatedTypes`
  - `abstract class PlayerSession<TPlayer> : RpcSession where TPlayer : Player` — `TPlayer? Player`, `bool IsAuthenticated`, `ValueTask<SignInResult<TPlayer>> SignInAsync(string accountKey)`, sealed `OnGateRequest`/`OnSessionClosedAsync`, 재노출 `protected virtual ValueTask OnPlayerSessionClosedAsync(Exception? error)`
  - `sealed class PlayerRegistry<TPlayer> where TPlayer : Player` — ctor `(Func<string, ValueTask<TPlayer>> loader, PlayersOptions? = null, ILogger? = null)`, `Func<IConnection,TSession> Wrap<TSession>(PlayersConfig<TSession> config, Func<IConnection,TSession> factory) where TSession : PlayerSession<TPlayer>`, `TPlayer? TryGet(string)`, `IReadOnlyCollection<TPlayer> Players`, `ValueTask RetireAllAsync()`
  - Task 3/4가 전부 사용

- [ ] **Step 1: csproj + proto + 테스트 csproj 갱신**

`server/src/Bun3.Server.Players/Bun3.Server.Players.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>Bun3.Server.Players</RootNamespace>
    <PackageId>Bun3.Server.Players</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-kit</RepositoryUrl>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <Description>재접속에 살아남는 Player 계층 — SignIn 수명주기(유예/중복 로그인), 미인증 게이트</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Bun3.Server.Abstractions\Bun3.Server.Abstractions.csproj" />
    <ProjectReference Include="..\Bun3.Server.Core\Bun3.Server.Core.csproj" />
    <ProjectReference Include="..\Bun3.Server.Rpc\Bun3.Server.Rpc.csproj" />
  </ItemGroup>

</Project>
```

`server/tests/Bun3.Server.Tests/Protos/players_game.proto` (기존 game.proto 불변 — 케이스를 추가하면 기존 검증 테스트가 깨진다):

```proto
syntax = "proto3";

option csharp_namespace = "Bun3.Server.Tests.PlayersProtocol";

package players_game;

message PlayersRequest {
  int64 request_id = 1;
  oneof body {
    LoginRequest login = 10;
    AddGoldRequest add_gold = 11;
    GetGoldRequest get_gold = 12;
  }
}

message PlayersResponse {
  int64 request_id = 1;
  int32 status = 2;
  oneof body {
    LoginResponse login = 10;
    AddGoldResponse add_gold = 11;
    GetGoldResponse get_gold = 12;
  }
}

message PlayersUpdate {
  oneof body {
    NoticeUpdate notice = 10;
  }
}

message LoginRequest { string device_id = 1; }
message LoginResponse { int64 gold = 1; bool is_reconnect = 2; }
message AddGoldRequest { int32 amount = 1; }
message AddGoldResponse { int64 gold = 1; }
message GetGoldRequest {}
message GetGoldResponse { int64 gold = 1; }
message NoticeUpdate { string text = 1; }
```

테스트 csproj: `<Protobuf Include="Protos\players_game.proto" GrpcServices="None" />` 추가 + `<ProjectReference Include="..\..\src\Bun3.Server.Players\Bun3.Server.Players.csproj" />` 추가. 솔루션 편입: `dotnet sln Bun3.sln add server/src/Bun3.Server.Players/Bun3.Server.Players.csproj`

- [ ] **Step 2: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/PlayersTests.cs`:

```csharp
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

        protected internal override ValueTask OnAttachedAsync(bool isReconnect)
        {
            AttachedReconnectFlags.Add(isReconnect);
            return default;
        }

        protected internal override ValueTask OnDetachedAsync()
        {
            DetachedCalls++;
            Detached.TrySetResult(true);
            return default;
        }

        protected internal override ValueTask OnRetiredAsync()
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
```

- [ ] **Step 3: 테스트 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~PlayersTests"`
Expected: 컴파일 에러 — Players 타입들 미정의

- [ ] **Step 4: 구현**

`Player.cs`:

```csharp
using System.Threading.Tasks;
using Bun3.Server.Rpc;
using Google.Protobuf;

namespace Bun3.Server.Players
{
    /// <summary>
    /// accountKey당 1개, 재접속에 살아남는 단위. 상태(재화·인벤토리 등)는 이 파생
    /// 클래스에 둔다. 훅들은 레지스트리의 계정 키 스트라이프 락 안에서 실행되므로
    /// 훅 안에서 SignInAsync/Kick을 재호출하면 안 된다(교착).
    /// </summary>
    public abstract class Player
    {
        /// <summary>불투명 신원 키 (권장 규약 "provider:subject"). SignIn 시 설정된다.</summary>
        public string AccountKey { get; internal set; } = "";

        /// <summary>접속 중이면 현재 세션, 유예 중이면 null.</summary>
        public RpcSession? CurrentSession { get; internal set; }

        public bool IsConnected => CurrentSession != null;

        /// <summary>세션 바인딩 직후. isReconnect=true면 유예 재바인딩 또는 중복 로그인 이전.</summary>
        protected internal virtual ValueTask OnAttachedAsync(bool isReconnect) => default;

        /// <summary>연결 끊김(유예 시작) 시.</summary>
        protected internal virtual ValueTask OnDetachedAsync() => default;

        /// <summary>유예 만료·RetireAll 시 — 저장 지점. 이후 레지스트리에서 제거된다.</summary>
        protected internal virtual ValueTask OnRetiredAsync() => default;

        /// <summary>접속 중이면 현재 세션으로 푸시하고 true, 유예 중이면 false.</summary>
        public async ValueTask<bool> PushUpdateAsync(IMessage update)
        {
            var session = CurrentSession;
            if (session == null)
            {
                return false;
            }

            await session.SendUpdateAsync(update).ConfigureAwait(false);
            return true;
        }
    }
}
```

`SignInResult.cs`:

```csharp
namespace Bun3.Server.Players
{
    /// <summary>SignInAsync 결과.</summary>
    public readonly struct SignInResult<TPlayer> where TPlayer : Player
    {
        /// <summary>바인딩된 Player (신규 로드 또는 기존 재바인딩).</summary>
        public TPlayer Player { get; }

        /// <summary>true면 기존 Player 재사용(유예 재바인딩 또는 중복 로그인 이전).</summary>
        public bool IsReconnect { get; }

        public SignInResult(TPlayer player, bool isReconnect)
        {
            Player = player;
            IsReconnect = isReconnect;
        }
    }
}
```

`PlayersOptions.cs`:

```csharp
using System;

namespace Bun3.Server.Players
{
    /// <summary>같은 계정이 접속 중일 때 새 로그인의 처리.</summary>
    public enum DuplicateLoginPolicy
    {
        /// <summary>기존 연결을 킥하고 새 세션에 재바인딩 (기본).</summary>
        NewWins,

        /// <summary>새 로그인을 거부 — SignInAsync가 DuplicateLoginException을 던진다.</summary>
        RejectNew,
    }

    public sealed class PlayersOptions
    {
        /// <summary>연결이 끊긴 Player를 메모리에 유지하는 재접속 유예. Zero면 즉시 은퇴.</summary>
        public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(60);

        public DuplicateLoginPolicy DuplicatePolicy { get; set; } = DuplicateLoginPolicy.NewWins;
    }
}
```

`DuplicateLoginException.cs`:

```csharp
using System;

namespace Bun3.Server.Players
{
    /// <summary>RejectNew 정책에서 이미 접속 중인 계정으로 SignInAsync 시 던져진다.
    /// 게임 로그인 핸들러가 잡아 게임 상태코드로 변환하는 것을 권장.</summary>
    public sealed class DuplicateLoginException : Exception
    {
        public string AccountKey { get; }

        public DuplicateLoginException(string accountKey)
            : base($"계정 {accountKey}은(는) 이미 접속 중이다 (RejectNew 정책).")
        {
            AccountKey = accountKey;
        }
    }
}
```

`PlayersConfig.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Bun3.Server.Rpc;
using Google.Protobuf;

namespace Bun3.Server.Players
{
    /// <summary>RpcConfig 래퍼 — 미인증 세션에도 허용할 요청(로그인 등)을 함께 기록한다.</summary>
    public sealed class PlayersConfig<TSession> where TSession : Session
    {
        /// <summary>내부 Rpc 등록표. RpcServer 생성 시 이걸 넘긴다.</summary>
        public RpcConfig<TSession> Rpc { get; } = new RpcConfig<TSession>();

        internal HashSet<Type> UnauthenticatedTypes { get; } = new HashSet<Type>();

        /// <summary>인증된 세션만 접근 가능한 일반 요청 등록.</summary>
        public void OnRequest<TReq, TRes>(Func<TSession, TReq, ValueTask<Reply<TRes>>> handler)
            where TReq : class, IMessage<TReq>
            where TRes : class, IMessage<TRes>
            => Rpc.OnRequest(handler);

        /// <summary>미인증 세션에도 허용되는 요청 등록 (로그인 등).</summary>
        public void OnRequestUnauthenticated<TReq, TRes>(Func<TSession, TReq, ValueTask<Reply<TRes>>> handler)
            where TReq : class, IMessage<TReq>
            where TRes : class, IMessage<TRes>
        {
            Rpc.OnRequest(handler);
            UnauthenticatedTypes.Add(typeof(TReq));
        }
    }
}
```

`PlayerSession.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;

namespace Bun3.Server.Players
{
    /// <summary>
    /// Player 수명주기가 붙은 세션 베이스. 반드시 PlayerRegistry.Wrap을 거친 팩토리로
    /// 생성해야 한다(레지스트리·허용 목록 부착).
    /// </summary>
    public abstract class PlayerSession<TPlayer> : RpcSession where TPlayer : Player
    {
        private PlayerRegistry<TPlayer>? _registry;
        private HashSet<Type>? _unauthenticatedTypes;

        protected PlayerSession(IConnection connection) : base(connection) { }

        /// <summary>인증 후 non-null. 미인증 요청은 게이트가 차단하므로 핸들러에선 null 아님.</summary>
        public TPlayer? Player { get; private set; }

        public bool IsAuthenticated => Player != null;

        /// <summary>
        /// 자격증명 검증(게임 몫) 후 호출하는 프레임워크 진입점. 신규 로드/유예 재바인딩/
        /// 중복 로그인 이전을 처리한다. RejectNew 정책에서 이미 접속 중이면
        /// DuplicateLoginException, 같은 세션 이중 호출이면 InvalidOperationException.
        /// </summary>
        public ValueTask<SignInResult<TPlayer>> SignInAsync(string accountKey) =>
            RequireRegistry().SignInAsync(this, accountKey);

        /// <summary>세션 종료 훅 재노출 (detach 처리 후 호출됨).</summary>
        protected virtual ValueTask OnPlayerSessionClosedAsync(Exception? error) => default;

        protected internal sealed override int OnGateRequest(Type requestType) =>
            Player != null || (_unauthenticatedTypes != null && _unauthenticatedTypes.Contains(requestType))
                ? RpcStatus.Ok
                : RpcStatus.Unauthenticated;

        protected sealed override async ValueTask OnSessionClosedAsync(Exception? error)
        {
            var registry = _registry;
            if (registry != null)
            {
                await registry.HandleSessionClosedAsync(this).ConfigureAwait(false);
            }

            await OnPlayerSessionClosedAsync(error).ConfigureAwait(false);
        }

        internal void AttachPlayers(PlayerRegistry<TPlayer> registry, HashSet<Type> unauthenticatedTypes)
        {
            _registry = registry;
            _unauthenticatedTypes = unauthenticatedTypes;
        }

        internal void SetPlayer(TPlayer? player) => Player = player;

        private PlayerRegistry<TPlayer> RequireRegistry() =>
            _registry ?? throw new InvalidOperationException(
                "레지스트리 미부착 — PlayerSession은 PlayerRegistry.Wrap을 거친 팩토리로 생성해야 한다.");
    }
}
```

`PlayerRegistry.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Players
{
    /// <summary>
    /// accountKey → Player 레지스트리. 프로세스 내 메모리 전제(다중 서버 스케일아웃은
    /// 별도 설계). 계정 키 단위 직렬화는 스트라이프 락 256개로 수행한다.
    /// </summary>
    public sealed class PlayerRegistry<TPlayer> where TPlayer : Player
    {
        private const int StripeCount = 256;

        private sealed class Entry
        {
            public readonly TPlayer Player;
            public PlayerSession<TPlayer>? Session;
            public long DetachedAtTicksUtc;   // 0 = 접속 중

            public Entry(TPlayer player) => Player = player;
        }

        private readonly Func<string, ValueTask<TPlayer>> _loader;
        private readonly PlayersOptions _options;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, Entry> _entries =
            new ConcurrentDictionary<string, Entry>();
        private readonly SemaphoreSlim[] _stripes;
        private readonly CancellationTokenSource _sweepCts = new CancellationTokenSource();

        public PlayerRegistry(
            Func<string, ValueTask<TPlayer>> loader,
            PlayersOptions? options = null,
            ILogger? logger = null)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _options = options ?? new PlayersOptions();
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
            _stripes = new SemaphoreSlim[StripeCount];
            for (var i = 0; i < StripeCount; i++)
            {
                _stripes[i] = new SemaphoreSlim(1, 1);
            }

            if (_options.GracePeriod > TimeSpan.Zero)
            {
                _ = RunSweepAsync(_sweepCts.Token);
            }
        }

        /// <summary>현재 레지스트리의 Player 스냅샷 (브로드캐스트용).</summary>
        public IReadOnlyCollection<TPlayer> Players => _entries.Values.Select(e => e.Player).ToArray();

        /// <summary>accountKey로 조회. 없으면 null.</summary>
        public TPlayer? TryGet(string accountKey) =>
            _entries.TryGetValue(accountKey, out var entry) ? entry.Player : null;

        /// <summary>세션 팩토리를 감싸 레지스트리·허용 목록을 부착한다. Players 사용의 필수 경로.</summary>
        public Func<IConnection, TSession> Wrap<TSession>(
            PlayersConfig<TSession> config, Func<IConnection, TSession> factory)
            where TSession : PlayerSession<TPlayer>
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            return connection =>
            {
                var session = factory(connection);
                session.AttachPlayers(this, config.UnauthenticatedTypes);
                return session;
            };
        }

        internal async ValueTask<SignInResult<TPlayer>> SignInAsync(
            PlayerSession<TPlayer> session, string accountKey)
        {
            if (string.IsNullOrEmpty(accountKey))
            {
                throw new ArgumentException("accountKey가 비어 있다.", nameof(accountKey));
            }

            if (session.Player != null)
            {
                throw new InvalidOperationException("이미 인증된 세션에서 SignInAsync를 재호출했다.");
            }

            PlayerSession<TPlayer>? kickAfterRelease = null;
            var stripe = GetStripe(accountKey);
            await stripe.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_entries.TryGetValue(accountKey, out var entry))
                {
                    if (entry.Session != null && _options.DuplicatePolicy == DuplicateLoginPolicy.RejectNew)
                    {
                        throw new DuplicateLoginException(accountKey);
                    }

                    kickAfterRelease = entry.Session;   // NewWins: 락 해제 후 킥 (재진입 교착 방지)
                    entry.DetachedAtTicksUtc = 0;
                    Attach(entry, session);
                    await SafeHookAsync(() => entry.Player.OnAttachedAsync(true), "OnAttachedAsync").ConfigureAwait(false);
                    return new SignInResult<TPlayer>(entry.Player, true);
                }

                // ponytail: 스트라이프 락 안 DB 로드 — 같은 스트라이프의 다른 키가 로드 시간만큼
                // 대기한다(256 스트라이프라 희박). 병목이 측정되면 키별 락 승격.
                var player = await _loader(accountKey).ConfigureAwait(false);
                player.AccountKey = accountKey;
                var created = new Entry(player);
                _entries[accountKey] = created;
                Attach(created, session);
                await SafeHookAsync(() => player.OnAttachedAsync(false), "OnAttachedAsync").ConfigureAwait(false);
                return new SignInResult<TPlayer>(player, false);
            }
            finally
            {
                stripe.Release();
                kickAfterRelease?.Kick();
            }
        }

        internal async ValueTask HandleSessionClosedAsync(PlayerSession<TPlayer> session)
        {
            var player = session.Player;
            if (player == null)
            {
                return;   // 미인증 세션
            }

            var accountKey = player.AccountKey;
            var stripe = GetStripe(accountKey);
            await stripe.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_entries.TryGetValue(accountKey, out var entry)
                    || !ReferenceEquals(entry.Session, session))
                {
                    return;   // 이미 다른 세션으로 재바인딩(중복 로그인)되었거나 은퇴함
                }

                entry.Session = null;
                player.CurrentSession = null;
                await SafeHookAsync(() => player.OnDetachedAsync(), "OnDetachedAsync").ConfigureAwait(false);

                if (_options.GracePeriod <= TimeSpan.Zero)
                {
                    _entries.TryRemove(accountKey, out _);
                    await SafeHookAsync(() => player.OnRetiredAsync(), "OnRetiredAsync").ConfigureAwait(false);
                }
                else
                {
                    entry.DetachedAtTicksUtc = DateTime.UtcNow.Ticks;
                }
            }
            finally
            {
                stripe.Release();
            }
        }

        /// <summary>전 Player 은퇴(저장 플러시) — 서버 정지 후 호출. 스윕도 함께 멈춘다.</summary>
        public async ValueTask RetireAllAsync()
        {
            _sweepCts.Cancel();
            foreach (var accountKey in _entries.Keys.ToArray())
            {
                PlayerSession<TPlayer>? toKick = null;
                var stripe = GetStripe(accountKey);
                await stripe.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_entries.TryRemove(accountKey, out var entry))
                    {
                        toKick = entry.Session;
                        entry.Player.CurrentSession = null;
                        await SafeHookAsync(() => entry.Player.OnRetiredAsync(), "OnRetiredAsync").ConfigureAwait(false);
                    }
                }
                finally
                {
                    stripe.Release();
                    toKick?.Kick();
                }
            }
        }

        private void Attach(Entry entry, PlayerSession<TPlayer> session)
        {
            entry.Session = session;
            entry.Player.CurrentSession = session;
            session.SetPlayer(entry.Player);
        }

        private SemaphoreSlim GetStripe(string accountKey) =>
            _stripes[(accountKey.GetHashCode() & int.MaxValue) % StripeCount];

        private async ValueTask SafeHookAsync(Func<ValueTask> hook, string name)
        {
            try
            {
                await hook().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Player 훅 {Hook} 예외", name);
            }
        }

        private async Task RunSweepAsync(CancellationToken ct)
        {
            var half = TimeSpan.FromTicks(_options.GracePeriod.Ticks / 2);
            var floor = TimeSpan.FromMilliseconds(50);
            var ceiling = TimeSpan.FromSeconds(15);
            var interval = half < floor ? floor : (half > ceiling ? ceiling : half);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    var cutoff = DateTime.UtcNow.Ticks - _options.GracePeriod.Ticks;
                    foreach (var pair in _entries)
                    {
                        var detachedAt = Volatile.Read(ref pair.Value.DetachedAtTicksUtc);
                        if (detachedAt == 0 || detachedAt > cutoff)
                        {
                            continue;
                        }

                        await RetireIfStillExpiredAsync(pair.Key, cutoff).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // RetireAll/종료로 인한 정상 취소
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "유예 스윕 루프 예외 — 스윕 중단");
            }
        }

        private async ValueTask RetireIfStillExpiredAsync(string accountKey, long cutoff)
        {
            var stripe = GetStripe(accountKey);
            await stripe.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_entries.TryGetValue(accountKey, out var entry)
                    && entry.Session == null
                    && entry.DetachedAtTicksUtc != 0
                    && entry.DetachedAtTicksUtc <= cutoff)
                {
                    _entries.TryRemove(accountKey, out _);
                    await SafeHookAsync(() => entry.Player.OnRetiredAsync(), "OnRetiredAsync").ConfigureAwait(false);
                }
            }
            finally
            {
                stripe.Release();
            }
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: 필터 → 11/11. 전체 → 99/99. `dotnet build Bun3.sln` 0 경고/0 오류.

- [ ] **Step 6: Commit**

```
git add server/ Bun3.sln
git commit -m "✨ Add Bun3.Server.Players (registry state machine, player session, gate)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: 호스팅 통합 — AddPlayerServer

**Files:**
- Create: `server/src/Bun3.Server.Hosting/PlayersServiceCollectionExtensions.cs`
- Modify: `server/src/Bun3.Server.Hosting/Bun3.Server.Hosting.csproj` (Players 참조 추가)
- Test: `server/tests/Bun3.Server.Tests/PlayersHostingTests.cs`

**Interfaces:**
- Consumes: Task 2 전부, 기존 `ServerOptions`/`TcpTransportListener` 패턴, `ServerServiceCollectionExtensions.ResolveLogger`(internal static — 동일 어셈블리)
- Produces: `AddPlayerServer<TSession, TPlayer, TRequest, TResponse, TUpdate>(this IServiceCollection, Func<IServiceProvider, string, ValueTask<TPlayer>> loader, Action<PlayersConfig<TSession>> configure, Action<ServerOptions>? serverOptions = null, Action<PlayersOptions>? playersOptions = null)` — `PlayerRegistry<TPlayer>` DI 싱글턴(테스트가 resolve), 정지 시 서버 drain 후 `RetireAllAsync` 자동. TSession은 IConnection 받는 public ctor(나머지 DI).

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/PlayersHostingTests.cs`:

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Hosting;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayersHostingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public sealed class HostPlayer : Player
    {
        public long Gold = 100;
        public readonly TaskCompletionSource<bool> Retired = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected internal override ValueTask OnRetiredAsync()
        {
            Retired.TrySetResult(true);
            return default;
        }
    }

    public sealed class HostSession : PlayerSession<HostPlayer>
    {
        public HostSession(IConnection connection) : base(connection) { }
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddPlayerServer<HostSession, HostPlayer, PlayersRequest, PlayersResponse, PlayersUpdate>(
            loader: (sp, key) => new ValueTask<HostPlayer>(new HostPlayer()),
            configure: players =>
            {
                players.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
                {
                    var result = await s.SignInAsync($"guest:{req.DeviceId}");
                    return new LoginResponse { Gold = result.Player.Gold, IsReconnect = result.IsReconnect };
                });
                players.OnRequest<AddGoldRequest, AddGoldResponse>((s, req) =>
                {
                    s.Player!.Gold += req.Amount;
                    return new ValueTask<Reply<AddGoldResponse>>(new AddGoldResponse { Gold = s.Player.Gold });
                });
                players.OnRequest<GetGoldRequest, GetGoldResponse>((s, req) =>
                    new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse { Gold = s.Player!.Gold }));
            },
            serverOptions: options => options.Port = 0);
        return builder.Build();
    }

    private static ValueTask<RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>> ConnectAsync(IHost host)
    {
        var port = host.Services.GetRequiredService<TcpTransportListener>().BoundPort!.Value;
        var connector = new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = port });
        return RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(connector);
    }

    [Test]
    public async Task Host_boots_gates_then_serves_login_roundtrip()
    {
        using var host = BuildHost();
        await host.StartAsync();
        try
        {
            var client = await ConnectAsync(host);

            var gated = await client.RequestAsync<GetGoldResponse>(new GetGoldRequest()).AsTask().WaitAsync(Timeout);
            Assert.That(gated.Status, Is.EqualTo(RpcStatus.Unauthenticated));

            var login = await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "h" })
                .AsTask().WaitAsync(Timeout);
            Assert.That(login.IsOk, Is.True);
            Assert.That(login.Value!.Gold, Is.EqualTo(100));

            var gold = await client.RequestAsync<GetGoldResponse>(new GetGoldRequest()).AsTask().WaitAsync(Timeout);
            Assert.That(gold.Value!.Gold, Is.EqualTo(100));
            client.Close();
        }
        finally
        {
            await host.StopAsync().WaitAsync(Timeout);
        }
    }

    [Test]
    public async Task Host_stop_retires_all_players()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = await ConnectAsync(host);
        var login = await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "h" })
            .AsTask().WaitAsync(Timeout);
        Assert.That(login.IsOk, Is.True);
        var registry = host.Services.GetRequiredService<PlayerRegistry<HostPlayer>>();
        var player = registry.TryGet("guest:h")!;

        await host.StopAsync().WaitAsync(Timeout);

        await player.Retired.Task.WaitAsync(Timeout);
        Assert.That(registry.Players, Is.Empty);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~PlayersHostingTests"`
Expected: 컴파일 에러 — `AddPlayerServer` 미정의

- [ ] **Step 3: 구현**

Hosting csproj ProjectReference 추가: `<ProjectReference Include="..\Bun3.Server.Players\Bun3.Server.Players.csproj" />`

`server/src/Bun3.Server.Hosting/PlayersServiceCollectionExtensions.cs`:

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Transport.Tcp;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

/// <summary>Player 수명주기가 붙은 Rpc 서버를 Generic Host에 등록하는 확장.</summary>
public static class PlayersServiceCollectionExtensions
{
    /// <summary>
    /// Players + Rpc 서버(TCP)를 등록한다. 정지 시 서버 drain 후 전 Player를
    /// 은퇴(RetireAllAsync — 저장 플러시)시킨다. TSession은 IConnection을 받는
    /// public 생성자가 필요하며 나머지 인자는 DI로 주입된다. 호스트당 1회만 호출.
    /// </summary>
    public static IServiceCollection AddPlayerServer<TSession, TPlayer, TRequest, TResponse, TUpdate>(
        this IServiceCollection services,
        Func<IServiceProvider, string, ValueTask<TPlayer>> loader,
        Action<PlayersConfig<TSession>> configure,
        Action<ServerOptions>? serverOptions = null,
        Action<PlayersOptions>? playersOptions = null)
        where TSession : PlayerSession<TPlayer>
        where TPlayer : Player
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(configure);

        var optionsBuilder = services.AddOptions<ServerOptions>()
            .BindConfiguration(ServerOptions.SectionName);
        if (serverOptions != null)
        {
            optionsBuilder.Configure(serverOptions);
        }

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServerOptions>>().Value;
            return new TcpTransportListener(
                new TcpTransportOptions
                {
                    Port = options.Port,
                    MaxPacketSize = options.MaxPacketSize,
                    Backlog = options.Backlog,
                },
                ServerServiceCollectionExtensions.ResolveLogger(sp));
        });

        services.AddSingleton(sp =>
        {
            var effectivePlayersOptions = new PlayersOptions();
            playersOptions?.Invoke(effectivePlayersOptions);
            return new PlayerRegistry<TPlayer>(
                key => loader(sp, key),
                effectivePlayersOptions,
                ServerServiceCollectionExtensions.ResolveLogger(sp));
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServerOptions>>().Value;
            var config = new PlayersConfig<TSession>();
            configure(config);

            TSession Factory(IConnection connection) =>
                ActivatorUtilities.CreateInstance<TSession>(sp, connection);

            return new RpcServer<TSession, TRequest, TResponse, TUpdate>(
                sp.GetRequiredService<TcpTransportListener>(),
                sp.GetRequiredService<PlayerRegistry<TPlayer>>().Wrap(config, Factory),
                config.Rpc,
                new RpcServerOptions { MaxQueuedPackets = options.MaxQueuedPacketsPerSession },
                ServerServiceCollectionExtensions.ResolveLogger(sp));
        });

        services.AddHostedService(sp => new PlayersLifetimeService<TSession, TPlayer, TRequest, TResponse, TUpdate>(
            sp.GetRequiredService<RpcServer<TSession, TRequest, TResponse, TUpdate>>(),
            sp.GetRequiredService<PlayerRegistry<TPlayer>>(),
            sp.GetRequiredService<IOptions<ServerOptions>>()));

        return services;
    }
}

/// <summary>서버 수명 + 정지 시 Player 전원 은퇴. 닫힌 제네릭이 서버마다 달라
/// 중복 등록이 TryAddEnumerable에 조용히 떨어지지 않는다.</summary>
internal sealed class PlayersLifetimeService<TSession, TPlayer, TRequest, TResponse, TUpdate> : IHostedService
    where TSession : PlayerSession<TPlayer>
    where TPlayer : Player
    where TRequest : class, IMessage<TRequest>, new()
    where TResponse : class, IMessage<TResponse>, new()
    where TUpdate : class, IMessage<TUpdate>, new()
{
    private readonly RpcServer<TSession, TRequest, TResponse, TUpdate> _server;
    private readonly PlayerRegistry<TPlayer> _registry;
    private readonly IOptions<ServerOptions> _options;

    public PlayersLifetimeService(
        RpcServer<TSession, TRequest, TResponse, TUpdate> server,
        PlayerRegistry<TPlayer> registry,
        IOptions<ServerOptions> options)
    {
        _server = server;
        _registry = registry;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _server.StartAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _server.StopAsync(_options.Value.DrainTimeout, cancellationToken).ConfigureAwait(false);
        await _registry.RetireAllAsync().ConfigureAwait(false);   // 세션 정리 후 저장 플러시
    }
}
```

(주의: `ResolveLogger`는 `ServerServiceCollectionExtensions`의 internal static — 접근 한정자가 private이면 internal로 승격한다. v1에서 이미 internal로 승격됨.)

- [ ] **Step 4: 테스트 통과 확인**

Run: 필터 → 2/2. 전체 → 101/101. 빌드 0 경고.

- [ ] **Step 5: Commit**

```
git add server/
git commit -m "✨ Add AddPlayerServer hosting integration with retire-on-stop" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: E2E — 게스트 로그인 수직 슬라이스 (실 TCP)

**Files:**
- Test: `server/tests/Bun3.Server.Tests/PlayersE2ETests.cs`

**Interfaces:**
- Consumes: Task 2의 Players 스택 + `RpcServer`/`RpcClient`/`TcpConnector`/`TcpTransportListener` — 신규 프로덕션 코드 없음
- Produces: 없음 (스펙 §6 수직 슬라이스 인수 검증). 실패 시 테스트를 고치지 말고 superpowers:systematic-debugging으로 원인 추적.

- [ ] **Step 1: E2E 테스트 작성**

`server/tests/Bun3.Server.Tests/PlayersE2ETests.cs`:

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayersE2ETests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class E2EPlayer : Player
    {
        public long Gold = 100;
    }

    private sealed class E2ESession : PlayerSession<E2EPlayer>
    {
        public E2ESession(IConnection connection) : base(connection) { }
    }

    [Test]
    public async Task Guest_login_vertical_slice()
    {
        var loaderCalls = 0;
        var registry = new PlayerRegistry<E2EPlayer>(key =>
        {
            Interlocked.Increment(ref loaderCalls);
            return new ValueTask<E2EPlayer>(new E2EPlayer());
        });
        var config = new PlayersConfig<E2ESession>();
        config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
        {
            var result = await s.SignInAsync($"guest:{req.DeviceId}");
            return new LoginResponse { Gold = result.Player.Gold, IsReconnect = result.IsReconnect };
        });
        config.OnRequest<AddGoldRequest, AddGoldResponse>((s, req) =>
        {
            s.Player!.Gold += req.Amount;
            return new ValueTask<Reply<AddGoldResponse>>(new AddGoldResponse { Gold = s.Player.Gold });
        });
        config.OnRequest<GetGoldRequest, GetGoldResponse>((s, req) =>
            new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse { Gold = s.Player!.Gold }));

        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
        var server = new RpcServer<E2ESession, PlayersRequest, PlayersResponse, PlayersUpdate>(
            listener, registry.Wrap(config, conn => new E2ESession(conn)), config.Rpc);
        await server.StartAsync();
        try
        {
            ValueTask<RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>> Connect() =>
                RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(
                    new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = listener.BoundPort!.Value }));

            // ⓪ 미인증 게이트
            var client1 = await Connect();
            var gated = await client1.RequestAsync<GetGoldResponse>(new GetGoldRequest()).AsTask().WaitAsync(Timeout);
            Assert.That(gated.Status, Is.EqualTo(RpcStatus.Unauthenticated));

            // ① 게스트 로그인
            var login1 = await client1.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "e2e" })
                .AsTask().WaitAsync(Timeout);
            Assert.That(login1.Value!.IsReconnect, Is.False);
            Assert.That(login1.Value.Gold, Is.EqualTo(100));

            // ② Player 상태를 쓰는 요청
            var added = await client1.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 23 })
                .AsTask().WaitAsync(Timeout);
            Assert.That(added.Value!.Gold, Is.EqualTo(123));

            // ③ 강제 절단
            client1.Close();

            // ④ 유예(기본 60초) 내 재접속 — 상태 그대로, 로더 재호출 없음
            var client2 = await Connect();
            var login2 = await client2.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "e2e" })
                .AsTask().WaitAsync(Timeout);
            Assert.That(login2.Value!.IsReconnect, Is.True);
            Assert.That(login2.Value.Gold, Is.EqualTo(123));
            Assert.That(loaderCalls, Is.EqualTo(1));

            // ⑤ 같은 계정 두 번째 클라 → 기존 클라 킥
            var client2Closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            client2.Closed += _ => client2Closed.TrySetResult(true);
            var client3 = await Connect();
            var login3 = await client3.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "e2e" })
                .AsTask().WaitAsync(Timeout);
            Assert.That(login3.Value!.IsReconnect, Is.True);
            Assert.That(login3.Value.Gold, Is.EqualTo(123));
            await client2Closed.Task.WaitAsync(Timeout);
            Assert.That(loaderCalls, Is.EqualTo(1));
            client3.Close();
        }
        finally
        {
            await server.StopAsync();
            await registry.RetireAllAsync();
        }
    }
}
```

- [ ] **Step 2: 테스트 실행 + 전체 확인**

Run: 필터 → 1/1 (안정성 3회). 전체 `dotnet test server/tests/Bun3.Server.Tests` → 102/102. `dotnet test common/tests/Bun3.Common.Tests` 초록.

- [ ] **Step 3: Commit**

```
git add server/tests/
git commit -m "✅ Add guest-login vertical-slice E2E (Players acceptance)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## 완료 기준 (스펙 §6 대응)

- [ ] Rpc 게이트 — 거부는 상태코드로(세션 유지), 핸들러 미도달, Ping 무관 (Task 1)
- [ ] SignIn 신규/유예 재바인딩/유예 만료/유예 0/중복(NewWins·RejectNew)/동시 로그인/이중 SignIn/RetireAll/PushUpdate 단위 초록 (Task 2)
- [ ] 호스팅 — 부팅·게이트·로그인 roundtrip + 정지 시 전원 은퇴 (Task 3)
- [ ] **수직 슬라이스 E2E ⓪~⑤ 초록 (Task 4) = Players 완료**
- [ ] `dotnet build Bun3.sln` 0 오류/0 경고, 전체 102/102 + common 회귀 없음

## 실행 참고

- Player 훅은 스트라이프 락 안에서 실행 — 훅에서 SignInAsync/Kick 재호출 금지(교착). 이를 어기는 테스트를 작성하지 말 것.
- 중복 로그인 킥은 반드시 스트라이프 락 해제 후(FakeTransport의 동기 OnClosed가 같은 락을 재획득하면 교착).
- 기존 game.proto에 케이스를 추가하지 말 것 — 기존 검증 테스트(FullConfig)가 깨진다. Players 테스트는 players_game.proto 전용 루트 사용.
