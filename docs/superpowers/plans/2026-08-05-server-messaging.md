# Bun3.Server.Messaging v1 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** v0 패킷 전송 위에 타입 있는 요청/응답 + 서버 푸시 계층(`Bun3.Server.Messaging`)과 클라이언트 커넥터를 구현하고, 실 TCP 미니 프로토콜 E2E 5종 통과로 v1을 완료한다.

**Architecture:** 게임 소유 protobuf 루트 3형(Request/Response/Update)을 제네릭으로 받아 디스크립터로 oneof↔핸들러를 기동 시 1회 매핑(전수 검증 포함), 와이어는 v0 패킷 안에 1바이트 채널(0x01 Control/0x02 Request/0x03 Response/0x04 Update). 스펙: `docs/superpowers/specs/2026-08-05-server-messaging-design.md`.

**Tech Stack:** Google.Protobuf 3.29.3 + Grpc.Tools 2.68.0(빌드 시 protoc). Messaging은 netstandard2.1 + C# 9. 테스트 NUnit 4 (net10.0).

## Global Constraints

- **TFM/언어**: `Bun3.Server.Messaging`은 netstandard2.1 + `<LangVersion>9.0</LangVersion>` + `<Nullable>enable</Nullable>`, ImplicitUsings 금지(명시 using, 블록 네임스페이스). 테스트는 net10.0 + ImplicitUsings(+`using NUnit.Framework;` 명시).
- **비동기**: 라이브러리 코드 모든 await에 `.ConfigureAwait(false)`. hot path는 `ValueTask`, 수명주기는 `Task`. 라이브러리에서 `Task.WaitAsync` 금지(net6+ API — ns2.1에 없음).
- **의존**: Google.Protobuf는 Messaging에만. v0 패키지(Abstractions/Core/Transport.Tcp)에 protobuf 유입 금지. **v0 Core(Session/ServerBase)는 무변경**.
- **채널 바이트**: 0x01=Control, 0x02=Request, 0x03=Response, 0x04=Update. 알 수 없는 채널/파싱 실패/방향 위반(클라가 0x03을 보냄 등) = 프로토콜 위반 → 연결 종료.
- **상태코드**: 0=OK, 1=핸들러 미등록(방어), 2=핸들러 예외. 음수는 게임 정의.
- **매칭 규약**: `Request.body`의 각 케이스는 `Response.body`에 같은 필드 이름·번호 케이스 필수. 모든 요청은 응답을 받는다(빈 응답 메시지 포함).
- **기본값**: 요청 타임아웃 10초, Ping 주기 30초, idle kick 120초(옵션, 서버), MaxPacketSize 1MB.
- **디스크립터는 기동 1회 맵 구축에만** — hot path는 기동 시 준비한 케이스별 델리게이트(캐시된 accessor) 경로.
- **커밋**: gitmoji + 두 번째 `-m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"`. 작업 디렉터리는 레포 루트.
- **테스트 실행**: `dotnet test server/tests/Bun3.Server.Tests` (필터 `--filter "FullyQualifiedName~<클래스>"`). 시작 시점 테스트 수 37개 — 각 태스크 후 전체 초록 유지.

## 파일 구조 (전체 조감)

```
common/src/com.bun3.common/Runtime/Network/
├── PacketFormat.cs                      [Task 1: Transport.Tcp에서 이동, ns → Bun3.Common.Network]
├── PacketFormat.cs.meta / Network.meta  [Task 1]
server/src/Bun3.Server.Abstractions/
└── IConnector.cs                        [Task 2]
server/src/Bun3.Server.Transport.Tcp/
├── TcpConnectorOptions.cs               [Task 2]
└── TcpConnector.cs                      [Task 2]
server/src/Bun3.Server.Messaging/
├── Bun3.Server.Messaging.csproj         [Task 3]
├── Protos/control.proto                 [Task 3] (Ping/Pong — Grpc.Tools로 빌드 시 생성)
├── Channels.cs                          [Task 3]
├── Reply.cs                             [Task 3] (Reply<TRes>, ReplyFailure, Reply)
├── ConnectionClosedException.cs         [Task 3]
├── OneofMap.cs                          [Task 4] (디스크립터 → 케이스 맵, 기동 1회)
├── MessagingConfig.cs                   [Task 4] (OnRequest 등록 + 검증 자료)
├── MessagingValidationException.cs      [Task 4]
├── MessagingRuntime.cs                  [Task 5] (채널 파싱·디스패치·응답 조립 — 서버측 두뇌)
├── MessagingSession.cs                  [Task 5]
├── MessagingServer.cs                   [Task 5]
├── MessagingClientOptions.cs            [Task 6]
└── MessagingClient.cs                   [Task 6]
server/src/Bun3.Server.Hosting/
└── MessagingServiceCollectionExtensions.cs [Task 8] (AddMessagingServer)
server/tests/Bun3.Server.Tests/
├── Protos/game.proto                    [Task 3] (테스트 미니 프로토콜)
├── ReplyTests.cs                        [Task 3]
├── MessagingValidationTests.cs          [Task 4]
├── MessagingServerTests.cs              [Task 5] (FakeTransport 기반)
├── Helpers/InMemoryDuplex.cs            [Task 6] (클라↔서버 인메모리 페어)
├── MessagingClientTests.cs              [Task 6]
├── MessagingE2ETests.cs                 [Task 7] (실 TCP 5종 — v1 완료 조건)
├── TcpConnectorTests.cs                 [Task 2]
└── MessagingHostingTests.cs             [Task 8]
```

의존 방향: Messaging → Core + Abstractions + Google.Protobuf. Transport.Tcp → Abstractions + **Bun3.Common(신규, PacketFormat)**. Hosting → (기존) + Messaging.

---

### Task 1: PacketFormat → Bun3.Common 이동

**Files:**
- Move (git mv): `server/src/Bun3.Server.Transport.Tcp/PacketFormat.cs` → `common/src/com.bun3.common/Runtime/Network/PacketFormat.cs`
- Create: `common/src/com.bun3.common/Runtime/Network.meta`, `common/src/com.bun3.common/Runtime/Network/PacketFormat.cs.meta`
- Modify: `server/src/Bun3.Server.Transport.Tcp/Bun3.Server.Transport.Tcp.csproj` (Bun3.Common 참조 추가), `TcpConnection.cs` (using), 테스트 4파일 (`PacketFormatTests.cs`, `TcpTransportTests.cs`, `EchoE2ETests.cs`, `HostingTests.cs`)의 using

**Interfaces:**
- Consumes: 기존 `PacketFormat` (WritePacketAsync/ReadPacketAsync/HeaderSize)
- Produces: 동일 API, 새 네임스페이스 **`Bun3.Common.Network`** — 이후 모든 태스크는 `using Bun3.Common.Network;`으로 사용

- [ ] **Step 1: 파일 이동 + 네임스페이스 변경**

```
git mv server/src/Bun3.Server.Transport.Tcp/PacketFormat.cs common/src/com.bun3.common/Runtime/Network/PacketFormat.cs
```

(폴더가 없으므로 먼저 `mkdir common/src/com.bun3.common/Runtime/Network` 후 git mv.) 파일 내 `namespace Bun3.Server.Transport.Tcp` → `namespace Bun3.Common.Network`. 파일의 나머지(클래스 본문·doc 주석)는 무변경.

- [ ] **Step 2: Unity .meta 생성**

`common/src/com.bun3.common/Runtime/Network.meta`:

```yaml
fileFormatVersion: 2
guid: 3f8a1c2e9b4d4e6f8a0b1c2d3e4f5a6b
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

`common/src/com.bun3.common/Runtime/Network/PacketFormat.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 7c2d9e0f1a3b4c5d6e7f8091a2b3c4d5
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

(형식은 `common/src/com.bun3.common/Runtime/Pooling/`의 기존 .meta들을 열어 동일 구조인지 확인하고, 다르면 기존 형식을 따른다. GUID는 위 값 그대로 사용 — 레포 내 유일하면 된다.)

- [ ] **Step 3: 참조 재배선**

`Bun3.Server.Transport.Tcp.csproj`의 ItemGroup에 추가:

```xml
    <ProjectReference Include="..\..\..\common\src\com.bun3.common\Bun3.Common.csproj" />
```

`TcpConnection.cs`에 `using Bun3.Common.Network;` 추가(기존 using 블록 알파벳 순서 유지). 테스트 4파일에서 `PacketFormat` 사용처의 using을 `using Bun3.Common.Network;`로 교체/추가 (`using Bun3.Server.Transport.Tcp;`는 다른 타입 사용 시 유지).

- [ ] **Step 4: 빌드/테스트 확인**

Run: `dotnet build Bun3.sln` → 0 오류/0 경고. `dotnet test server/tests/Bun3.Server.Tests` → 37/37. `dotnet test common/tests/Bun3.Common.Tests` → 전체 초록(28개).

- [ ] **Step 5: Commit**

```
git add -A common/ server/
git commit -m "♻️ Move PacketFormat to Bun3.Common.Network (client/server shared wire format)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: IConnector 계약 + TcpConnector

**Files:**
- Create: `server/src/Bun3.Server.Abstractions/IConnector.cs`
- Create: `server/src/Bun3.Server.Transport.Tcp/TcpConnectorOptions.cs`
- Create: `server/src/Bun3.Server.Transport.Tcp/TcpConnector.cs`
- Test: `server/tests/Bun3.Server.Tests/TcpConnectorTests.cs`

**Interfaces:**
- Consumes: Task 1의 `Bun3.Common.Network.PacketFormat`(TcpConnection 경유), 기존 `IConnection`/`IConnectionHandler`, internal `TcpConnection`(동일 어셈블리라 재사용 가능 — ctor `(long id, TcpClient, TcpTransportOptions, IConnectionHandler, ILogger)`, `RunReceiveLoopAsync()`)
- Produces: `IConnector { ValueTask<IConnection> ConnectAsync(IConnectionHandler handler, CancellationToken ct = default); }` — OnConnected는 반환 전에 호출됨(리스너와 동일 순서 계약). `TcpConnectorOptions { string Host; int Port; int MaxPacketSize = 1024*1024; }`, `TcpConnector(TcpConnectorOptions, ILogger? = null)`. Task 6의 MessagingClient가 사용.

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/TcpConnectorTests.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Bun3.Common.Network;
using Bun3.Server.Abstractions;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class TcpConnectorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class RecordingHandler : IConnectionHandler
    {
        public readonly TaskCompletionSource<IConnection> Connected =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<Exception?> Closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ConcurrentQueue<byte[]> Packets = new();
        public readonly SemaphoreSlim PacketSignal = new(0);

        public void OnConnected(IConnection connection) => Connected.TrySetResult(connection);

        public void OnPacket(IConnection connection, ReadOnlyMemory<byte> packet)
        {
            Packets.Enqueue(packet.ToArray());
            PacketSignal.Release();
        }

        public void OnClosed(IConnection connection, Exception? error) => Closed.TrySetResult(error);
    }

    private static async Task<(TcpTransportListener listener, RecordingHandler serverHandler)> StartListenerAsync()
    {
        var handler = new RecordingHandler();
        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
        await listener.StartAsync(handler);
        return (listener, handler);
    }

    [Test]
    public async Task Connect_raises_client_OnConnected_before_returning()
    {
        var (listener, _) = await StartListenerAsync();
        try
        {
            var clientHandler = new RecordingHandler();
            var connector = new TcpConnector(new TcpConnectorOptions
            {
                Host = "127.0.0.1",
                Port = listener.BoundPort!.Value,
            });

            var connection = await connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);

            Assert.That(clientHandler.Connected.Task.IsCompletedSuccessfully, Is.True); // 반환 전에 이미 호출됨
            Assert.That(connection.IsOpen, Is.True);
            connection.Close();
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Packets_flow_both_directions()
    {
        var (listener, serverHandler) = await StartListenerAsync();
        try
        {
            var clientHandler = new RecordingHandler();
            var connector = new TcpConnector(new TcpConnectorOptions
            {
                Host = "127.0.0.1",
                Port = listener.BoundPort!.Value,
            });
            var clientConn = await connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);
            var serverConn = await serverHandler.Connected.Task.WaitAsync(Timeout);

            await clientConn.SendAsync(Encoding.UTF8.GetBytes("to server"));
            await serverHandler.PacketSignal.WaitAsync(Timeout);
            Assert.That(serverHandler.Packets.TryDequeue(out var p1), Is.True);
            Assert.That(p1, Is.EqualTo(Encoding.UTF8.GetBytes("to server")));

            await serverConn.SendAsync(Encoding.UTF8.GetBytes("to client"));
            await clientHandler.PacketSignal.WaitAsync(Timeout);
            Assert.That(clientHandler.Packets.TryDequeue(out var p2), Is.True);
            Assert.That(p2, Is.EqualTo(Encoding.UTF8.GetBytes("to client")));

            clientConn.Close();
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Server_close_raises_client_OnClosed_with_null()
    {
        var (listener, serverHandler) = await StartListenerAsync();
        try
        {
            var clientHandler = new RecordingHandler();
            var connector = new TcpConnector(new TcpConnectorOptions
            {
                Host = "127.0.0.1",
                Port = listener.BoundPort!.Value,
            });
            var clientConn = await connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);
            var serverConn = await serverHandler.Connected.Task.WaitAsync(Timeout);

            serverConn.Close();

            var error = await clientHandler.Closed.Task.WaitAsync(Timeout);
            Assert.That(error, Is.Null);
            Assert.That(clientConn.IsOpen, Is.False);
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public void Connect_to_dead_port_throws_SocketException()
    {
        // 청취자 없는 포트: OS가 즉시 거부한다
        var deadPortListener = new TcpListener(IPAddress.Loopback, 0);
        deadPortListener.Start();
        var deadPort = ((IPEndPoint)deadPortListener.LocalEndpoint).Port;
        deadPortListener.Stop();

        var connector = new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = deadPort });
        Assert.ThrowsAsync<SocketException>(async () =>
            await connector.ConnectAsync(new RecordingHandler()).AsTask().WaitAsync(Timeout));
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~TcpConnectorTests"`
Expected: 컴파일 에러 — `IConnector`/`TcpConnector`/`TcpConnectorOptions` 미정의

- [ ] **Step 3: 구현**

`server/src/Bun3.Server.Abstractions/IConnector.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Abstractions
{
    /// <summary>
    /// 나가는 연결(클라이언트 측)의 계약. 전송 구현은 리스너와 동일한 순서 계약을 지킨다:
    /// handler.OnConnected는 ConnectAsync 반환 전에 호출되고, 그 전에는 OnPacket/OnClosed가
    /// 발생하지 않으며, OnClosed는 연결당 정확히 1회다.
    /// </summary>
    public interface IConnector
    {
        /// <summary>연결을 수립하고 수신을 시작한다. 실패 시 전송별 예외를 던진다.</summary>
        ValueTask<IConnection> ConnectAsync(IConnectionHandler handler, CancellationToken ct = default);
    }
}
```

`server/src/Bun3.Server.Transport.Tcp/TcpConnectorOptions.cs`:

```csharp
namespace Bun3.Server.Transport.Tcp
{
    public sealed class TcpConnectorOptions
    {
        /// <summary>접속할 호스트명 또는 IP.</summary>
        public string Host { get; set; } = "127.0.0.1";

        public int Port { get; set; }

        /// <summary>수신 패킷 크기 상한. 초과 시 프로토콜 위반으로 연결을 종료한다.</summary>
        public int MaxPacketSize { get; set; } = 1024 * 1024;
    }
}
```

`server/src/Bun3.Server.Transport.Tcp/TcpConnector.cs`:

```csharp
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Transport.Tcp
{
    /// <summary>TCP 나가는 연결. 수신 프레이밍/생명주기는 서버와 같은 TcpConnection을 재사용한다.</summary>
    public sealed class TcpConnector : IConnector
    {
        private readonly TcpConnectorOptions _options;
        private readonly ILogger _logger;
        private long _nextConnectionId;

        public TcpConnector(TcpConnectorOptions options, ILogger? logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
        }

        public async ValueTask<IConnection> ConnectAsync(IConnectionHandler handler, CancellationToken ct = default)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            ct.ThrowIfCancellationRequested();
            var client = new TcpClient();
            try
            {
                // ns2.1의 ConnectAsync에는 ct 오버로드가 없다 — 취소 시 소켓을 닫아 깨운다.
                using (ct.Register(() => client.Close()))
                {
                    await client.ConnectAsync(_options.Host, _options.Port).ConfigureAwait(false);
                }
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                client.Dispose();
                throw new OperationCanceledException(ct);
            }
            catch
            {
                client.Dispose();
                throw;
            }

            client.NoDelay = true;
            var connection = new TcpConnection(
                Interlocked.Increment(ref _nextConnectionId),
                client,
                new TcpTransportOptions { MaxPacketSize = _options.MaxPacketSize },
                handler,
                _logger);

            // 계약: OnConnected 반환 전에는 OnPacket/OnClosed가 발생하지 않도록
            // 수신 루프는 OnConnected 이후에 시작한다.
            handler.OnConnected(connection);
            _ = Task.Run(connection.RunReceiveLoopAsync);
            return connection;
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~TcpConnectorTests"` → 4/4.
이후 전체: `dotnet test server/tests/Bun3.Server.Tests` → 41/41.

- [ ] **Step 5: Commit**

```
git add server/src/ server/tests/
git commit -m "✨ Add IConnector contract and TcpConnector (outgoing connections)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Messaging 스캐폴딩 + Reply + proto 파이프라인

**Files:**
- Create: `server/src/Bun3.Server.Messaging/Bun3.Server.Messaging.csproj`
- Create: `server/src/Bun3.Server.Messaging/Protos/control.proto`
- Create: `server/src/Bun3.Server.Messaging/Channels.cs`
- Create: `server/src/Bun3.Server.Messaging/Reply.cs`
- Create: `server/src/Bun3.Server.Messaging/ConnectionClosedException.cs`
- Modify: `server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj` (Messaging 참조 + Grpc.Tools + game.proto)
- Create: `server/tests/Bun3.Server.Tests/Protos/game.proto`
- Test: `server/tests/Bun3.Server.Tests/ReplyTests.cs`
- Modify: `Bun3.sln` (`dotnet sln add`)

**Interfaces:**
- Consumes: 없음 (신규 패키지)
- Produces:
  - `public static class Channels { public const byte Control = 0x01; Request = 0x02; Response = 0x03; Update = 0x04; }`
  - `Reply<TRes> where TRes : class, IMessage<TRes>` — `int Status`, `TRes? Value`, `bool IsOk`, `static Reply<TRes> Ok(TRes)`, `static Reply<TRes> Fail(int)`, implicit from `TRes`와 `ReplyFailure`; `public static class Reply { static ReplyFailure Fail(int); }`; `public readonly struct ReplyFailure { int Status; }`
  - `ConnectionClosedException : Exception` (ctor string)
  - control.proto 생성 타입: `Bun3.Server.Messaging.ControlMessages.Control/Ping/Pong` (Pong은 client_time_unix_ms 에코)
  - 테스트 프로토콜(`Bun3.Server.Tests.GameProtocol`): 루트 `Request/Response/Update` + `GetServerTimeRequest/Response`(빈 요청→unix_ms), `BuyItemRequest/Response`(item_id→remaining_gold), `BroadcastedUpdate`(text) + 검증 실패용 `MismatchRequest/MismatchResponse` — Task 4~8이 사용

- [ ] **Step 1: Messaging csproj 작성**

`server/src/Bun3.Server.Messaging/Bun3.Server.Messaging.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>Bun3.Server.Messaging</RootNamespace>
    <PackageId>Bun3.Server.Messaging</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-kit</RepositoryUrl>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <Description>protobuf 기반 타입 메시징 — 요청/응답(상태코드)·서버 푸시·기동 검증·클라이언트</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Google.Protobuf" Version="3.29.3" />
    <PackageReference Include="Grpc.Tools" Version="2.68.0" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <Protobuf Include="Protos\control.proto" GrpcServices="None" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Bun3.Server.Abstractions\Bun3.Server.Abstractions.csproj" />
    <ProjectReference Include="..\Bun3.Server.Core\Bun3.Server.Core.csproj" />
  </ItemGroup>

</Project>
```

(참고: 생성 코드는 XML 주석이 있어 `GenerateDocumentationFile`과 충돌하는 CS1591이 나올 수 있음 — 생성 코드가 아닌 우리 소스의 public 멤버에는 doc 주석을 달고, 생성 코드에서 경고가 나오면 그 파일만이 아니라 `<NoWarn>$(NoWarn);1591</NoWarn>`을 다는 대신 **경고 원인을 보고**하고 지시를 기다릴 것 — 기본 시도는 doc 주석으로 해결.)

- [ ] **Step 2: control.proto + Channels + ConnectionClosedException 작성**

`server/src/Bun3.Server.Messaging/Protos/control.proto`:

```proto
syntax = "proto3";

option csharp_namespace = "Bun3.Server.Messaging.ControlMessages";

package bun3.control;

// 채널 0x01의 프레임워크 소유 메시지.
message Control {
  oneof body {
    Ping ping = 1;
    Pong pong = 2;
  }
}

message Ping {
  int64 client_time_unix_ms = 1;
}

// 서버는 client_time_unix_ms를 그대로 에코한다 — 클라가 RTT를 계산한다.
message Pong {
  int64 client_time_unix_ms = 1;
}
```

`server/src/Bun3.Server.Messaging/Channels.cs`:

```csharp
namespace Bun3.Server.Messaging
{
    /// <summary>패킷 첫 바이트의 채널 값. 0x10 이상은 예약(게임 커스텀/고빈도 채널).</summary>
    public static class Channels
    {
        public const byte Control = 0x01;
        public const byte Request = 0x02;
        public const byte Response = 0x03;
        public const byte Update = 0x04;
    }
}
```

`server/src/Bun3.Server.Messaging/ConnectionClosedException.cs`:

```csharp
using System;

namespace Bun3.Server.Messaging
{
    /// <summary>응답 대기 중 연결이 닫혀 요청이 완료될 수 없을 때 pending await에 전달된다.</summary>
    public sealed class ConnectionClosedException : Exception
    {
        public ConnectionClosedException(string message) : base(message) { }
    }
}
```

- [ ] **Step 3: game.proto + 테스트 csproj 갱신**

`server/tests/Bun3.Server.Tests/Protos/game.proto`:

```proto
syntax = "proto3";

option csharp_namespace = "Bun3.Server.Tests.GameProtocol";

package game;

// ---- 정상 프로토콜 (규약 준수) ----
message Request {
  int64 request_id = 1;
  oneof body {
    GetServerTimeRequest get_server_time = 10;
    BuyItemRequest buy_item = 11;
  }
}

message Response {
  int64 request_id = 1;
  int32 status = 2;
  oneof body {
    GetServerTimeResponse get_server_time = 10;
    BuyItemResponse buy_item = 11;
  }
}

message Update {
  oneof body {
    BroadcastedUpdate broadcasted = 10;
  }
}

message GetServerTimeRequest {}
message GetServerTimeResponse { int64 unix_ms = 1; }
message BuyItemRequest { int32 item_id = 1; }
message BuyItemResponse { int32 remaining_gold = 1; }
message BroadcastedUpdate { string text = 1; }

// ---- 검증 실패 테스트용 (의도적 규약 위반 루트) ----
message MismatchRequest {
  int64 request_id = 1;
  oneof body {
    GetServerTimeRequest get_server_time = 10;
    BuyItemRequest buy_item = 11;
  }
}

// buy_item 케이스 없음 + get_server_time 번호 불일치(10이 아닌 12)
message MismatchResponse {
  int64 request_id = 1;
  int32 status = 2;
  oneof body {
    GetServerTimeResponse get_server_time = 12;
  }
}
```

`server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`에 ItemGroup 추가:

```xml
  <ItemGroup>
    <PackageReference Include="Grpc.Tools" Version="2.68.0" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <Protobuf Include="Protos\game.proto" GrpcServices="None" />
  </ItemGroup>
```

기존 ProjectReference ItemGroup에 추가:

```xml
    <ProjectReference Include="..\..\src\Bun3.Server.Messaging\Bun3.Server.Messaging.csproj" />
```

(Google.Protobuf는 Messaging 참조로 전이된다.)

- [ ] **Step 4: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/ReplyTests.cs`:

```csharp
using Bun3.Server.Messaging;
using Bun3.Server.Tests.GameProtocol;
using Google.Protobuf.Reflection;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class ReplyTests
{
    [Test]
    public void Ok_holds_value_with_status_zero()
    {
        var res = new BuyItemResponse { RemainingGold = 900 };
        var reply = Reply<BuyItemResponse>.Ok(res);
        Assert.That(reply.IsOk, Is.True);
        Assert.That(reply.Status, Is.EqualTo(0));
        Assert.That(reply.Value, Is.SameAs(res));
    }

    [Test]
    public void Implicit_conversion_from_value_is_Ok()
    {
        Reply<BuyItemResponse> reply = new BuyItemResponse { RemainingGold = 1 };
        Assert.That(reply.IsOk, Is.True);
    }

    [Test]
    public void ReplyFailure_converts_to_failed_reply()
    {
        Reply<BuyItemResponse> reply = Reply.Fail(-1001);
        Assert.That(reply.IsOk, Is.False);
        Assert.That(reply.Status, Is.EqualTo(-1001));
        Assert.That(reply.Value, Is.Null);
    }

    [Test]
    public void Ok_with_null_throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => Reply<BuyItemResponse>.Ok(null!));
    }

    [Test]
    public void Fail_with_zero_throws()
    {
        Assert.Throws<System.ArgumentException>(() => Reply<BuyItemResponse>.Fail(0));
    }

    [Test]
    public void Generated_game_protocol_matches_root_conventions()
    {
        // Grpc.Tools 파이프라인 스모크: 루트 3형의 oneof "body"와 규약 필드가 생성됐는지
        Assert.That(Request.Descriptor.Oneofs, Has.Some.Matches<OneofDescriptor>(o => o.Name == "body"));
        Assert.That(Request.Descriptor.FindFieldByName("request_id"), Is.Not.Null);
        Assert.That(Response.Descriptor.FindFieldByName("status"), Is.Not.Null);
        Assert.That(Update.Descriptor.Oneofs, Has.Some.Matches<OneofDescriptor>(o => o.Name == "body"));
    }
}
```

- [ ] **Step 5: 테스트 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~ReplyTests"`
Expected: 컴파일 에러 — `Reply` 미정의. (game.proto 생성 타입이 해석 안 되면 Grpc.Tools 파이프라인부터 해결 — `obj/Debug/**/Protos`에 생성 .cs가 나오는지 확인.)

- [ ] **Step 6: Reply 구현**

`server/src/Bun3.Server.Messaging/Reply.cs`:

```csharp
using System;
using Google.Protobuf;

namespace Bun3.Server.Messaging
{
    /// <summary>요청 처리의 결과 — 성공(응답 메시지) 또는 실패(상태코드). 무할당 readonly struct.</summary>
    public readonly struct Reply<TRes> where TRes : class, IMessage<TRes>
    {
        /// <summary>0 = OK. 1~99 프레임워크 예약, 음수 게임 정의.</summary>
        public int Status { get; }

        /// <summary>불변식: Status == 0 ⟺ Value != null.</summary>
        public TRes? Value { get; }

        public bool IsOk => Status == 0;

        private Reply(int status, TRes? value)
        {
            Status = status;
            Value = value;
        }

        public static Reply<TRes> Ok(TRes value) =>
            new Reply<TRes>(0, value ?? throw new ArgumentNullException(nameof(value)));

        public static Reply<TRes> Fail(int status) =>
            status != 0
                ? new Reply<TRes>(status, null)
                : throw new ArgumentException("실패 상태코드는 0이 될 수 없다.", nameof(status));

        public static implicit operator Reply<TRes>(TRes value) => Ok(value);

        public static implicit operator Reply<TRes>(ReplyFailure failure) => Fail(failure.Status);
    }

    /// <summary>제네릭 인자 없이 Reply.Fail(코드)를 쓰게 해주는 중간 값.</summary>
    public readonly struct ReplyFailure
    {
        public int Status { get; }

        public ReplyFailure(int status)
        {
            Status = status;
        }
    }

    /// <summary>Reply&lt;TRes&gt;의 비제네릭 도우미.</summary>
    public static class Reply
    {
        public static ReplyFailure Fail(int status) => new ReplyFailure(status);
    }
}
```

- [ ] **Step 7: 솔루션 편입 + 통과 확인 + Commit**

```
dotnet sln Bun3.sln add server/src/Bun3.Server.Messaging/Bun3.Server.Messaging.csproj
dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~ReplyTests"
dotnet test server/tests/Bun3.Server.Tests
dotnet build Bun3.sln
```

Expected: ReplyTests 6/6, 전체 47/47, 빌드 0 오류/0 경고. Commit:

```
git add server/ Bun3.sln
git commit -m "✨ Scaffold Bun3.Server.Messaging with Reply, channels, proto pipeline" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: 스키마 맵(OneofMap/MessagingSchema) + 핸들러 등록 + 전수 검증

**Files:**
- Create: `server/src/Bun3.Server.Messaging/OneofMap.cs`
- Create: `server/src/Bun3.Server.Messaging/MessagingSchema.cs`
- Create: `server/src/Bun3.Server.Messaging/MessagingConfig.cs`
- Create: `server/src/Bun3.Server.Messaging/MessagingValidationException.cs`
- Test: `server/tests/Bun3.Server.Tests/MessagingValidationTests.cs`

**Interfaces:**
- Consumes: Task 3의 `Reply<TRes>`/`ReplyFailure`, 테스트 프로토콜 타입, `Bun3.Server.Core.Session`
- Produces:
  - `MessagingValidationException : Exception { IReadOnlyList<string> Errors }` — 메시지에 전체 오류 목록 포함
  - `MessagingConfig<TSession> where TSession : Session` — `void OnRequest<TReq, TRes>(Func<TSession, TReq, ValueTask<Reply<TRes>>> handler)` (TReq/TRes: class, IMessage<T>; 같은 TReq 중복 등록은 즉시 throw); internal `Dictionary<Type, Registration> Registrations`, `Registration { Type RequestType; Type ResponseType; Func<TSession, IMessage, ValueTask<(int Status, IMessage? Response)>> Invoke; }`
  - `MessagingSchema<TRequest, TResponse, TUpdate>` (각각 class, IMessage<T>, new()) — `static Create()` (루트 규약 위반 시 throw: oneof "body" 필수, TRequest/TResponse에 int64 `request_id`, TResponse에 int32 `status`); `void Validate<TSession>(MessagingConfig<TSession>)` (미등록/미상 타입/응답 케이스·타입 불일치 전체 목록 throw); internal `OneofMap RequestMap/ResponseMap/UpdateMap`, `MessageParser<T> RequestParser/ResponseParser/UpdateParser`, `FieldDescriptor RequestIdOfRequest/RequestIdOfResponse/StatusOfResponse`
  - internal `OneofMap` — `IReadOnlyCollection<OneofCase> Cases`, `OneofCase? ByFieldNumber(int)`, `ByPayloadType(Type)`, `GetActiveCase(IMessage)`; `OneofCase { int FieldNumber; string Name; Type PayloadType; Func<IMessage, IMessage> Get; Action<IMessage, IMessage> Set; }` — accessor는 구축 시 1회 캐시(hot path는 이 델리게이트만)
  - Task 5의 런타임이 전부 소비

- [ ] **Step 1: 실패하는 검증 테스트 작성**

`server/tests/Bun3.Server.Tests/MessagingValidationTests.cs`:

```csharp
using Bun3.Server.Messaging;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Tests.Helpers;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class MessagingValidationTests
{
    private static MessagingConfig<EchoSession> FullConfig()
    {
        var config = new MessagingConfig<EchoSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse { UnixMs = 1 }));
        config.OnRequest<BuyItemRequest, BuyItemResponse>(
            (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse { RemainingGold = 1 }));
        return config;
    }

    [Test]
    public void Valid_config_passes()
    {
        var schema = MessagingSchema<Request, Response, Update>.Create();
        Assert.DoesNotThrow(() => schema.Validate(FullConfig()));
    }

    [Test]
    public void Missing_handler_fails_listing_the_case()
    {
        var schema = MessagingSchema<Request, Response, Update>.Create();
        var config = new MessagingConfig<EchoSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse()));

        var ex = Assert.Throws<MessagingValidationException>(() => schema.Validate(config))!;
        Assert.That(ex.Message, Does.Contain("buy_item"));
    }

    [Test]
    public void Response_case_mismatch_reports_all_violations()
    {
        // MismatchResponse: buy_item 케이스 없음, get_server_time은 번호 12(요청은 10)
        var schema = MessagingSchema<MismatchRequest, MismatchResponse, Update>.Create();

        var ex = Assert.Throws<MessagingValidationException>(() => schema.Validate(FullConfig()))!;
        Assert.That(ex.Errors, Has.Some.Contains("get_server_time"));
        Assert.That(ex.Errors, Has.Some.Contains("buy_item"));
    }

    [Test]
    public void Wrong_response_type_fails()
    {
        var schema = MessagingSchema<Request, Response, Update>.Create();
        var config = new MessagingConfig<EchoSession>();
        config.OnRequest<GetServerTimeRequest, BuyItemResponse>(   // 잘못된 TRes
            (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse()));
        config.OnRequest<BuyItemRequest, BuyItemResponse>(
            (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse()));

        var ex = Assert.Throws<MessagingValidationException>(() => schema.Validate(config))!;
        Assert.That(ex.Errors, Has.Some.Contains("응답 타입 불일치"));
    }

    [Test]
    public void Duplicate_registration_throws_immediately()
    {
        var config = new MessagingConfig<EchoSession>();
        config.OnRequest<BuyItemRequest, BuyItemResponse>(
            (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse()));

        Assert.Throws<MessagingValidationException>(() =>
            config.OnRequest<BuyItemRequest, BuyItemResponse>(
                (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse())));
    }

    [Test]
    public void Root_without_request_id_fails_schema_creation()
    {
        // Update를 TRequest 자리에 — oneof body는 있지만 request_id가 없다
        var ex = Assert.Throws<MessagingValidationException>(() =>
            MessagingSchema<Update, Response, Update>.Create())!;
        Assert.That(ex.Message, Does.Contain("request_id"));
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~MessagingValidationTests"`
Expected: 컴파일 에러 — `MessagingSchema`/`MessagingConfig`/`MessagingValidationException` 미정의

- [ ] **Step 3: 구현**

`server/src/Bun3.Server.Messaging/MessagingValidationException.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Bun3.Server.Messaging
{
    /// <summary>메시징 스키마/등록 검증 실패. Errors에 위반 전체 목록이 담긴다(fail-fast 기동 실패용).</summary>
    public sealed class MessagingValidationException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public MessagingValidationException(IReadOnlyList<string> errors)
            : base("메시징 구성 검증 실패:\n- " + string.Join("\n- ", errors))
        {
            Errors = errors;
        }
    }
}
```

`server/src/Bun3.Server.Messaging/OneofMap.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Bun3.Server.Messaging
{
    /// <summary>oneof "body"의 케이스 하나. 접근자 델리게이트는 구축 시 1회 캐시된다.</summary>
    internal sealed class OneofCase
    {
        public int FieldNumber { get; }
        public string Name { get; }
        public Type PayloadType { get; }
        public Func<IMessage, IMessage> Get { get; }
        public Action<IMessage, IMessage> Set { get; }

        public OneofCase(FieldDescriptor field)
        {
            FieldNumber = field.FieldNumber;
            Name = field.Name;
            PayloadType = field.MessageType.ClrType;
            var accessor = field.Accessor;
            Get = message => (IMessage)accessor.GetValue(message);
            Set = (message, payload) => accessor.SetValue(message, payload);
        }
    }

    /// <summary>루트 메시지의 oneof "body"를 기동 1회 열거해 만든 케이스 맵.</summary>
    internal sealed class OneofMap
    {
        private readonly OneofDescriptor _oneof;
        private readonly Dictionary<int, OneofCase> _byNumber = new Dictionary<int, OneofCase>();
        private readonly Dictionary<Type, OneofCase> _byType = new Dictionary<Type, OneofCase>();

        private OneofMap(OneofDescriptor oneof)
        {
            _oneof = oneof;
            foreach (var field in oneof.Fields)
            {
                var oneofCase = new OneofCase(field);
                _byNumber.Add(oneofCase.FieldNumber, oneofCase);
                _byType.Add(oneofCase.PayloadType, oneofCase);
            }
        }

        public IReadOnlyCollection<OneofCase> Cases => _byNumber.Values;

        /// <summary>oneof "body"가 없거나 메시지 아닌 케이스가 있으면 errors에 추가하고 null.</summary>
        public static OneofMap? TryBuild(MessageDescriptor message, string rootLabel, List<string> errors)
        {
            var oneof = message.Oneofs.FirstOrDefault(o => o.Name == "body");
            if (oneof == null)
            {
                errors.Add($"{rootLabel}({message.Name}): oneof \"body\" 없음");
                return null;
            }

            foreach (var field in oneof.Fields)
            {
                if (field.FieldType != FieldType.Message)
                {
                    errors.Add($"{rootLabel}({message.Name}): body 케이스 {field.Name}은 message 타입이어야 함");
                    return null;
                }
            }

            return new OneofMap(oneof);
        }

        public OneofCase? ByFieldNumber(int fieldNumber) =>
            _byNumber.TryGetValue(fieldNumber, out var found) ? found : null;

        public OneofCase? ByPayloadType(Type payloadType) =>
            _byType.TryGetValue(payloadType, out var found) ? found : null;

        /// <summary>envelope에 실제 설정된 케이스. 비어 있으면 null.</summary>
        public OneofCase? GetActiveCase(IMessage envelope)
        {
            var field = _oneof.Accessor.GetCaseFieldDescriptor(envelope);
            return field == null ? null : ByFieldNumber(field.FieldNumber);
        }
    }
}
```

`server/src/Bun3.Server.Messaging/MessagingConfig.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Google.Protobuf;

namespace Bun3.Server.Messaging
{
    /// <summary>서버 수준 핸들러 등록표. 부팅 시 1회 구성되고 MessagingSchema.Validate로 전수 검증된다.</summary>
    public sealed class MessagingConfig<TSession> where TSession : Session
    {
        internal sealed class Registration
        {
            public Type RequestType { get; }
            public Type ResponseType { get; }
            public Func<TSession, IMessage, ValueTask<(int Status, IMessage? Response)>> Invoke { get; }

            public Registration(
                Type requestType,
                Type responseType,
                Func<TSession, IMessage, ValueTask<(int Status, IMessage? Response)>> invoke)
            {
                RequestType = requestType;
                ResponseType = responseType;
                Invoke = invoke;
            }
        }

        internal Dictionary<Type, Registration> Registrations { get; } = new Dictionary<Type, Registration>();

        /// <summary>요청 타입 하나의 핸들러를 등록한다. 같은 TReq 중복 등록은 즉시 예외.</summary>
        public void OnRequest<TReq, TRes>(Func<TSession, TReq, ValueTask<Reply<TRes>>> handler)
            where TReq : class, IMessage<TReq>
            where TRes : class, IMessage<TRes>
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (Registrations.ContainsKey(typeof(TReq)))
            {
                throw new MessagingValidationException(new[] { $"중복 등록: {typeof(TReq).Name}" });
            }

            Registrations.Add(typeof(TReq), new Registration(
                typeof(TReq),
                typeof(TRes),
                async (session, message) =>
                {
                    var reply = await handler(session, (TReq)message).ConfigureAwait(false);
                    return (reply.Status, (IMessage?)reply.Value);
                }));
        }
    }
}
```

`server/src/Bun3.Server.Messaging/MessagingSchema.cs`:

```csharp
using System.Collections.Generic;
using Bun3.Server.Core;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Bun3.Server.Messaging
{
    /// <summary>
    /// 게임 소유 루트 3형(Request/Response/Update)의 디스크립터에서 기동 1회 구축되는 스키마 맵.
    /// 규약: 세 루트 모두 oneof "body"; TRequest/TResponse에 int64 request_id; TResponse에 int32 status.
    /// </summary>
    public sealed class MessagingSchema<TRequest, TResponse, TUpdate>
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        internal OneofMap RequestMap { get; }
        internal OneofMap ResponseMap { get; }
        internal OneofMap UpdateMap { get; }
        internal FieldDescriptor RequestIdOfRequest { get; }
        internal FieldDescriptor RequestIdOfResponse { get; }
        internal FieldDescriptor StatusOfResponse { get; }
        internal MessageParser<TRequest> RequestParser { get; } = new MessageParser<TRequest>(() => new TRequest());
        internal MessageParser<TResponse> ResponseParser { get; } = new MessageParser<TResponse>(() => new TResponse());
        internal MessageParser<TUpdate> UpdateParser { get; } = new MessageParser<TUpdate>(() => new TUpdate());

        private MessagingSchema(
            OneofMap requestMap,
            OneofMap responseMap,
            OneofMap updateMap,
            FieldDescriptor requestIdOfRequest,
            FieldDescriptor requestIdOfResponse,
            FieldDescriptor statusOfResponse)
        {
            RequestMap = requestMap;
            ResponseMap = responseMap;
            UpdateMap = updateMap;
            RequestIdOfRequest = requestIdOfRequest;
            RequestIdOfResponse = requestIdOfResponse;
            StatusOfResponse = statusOfResponse;
        }

        /// <summary>루트 규약 위반 시 전체 목록과 함께 MessagingValidationException.</summary>
        public static MessagingSchema<TRequest, TResponse, TUpdate> Create()
        {
            var errors = new List<string>();
            var requestDescriptor = new TRequest().Descriptor;
            var responseDescriptor = new TResponse().Descriptor;
            var updateDescriptor = new TUpdate().Descriptor;

            var requestMap = OneofMap.TryBuild(requestDescriptor, "Request", errors);
            var responseMap = OneofMap.TryBuild(responseDescriptor, "Response", errors);
            var updateMap = OneofMap.TryBuild(updateDescriptor, "Update", errors);
            var requestId = RequireField(requestDescriptor, "Request", "request_id", FieldType.Int64, errors);
            var responseRequestId = RequireField(responseDescriptor, "Response", "request_id", FieldType.Int64, errors);
            var status = RequireField(responseDescriptor, "Response", "status", FieldType.Int32, errors);

            if (errors.Count > 0)
            {
                throw new MessagingValidationException(errors);
            }

            return new MessagingSchema<TRequest, TResponse, TUpdate>(
                requestMap!, responseMap!, updateMap!, requestId!, responseRequestId!, status!);
        }

        private static FieldDescriptor? RequireField(
            MessageDescriptor message, string rootLabel, string fieldName, FieldType fieldType, List<string> errors)
        {
            var field = message.FindFieldByName(fieldName);
            if (field == null || field.FieldType != fieldType)
            {
                errors.Add($"{rootLabel}({message.Name}): {fieldType} {fieldName} 필드 필요");
                return null;
            }

            return field;
        }

        /// <summary>등록표를 스키마에 대해 전수 검증한다. 위반 전체 목록과 함께 throw.</summary>
        public void Validate<TSession>(MessagingConfig<TSession> config) where TSession : Session
        {
            var errors = new List<string>();

            foreach (var requestCase in RequestMap.Cases)
            {
                if (!config.Registrations.ContainsKey(requestCase.PayloadType))
                {
                    errors.Add($"핸들러 미등록: {requestCase.Name} ({requestCase.PayloadType.Name})");
                }
            }

            foreach (var pair in config.Registrations)
            {
                var registration = pair.Value;
                var requestCase = RequestMap.ByPayloadType(registration.RequestType);
                if (requestCase == null)
                {
                    errors.Add($"Request oneof에 없는 타입 등록: {registration.RequestType.Name}");
                    continue;
                }

                var responseCase = ResponseMap.ByFieldNumber(requestCase.FieldNumber);
                if (responseCase == null || responseCase.Name != requestCase.Name)
                {
                    errors.Add(
                        $"응답 케이스 불일치: {requestCase.Name}(#{requestCase.FieldNumber}) — " +
                        "Response.body에 같은 이름·번호의 케이스 필요");
                }
                else if (responseCase.PayloadType != registration.ResponseType)
                {
                    errors.Add(
                        $"응답 타입 불일치: {requestCase.Name} — 등록 {registration.ResponseType.Name}, " +
                        $"스키마 {responseCase.PayloadType.Name}");
                }
            }

            if (errors.Count > 0)
            {
                throw new MessagingValidationException(errors);
            }
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~MessagingValidationTests"` → 6/6.
전체: `dotnet test server/tests/Bun3.Server.Tests` → 53/53.

- [ ] **Step 5: Commit**

```
git add server/
git commit -m "✨ Add messaging schema maps, handler registry, and boot validation" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: 서버 런타임 + MessagingSession + MessagingServer

**Files:**
- Create: `server/src/Bun3.Server.Messaging/MessagingServerOptions.cs`
- Create: `server/src/Bun3.Server.Messaging/MessagingSession.cs`
- Create: `server/src/Bun3.Server.Messaging/MessagingRuntime.cs`
- Create: `server/src/Bun3.Server.Messaging/MessagingServer.cs`
- Modify: `server/tests/Bun3.Server.Tests/Helpers/FakeTransport.cs` (송신 캡처 재도입 — 이번엔 소비자가 있음)
- Test: `server/tests/Bun3.Server.Tests/MessagingServerTests.cs`

**Interfaces:**
- Consumes: Task 4 전부, v0 `Session`/`ServerBase`(무변경), `Channels`, ControlMessages
- Produces:
  - `MessagingServerOptions { TimeSpan? IdleKickTimeout = 120초(null=비활성); int MaxQueuedPackets = 256; }`
  - `abstract class MessagingSession : Session` — ctor `(IConnection)`; `OnPacketAsync`/`OnConnectedAsync`/`OnDisconnectedAsync`는 **sealed**; 게임 훅은 `protected virtual ValueTask OnSessionOpenedAsync()` / `OnSessionClosedAsync(Exception? error)`; `protected override ErrorDecision OnHandlerError(Exception) => Continue`(메시징 기본 — 게임 재정의 가능); `public ValueTask SendUpdateAsync(IMessage update)`; internal `AttachRuntime(IMessagingRuntime)`, `RaiseHandlerError(Exception)`
  - internal `IMessagingRuntime { ValueTask ProcessPacketAsync(MessagingSession, ReadOnlyMemory<byte>); ValueTask SendUpdateAsync(Session, IMessage); TimeSpan? IdleKickTimeout { get; } ILogger Logger { get; } }`
  - `sealed class MessagingServer<TSession, TRequest, TResponse, TUpdate> : ServerBase<TSession> where TSession : MessagingSession` — ctor `(ITransportListener transport, Func<IConnection, TSession> sessionFactory, MessagingConfig<TSession> config, MessagingServerOptions? options = null, ILogger? logger = null)` — **ctor에서 Schema.Create + Validate 실행(기동 fail-fast)**; Task 7/8이 사용
  - FakeConnection에 `public readonly ConcurrentQueue<byte[]> SentPackets`, `public readonly SemaphoreSlim SentSignal` 추가 (SendAsync가 기록+신호)

- [ ] **Step 1: FakeConnection 송신 캡처 재도입**

`Helpers/FakeTransport.cs`의 `FakeConnection`에 필드 추가 + `SendAsync` 수정:

```csharp
    public readonly ConcurrentQueue<byte[]> SentPackets = new();
    public readonly SemaphoreSlim SentSignal = new(0);

    public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct = default)
    {
        if (IsOpen)
        {
            SentPackets.Enqueue(packet.ToArray());
            SentSignal.Release();
        }
        return default;
    }
```

- [ ] **Step 2: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/MessagingServerTests.cs`:

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Messaging;
using Bun3.Server.Messaging.ControlMessages;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Tests.Helpers;
using Google.Protobuf;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class MessagingServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class TestSession : MessagingSession
    {
        public readonly TaskCompletionSource<Exception?> Closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TestSession(IConnection connection) : base(connection) { }

        protected override ValueTask OnSessionClosedAsync(Exception? error)
        {
            Closed.TrySetResult(error);
            return default;
        }
    }

    private static MessagingConfig<TestSession> DefaultConfig()
    {
        var config = new MessagingConfig<TestSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse { UnixMs = 123 }));
        config.OnRequest<BuyItemRequest, BuyItemResponse>((s, req) =>
        {
            if (req.ItemId == 666) throw new InvalidOperationException("boom");
            if (req.ItemId == 1) return new ValueTask<Reply<BuyItemResponse>>(Reply.Fail(-1001));
            return new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse { RemainingGold = 1000 + req.ItemId });
        });
        return config;
    }

    private static async Task<(MessagingServer<TestSession, Request, Response, Update> server, FakeTransport transport)>
        StartAsync(MessagingServerOptions? options = null, MessagingConfig<TestSession>? config = null)
    {
        var transport = new FakeTransport();
        var server = new MessagingServer<TestSession, Request, Response, Update>(
            transport, conn => new TestSession(conn), config ?? DefaultConfig(), options);
        await server.StartAsync();
        return (server, transport);
    }

    private static byte[] Wrap(byte channel, IMessage message)
    {
        var body = message.ToByteArray();
        var packet = new byte[1 + body.Length];
        packet[0] = channel;
        body.CopyTo(packet, 1);
        return packet;
    }

    private static async Task<(byte Channel, T Message)> NextSentAsync<T>(FakeConnection conn, MessageParser<T> parser)
        where T : class, IMessage<T>
    {
        await conn.SentSignal.WaitAsync(Timeout);
        Assert.That(conn.SentPackets.TryDequeue(out var packet), Is.True);
        return (packet![0], parser.ParseFrom(packet.AsSpan(1).ToArray()));
    }

    [Test]
    public async Task Request_roundtrip_returns_ok_response_with_same_request_id()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);

        conn.ReceivePacket(Wrap(Channels.Request, new Request
        {
            RequestId = 7,
            GetServerTime = new GetServerTimeRequest(),
        }));

        var (channel, response) = await NextSentAsync(conn, Response.Parser);
        Assert.That(channel, Is.EqualTo(Channels.Response));
        Assert.That(response.RequestId, Is.EqualTo(7));
        Assert.That(response.Status, Is.EqualTo(0));
        Assert.That(response.GetServerTime.UnixMs, Is.EqualTo(123));
        await server.StopAsync();
    }

    [Test]
    public async Task Failed_reply_returns_status_without_body()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);

        conn.ReceivePacket(Wrap(Channels.Request, new Request { RequestId = 8, BuyItem = new BuyItemRequest { ItemId = 1 } }));

        var (_, response) = await NextSentAsync(conn, Response.Parser);
        Assert.That(response.Status, Is.EqualTo(-1001));
        Assert.That(response.BodyCase, Is.EqualTo(Response.BodyOneofCase.None));
        await server.StopAsync();
    }

    [Test]
    public async Task Handler_exception_returns_status_2_and_keeps_session()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);

        conn.ReceivePacket(Wrap(Channels.Request, new Request { RequestId = 9, BuyItem = new BuyItemRequest { ItemId = 666 } }));
        var (_, errorResponse) = await NextSentAsync(conn, Response.Parser);
        Assert.That(errorResponse.Status, Is.EqualTo(2));
        Assert.That(conn.IsOpen, Is.True);

        // 세션이 살아 있어 후속 요청이 정상 처리된다
        conn.ReceivePacket(Wrap(Channels.Request, new Request { RequestId = 10, GetServerTime = new GetServerTimeRequest() }));
        var (_, next) = await NextSentAsync(conn, Response.Parser);
        Assert.That(next.Status, Is.EqualTo(0));
        await server.StopAsync();
    }

    [Test]
    public async Task Unknown_channel_kicks_the_session()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);
        var session = (TestSession)server.Sessions.Single();

        conn.ReceivePacket(new byte[] { 0x7F, 1, 2, 3 });

        await session.Closed.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
        await server.StopAsync();
    }

    [Test]
    public async Task Malformed_request_body_kicks_the_session()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);
        var session = (TestSession)server.Sessions.Single();

        conn.ReceivePacket(new byte[] { Channels.Request, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });

        await session.Closed.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
        await server.StopAsync();
    }

    [Test]
    public async Task Client_sending_response_channel_is_a_violation()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);
        var session = (TestSession)server.Sessions.Single();

        conn.ReceivePacket(Wrap(Channels.Response, new Response { RequestId = 1 }));

        await session.Closed.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
        await server.StopAsync();
    }

    [Test]
    public async Task Ping_is_answered_with_echoing_pong()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);

        conn.ReceivePacket(Wrap(Channels.Control, new Control { Ping = new Ping { ClientTimeUnixMs = 555 } }));

        var (channel, control) = await NextSentAsync(conn, Control.Parser);
        Assert.That(channel, Is.EqualTo(Channels.Control));
        Assert.That(control.BodyCase, Is.EqualTo(Control.BodyOneofCase.Pong));
        Assert.That(control.Pong.ClientTimeUnixMs, Is.EqualTo(555));
        Assert.That(conn.IsOpen, Is.True);
        await server.StopAsync();
    }

    [Test]
    public async Task SendUpdateAsync_wraps_payload_into_update_envelope()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);
        var session = (TestSession)server.Sessions.Single();

        await session.SendUpdateAsync(new BroadcastedUpdate { Text = "hi" });

        var (channel, update) = await NextSentAsync(conn, Update.Parser);
        Assert.That(channel, Is.EqualTo(Channels.Update));
        Assert.That(update.Broadcasted.Text, Is.EqualTo("hi"));
        await server.StopAsync();
    }

    [Test]
    public async Task SendUpdateAsync_with_type_outside_oneof_throws()
    {
        var (server, transport) = await StartAsync();
        transport.Connect(1);
        var session = (TestSession)server.Sessions.Single();

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await session.SendUpdateAsync(new BuyItemRequest()));
        await server.StopAsync();
    }

    [Test]
    public void Incomplete_config_fails_server_construction()
    {
        var config = new MessagingConfig<TestSession>();  // 아무 핸들러 없음
        Assert.Throws<MessagingValidationException>(() =>
            new MessagingServer<TestSession, Request, Response, Update>(
                new FakeTransport(), conn => new TestSession(conn), config));
    }

    private sealed class StrictSession : MessagingSession
    {
        public readonly TaskCompletionSource<Exception?> Closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StrictSession(IConnection connection) : base(connection) { }

        protected override ErrorDecision OnHandlerError(Exception ex) => ErrorDecision.CloseSession;

        protected override ValueTask OnSessionClosedAsync(Exception? error)
        {
            Closed.TrySetResult(error);
            return default;
        }
    }

    [Test]
    public async Task OnHandlerError_override_can_close_instead_of_status2()
    {
        var transport = new FakeTransport();
        var config = new MessagingConfig<StrictSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse()));
        config.OnRequest<BuyItemRequest, BuyItemResponse>(
            (s, req) => throw new InvalidOperationException("boom"));
        var server = new MessagingServer<StrictSession, Request, Response, Update>(
            transport, conn => new StrictSession(conn), config);
        await server.StartAsync();
        var conn = transport.Connect(1);
        var session = server.Sessions.Single();

        conn.ReceivePacket(Wrap(Channels.Request, new Request { RequestId = 1, BuyItem = new BuyItemRequest { ItemId = 666 } }));

        await session.Closed.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
        Assert.That(conn.SentPackets.IsEmpty, Is.True);   // 응답 없이 종료
        await server.StopAsync();
    }

    [Test]
    public async Task Idle_session_is_kicked_after_timeout()
    {
        var (server, transport) = await StartAsync(new MessagingServerOptions
        {
            IdleKickTimeout = TimeSpan.FromMilliseconds(200),
        });
        var conn = transport.Connect(1);
        var session = (TestSession)server.Sessions.Single();

        await session.Closed.Task.WaitAsync(Timeout);   // 패킷 없이 방치 → 킥
        Assert.That(conn.IsOpen, Is.False);
        await server.StopAsync();
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~MessagingServerTests"`
Expected: 컴파일 에러 — `MessagingSession`/`MessagingServer`/`MessagingServerOptions` 미정의

- [ ] **Step 4: 구현**

`server/src/Bun3.Server.Messaging/MessagingServerOptions.cs`:

```csharp
using System;

namespace Bun3.Server.Messaging
{
    public sealed class MessagingServerOptions
    {
        /// <summary>이 시간 동안 아무 패킷도 안 온 세션을 킥한다. null = 비활성.</summary>
        public TimeSpan? IdleKickTimeout { get; set; } = TimeSpan.FromSeconds(120);

        /// <summary>세션 수신 큐 상한 (v0 Session과 동일 의미).</summary>
        public int MaxQueuedPackets { get; set; } = 256;
    }
}
```

`server/src/Bun3.Server.Messaging/MessagingSession.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Messaging
{
    /// <summary>
    /// 메시징 계층의 세션 베이스. 원시 패킷 처리(OnPacketAsync)는 프레임워크가 소유하고,
    /// 게임은 OnSessionOpenedAsync/OnSessionClosedAsync 훅과 등록된 핸들러로만 참여한다.
    /// </summary>
    public abstract class MessagingSession : Session
    {
        private IMessagingRuntime? _runtime;
        private CancellationTokenSource? _watchdogCts;
        private long _lastReceivedTicksUtc;

        protected MessagingSession(IConnection connection) : base(connection) { }

        /// <summary>메시징 기본값: 핸들러 예외는 status=2 응답 + 세션 유지. 게임이 재정의 가능.</summary>
        protected override ErrorDecision OnHandlerError(Exception ex) => ErrorDecision.Continue;

        /// <summary>연결 수립 훅 (v0 OnConnectedAsync 대체).</summary>
        protected virtual ValueTask OnSessionOpenedAsync() => default;

        /// <summary>세션 종료 훅 (v0 OnDisconnectedAsync 대체). 정상 종료면 error는 null.</summary>
        protected virtual ValueTask OnSessionClosedAsync(Exception? error) => default;

        /// <summary>서버 푸시. update는 게임 Update oneof의 케이스 타입이어야 한다.</summary>
        public ValueTask SendUpdateAsync(IMessage update) =>
            RequireRuntime().SendUpdateAsync(this, update);

        protected sealed override ValueTask OnConnectedAsync()
        {
            Volatile.Write(ref _lastReceivedTicksUtc, DateTime.UtcNow.Ticks);
            StartIdleWatchdog();
            return OnSessionOpenedAsync();
        }

        protected sealed override ValueTask OnPacketAsync(ReadOnlyMemory<byte> packet)
        {
            Volatile.Write(ref _lastReceivedTicksUtc, DateTime.UtcNow.Ticks);
            return RequireRuntime().ProcessPacketAsync(this, packet);
        }

        protected sealed override ValueTask OnDisconnectedAsync(Exception? error)
        {
            _watchdogCts?.Cancel();
            return OnSessionClosedAsync(error);
        }

        internal void AttachRuntime(IMessagingRuntime runtime) => _runtime = runtime;

        internal ErrorDecision RaiseHandlerError(Exception ex) => OnHandlerError(ex);

        private IMessagingRuntime RequireRuntime() =>
            _runtime ?? throw new InvalidOperationException(
                "런타임 미부착 — MessagingSession은 MessagingServer를 통해서만 생성되어야 한다.");

        private void StartIdleWatchdog()
        {
            var timeout = RequireRuntime().IdleKickTimeout;
            if (timeout == null)
            {
                return;
            }

            _watchdogCts = new CancellationTokenSource();
            _ = RunWatchdogAsync(timeout.Value, _watchdogCts.Token);
        }

        private async Task RunWatchdogAsync(TimeSpan timeout, CancellationToken ct)
        {
            var interval = TimeSpan.FromTicks(Math.Max(timeout.Ticks / 2, TimeSpan.TicksPerMillisecond * 50));
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    var last = new DateTime(Volatile.Read(ref _lastReceivedTicksUtc), DateTimeKind.Utc);
                    if (DateTime.UtcNow - last > timeout)
                    {
                        RequireRuntime().Logger.LogInformation(
                            "Session {SessionId}: idle for {Timeout}; kicking.", Id, timeout);
                        Kick();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 세션 종료로 인한 정상 취소
            }
        }
    }
}
```

`server/src/Bun3.Server.Messaging/MessagingRuntime.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Bun3.Server.Messaging.ControlMessages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Messaging
{
    /// <summary>MessagingSession이 패킷 처리를 위임하는 비제네릭 창구.</summary>
    internal interface IMessagingRuntime
    {
        TimeSpan? IdleKickTimeout { get; }
        ILogger Logger { get; }
        ValueTask ProcessPacketAsync(MessagingSession session, ReadOnlyMemory<byte> packet);
        ValueTask SendUpdateAsync(Session session, IMessage update);
    }

    /// <summary>채널 분기·요청 디스패치·응답 조립 — 서버 측 메시징의 두뇌. 상태는 전부 기동 시 구축.</summary>
    internal sealed class MessagingRuntime<TSession, TRequest, TResponse, TUpdate> : IMessagingRuntime
        where TSession : MessagingSession
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        private readonly MessagingSchema<TRequest, TResponse, TUpdate> _schema;
        private readonly Dictionary<Type, MessagingConfig<TSession>.Registration> _registrations;

        public MessagingRuntime(
            MessagingSchema<TRequest, TResponse, TUpdate> schema,
            MessagingConfig<TSession> config,
            MessagingServerOptions options,
            ILogger logger)
        {
            schema.Validate(config);   // 기동 fail-fast — 위반 전체 목록과 함께 throw
            _schema = schema;
            _registrations = config.Registrations;
            IdleKickTimeout = options.IdleKickTimeout;
            Logger = logger;
        }

        public TimeSpan? IdleKickTimeout { get; }

        public ILogger Logger { get; }

        public async ValueTask ProcessPacketAsync(MessagingSession session, ReadOnlyMemory<byte> packet)
        {
            if (packet.Length < 1)
            {
                Violation(session, "빈 패킷");
                return;
            }

            var channel = packet.Span[0];
            var body = packet.Slice(1);
            switch (channel)
            {
                case Channels.Control:
                    await HandleControlAsync(session, body).ConfigureAwait(false);
                    break;
                case Channels.Request:
                    await HandleRequestAsync(session, body).ConfigureAwait(false);
                    break;
                default:
                    Violation(session, $"허용되지 않은 채널 0x{channel:X2}");
                    break;
            }
        }

        public ValueTask SendUpdateAsync(Session session, IMessage update)
        {
            var updateCase = _schema.UpdateMap.ByPayloadType(update.GetType())
                ?? throw new ArgumentException($"Update oneof에 없는 타입: {update.GetType().Name}", nameof(update));
            var envelope = new TUpdate();
            updateCase.Set(envelope, update);
            return SendAsync(session, Channels.Update, envelope);
        }

        private async ValueTask HandleControlAsync(MessagingSession session, ReadOnlyMemory<byte> body)
        {
            Control control;
            try
            {
                control = Control.Parser.ParseFrom(body.ToArray());
            }
            catch (InvalidProtocolBufferException ex)
            {
                Violation(session, $"Control 파싱 실패: {ex.Message}");
                return;
            }

            if (control.BodyCase != Control.BodyOneofCase.Ping)
            {
                Violation(session, $"클라이언트가 보낼 수 없는 Control: {control.BodyCase}");
                return;
            }

            var pong = new Control { Pong = new Pong { ClientTimeUnixMs = control.Ping.ClientTimeUnixMs } };
            await SendAsync(session, Channels.Control, pong).ConfigureAwait(false);
        }

        private async ValueTask HandleRequestAsync(MessagingSession session, ReadOnlyMemory<byte> body)
        {
            TRequest envelope;
            try
            {
                envelope = _schema.RequestParser.ParseFrom(body.ToArray());
            }
            catch (InvalidProtocolBufferException ex)
            {
                Violation(session, $"Request 파싱 실패: {ex.Message}");
                return;
            }

            var requestId = (long)_schema.RequestIdOfRequest.Accessor.GetValue(envelope);
            var requestCase = _schema.RequestMap.GetActiveCase(envelope);
            if (requestCase == null)
            {
                Violation(session, "body 없는 Request");
                return;
            }

            int status;
            IMessage? responsePayload = null;
            if (!_registrations.TryGetValue(requestCase.PayloadType, out var registration))
            {
                status = 1;   // 기동 검증상 불가 — 방어
            }
            else
            {
                try
                {
                    (status, responsePayload) = await registration
                        .Invoke((TSession)session, requestCase.Get(envelope))
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (session.RaiseHandlerError(ex) == ErrorDecision.CloseSession)
                    {
                        Logger.LogError(ex,
                            "Session {SessionId}: handler exception on {Case}; closing per OnHandlerError.",
                            session.Id, requestCase.Name);
                        session.Kick();
                        return;
                    }

                    Logger.LogError(ex,
                        "Session {SessionId}: handler exception on {Case}; replying status 2.",
                        session.Id, requestCase.Name);
                    status = 2;
                }
            }

            var response = new TResponse();
            _schema.RequestIdOfResponse.Accessor.SetValue(response, requestId);
            _schema.StatusOfResponse.Accessor.SetValue(response, status);
            if (status == 0 && responsePayload != null)
            {
                _schema.ResponseMap.ByFieldNumber(requestCase.FieldNumber)!.Set(response, responsePayload);
            }

            await SendAsync(session, Channels.Response, response).ConfigureAwait(false);
        }

        private static ValueTask SendAsync(Session session, byte channel, IMessage message)
        {
            var body = message.ToByteArray();
            var packet = new byte[1 + body.Length];
            packet[0] = channel;
            body.CopyTo(packet, 1);
            return session.SendAsync(packet);
        }

        private void Violation(MessagingSession session, string reason)
        {
            Logger.LogWarning("Session {SessionId}: 프로토콜 위반 — {Reason}; kicking.", session.Id, reason);
            session.Kick();
        }
    }
}
```

`server/src/Bun3.Server.Messaging/MessagingServer.cs`:

```csharp
using System;
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Messaging
{
    /// <summary>
    /// 메시징 계층이 조립된 서버. 생성 시 스키마 구축과 등록표 전수 검증을 수행하므로
    /// 구성 오류는 기동 시점에 전체 목록과 함께 실패한다(fail-fast).
    /// </summary>
    public sealed class MessagingServer<TSession, TRequest, TResponse, TUpdate> : ServerBase<TSession>
        where TSession : MessagingSession
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        private readonly Func<IConnection, TSession> _sessionFactory;
        private readonly MessagingRuntime<TSession, TRequest, TResponse, TUpdate> _runtime;

        public MessagingServer(
            ITransportListener transport,
            Func<IConnection, TSession> sessionFactory,
            MessagingConfig<TSession> config,
            MessagingServerOptions? options = null,
            ILogger? logger = null)
            : base(transport, logger, (options ?? new MessagingServerOptions()).MaxQueuedPackets)
        {
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            var effectiveOptions = options ?? new MessagingServerOptions();
            _runtime = new MessagingRuntime<TSession, TRequest, TResponse, TUpdate>(
                MessagingSchema<TRequest, TResponse, TUpdate>.Create(),
                config,
                effectiveOptions,
                new SafeLogger(logger ?? NullLogger.Instance));
        }

        protected override TSession CreateSession(IConnection connection)
        {
            var session = _sessionFactory(connection);
            session.AttachRuntime(_runtime);
            return session;
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~MessagingServerTests"` → 12/12.
전체: `dotnet test server/tests/Bun3.Server.Tests` → 65/65 (기존 회귀 없음 — 특히 SessionActorTests가 FakeConnection 변경에 영향 없는지 확인).

- [ ] **Step 6: Commit**

```
git add server/
git commit -m "✨ Add server-side messaging runtime, MessagingSession, MessagingServer" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: MessagingClient + 인메모리 듀플렉스 테스트

**Files:**
- Create: `server/src/Bun3.Server.Messaging/MessagingClientOptions.cs`
- Create: `server/src/Bun3.Server.Messaging/MessagingClient.cs`
- Create: `server/tests/Bun3.Server.Tests/Helpers/InMemoryDuplex.cs`
- Test: `server/tests/Bun3.Server.Tests/MessagingClientTests.cs`

**Interfaces:**
- Consumes: Task 2 `IConnector`, Task 4 `MessagingSchema`/`OneofMap`, Task 3 `Reply`/`Channels`/`ConnectionClosedException`, ControlMessages
- Produces:
  - `MessagingClientOptions { TimeSpan RequestTimeout = 10초; TimeSpan? PingInterval = 30초(null=비활성); bool UseSynchronizationContext = true; }`
  - `sealed class MessagingClient<TRequest, TResponse, TUpdate>` (루트 3형 각각 class, IMessage<T>, new()) — `static ValueTask<MessagingClient<...>> ConnectAsync(IConnector connector, MessagingClientOptions? options = null, ILogger? logger = null, CancellationToken ct = default)`; `ValueTask<Reply<TRes>> RequestAsync<TRes>(IMessage request, CancellationToken ct = default)`; `void OnUpdate<TUpd>(Action<TUpd> handler)` (같은 타입 재등록 = 교체); `long LastRttMs`(-1 초기); `bool IsConnected`; `event Action<Exception?>? Closed`; `void Close()`
  - 인프라 실패는 예외(`TimeoutException`/`OperationCanceledException`/`ConnectionClosedException`), 서버 판정은 `Reply` 값
  - 테스트 헬퍼: `InMemoryConnector : IConnector`(서버측 IConnectionHandler를 받아 동기 듀플렉스 페어 생성, `ServerConnection` 노출), `DuplexConnection : IConnection` — Task 7 이전의 클라 로직 검증용

- [ ] **Step 1: InMemoryDuplex 헬퍼 작성**

`server/tests/Bun3.Server.Tests/Helpers/InMemoryDuplex.cs`:

```csharp
using Bun3.Server.Abstractions;

namespace Bun3.Server.Tests.Helpers;

/// <summary>클라↔서버 양끝을 동기로 잇는 인메모리 커넥터. MessagingClient 단위 검증용.</summary>
public sealed class InMemoryConnector : IConnector
{
    private readonly IConnectionHandler _serverHandler;

    public InMemoryConnector(IConnectionHandler serverHandler) => _serverHandler = serverHandler;

    public DuplexConnection? ServerConnection { get; private set; }

    public ValueTask<IConnection> ConnectAsync(IConnectionHandler clientHandler, CancellationToken ct = default)
    {
        var link = new DuplexLink();
        var client = new DuplexConnection(1, link, clientHandler);
        var server = new DuplexConnection(2, link, _serverHandler);
        client.Peer = server;
        server.Peer = client;
        ServerConnection = server;

        clientHandler.OnConnected(client);
        _serverHandler.OnConnected(server);
        return new ValueTask<IConnection>(client);
    }
}

internal sealed class DuplexLink
{
    private int _closed;

    public bool TryClose() => Interlocked.Exchange(ref _closed, 1) == 0;

    public bool IsClosed => Volatile.Read(ref _closed) != 0;
}

public sealed class DuplexConnection : IConnection
{
    private readonly DuplexLink _link;
    private readonly IConnectionHandler _handler;

    internal DuplexConnection(long id, DuplexLink link, IConnectionHandler handler)
    {
        Id = id;
        _link = link;
        _handler = handler;
    }

    internal DuplexConnection? Peer { get; set; }

    public long Id { get; }
    public string? RemoteAddress => "in-memory";
    public bool IsOpen => !_link.IsClosed;

    public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct = default)
    {
        if (IsOpen && Peer != null)
        {
            // 상대편 핸들러에 동기 전달 — 버퍼는 호출 동안만 유효 계약 그대로
            Peer._handler.OnPacket(Peer, packet);
        }
        return default;
    }

    public void Close()
    {
        if (!_link.TryClose())
        {
            return;
        }

        _handler.OnClosed(this, null);
        Peer?._handler.OnClosed(Peer!, null);
    }
}
```

- [ ] **Step 2: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/MessagingClientTests.cs`:

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Messaging;
using Bun3.Server.Messaging.ControlMessages;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Tests.Helpers;
using Google.Protobuf;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class MessagingClientTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>수신 원시 패킷마다 콜백을 실행하는 스크립트형 서버 대역.</summary>
    private sealed class ScriptedResponder : IConnectionHandler
    {
        public Action<IConnection, byte[]>? OnPacketReceived;
        public IConnection? Connection;

        public void OnConnected(IConnection connection) => Connection = connection;

        public void OnPacket(IConnection connection, ReadOnlyMemory<byte> packet) =>
            OnPacketReceived?.Invoke(connection, packet.ToArray());

        public void OnClosed(IConnection connection, Exception? error) { }
    }

    private static byte[] Wrap(byte channel, IMessage message)
    {
        var body = message.ToByteArray();
        var packet = new byte[1 + body.Length];
        packet[0] = channel;
        body.CopyTo(packet, 1);
        return packet;
    }

    private static Task<MessagingClient<Request, Response, Update>> ConnectAsync(
        ScriptedResponder responder, MessagingClientOptions? options = null)
    {
        return MessagingClient<Request, Response, Update>
            .ConnectAsync(new InMemoryConnector(responder), options).AsTask();
    }

    private static void RespondOk(IConnection serverConn, byte[] packet)
    {
        var request = Request.Parser.ParseFrom(packet.AsSpan(1).ToArray());
        var response = new Response { RequestId = request.RequestId, Status = 0 };
        if (request.BodyCase == Request.BodyOneofCase.GetServerTime)
        {
            response.GetServerTime = new GetServerTimeResponse { UnixMs = 42 };
        }
        else
        {
            response.BuyItem = new BuyItemResponse { RemainingGold = 1000 + request.BuyItem.ItemId };
        }
        _ = serverConn.SendAsync(Wrap(Channels.Response, response));
    }

    [Test]
    public async Task Request_response_roundtrip()
    {
        var responder = new ScriptedResponder { OnPacketReceived = RespondOk };
        var client = await ConnectAsync(responder);

        var reply = await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
            .AsTask().WaitAsync(Timeout);

        Assert.That(reply.IsOk, Is.True);
        Assert.That(reply.Value!.UnixMs, Is.EqualTo(42));
    }

    [Test]
    public async Task Failed_status_arrives_as_reply_value()
    {
        var responder = new ScriptedResponder();
        responder.OnPacketReceived = (conn, packet) =>
        {
            var request = Request.Parser.ParseFrom(packet.AsSpan(1).ToArray());
            _ = conn.SendAsync(Wrap(Channels.Response, new Response { RequestId = request.RequestId, Status = -7 }));
        };
        var client = await ConnectAsync(responder);

        var reply = await client.RequestAsync<BuyItemResponse>(new BuyItemRequest { ItemId = 3 })
            .AsTask().WaitAsync(Timeout);

        Assert.That(reply.Status, Is.EqualTo(-7));
        Assert.That(reply.Value, Is.Null);
    }

    [Test]
    public async Task Silent_server_causes_TimeoutException()
    {
        var responder = new ScriptedResponder();   // 응답하지 않음
        var client = await ConnectAsync(responder, new MessagingClientOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(200),
        });

        Assert.ThrowsAsync<TimeoutException>(async () =>
            await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
                .AsTask().WaitAsync(Timeout));
    }

    [Test]
    public async Task Connection_close_fails_pending_requests()
    {
        var responder = new ScriptedResponder();
        responder.OnPacketReceived = (conn, _) => conn.Close();   // 응답 대신 끊음
        var client = await ConnectAsync(responder);

        Assert.ThrowsAsync<ConnectionClosedException>(async () =>
            await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
                .AsTask().WaitAsync(Timeout));
        Assert.That(client.IsConnected, Is.False);
    }

    [Test]
    public async Task Registered_update_handler_receives_push()
    {
        var responder = new ScriptedResponder();
        var connector = new InMemoryConnector(responder);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var client = await MessagingClient<Request, Response, Update>.ConnectAsync(connector).AsTask();
        client.OnUpdate<BroadcastedUpdate>(u => received.TrySetResult(u.Text));

        _ = connector.ServerConnection!.SendAsync(
            Wrap(Channels.Update, new Update { Broadcasted = new BroadcastedUpdate { Text = "hello" } }));

        Assert.That(await received.Task.WaitAsync(Timeout), Is.EqualTo("hello"));
    }

    [Test]
    public async Task Unregistered_update_is_ignored_without_closing()
    {
        var responder = new ScriptedResponder { OnPacketReceived = RespondOk };
        var connector = new InMemoryConnector(responder);
        var client = await MessagingClient<Request, Response, Update>.ConnectAsync(connector).AsTask();

        _ = connector.ServerConnection!.SendAsync(
            Wrap(Channels.Update, new Update { Broadcasted = new BroadcastedUpdate { Text = "nobody listens" } }));

        // 여전히 정상 동작
        var reply = await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
            .AsTask().WaitAsync(Timeout);
        Assert.That(reply.IsOk, Is.True);
        Assert.That(client.IsConnected, Is.True);
    }

    [Test]
    public async Task Ping_loop_measures_rtt()
    {
        var responder = new ScriptedResponder();
        responder.OnPacketReceived = (conn, packet) =>
        {
            if (packet[0] != Channels.Control) return;
            var control = Control.Parser.ParseFrom(packet.AsSpan(1).ToArray());
            if (control.BodyCase != Control.BodyOneofCase.Ping) return;
            _ = conn.SendAsync(Wrap(Channels.Control, new Control
            {
                Pong = new Pong { ClientTimeUnixMs = control.Ping.ClientTimeUnixMs },
            }));
        };
        var client = await ConnectAsync(responder, new MessagingClientOptions
        {
            PingInterval = TimeSpan.FromMilliseconds(100),
        });

        for (var i = 0; i < 50 && client.LastRttMs < 0; i++)
        {
            await Task.Delay(50);
        }

        Assert.That(client.LastRttMs, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task Mismatched_TRes_throws_ArgumentException()
    {
        var responder = new ScriptedResponder();
        var client = await ConnectAsync(responder);

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.RequestAsync<BuyItemResponse>(new GetServerTimeRequest()));
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~MessagingClientTests"`
Expected: 컴파일 에러 — `MessagingClient`/`MessagingClientOptions` 미정의

- [ ] **Step 4: 구현**

`server/src/Bun3.Server.Messaging/MessagingClientOptions.cs`:

```csharp
using System;

namespace Bun3.Server.Messaging
{
    public sealed class MessagingClientOptions
    {
        /// <summary>요청별 응답 대기 기한. 초과 시 해당 요청만 TimeoutException.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Ping 주기. null = 비활성.</summary>
        public TimeSpan? PingInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>true면 접속 시점의 SynchronizationContext로 푸시 콜백·Closed 이벤트를 올린다(Unity 메인 스레드).</summary>
        public bool UseSynchronizationContext { get; set; } = true;
    }
}
```

`server/src/Bun3.Server.Messaging/MessagingClient.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Messaging.ControlMessages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Messaging
{
    /// <summary>
    /// 타입 있는 요청/응답과 푸시 구독을 제공하는 클라이언트.
    /// 서버 판정은 Reply 값으로, 인프라 실패(타임아웃·연결 종료)는 예외로 구분된다.
    /// </summary>
    public sealed class MessagingClient<TRequest, TResponse, TUpdate>
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        private sealed class Pending
        {
            public readonly TaskCompletionSource<(int Status, IMessage? Payload)> Tcs =
                new TaskCompletionSource<(int, IMessage?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly MessagingSchema<TRequest, TResponse, TUpdate> _schema;
        private readonly MessagingClientOptions _options;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<long, Pending> _pending =
            new ConcurrentDictionary<long, Pending>();
        private readonly ConcurrentDictionary<Type, Action<IMessage>> _updateHandlers =
            new ConcurrentDictionary<Type, Action<IMessage>>();
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();

        private IConnection? _connection;
        private SynchronizationContext? _syncContext;
        private long _nextRequestId;
        private long _lastRttMs = -1;
        private volatile bool _closed;

        private MessagingClient(MessagingClientOptions options, ILogger logger)
        {
            _schema = MessagingSchema<TRequest, TResponse, TUpdate>.Create();
            _options = options;
            _logger = logger;
        }

        /// <summary>마지막 Ping의 왕복 시간(ms). 측정 전에는 -1.</summary>
        public long LastRttMs => Volatile.Read(ref _lastRttMs);

        public bool IsConnected => !_closed && _connection?.IsOpen == true;

        /// <summary>연결 종료 시 1회. 정상 종료면 null. UseSynchronizationContext 시 캡처 컨텍스트에서 호출.</summary>
        public event Action<Exception?>? Closed;

        public static async ValueTask<MessagingClient<TRequest, TResponse, TUpdate>> ConnectAsync(
            IConnector connector,
            MessagingClientOptions? options = null,
            ILogger? logger = null,
            CancellationToken ct = default)
        {
            if (connector == null)
            {
                throw new ArgumentNullException(nameof(connector));
            }

            var client = new MessagingClient<TRequest, TResponse, TUpdate>(
                options ?? new MessagingClientOptions(),
                new SafeLogger(logger ?? NullLogger.Instance));
            if (client._options.UseSynchronizationContext)
            {
                client._syncContext = SynchronizationContext.Current;
            }

            client._connection = await connector.ConnectAsync(new Handler(client), ct).ConfigureAwait(false);
            client.StartPingLoop();
            return client;
        }

        /// <summary>요청을 보내고 응답을 기다린다. 서버 판정은 Reply로, 인프라 실패는 예외로 온다.</summary>
        public async ValueTask<Reply<TRes>> RequestAsync<TRes>(IMessage request, CancellationToken ct = default)
            where TRes : class, IMessage<TRes>
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (_closed)
            {
                throw new ConnectionClosedException("이미 종료된 연결");
            }

            var requestCase = _schema.RequestMap.ByPayloadType(request.GetType())
                ?? throw new ArgumentException($"Request oneof에 없는 타입: {request.GetType().Name}", nameof(request));
            var responseCase = _schema.ResponseMap.ByFieldNumber(requestCase.FieldNumber);
            if (responseCase != null && responseCase.PayloadType != typeof(TRes))
            {
                throw new ArgumentException(
                    $"{requestCase.Name}의 응답 타입은 {responseCase.PayloadType.Name} — TRes 불일치", nameof(TRes));
            }

            var requestId = Interlocked.Increment(ref _nextRequestId);
            var envelope = new TRequest();
            _schema.RequestIdOfRequest.Accessor.SetValue(envelope, requestId);
            requestCase.Set(envelope, request);

            var pending = new Pending();
            _pending[requestId] = pending;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.RequestTimeout);
            using var registration = timeoutCts.Token.Register(() =>
            {
                if (_pending.TryRemove(requestId, out var removed))
                {
                    if (ct.IsCancellationRequested)
                    {
                        removed.Tcs.TrySetCanceled(ct);
                    }
                    else
                    {
                        removed.Tcs.TrySetException(
                            new TimeoutException($"요청 {requestId} 응답 없음 ({_options.RequestTimeout})"));
                    }
                }
            });

            await SendAsync(Channels.Request, envelope).ConfigureAwait(false);
            var (status, payload) = await pending.Tcs.Task.ConfigureAwait(false);

            if (status != 0)
            {
                return Reply<TRes>.Fail(status);
            }

            return payload is TRes typed
                ? Reply<TRes>.Ok(typed)
                : throw new InvalidOperationException(
                    $"응답 본문 타입 불일치: {payload?.GetType().Name ?? "없음"} (기대: {typeof(TRes).Name})");
        }

        /// <summary>푸시 구독. 같은 타입 재등록은 교체된다. 미등록 Update는 경고 로그 후 무시.</summary>
        public void OnUpdate<TUpd>(Action<TUpd> handler) where TUpd : class, IMessage<TUpd>
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _updateHandlers[typeof(TUpd)] = message => handler((TUpd)message);
        }

        public void Close() => _connection?.Close();

        private void HandlePacket(ReadOnlyMemory<byte> packet)
        {
            if (packet.Length < 1)
            {
                ViolationClose("빈 패킷");
                return;
            }

            var channel = packet.Span[0];
            var body = packet.Slice(1).ToArray();
            switch (channel)
            {
                case Channels.Response:
                    HandleResponse(body);
                    break;
                case Channels.Update:
                    HandleUpdate(body);
                    break;
                case Channels.Control:
                    HandleControl(body);
                    break;
                default:
                    ViolationClose($"허용되지 않은 채널 0x{channel:X2}");
                    break;
            }
        }

        private void HandleResponse(byte[] body)
        {
            TResponse envelope;
            try
            {
                envelope = _schema.ResponseParser.ParseFrom(body);
            }
            catch (InvalidProtocolBufferException ex)
            {
                ViolationClose($"Response 파싱 실패: {ex.Message}");
                return;
            }

            var requestId = (long)_schema.RequestIdOfResponse.Accessor.GetValue(envelope);
            if (!_pending.TryRemove(requestId, out var pending))
            {
                _logger.LogWarning("대응 없는 응답 request_id={RequestId} — 무시", requestId);
                return;
            }

            var status = (int)_schema.StatusOfResponse.Accessor.GetValue(envelope);
            var payload = status == 0 ? _schema.ResponseMap.GetActiveCase(envelope)?.Get(envelope) : null;
            pending.Tcs.TrySetResult((status, payload));
        }

        private void HandleUpdate(byte[] body)
        {
            TUpdate envelope;
            try
            {
                envelope = _schema.UpdateParser.ParseFrom(body);
            }
            catch (InvalidProtocolBufferException ex)
            {
                ViolationClose($"Update 파싱 실패: {ex.Message}");
                return;
            }

            var updateCase = _schema.UpdateMap.GetActiveCase(envelope);
            if (updateCase == null)
            {
                _logger.LogWarning("body 없는 Update — 무시");
                return;
            }

            if (!_updateHandlers.TryGetValue(updateCase.PayloadType, out var handler))
            {
                _logger.LogWarning("미등록 Update {Case} — 무시", updateCase.Name);
                return;
            }

            var payload = updateCase.Get(envelope);
            Dispatch(() => handler(payload));
        }

        private void HandleControl(byte[] body)
        {
            Control control;
            try
            {
                control = Control.Parser.ParseFrom(body);
            }
            catch (InvalidProtocolBufferException ex)
            {
                ViolationClose($"Control 파싱 실패: {ex.Message}");
                return;
            }

            if (control.BodyCase == Control.BodyOneofCase.Pong)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Volatile.Write(ref _lastRttMs, Math.Max(0, now - control.Pong.ClientTimeUnixMs));
            }
            else
            {
                _logger.LogWarning("예상 밖 Control {Case} — 무시", control.BodyCase);
            }
        }

        private void HandleClosed(Exception? error)
        {
            _closed = true;
            _lifetimeCts.Cancel();
            foreach (var pair in _pending)
            {
                if (_pending.TryRemove(pair.Key, out var pending))
                {
                    pending.Tcs.TrySetException(new ConnectionClosedException("응답 대기 중 연결 종료"));
                }
            }

            Dispatch(() => Closed?.Invoke(error));
        }

        private void StartPingLoop()
        {
            var interval = _options.PingInterval;
            if (interval == null)
            {
                return;
            }

            _ = RunPingLoopAsync(interval.Value, _lifetimeCts.Token);
        }

        private async Task RunPingLoopAsync(TimeSpan interval, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    var ping = new Control
                    {
                        Ping = new Ping { ClientTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                    };
                    await SendAsync(Channels.Control, ping).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 연결 종료로 인한 정상 취소
            }
        }

        private ValueTask SendAsync(byte channel, IMessage message)
        {
            var connection = _connection;
            if (connection == null || _closed)
            {
                return default;
            }

            var body = message.ToByteArray();
            var packet = new byte[1 + body.Length];
            packet[0] = channel;
            body.CopyTo(packet, 1);
            return connection.SendAsync(packet);
        }

        private void Dispatch(Action action)
        {
            var context = _syncContext;
            if (context != null)
            {
                context.Post(_ => Run(action), null);
            }
            else
            {
                Run(action);
            }

            void Run(Action inner)
            {
                try
                {
                    inner();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "푸시/이벤트 콜백 예외");
                }
            }
        }

        private void ViolationClose(string reason)
        {
            _logger.LogWarning("프로토콜 위반 — {Reason}; 연결 종료", reason);
            _connection?.Close();
        }

        private sealed class Handler : IConnectionHandler
        {
            private readonly MessagingClient<TRequest, TResponse, TUpdate> _client;

            public Handler(MessagingClient<TRequest, TResponse, TUpdate> client) => _client = client;

            public void OnConnected(IConnection connection) => _client._connection = connection;

            public void OnPacket(IConnection connection, ReadOnlyMemory<byte> packet) =>
                _client.HandlePacket(packet);

            public void OnClosed(IConnection connection, Exception? error) => _client.HandleClosed(error);
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~MessagingClientTests"` → 8/8.
전체: `dotnet test server/tests/Bun3.Server.Tests` → 73/73.

- [ ] **Step 6: Commit**

```
git add server/
git commit -m "✨ Add MessagingClient with request correlation, updates, ping loop" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: 실 TCP E2E — 미니 프로토콜 5종 (v1 완료 조건)

**Files:**
- Test: `server/tests/Bun3.Server.Tests/MessagingE2ETests.cs`

**Interfaces:**
- Consumes: Task 2 `TcpConnector`, Task 5 `MessagingServer`/`MessagingSession`, Task 6 `MessagingClient` — 신규 프로덕션 코드 없음
- Produces: 없음 (v1 인수 검증). 실패 시 테스트를 고치지 말고 superpowers:systematic-debugging으로 원인 추적 — 프로덕션 수정이 필요하면 DONE_WITH_CONCERNS로 상세 보고.

- [ ] **Step 1: E2E 테스트 작성**

`server/tests/Bun3.Server.Tests/MessagingE2ETests.cs`:

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Messaging;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class MessagingE2ETests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class E2ESession : MessagingSession
    {
        public E2ESession(IConnection connection) : base(connection) { }

        protected override ValueTask OnSessionOpenedAsync() =>
            SendUpdateAsync(new BroadcastedUpdate { Text = "welcome" });
    }

    private static MessagingConfig<E2ESession> Config()
    {
        var config = new MessagingConfig<E2ESession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse { UnixMs = 777 }));
        config.OnRequest<BuyItemRequest, BuyItemResponse>((s, req) =>
            req.ItemId == 1
                ? new ValueTask<Reply<BuyItemResponse>>(Reply.Fail(-1001))
                : new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse { RemainingGold = 1000 + req.ItemId }));
        return config;
    }

    private static async Task<(MessagingServer<E2ESession, Request, Response, Update> server, TcpTransportListener listener)>
        StartServerAsync()
    {
        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
        var server = new MessagingServer<E2ESession, Request, Response, Update>(
            listener, conn => new E2ESession(conn), Config());
        await server.StartAsync();
        return (server, listener);
    }

    private static ValueTask<MessagingClient<Request, Response, Update>> ConnectClientAsync(
        TcpTransportListener listener, MessagingClientOptions? options = null)
    {
        var connector = new TcpConnector(new TcpConnectorOptions
        {
            Host = "127.0.0.1",
            Port = listener.BoundPort!.Value,
        });
        return MessagingClient<Request, Response, Update>.ConnectAsync(connector, options);
    }

    [Test]
    public async Task E2E_request_response_roundtrip()
    {
        var (server, listener) = await StartServerAsync();
        try
        {
            var client = await ConnectClientAsync(listener);
            var reply = await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
                .AsTask().WaitAsync(Timeout);
            Assert.That(reply.IsOk, Is.True);
            Assert.That(reply.Value!.UnixMs, Is.EqualTo(777));
            client.Close();
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task E2E_failure_status_code()
    {
        var (server, listener) = await StartServerAsync();
        try
        {
            var client = await ConnectClientAsync(listener);
            var reply = await client.RequestAsync<BuyItemResponse>(new BuyItemRequest { ItemId = 1 })
                .AsTask().WaitAsync(Timeout);
            Assert.That(reply.Status, Is.EqualTo(-1001));
            Assert.That(reply.Value, Is.Null);
            client.Close();
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task E2E_push_is_received()
    {
        var (server, listener) = await StartServerAsync();
        try
        {
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = await ConnectClientAsync(listener);
            client.OnUpdate<BroadcastedUpdate>(u => received.TrySetResult(u.Text));

            // OnSessionOpenedAsync의 welcome 푸시 — 구독 등록과 경합할 수 있으므로
            // 수신 실패 시 서버 세션에서 한 번 더 밀어 재검증한다.
            var sessionPush = server.Sessions.Count > 0
                ? server.Sessions.First().SendUpdateAsync(new BroadcastedUpdate { Text = "welcome" })
                : default;
            await sessionPush;

            Assert.That(await received.Task.WaitAsync(Timeout), Is.EqualTo("welcome"));
            client.Close();
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task E2E_concurrent_requests_correlate_correctly()
    {
        var (server, listener) = await StartServerAsync();
        try
        {
            var client = await ConnectClientAsync(listener);

            var tasks = new List<Task<Reply<BuyItemResponse>>>();
            for (var itemId = 10; itemId < 30; itemId++)
            {
                tasks.Add(client.RequestAsync<BuyItemResponse>(new BuyItemRequest { ItemId = itemId }).AsTask());
            }

            var replies = await Task.WhenAll(tasks).WaitAsync(Timeout);
            for (var i = 0; i < replies.Length; i++)
            {
                Assert.That(replies[i].IsOk, Is.True);
                Assert.That(replies[i].Value!.RemainingGold, Is.EqualTo(1000 + 10 + i),
                    "응답이 자기 요청과 상관되어야 한다");
            }
            client.Close();
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task E2E_graceful_shutdown_fails_pending_and_fires_closed()
    {
        var (server, listener) = await StartServerAsync();
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = await ConnectClientAsync(listener);
        client.Closed += _ => closed.TrySetResult(true);

        // 세션 수립 확인 후 정지
        var warmup = await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
            .AsTask().WaitAsync(Timeout);
        Assert.That(warmup.IsOk, Is.True);

        await server.StopAsync();

        await closed.Task.WaitAsync(Timeout);
        Assert.That(client.IsConnected, Is.False);
        Assert.ThrowsAsync<ConnectionClosedException>(async () =>
            await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest()));
    }
}
```

- [ ] **Step 2: 테스트 실행**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~MessagingE2ETests"`
Expected: 5/5 PASS (Task 2~6이 올바르면 신규 코드 없이 통과하는 통합 검증).

- [ ] **Step 3: 전체 확인 + Commit**

Run: `dotnet test server/tests/Bun3.Server.Tests` → 78/78.

```
git add server/tests/
git commit -m "✅ Add messaging E2E over real TCP (v1 acceptance)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Hosting 통합 — AddMessagingServer

**Files:**
- Create: `server/src/Bun3.Server.Hosting/MessagingServiceCollectionExtensions.cs`
- Modify: `server/src/Bun3.Server.Hosting/Bun3.Server.Hosting.csproj` (Messaging 참조 추가)
- Test: `server/tests/Bun3.Server.Tests/MessagingHostingTests.cs`

**Interfaces:**
- Consumes: Task 5 `MessagingServer`/`MessagingConfig`/`MessagingServerOptions`, 기존 Hosting의 `ServerOptions`(Port/MaxPacketSize/Backlog/DrainTimeout — "Bun3:Server" 바인딩)와 `TcpTransportListener` 싱글턴 패턴
- Produces: `AddMessagingServer<TSession, TRequest, TResponse, TUpdate>(this IServiceCollection, Action<MessagingConfig<TSession>> configure, Action<ServerOptions>? serverOptions = null, Action<MessagingServerOptions>? messagingOptions = null)` — TSession은 IConnection 받는 public ctor(나머지 DI). 구성 오류는 호스트 StartAsync에서 MessagingValidationException으로 실패.

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/MessagingHostingTests.cs`:

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Messaging;
using Bun3.Server.Hosting;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class MessagingHostingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public sealed class HostedSession : MessagingSession
    {
        public HostedSession(IConnection connection) : base(connection) { }
    }

    [Test]
    public async Task Host_boots_and_serves_typed_request()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddMessagingServer<HostedSession, Request, Response, Update>(
            messaging =>
            {
                messaging.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
                    (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse { UnixMs = 99 }));
                messaging.OnRequest<BuyItemRequest, BuyItemResponse>(
                    (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse { RemainingGold = 1 }));
            },
            serverOptions: options => options.Port = 0);
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            var port = host.Services.GetRequiredService<TcpTransportListener>().BoundPort!.Value;
            var client = await MessagingClient<Request, Response, Update>.ConnectAsync(
                new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = port }));

            var reply = await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
                .AsTask().WaitAsync(Timeout);
            Assert.That(reply.IsOk, Is.True);
            Assert.That(reply.Value!.UnixMs, Is.EqualTo(99));
            client.Close();
        }
        finally
        {
            await host.StopAsync().WaitAsync(Timeout);
        }
    }

    [Test]
    public async Task Incomplete_config_fails_host_start_with_full_error_list()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddMessagingServer<HostedSession, Request, Response, Update>(
            messaging => { },   // 아무 핸들러도 등록하지 않음
            serverOptions: options => options.Port = 0);
        using var host = builder.Build();

        var ex = Assert.ThrowsAsync<MessagingValidationException>(async () => await host.StartAsync())!;
        Assert.That(ex.Message, Does.Contain("get_server_time"));
        Assert.That(ex.Message, Does.Contain("buy_item"));
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~MessagingHostingTests"`
Expected: 컴파일 에러 — `AddMessagingServer` 미정의

- [ ] **Step 3: 구현**

`Bun3.Server.Hosting.csproj`의 ProjectReference ItemGroup에 추가:

```xml
    <ProjectReference Include="..\Bun3.Server.Messaging\Bun3.Server.Messaging.csproj" />
```

`server/src/Bun3.Server.Hosting/MessagingServiceCollectionExtensions.cs`:

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Messaging;
using Bun3.Server.Transport.Tcp;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// 메시징 서버(TCP)를 Generic Host에 등록한다. 핸들러 등록표는 여기서 1회 구성되며,
    /// 구성 오류(미등록 핸들러 등)는 호스트 StartAsync에서 전체 목록과 함께 실패한다.
    /// TSession은 IConnection을 받는 public 생성자가 필요하며 나머지 인자는 DI로 주입된다.
    /// </summary>
    /// <remarks>제약(v0/v1 동일): 세션 생성자 의존성은 루트 컨테이너에서 해석되고(스코프 금지),
    /// 호스트당 1회만 호출한다(AddServer와 리스너 싱글턴을 공유하지 않도록 함께 쓰지 말 것).</remarks>
    public static IServiceCollection AddMessagingServer<TSession, TRequest, TResponse, TUpdate>(
        this IServiceCollection services,
        Action<MessagingConfig<TSession>> configure,
        Action<ServerOptions>? serverOptions = null,
        Action<MessagingServerOptions>? messagingOptions = null)
        where TSession : MessagingSession
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
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
                ResolveLogger(sp));
        });

        services.AddSingleton(sp =>
        {
            var config = new MessagingConfig<TSession>();
            configure(config);
            var messagingServerOptions = new MessagingServerOptions();
            var options = sp.GetRequiredService<IOptions<ServerOptions>>().Value;
            messagingServerOptions.MaxQueuedPackets = options.MaxQueuedPacketsPerSession;
            messagingOptions?.Invoke(messagingServerOptions);

            TSession Factory(IConnection connection) =>
                ActivatorUtilities.CreateInstance<TSession>(sp, connection);

            // MessagingServer ctor가 스키마 구축 + 전수 검증을 수행 — 여기서 throw되면
            // 호스트 StartAsync가 MessagingValidationException으로 실패한다(fail-fast).
            return new MessagingServer<TSession, TRequest, TResponse, TUpdate>(
                sp.GetRequiredService<TcpTransportListener>(),
                Factory,
                config,
                messagingServerOptions,
                ResolveLogger(sp));
        });

        services.AddHostedService(sp => new MessagingHostedService<TSession, TRequest, TResponse, TUpdate>(
            sp.GetRequiredService<MessagingServer<TSession, TRequest, TResponse, TUpdate>>(),
            sp.GetRequiredService<IOptions<ServerOptions>>()));

        return services;
    }

    private static ILogger ResolveLogger(IServiceProvider sp) =>
        sp.GetService<ILoggerFactory>()?.CreateLogger("Bun3.Server")
        ?? (ILogger)NullLogger.Instance;
}

internal sealed class MessagingHostedService<TSession, TRequest, TResponse, TUpdate> : IHostedService
    where TSession : MessagingSession
    where TRequest : class, IMessage<TRequest>, new()
    where TResponse : class, IMessage<TResponse>, new()
    where TUpdate : class, IMessage<TUpdate>, new()
{
    private readonly MessagingServer<TSession, TRequest, TResponse, TUpdate> _server;
    private readonly IOptions<ServerOptions> _options;

    public MessagingHostedService(
        MessagingServer<TSession, TRequest, TResponse, TUpdate> server,
        IOptions<ServerOptions> options)
    {
        _server = server;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _server.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        _server.StopAsync(_options.Value.DrainTimeout, cancellationToken);
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~MessagingHostingTests"` → 2/2.
전체: `dotnet test server/tests/Bun3.Server.Tests` → 80/80. `dotnet build Bun3.sln` 0 오류/0 경고. `dotnet test common/tests/Bun3.Common.Tests` 초록.

(주의: `Incomplete_config_fails_host_start...`에서 호스트가 예외를 래핑해 던지면 — 예:
AggregateException — 테스트를 최심부 예외를 검사하는 형태로 조정한다. 이는 테스트 결함
수정이지 프로덕션 완화가 아니다.)

- [ ] **Step 5: Commit**

```
git add server/
git commit -m "✨ Add AddMessagingServer hosting integration with boot-validation fail-fast" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## 완료 기준 (스펙 §7 대응)

- [ ] Reply 불변식 단위 테스트 초록 (Task 3)
- [ ] 스키마/등록 전수 검증 단위 테스트 초록 — 미등록 목록·매칭 위반·중복·루트 규약 (Task 4)
- [ ] 채널 파싱·위반 종료·핸들러 예외 status=2·Ping/Pong·idle kick 단위 테스트 초록 (Task 5)
- [ ] requestId 상관·타임아웃·연결 종료 pending 실패·푸시 구독 단위 테스트 초록 (Task 6)
- [ ] **실 TCP E2E 5종 초록 (Task 7) = v1 완료**
- [ ] 호스팅 통합 + 기동 검증 실패가 호스트 기동 실패로 전파 (Task 8)
- [ ] `dotnet build Bun3.sln` 0 오류/0 경고, common 테스트 회귀 없음

## 실행 참고

- protobuf 생성 코드가 스타일 경고(CS1591 등)를 낼 경우: 우리 소스가 아닌 생성 코드 경고는 실행자가 원인을 보고하고 지시를 기다린다(NoWarn 임의 추가 금지).
- Google.Protobuf 3.29.3 / Grpc.Tools 2.68.0 복원 실패 시: 임의 버전 교체 대신 정확한 오류를 보고.
- FakeConnection의 SentPackets 재도입은 과거 ponytail 정리(0210fc4)의 되돌림이 아니라 신규 소비자(Task 5 테스트) 발생에 따른 재추가다.
