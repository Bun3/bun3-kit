# v2 Disconnect 사유 + 수명주기 봉인 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 절단 사유가 클라에 도달하고(중복 로그인/서버 종료/idle/게임 킥), 리뷰들이 미룬 수명주기 구멍(SignIn TOCTOU·retired·dirty 버전·dispose·StopAsync ct)을 봉인한다.

**Architecture:** control.proto(채널 0x01)에 `Disconnect{code}` 추가 — `Session.Kick(int)` 가상(Core는 개념, RpcSession이 와이어) + 프레임워크 자동 배선. `RpcClient`는 수신 코드를 기억했다가 `Closed(DisconnectInfo)`로 통지(파괴적 변경). 스펙: `docs/superpowers/specs/2026-08-07-server-disconnect-design.md`.

**Tech Stack:** 기존 스택 그대로 (ns2.1 + C#9, Google.Protobuf, NUnit 4 net10.0).

## Global Constraints

- 패키지 코드 `netstandard2.1` + `LangVersion 9.0` + 블록 네임스페이스, 모든 await `ConfigureAwait(false)`, 한국어 XML 문서, **빌드 경고 0**.
- 버전(csproj `<Version>` 갱신 필수): Core 0.2.0→**0.3.0**, Rpc 0.3.0→**0.4.0**, Players 0.2.0→**0.3.0**, Ticking 0.1.0→**0.2.0**, Hosting 0.3.0→**0.4.0**.
- 코드 대역 규약: `Disconnect.code`는 1~99 프레임워크 예약, 음수 게임 정의(`Reply.Status`와 동일). 0은 와이어에 싣지 않음(클라 전용 "미수신" 의미).
- best-effort 의미론: Disconnect 송신은 1초 상한 후 무조건 close, 송신 예외 무시, 멱등(사유 송신은 최초 1회만).
- 파괴적 변경 허용: `RpcClient.Closed`가 `Action<Exception?>` → `Action<DisconnectInfo>` (pre-1.0).
- 패킷 크기 초과는 전송 계층 절단이라 사유 전달 불가(스펙 §3 주의) — ProtocolViolation은 Rpc 판정 위반에만.
- 커밋: gitmoji + `git commit -m "<제목>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"` 이중 플래그(bash로 here-string 금지).
- 타이밍 테스트는 여유 마진(CI 스톨 대비).

---

### Task 1: Disconnect 와이어 전체 — Core 0.3.0 + Rpc 0.4.0

**Files:**
- Create: `server/src/Bun3.Server.Core/DisconnectCode.cs`
- Create: `server/src/Bun3.Server.Rpc/DisconnectInfo.cs`
- Modify: `server/src/Bun3.Server.Rpc/Protos/control.proto`
- Modify: `server/src/Bun3.Server.Core/Session.cs` (Kick(int) 가상 + 큐 초과 사유)
- Modify: `server/src/Bun3.Server.Core/ServerBase.cs:70` (drain 킥 사유)
- Modify: `server/src/Bun3.Server.Rpc/RpcSession.cs` (Kick 재정의 + idle 사유)
- Modify: `server/src/Bun3.Server.Rpc/RpcRuntime.cs:184-188` (violation 사유)
- Modify: `server/src/Bun3.Server.Rpc/RpcClient.cs` (Disconnect 수신, Closed 교체, IDisposable)
- Modify: `server/src/Bun3.Server.Core/Bun3.Server.Core.csproj` (0.3.0), `server/src/Bun3.Server.Rpc/Bun3.Server.Rpc.csproj` (0.4.0)
- Modify: `server/tests/Bun3.Server.Tests/PlayersE2ETests.cs:86-87` (Closed 시그니처 회귀 — `_` 람다라 무수정 컴파일되지만 확인)
- Test: `server/tests/Bun3.Server.Tests/DisconnectTests.cs`

**Interfaces:**
- Consumes: 기존 Session/RpcSession/RpcRuntime/RpcClient 구조(위 파일들).
- Produces (Task 2·3이 사용):
  - `Bun3.Server.Core.DisconnectCode` — `None=0, ServerShutdown=1, DuplicateLogin=2, IdleKick=3, QueueOverflow=4, ProtocolViolation=5`
  - `Session`: `public virtual void Kick(int reasonCode)`
  - `Bun3.Server.Rpc.DisconnectInfo` — `int Code`, `Exception? Error`, `bool HasReason`, ctor `(int code, Exception? error)`
  - `RpcClient`: `event Action<DisconnectInfo>? Closed`, `IDisposable`

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/DisconnectTests.cs` (SessionPostTests와 같은 실스택 하네스, `players_game.proto` 타입 재사용):

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class DisconnectTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private const int GameBanCode = -7;

    private sealed class KickSession : RpcSession
    {
        public KickSession(IConnection connection) : base(connection) { }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public RpcServer<KickSession, PlayersRequest, PlayersResponse, PlayersUpdate> Server = null!;
        public TcpTransportListener Listener = null!;
        public TaskCompletionSource<bool> BlockHandlers = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<Harness> StartAsync(
            TimeSpan? idleKick = null, int maxQueued = 256)
        {
            var h = new Harness();
            var config = new RpcConfig<KickSession>();
            // 기동 전수 검증 충족용 — GetGold는 게임 킥 트리거, 나머지는 스텁
            config.OnRequest<GetGoldRequest, GetGoldResponse>((s, req) =>
            {
                s.Kick(GameBanCode);
                s.Kick(-99);   // 이중 킥 — 사유는 최초 1회만 송신(멱등), 클라는 GameBanCode를 받아야 한다
                return new ValueTask<Reply<GetGoldResponse>>(Reply.Fail(GameBanCode));
            });
            config.OnRequest<LoginRequest, LoginResponse>((s, req) =>
                new ValueTask<Reply<LoginResponse>>(new LoginResponse { Gold = 0 }));
            config.OnRequest<AddGoldRequest, AddGoldResponse>(async (s, req) =>
            {
                await h.BlockHandlers.Task;   // 큐 초과 테스트용 블로커
                return new AddGoldResponse { Gold = 0 };
            });

            h.Listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
            h.Server = new RpcServer<KickSession, PlayersRequest, PlayersResponse, PlayersUpdate>(
                h.Listener, conn => new KickSession(conn), config,
                new RpcServerOptions { IdleKickTimeout = idleKick, MaxQueuedPackets = maxQueued });
            await h.Server.StartAsync();
            return h;
        }

        public ValueTask<RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>> ConnectAsync(
            TimeSpan? pingInterval = null) =>
            RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(
                new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = Listener.BoundPort!.Value }),
                new RpcClientOptions { PingInterval = pingInterval });

        public async ValueTask DisposeAsync()
        {
            BlockHandlers.TrySetResult(true);
            await Server.StopAsync();
        }
    }

    private static TaskCompletionSource<DisconnectInfo> Watch(
        RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate> client)
    {
        var closed = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Closed += info => closed.TrySetResult(info);
        return closed;
    }

    [Test]
    public async Task Game_kick_delivers_negative_code()
    {
        await using var h = await Harness.StartAsync();
        using var client = await h.ConnectAsync();
        var closed = Watch(client);

        try
        {
            await client.RequestAsync<GetGoldResponse>(new GetGoldRequest()).AsTask().WaitAsync(Timeout);
        }
        catch (ConnectionClosedException)
        {
            // 응답 송신과 close가 경합 — 응답을 못 받아도 무방, 사유 전달이 검증 대상
        }

        var info = await closed.Task.WaitAsync(Timeout);
        Assert.That(info.Code, Is.EqualTo(GameBanCode));
        Assert.That(info.HasReason, Is.True);
    }

    [Test]
    public async Task Voluntary_close_reports_code_zero()
    {
        await using var h = await Harness.StartAsync();
        using var client = await h.ConnectAsync();
        var closed = Watch(client);

        client.Close();

        var info = await closed.Task.WaitAsync(Timeout);
        Assert.That(info.Code, Is.EqualTo(DisconnectCode.None));
        Assert.That(info.HasReason, Is.False);
    }

    [Test]
    public async Task Idle_kick_delivers_reason()
    {
        await using var h = await Harness.StartAsync(idleKick: TimeSpan.FromMilliseconds(150));
        using var client = await h.ConnectAsync(pingInterval: null);   // 핑 끔 — idle 유도
        var closed = Watch(client);

        var info = await closed.Task.WaitAsync(Timeout);
        Assert.That(info.Code, Is.EqualTo(DisconnectCode.IdleKick));
    }

    [Test]
    public async Task Server_shutdown_delivers_reason()
    {
        var h = await Harness.StartAsync();
        using var client = await h.ConnectAsync();
        var closed = Watch(client);

        await h.Server.StopAsync();

        var info = await closed.Task.WaitAsync(Timeout);
        Assert.That(info.Code, Is.EqualTo(DisconnectCode.ServerShutdown));
    }

    [Test]
    public async Task Queue_overflow_delivers_reason()
    {
        await using var h = await Harness.StartAsync(maxQueued: 3);
        using var client = await h.ConnectAsync();
        var closed = Watch(client);

        // 블로킹 핸들러 1개 + 큐 상한 초과까지 요청 폭주 (응답은 기대하지 않음)
        var floods = new List<Task>();
        for (var i = 0; i < 10; i++)
        {
            floods.Add(client.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 1 }).AsTask());
        }

        var info = await closed.Task.WaitAsync(Timeout);
        Assert.That(info.Code, Is.EqualTo(DisconnectCode.QueueOverflow));

        h.BlockHandlers.TrySetResult(true);
        foreach (var flood in floods)
        {
            try { await flood; } catch { /* ConnectionClosed/Timeout — 관전 처리만 */ }
        }
    }

    [Test]
    public async Task Dispose_closes_and_is_idempotent()
    {
        await using var h = await Harness.StartAsync();
        var client = await h.ConnectAsync();
        var closed = Watch(client);

        client.Dispose();
        client.Dispose();   // 멱등

        var info = await closed.Task.WaitAsync(Timeout);
        Assert.That(info.Code, Is.EqualTo(DisconnectCode.None));
        Assert.That(client.IsConnected, Is.False);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~DisconnectTests"`
Expected: 컴파일 오류 (`DisconnectInfo`/`DisconnectCode` 미정의)

- [ ] **Step 3: 구현 — proto + Core**

`server/src/Bun3.Server.Rpc/Protos/control.proto` — oneof에 케이스 추가 + 메시지:

```protobuf
message Control {
  oneof body {
    Ping ping = 1;
    Pong pong = 2;
    Disconnect disconnect = 3;
  }
}
```

(파일 하단, Pong 아래에):

```protobuf
// 서버가 사유 있는 킥 직전에 best-effort로 보낸다. 클라는 절단 시 이 코드로 통지한다.
message Disconnect {
  int32 code = 1;   // 1~99 프레임워크 예약, 음수 게임 정의 (Reply.Status와 동일 규약)
}
```

`server/src/Bun3.Server.Core/DisconnectCode.cs` (신규):

```csharp
namespace Bun3.Server.Core
{
    /// <summary>절단 사유 코드 — 1~99 프레임워크 예약, 음수 게임 정의 (Reply.Status와 동일 대역 규약).
    /// Core에 두는 이유: 킥 발생 지점이 Core(큐 초과·drain)/Rpc(idle·위반)/Players(중복 로그인)에 걸쳐 있다.</summary>
    public static class DisconnectCode
    {
        /// <summary>클라 전용 의미 — Disconnect 미수신 절단(네트워크/자발적 Close). 와이어에 싣지 않는다.</summary>
        public const int None = 0;

        /// <summary>서버 정지 drain.</summary>
        public const int ServerShutdown = 1;

        /// <summary>중복 로그인(NewWins) — 다른 기기에서 로그인.</summary>
        public const int DuplicateLogin = 2;

        /// <summary>idle 타임아웃.</summary>
        public const int IdleKick = 3;

        /// <summary>세션 큐 초과 킥.</summary>
        public const int QueueOverflow = 4;

        /// <summary>Rpc 계층이 판정한 프로토콜 위반(미지 채널, 파싱 실패 등).
        /// 전송 계층 절단(패킷 크기 초과)은 사유 전달 불가.</summary>
        public const int ProtocolViolation = 5;
    }
}
```

`server/src/Bun3.Server.Core/Session.cs` — `Kick()` 아래에 가상 오버로드 추가:

```csharp
        /// <summary>사유 코드와 함께 연결을 끊는다. Core는 와이어 전달을 모르므로 기본은
        /// 사유 없는 킥과 동일 — Rpc 계층(RpcSession)이 재정의해 Disconnect를 best-effort 송신한다.</summary>
        public virtual void Kick(int reasonCode) => Kick();
```

같은 파일 `EnqueuePacket`의 오버플로 킥을 사유 있는 킥으로:

```csharp
                _logger.LogWarning(
                    "Session {SessionId}: inbox overflow (>{MaxQueuedPackets}); kicking.", Id, _maxQueuedPackets);
                Kick(DisconnectCode.QueueOverflow);
```

`server/src/Bun3.Server.Core/ServerBase.cs:70` — drain 킥에 사유:

```csharp
                entry.Session.Kick(DisconnectCode.ServerShutdown);
```

- [ ] **Step 4: 구현 — RpcSession.Kick 재정의 + idle/violation 사유**

`server/src/Bun3.Server.Rpc/RpcSession.cs` — using에 `Bun3.Server.Rpc.ControlMessages` 추가, 필드 `private int _disconnectSent;` 추가, 클래스에:

```csharp
        /// <summary>사유 코드와 함께 킥한다 — Disconnect{code}를 best-effort 송신(1초 상한,
        /// 예외 무시, 최초 1회만) 후 연결을 닫는다. 멱등.</summary>
        public override void Kick(int reasonCode)
        {
            if (Interlocked.Exchange(ref _disconnectSent, 1) != 0)
            {
                Kick();   // 사유는 이미 송신(또는 송신 중) — 닫기만 보장
                return;
            }

            _ = SendDisconnectThenCloseAsync(reasonCode);
        }

        private async Task SendDisconnectThenCloseAsync(int reasonCode)
        {
            try
            {
                var control = new Control { Disconnect = new Disconnect { Code = reasonCode } };
                var send = SendAsync(PacketWriter.Wrap(Channels.Control, control)).AsTask();
                await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
            }
            catch
            {
                // best-effort — 어차피 끊는 중
            }
            finally
            {
                Kick();
            }
        }
```

같은 파일 `RunWatchdogAsync`의 idle 킥에 사유:

```csharp
                        Kick(DisconnectCode.IdleKick);
```

(using에 `Bun3.Server.Core`는 이미 있음 — `DisconnectCode` 사용 가능.)

`server/src/Bun3.Server.Rpc/RpcRuntime.cs`의 `Violation`:

```csharp
        private void Violation(RpcSession session, string reason)
        {
            Logger.LogWarning("Session {SessionId}: 프로토콜 위반 — {Reason}; kicking.", session.Id, reason);
            session.Kick(DisconnectCode.ProtocolViolation);
        }
```

(핸들러 예외의 CloseSession 킥과 OnSessionOpenedAsync 실패 킥은 사유 없이 유지 — 스펙 §3 표에 없는 지점.)

- [ ] **Step 5: 구현 — 클라 수신 + DisconnectInfo + IDisposable**

`server/src/Bun3.Server.Rpc/DisconnectInfo.cs` (신규):

```csharp
using System;

namespace Bun3.Server.Rpc
{
    /// <summary>절단 통지 페이로드. Code 0 = Disconnect 미수신(네트워크 절단/자발적 Close) —
    /// 수신 = 의도된 킥(안내 UI), 미수신 = 사고(재접속 루트)의 분기점.</summary>
    public readonly struct DisconnectInfo
    {
        /// <summary>절단 사유 — 1~99 프레임워크(DisconnectCode), 음수 게임 정의, 0 미수신.</summary>
        public int Code { get; }

        /// <summary>전송 계층 오류(있으면).</summary>
        public Exception? Error { get; }

        /// <summary>사유가 전달되었는지 여부.</summary>
        public bool HasReason => Code != 0;

        /// <summary>통지 페이로드를 생성한다.</summary>
        public DisconnectInfo(int code, Exception? error)
        {
            Code = code;
            Error = error;
        }
    }
}
```

`server/src/Bun3.Server.Rpc/RpcClient.cs` 변경 5곳:

1. 클래스 선언에 `IDisposable` 추가:

```csharp
    public sealed class RpcClient<TRequest, TResponse, TUpdate> : IDisposable
```

2. 필드 추가 (`_closed` 옆):

```csharp
        private int _receivedDisconnectCode;
        private int _disposed;
```

3. `Closed` 이벤트 교체:

```csharp
        /// <summary>연결 종료 시 1회. Code 0 = 사유 미수신(자발적 Close/네트워크).
        /// UseSynchronizationContext 시 캡처 컨텍스트에서 호출.</summary>
        public event Action<DisconnectInfo>? Closed;
```

4. `HandleControl`의 Pong 분기 뒤에 Disconnect 분기 추가:

```csharp
            if (control.BodyCase == Control.BodyOneofCase.Pong)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Volatile.Write(ref _lastRttMs, Math.Max(0, now - control.Pong.ClientTimeUnixMs));
            }
            else if (control.BodyCase == Control.BodyOneofCase.Disconnect)
            {
                Volatile.Write(ref _receivedDisconnectCode, control.Disconnect.Code);   // 절단 시 통지에 사용
            }
            else
            {
                // 의도적 관대함: 미래 서버의 새 Control 메시지와의 전방 호환 (서버 쪽은 엄격)
                _logger.LogWarning("예상 밖 Control {Case} — 무시", control.BodyCase);
            }
```

5. `HandleClosed`의 통지를 DisconnectInfo로, `Close()` 아래 `Dispose` 추가:

```csharp
            Dispatch(() => Closed?.Invoke(new DisconnectInfo(Volatile.Read(ref _receivedDisconnectCode), error)));
```

```csharp
        /// <summary>연결을 닫고 내부 자원(핑 루프 CTS)을 정리한다. 멱등.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Close();
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
        }
```

- [ ] **Step 6: 버전 + 통과 확인 + 전체 회귀**

`Bun3.Server.Core.csproj` `<Version>` → `0.3.0`, `Bun3.Server.Rpc.csproj` → `0.4.0`.

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~DisconnectTests"`
Expected: PASS (신규 6개)

Run: `dotnet test server/tests/Bun3.Server.Tests`
Expected: 전체 PASS (기존 161 + 6 = 167) — `Closed` 시그니처 변경의 기존 사용처(PlayersE2ETests의 `_` 람다)가 그대로 컴파일되는지 이 단계에서 확인

Run: `dotnet build Bun3.sln --no-incremental` → 경고 0

- [ ] **Step 7: 커밋**

```powershell
git add server/src/Bun3.Server.Core server/src/Bun3.Server.Rpc server/tests/Bun3.Server.Tests/DisconnectTests.cs
git commit -m "✨ Deliver disconnect reasons to clients (control-channel Disconnect)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Players 봉인 + NewWins 사유 — Players 0.3.0

**Files:**
- Modify: `server/src/Bun3.Server.Players/PlayerSession.cs` (SignIn CAS 가드)
- Modify: `server/src/Bun3.Server.Players/PlayerRegistry.cs` (NewWins 사유, `_retired`, IDisposable)
- Modify: `server/src/Bun3.Server.Players/Player.cs` (dirty 버전 카운터)
- Modify: `server/src/Bun3.Server.Players/PlayerTicker.cs:50` (`captured` 별칭 제거)
- Modify: `server/src/Bun3.Server.Players/Bun3.Server.Players.csproj` (0.3.0)
- Modify: `server/tests/Bun3.Server.Tests/PlayersE2ETests.cs:85-94` (⑤단계에 사유 검증 추가)
- Test: `server/tests/Bun3.Server.Tests/LifecycleSealTests.cs`

**Interfaces:**
- Consumes (Task 1): `DisconnectCode.DuplicateLogin`, `DisconnectInfo`, `Session.Kick(int)`
- Produces: `PlayerRegistry<TPlayer> : IDisposable` (`Dispose()` — 스윕 정지, 은퇴 아님)

- [ ] **Step 1: 기존 E2E ⑤단계에 사유 검증 추가**

`server/tests/Bun3.Server.Tests/PlayersE2ETests.cs` — 파일 상단 using에 `using Bun3.Server.Core;` 추가. ⑤단계의:

```csharp
            var client2Closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            client2.Closed += _ => client2Closed.TrySetResult(true);
```

를 다음으로 교체:

```csharp
            var client2Closed = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            client2.Closed += info => client2Closed.TrySetResult(info);
```

그리고 `await client2Closed.Task.WaitAsync(Timeout);` 를:

```csharp
            var kicked = await client2Closed.Task.WaitAsync(Timeout);
            Assert.That(kicked.Code, Is.EqualTo(DisconnectCode.DuplicateLogin));   // "다른 기기에서 로그인" 사유 전달
```

- [ ] **Step 2: 봉인 테스트 작성**

`server/tests/Bun3.Server.Tests/LifecycleSealTests.cs` (PlayerTickingTests와 같은 하네스 골격):

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Ticking;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class LifecycleSealTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class SealPlayer : Player
    {
        public int SaveCalls;
        public volatile bool MarkDirtyDuringNextSave;
        public int RetireCalls;

        protected override ValueTask OnSaveAsync()
        {
            Interlocked.Increment(ref SaveCalls);
            if (MarkDirtyDuringNextSave)
            {
                MarkDirtyDuringNextSave = false;
                MarkDirty();   // 저장 "중" 도착한 변경 — 버전 카운터가 없으면 클리어에 지워진다
            }
            return default;
        }

        protected override ValueTask OnRetiredAsync()
        {
            Interlocked.Increment(ref RetireCalls);
            return default;
        }
    }

    private sealed class SealSession : PlayerSession<SealPlayer>
    {
        public SealSession(IConnection connection) : base(connection) { }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public PlayerRegistry<SealPlayer> Registry = null!;
        public RpcServer<SealSession, PlayersRequest, PlayersResponse, PlayersUpdate> Server = null!;
        public TcpTransportListener Listener = null!;
        public TickLoop? Loop;

        public static async Task<Harness> StartAsync(
            PlayersOptions? playersOptions = null, bool withTicker = false)
        {
            var h = new Harness();
            var options = playersOptions ?? new PlayersOptions();
            h.Registry = new PlayerRegistry<SealPlayer>(
                _ => new ValueTask<SealPlayer>(new SealPlayer()), options);

            var config = new PlayersConfig<SealSession>();
            config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
            {
                var result = await s.SignInAsync($"guest:{req.DeviceId}");
                return new LoginResponse { Gold = 0, IsReconnect = result.IsReconnect };
            });
            config.OnRequest<AddGoldRequest, AddGoldResponse>((s, req) =>
            {
                s.Player!.MarkDirty();
                return new ValueTask<Reply<AddGoldResponse>>(new AddGoldResponse { Gold = 0 });
            });
            config.OnRequest<GetGoldRequest, GetGoldResponse>((s, req) =>
                new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse { Gold = 0 }));

            h.Listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
            h.Server = new RpcServer<SealSession, PlayersRequest, PlayersResponse, PlayersUpdate>(
                h.Listener, h.Registry.Wrap(config, conn => new SealSession(conn)), config.Rpc);
            await h.Server.StartAsync();

            if (withTicker)
            {
                h.Loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(20) });
                new PlayerTicker<SealPlayer>(h.Registry, options).Register(h.Loop);
                h.Loop.Start();
            }
            return h;
        }

        public async Task<(RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate> Client, SealSession Session)>
            ConnectAsync()
        {
            var client = await RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(
                new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = Listener.BoundPort!.Value }));
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                foreach (var session in Server.Sessions)
                {
                    return (client, session);
                }
                await Task.Delay(10);
            }
            throw new TimeoutException("세션 미생성");
        }

        public async ValueTask DisposeAsync()
        {
            if (Loop != null) await Loop.StopAsync();
            await Server.StopAsync();
            await Registry.RetireAllAsync();
            Registry.Dispose();
        }
    }

    [Test]
    public async Task Concurrent_signin_exactly_one_wins()
    {
        await using var h = await Harness.StartAsync();
        var (client, session) = await h.ConnectAsync();

        // 핸들러 밖(두 병렬 Task)에서 같은 세션에 동시 SignIn — CAS 가드가 정확히 하나만 통과시킨다
        var first = Task.Run(() => session.SignInAsync("guest:race").AsTask());
        var second = Task.Run(() => session.SignInAsync("guest:race").AsTask());

        var results = await Task.WhenAll(WrapAsync(first), WrapAsync(second));
        Assert.That(results.Count(r => r == null), Is.EqualTo(1), "정확히 하나 성공");
        Assert.That(results.Count(r => r is InvalidOperationException), Is.EqualTo(1), "다른 하나는 InvalidOperationException");
        client.Close();

        static async Task<Exception?> WrapAsync(Task task)
        {
            try { await task; return null; }
            catch (Exception ex) { return ex; }
        }
    }

    [Test]
    public async Task Retired_registry_rejects_late_signin()
    {
        await using var h = await Harness.StartAsync();
        var (client, _) = await h.ConnectAsync();

        await h.Registry.RetireAllAsync();

        // 은퇴 후 로그인 시도 — 핸들러의 SignInAsync가 던져서 status 2로 표면화
        var reply = await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "late" })
            .AsTask().WaitAsync(Timeout);
        Assert.That(reply.Status, Is.EqualTo(RpcStatus.HandlerException));
        Assert.That(h.Registry.TryGet("guest:late"), Is.Null, "은퇴한 레지스트리에 새 entry가 생기면 안 된다");
        client.Close();
    }

    [Test]
    public async Task MarkDirty_during_save_survives_to_next_sweep()
    {
        await using var h = await Harness.StartAsync(new PlayersOptions
        {
            PlayerTickInterval = TimeSpan.FromMilliseconds(40),
            SaveInterval = TimeSpan.FromMilliseconds(120),
        }, withTicker: true);
        var (client, _) = await h.ConnectAsync();
        var login = await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "d" })
            .AsTask().WaitAsync(Timeout);
        Assert.That(login.IsOk, Is.True);
        var player = h.Registry.TryGet("guest:d")!;

        player.MarkDirtyDuringNextSave = true;
        await client.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 1 }).AsTask().WaitAsync(Timeout);

        var deadline = DateTime.UtcNow + Timeout;
        while (player.SaveCalls < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        // 1차 저장 "중" 들어온 MarkDirty가 살아남아 2차 저장이 일어났다 (버전 카운터 증명)
        Assert.That(player.SaveCalls, Is.GreaterThanOrEqualTo(2));
        Assert.That(player.IsDirty, Is.False, "2차 저장 후 클린");
        client.Close();
    }

    [Test]
    public async Task Dispose_stops_sweep_and_is_idempotent()
    {
        await using var h = await Harness.StartAsync(new PlayersOptions
        {
            GracePeriod = TimeSpan.FromMilliseconds(100),
        });
        var (client, _) = await h.ConnectAsync();
        var login = await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "sweep" })
            .AsTask().WaitAsync(Timeout);
        Assert.That(login.IsOk, Is.True);
        var player = h.Registry.TryGet("guest:sweep")!;

        h.Registry.Dispose();
        h.Registry.Dispose();   // 멱등

        client.Close();          // detach — 유예 진입
        await Task.Delay(500);   // 유예(100ms)의 5배 대기

        // Dispose는 은퇴가 아니다 — 스윕이 멈췄으므로 유예가 만료돼도 은퇴가 일어나지 않는다
        Assert.That(player.RetireCalls, Is.Zero);
        Assert.That(h.Registry.TryGet("guest:sweep"), Is.Not.Null);
    }
}
```

- [ ] **Step 3: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~LifecycleSealTests|FullyQualifiedName~PlayersE2ETests"`
Expected: 컴파일 오류 (`Registry.Dispose` 미정의) 또는 신규 어서션 실패

- [ ] **Step 4: 구현**

`server/src/Bun3.Server.Players/PlayerSession.cs` — using에 `System.Threading` 추가, 필드 `private int _signingIn;` 추가, `SignInAsync`를 교체:

```csharp
        /// <summary>
        /// 자격증명 검증(게임 몫) 후 호출하는 프레임워크 진입점. 신규 로드/유예 재바인딩/
        /// 중복 로그인 이전을 처리한다. 같은 세션의 동시·이중 호출은 원자적으로 거부되어
        /// InvalidOperationException, RejectNew 정책에서 이미 접속 중이면 DuplicateLoginException.
        /// 실패(예외) 시 가드가 풀려 재시도할 수 있다.
        /// </summary>
        public async ValueTask<SignInResult<TPlayer>> SignInAsync(string accountKey)
        {
            if (Interlocked.CompareExchange(ref _signingIn, 1, 0) != 0)
            {
                throw new InvalidOperationException("이미 인증되었거나 SignInAsync가 진행 중인 세션이다.");
            }

            try
            {
                return await RequireRegistry().SignInAsync(this, accountKey).ConfigureAwait(false);
            }
            catch
            {
                Interlocked.Exchange(ref _signingIn, 0);
                throw;
            }
        }
```

`server/src/Bun3.Server.Players/PlayerRegistry.cs` 변경 4곳:

1. 클래스 선언에 `IDisposable` (using `System` 있음):

```csharp
    public sealed class PlayerRegistry<TPlayer> : IDisposable where TPlayer : Player
```

2. 필드 추가:

```csharp
        private volatile bool _retired;
        private int _disposed;
```

3. `SignInAsync`(internal)의 `session.Player != null` 검사 아래에:

```csharp
            if (_retired)
            {
                throw new InvalidOperationException("레지스트리가 은퇴됨(서버 종료 중) — SignIn 불가.");
            }
```

`RetireAllAsync` 첫 줄(`_sweepCts.Cancel()` 앞)에 `_retired = true;` 추가. NewWins 킥을 사유 있는 킥으로 — `finally`의:

```csharp
                kickAfterRelease?.Kick(DisconnectCode.DuplicateLogin);
```

(using에 `Bun3.Server.Core` 추가.)

4. 클래스 끝에 Dispose:

```csharp
        /// <summary>유예 스윕을 멈추고 내부 자원을 정리한다. 멱등.
        /// **은퇴가 아니다** — 저장 훅을 부르지 않는다. 우아한 종료는 RetireAllAsync를 먼저 호출할 것.
        /// (Dispose는 테스트/비정상 정리용)</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _sweepCts.Cancel();
            _sweepCts.Dispose();
        }
```

그리고 `RetireAllAsync`의 `_sweepCts.Cancel()`을 Dispose 경합에서 안전하게:

```csharp
            _retired = true;
            if (Volatile.Read(ref _disposed) == 0)
            {
                _sweepCts.Cancel();
            }
```

`server/src/Bun3.Server.Players/Player.cs` — dirty 부울을 버전 카운터로 교체 (`private bool _dirty;` 삭제, `using System.Threading;` 추가):

```csharp
        private int _dirtyVersion;
        private int _savedVersion;

        /// <summary>상태 변경 후 호출 — 다음 저장 주기의 대상으로 표시한다.
        /// 저장이 진행 중일 때 호출해도 그 변경은 다음 저장 대상으로 살아남는다(버전 카운터).</summary>
        public void MarkDirty() => Interlocked.Increment(ref _dirtyVersion);

        /// <summary>저장 대기 중인 변경이 있는지 여부.</summary>
        public bool IsDirty => Volatile.Read(ref _dirtyVersion) != Volatile.Read(ref _savedVersion);

        internal async ValueTask TrySaveAsync(ILogger logger)
        {
            var capturedVersion = Volatile.Read(ref _dirtyVersion);
            try
            {
                await OnSaveAsync().ConfigureAwait(false);
                Volatile.Write(ref _savedVersion, capturedVersion);   // 저장 중 MarkDirty는 버전이 앞서 dirty 유지
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OnSaveAsync 실패 — dirty 유지, 다음 주기에 재시도 (Player {AccountKey})", AccountKey);
            }
        }
```

`server/src/Bun3.Server.Players/PlayerTicker.cs` — `var captured = player;` 줄 삭제 후 람다 안의 `captured`를 전부 `player`로(C#5+ foreach 변수는 반복마다 새 스코프라 캡처 안전).

`Bun3.Server.Players.csproj` `<Version>` → `0.3.0`.

- [ ] **Step 5: 통과 확인 + 전체 회귀**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~LifecycleSealTests|FullyQualifiedName~PlayersE2ETests"`
Expected: PASS (봉인 4 + E2E 1)

Run: `dotnet test server/tests/Bun3.Server.Tests` → 전체 PASS (167 + 4 = 171)
Run: `dotnet build Bun3.sln --no-incremental` → 경고 0

- [ ] **Step 6: 커밋**

```powershell
git add server/src/Bun3.Server.Players server/tests/Bun3.Server.Tests/LifecycleSealTests.cs server/tests/Bun3.Server.Tests/PlayersE2ETests.cs
git commit -m "✨ Seal player lifecycle races and deliver duplicate-login reason" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Ticking StopAsync(ct) + Hosting 추종 + 최종 검증 — Ticking 0.2.0 / Hosting 0.4.0

**Files:**
- Modify: `server/src/Bun3.Server.Ticking/TickLoop.cs:127-134` (StopAsync ct)
- Modify: `server/src/Bun3.Server.Hosting/PlayersServiceCollectionExtensions.cs` (정지 시 ct 전달)
- Modify: `server/src/Bun3.Server.Rpc/RpcStatus.cs:15` (Players 언급 제거)
- Modify: `server/src/Bun3.Server.Ticking/Bun3.Server.Ticking.csproj` (0.2.0), `server/src/Bun3.Server.Hosting/Bun3.Server.Hosting.csproj` (0.4.0)
- Test: `server/tests/Bun3.Server.Tests/TickLoopTests.cs` (테스트 1개 추가)

**Interfaces:**
- Consumes: 없음 (기존 구조)
- Produces: `TickLoop.StopAsync(CancellationToken ct = default)`

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/TickLoopTests.cs`에 추가:

```csharp
    [Test]
    public async Task StopAsync_with_canceled_ct_abandons_wait()
    {
        var block = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(10) });
        loop.Every(TimeSpan.FromMilliseconds(10), async _ =>
        {
            entered.TrySetResult(true);
            await block.Task;   // 잡이 행 — ct가 기다림을 포기하게 한다
        });

        loop.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(() => loop.StopAsync(cts.Token));

        block.TrySetResult(true);          // 잡 해제 — 루프는 취소 신호를 받았으므로 스스로 종료
        await loop.StopAsync();            // ct 없는 재호출은 정상 대기로 완료
    }
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~TickLoopTests"`
Expected: 컴파일 오류 (StopAsync가 인자를 안 받음)

- [ ] **Step 3: 구현**

`server/src/Bun3.Server.Ticking/TickLoop.cs` — `StopAsync` 교체:

```csharp
        /// <summary>루프를 정지한다 — 진행 중인 틱(잡)이 끝날 때까지 기다린다.
        /// ct는 "기다림의 포기"만 의미한다: 취소되어도 루프 강제 중단은 없으며(직렬화/무중단 철학),
        /// 이미 취소 신호를 받은 루프는 현재 잡이 끝나는 대로 스스로 종료한다.</summary>
        public async Task StopAsync(CancellationToken ct = default)
        {
            _cts.Cancel();
            if (_runTask == null)
            {
                return;
            }

            if (!ct.CanBeCanceled)
            {
                await _runTask.ConfigureAwait(false);
                return;
            }

            var abandon = Task.Delay(System.Threading.Timeout.Infinite, ct);
            var completed = await Task.WhenAny(_runTask, abandon).ConfigureAwait(false);
            await completed.ConfigureAwait(false);   // 루프 완료 또는 TaskCanceledException 전파
        }
```

`server/src/Bun3.Server.Hosting/PlayersServiceCollectionExtensions.cs` — `PlayersLifetimeService.StopAsync`의 첫 줄을:

```csharp
        await _tickLoop.StopAsync(cancellationToken).ConfigureAwait(false);   // 틱 먼저 정지 — 정지 중 새 틱 작업 유입 차단
```

`server/src/Bun3.Server.Rpc/RpcStatus.cs:15` — 문서 교체:

```csharp
        /// <summary>미인증 — OnGateRequest 게이트 거부.</summary>
```

버전: `Bun3.Server.Ticking.csproj` → `0.2.0`, `Bun3.Server.Hosting.csproj` → `0.4.0`.

- [ ] **Step 4: 최종 전체 검증**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~TickLoopTests"` → PASS (기존 9 + 1)
Run: `dotnet build Bun3.sln --no-incremental` → **경고 0**
Run: `dotnet test server/tests/Bun3.Server.Tests` → 전체 PASS (171 + 1 = **172**)
Run: `dotnet test common/tests/Bun3.Common.Tests` → 28 PASS

- [ ] **Step 5: 커밋**

```powershell
git add server/src/Bun3.Server.Ticking server/src/Bun3.Server.Hosting server/src/Bun3.Server.Rpc/RpcStatus.cs server/tests/Bun3.Server.Tests/TickLoopTests.cs
git commit -m "✨ Add TickLoop.StopAsync cancellation and follow-up version bumps" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```
