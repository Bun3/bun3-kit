# Bun3 서버 모듈 베이스 v0 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `server/` 하위에 4개 패키지(Abstractions/Core/Transport.Tcp/Hosting) + 에코 샘플 + 테스트를 구현하고, 실 TCP 에코 E2E 테스트 통과로 v0을 완료한다.

**Architecture:** 바이트 레벨 전송 추상화(`IConnection`/`ITransportListener`) 위에 세션 액터 코어(`ServerBase<TSession>`/`Session`)를 얹고, TCP 전송(길이 프리픽스)과 ASP.NET Core 호스팅을 별도 패키지로 격리한다. 스펙: `docs/superpowers/specs/2026-08-04-server-modulize-base-design.md`.

**Tech Stack:** .NET SDK 10.0.103. 코어 3개 패키지는 netstandard2.1 + C# 9.0 + 외부 의존 0 (Unity 호환). Hosting만 net10.0 + Microsoft.Extensions.Hosting. 테스트는 NUnit 4 (net10.0).

## Global Constraints

- **TFM/언어**: `Bun3.Server.Abstractions`, `Bun3.Server.Core`, `Bun3.Server.Transport.Tcp`는 `<TargetFramework>netstandard2.1</TargetFramework>` + `<LangVersion>9.0</LangVersion>` + `<Nullable>enable</Nullable>`. **`ImplicitUsings` 금지**(C# 9라 global using 불가 — using문 명시). `Bun3.Server.Hosting`과 샘플/테스트는 `net10.0` + `<ImplicitUsings>enable</ImplicitUsings>`.
- **의존성**: netstandard2.1 3개 패키지는 외부 NuGet 의존 0. DotNetty 등 네트워크 라이브러리 금지 — 순수 `Socket`/`TcpListener`/`NetworkStream`.
- **netstandard2.1 API 주의**: `System.Diagnostics.CodeAnalysis`의 nullable 어트리뷰트(`[NotNullWhen]` 등) 사용 금지(ns2.1에 없음). `Task.WaitAsync` 금지(net6+). 라이브러리 코드의 모든 await에 `.ConfigureAwait(false)`.
- **네이밍**: `RootNamespace` = `PackageId` = 프로젝트명. `<Version>0.1.0</Version>`, `<Authors>Bun3</Authors>`, `<RepositoryUrl>https://github.com/Bun3/bun3-kit</RepositoryUrl>` (Bun3.Common.csproj 스타일로 csproj 인라인).
- **전송 계약 순서 보장** (모든 전송 구현 필수): `OnConnected(conn)`가 반환되기 전에는 그 연결의 `OnFrame`/`OnClosed`를 호출하지 않는다. `OnClosed`는 연결당 정확히 1회. `OnFrame` 버퍼는 호출 동안만 유효.
- **에러 정책**: 핸들러 예외 기본값 = 세션 종료, `OnHandlerError` 훅으로 재정의. 닫힌 연결에 `SendAsync` = no-op. 프레임 크기 초과(기본 1MB) = 연결 종료. 세션 큐 적체(기본 256) = 연결 종료.
- **커밋**: gitmoji 프리픽스(✨ 기능, ✅ 테스트, 📦 프로젝트/패키징) + `-m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"` 트레일러. 작업 디렉터리는 레포 루트(`E:\Projects\orca\workspace\bun3-kit\server-modulize-base`).
- **테스트 실행**: `dotnet test server/tests/Bun3.Server.Tests` (필터: `--filter "FullyQualifiedName~<클래스명>"`). 전체 빌드: `dotnet build Bun3.sln`.

## 파일 구조 (전체 조감)

```
server/
├── src/
│   ├── Bun3.Server.Abstractions/
│   │   ├── Bun3.Server.Abstractions.csproj      [Task 1]
│   │   ├── IConnection.cs                        [Task 1]
│   │   ├── IConnectionHandler.cs                 [Task 1]
│   │   ├── ITransportListener.cs                 [Task 1]
│   │   └── Logging.cs                            [Task 1] (Bun3LogLevel, IBun3Logger, NullBun3Logger)
│   ├── Bun3.Server.Core/
│   │   ├── Bun3.Server.Core.csproj               [Task 1: 재타겟]
│   │   ├── ErrorDecision.cs                      [Task 3]
│   │   ├── SessionOptions.cs                     [Task 3]
│   │   ├── Session.cs                            [Task 3]
│   │   └── ServerBase.cs                         [Task 3]
│   ├── Bun3.Server.Transport.Tcp/
│   │   ├── Bun3.Server.Transport.Tcp.csproj      [Task 1]
│   │   ├── FrameFormat.cs                        [Task 2]
│   │   ├── TcpTransportOptions.cs                [Task 4]
│   │   ├── TcpConnection.cs                      [Task 4]
│   │   └── TcpTransportListener.cs               [Task 4]
│   └── Bun3.Server.Hosting/
│       ├── Bun3.Server.Hosting.csproj            [Task 1]
│       ├── Bun3ServerOptions.cs                  [Task 6]
│       ├── Bun3LoggerBridge.cs                   [Task 6]
│       ├── HostedServer.cs                       [Task 6]
│       ├── Bun3ServerHostedService.cs            [Task 6]
│       └── Bun3ServerServiceCollectionExtensions.cs [Task 6]
├── samples/
│   └── EchoServer/
│       ├── EchoServer.csproj                     [Task 7]
│       └── Program.cs                            [Task 7]
└── tests/
    └── Bun3.Server.Tests/
        ├── Bun3.Server.Tests.csproj              [Task 1]
        ├── Helpers/ChunkedReadStream.cs          [Task 2]
        ├── Helpers/FakeTransport.cs              [Task 3] (FakeTransport, FakeConnection)
        ├── FrameFormatTests.cs                   [Task 2]
        ├── SessionActorTests.cs                  [Task 3]
        ├── TcpTransportTests.cs                  [Task 4]
        ├── EchoE2ETests.cs                       [Task 5]
        └── HostingTests.cs                       [Task 6]
```

의존 방향: `Hosting → Core + Transport.Tcp`, `Core → Abstractions + Bun3.Common`, `Transport.Tcp → Abstractions`. (스펙 §3 도식은 Hosting → Core만 표기했으나, `AddBun3Server`가 기본 TCP 전송을 조립하므로 Hosting → Transport.Tcp 참조를 추가한다. Task 6에서 스펙 §3에 한 줄 반영.)

---

### Task 1: 프로젝트 스캐폴딩 + Abstractions 계약

**Files:**
- Create: `server/src/Bun3.Server.Abstractions/Bun3.Server.Abstractions.csproj`
- Create: `server/src/Bun3.Server.Abstractions/IConnection.cs`
- Create: `server/src/Bun3.Server.Abstractions/IConnectionHandler.cs`
- Create: `server/src/Bun3.Server.Abstractions/ITransportListener.cs`
- Create: `server/src/Bun3.Server.Abstractions/Logging.cs`
- Modify: `server/src/Bun3.Server.Core/Bun3.Server.Core.csproj` (net10.0 → netstandard2.1 재타겟)
- Create: `server/src/Bun3.Server.Transport.Tcp/Bun3.Server.Transport.Tcp.csproj`
- Create: `server/src/Bun3.Server.Hosting/Bun3.Server.Hosting.csproj`
- Create: `server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`
- Modify: `Bun3.sln` (`dotnet sln add`로 편입)

**Interfaces:**
- Consumes: 없음 (기존 `Bun3.Common` netstandard2.1 프로젝트 참조만 유지)
- Produces: 이후 모든 태스크가 사용하는 계약 — `IConnection { long Id; string? RemoteAddress; bool IsOpen; ValueTask SendAsync(ReadOnlyMemory<byte>, CancellationToken); void Close(); }`, `IConnectionHandler { void OnConnected(IConnection); void OnFrame(IConnection, ReadOnlyMemory<byte>); void OnClosed(IConnection, Exception?); }`, `ITransportListener { Task StartAsync(IConnectionHandler, CancellationToken); Task StopAsync(CancellationToken); }`, `Bun3LogLevel { Debug, Info, Warning, Error }`, `IBun3Logger { void Log(Bun3LogLevel, string, Exception?); }`, `NullBun3Logger.Instance`

- [ ] **Step 1: Abstractions csproj 작성**

`server/src/Bun3.Server.Abstractions/Bun3.Server.Abstractions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>Bun3.Server.Abstractions</RootNamespace>
    <PackageId>Bun3.Server.Abstractions</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-kit</RepositoryUrl>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: 계약 소스 작성**

`server/src/Bun3.Server.Abstractions/IConnection.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Abstractions
{
    /// <summary>
    /// 연결된 원격 상대 하나. 전송(TCP/Steam/인프로세스)에 무관한 프레임 단위 송신 계약.
    /// </summary>
    public interface IConnection
    {
        /// <summary>
        /// 프로세스 내 유일 연결 식별자(단조 증가). 로그 상관·레지스트리 키 용도.
        /// 계정/플레이어 ID가 아니며 재접속 시 새 값이 부여된다.
        /// </summary>
        long Id { get; }

        /// <summary>전송별 원격 주소 표현. TCP는 "IP:포트", Steam은 SteamID 문자열.</summary>
        string? RemoteAddress { get; }

        bool IsOpen { get; }

        /// <summary>
        /// 프레임 하나를 송신한다. 닫힌 연결에 대한 호출은 no-op이다(예외를 던지지 않는다).
        /// </summary>
        ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default);

        /// <summary>연결을 닫는다. 멱등. 이후 전송 구현이 OnClosed를 정확히 1회 통지한다.</summary>
        void Close();
    }
}
```

`server/src/Bun3.Server.Abstractions/IConnectionHandler.cs`:

```csharp
using System;

namespace Bun3.Server.Abstractions
{
    /// <summary>
    /// 전송 이벤트 수신자(Core가 구현). 전송 구현은 다음 순서 계약을 반드시 지킨다:
    /// (1) OnConnected가 반환되기 전에는 해당 연결의 OnFrame/OnClosed를 호출하지 않는다.
    /// (2) OnClosed는 연결당 정확히 1회 호출한다.
    /// (3) OnFrame의 버퍼는 호출 동안만 유효하다(반환 후 재사용될 수 있음).
    /// </summary>
    public interface IConnectionHandler
    {
        void OnConnected(IConnection connection);
        void OnFrame(IConnection connection, ReadOnlyMemory<byte> frame);
        /// <summary>정상 종료면 error는 null.</summary>
        void OnClosed(IConnection connection, Exception? error);
    }
}
```

`server/src/Bun3.Server.Abstractions/ITransportListener.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Abstractions
{
    /// <summary>연결을 받아들이는 쪽의 계약. StopAsync는 신규 수락만 중단한다(기존 연결 종료는 상위 책임).</summary>
    public interface ITransportListener
    {
        Task StartAsync(IConnectionHandler handler, CancellationToken ct = default);
        Task StopAsync(CancellationToken ct = default);
    }
}
```

`server/src/Bun3.Server.Abstractions/Logging.cs`:

```csharp
using System;

namespace Bun3.Server.Abstractions
{
    public enum Bun3LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
    }

    /// <summary>최소 로깅 계약. 호스팅 계층에서 Microsoft.Extensions.Logging으로 브리지된다.</summary>
    public interface IBun3Logger
    {
        void Log(Bun3LogLevel level, string message, Exception? exception = null);
    }

    public sealed class NullBun3Logger : IBun3Logger
    {
        public static readonly NullBun3Logger Instance = new NullBun3Logger();

        private NullBun3Logger() { }

        public void Log(Bun3LogLevel level, string message, Exception? exception = null) { }
    }
}
```

- [ ] **Step 3: Core csproj 재타겟**

`server/src/Bun3.Server.Core/Bun3.Server.Core.csproj` 전체를 다음으로 교체 (net10.0 → netstandard2.1, `ImplicitUsings` 제거, Abstractions 참조 추가):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>Bun3.Server.Core</RootNamespace>
    <PackageId>Bun3.Server.Core</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-kit</RepositoryUrl>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Bun3.Server.Abstractions\Bun3.Server.Abstractions.csproj" />
    <ProjectReference Include="..\..\..\common\src\com.bun3.common\Bun3.Common.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Transport.Tcp / Hosting / Tests csproj 작성**

`server/src/Bun3.Server.Transport.Tcp/Bun3.Server.Transport.Tcp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>Bun3.Server.Transport.Tcp</RootNamespace>
    <PackageId>Bun3.Server.Transport.Tcp</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-kit</RepositoryUrl>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Bun3.Server.Abstractions\Bun3.Server.Abstractions.csproj" />
  </ItemGroup>

</Project>
```

`server/src/Bun3.Server.Hosting/Bun3.Server.Hosting.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Bun3.Server.Hosting</RootNamespace>
    <PackageId>Bun3.Server.Hosting</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-kit</RepositoryUrl>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Bun3.Server.Core\Bun3.Server.Core.csproj" />
    <ProjectReference Include="..\Bun3.Server.Transport.Tcp\Bun3.Server.Transport.Tcp.csproj" />
  </ItemGroup>

</Project>
```

`server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj` (버전은 `common/tests/Bun3.Common.Tests` 관례, Test.Sdk만 net10 대응 상향):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>Bun3.Server.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.0" />
    <PackageReference Include="NUnit" Version="4.1.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Bun3.Server.Abstractions\Bun3.Server.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\Bun3.Server.Core\Bun3.Server.Core.csproj" />
    <ProjectReference Include="..\..\src\Bun3.Server.Transport.Tcp\Bun3.Server.Transport.Tcp.csproj" />
    <ProjectReference Include="..\..\src\Bun3.Server.Hosting\Bun3.Server.Hosting.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: 솔루션 편입**

Run (레포 루트에서):

```
dotnet sln Bun3.sln add server/src/Bun3.Server.Abstractions/Bun3.Server.Abstractions.csproj server/src/Bun3.Server.Transport.Tcp/Bun3.Server.Transport.Tcp.csproj server/src/Bun3.Server.Hosting/Bun3.Server.Hosting.csproj server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj
```

(`Bun3.Server.Core`는 이미 솔루션에 있음.)

- [ ] **Step 6: 빌드 검증**

Run: `dotnet build Bun3.sln`
Expected: Build succeeded, 오류 0. (경고 0이 이상적 — nullable 경고가 나오면 소스 수정)

- [ ] **Step 7: Commit**

```
git add server/ Bun3.sln
git commit -m "📦 Scaffold Bun3.Server packages and abstractions contracts" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: FrameFormat (길이 프리픽스 프레이밍) — TDD

**Files:**
- Create: `server/src/Bun3.Server.Transport.Tcp/FrameFormat.cs`
- Create: `server/tests/Bun3.Server.Tests/Helpers/ChunkedReadStream.cs`
- Test: `server/tests/Bun3.Server.Tests/FrameFormatTests.cs`

**Interfaces:**
- Consumes: 없음 (BCL Stream만)
- Produces: `FrameFormat.HeaderSize == 4`; `static ValueTask WriteFrameAsync(Stream, ReadOnlyMemory<byte>, CancellationToken = default)`; `static ValueTask<byte[]?> ReadFrameAsync(Stream, int maxFrameSize, CancellationToken = default)` — 프레임 경계에서의 깨끗한 EOF는 `null` 반환, 프레임 도중 EOF는 `EndOfStreamException`, 길이 음수 또는 maxFrameSize 초과는 `InvalidDataException`. 길이 0 프레임은 빈 배열 반환(널 아님). Task 4의 TcpConnection과 Task 5/6의 테스트 클라이언트가 사용.

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/Helpers/ChunkedReadStream.cs` (부분 도착 시뮬레이션용 — Read가 한 번에 최대 chunk 바이트만 반환):

```csharp
namespace Bun3.Server.Tests.Helpers;

/// <summary>한 번의 Read가 최대 chunkSize 바이트만 반환하는 읽기 전용 스트림. TCP 분할 도착 시뮬레이션.</summary>
public sealed class ChunkedReadStream : Stream
{
    private readonly byte[] _data;
    private readonly int _chunkSize;
    private int _position;

    public ChunkedReadStream(byte[] data, int chunkSize)
    {
        _data = data;
        _chunkSize = chunkSize;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _data.Length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = Math.Min(Math.Min(count, _chunkSize), _data.Length - _position);
        Array.Copy(_data, _position, buffer, offset, n);
        _position += n;
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
```

`server/tests/Bun3.Server.Tests/FrameFormatTests.cs`:

```csharp
using System.Text;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Transport.Tcp;

namespace Bun3.Server.Tests;

[TestFixture]
public class FrameFormatTests
{
    private const int MaxFrameSize = 1024;

    private static async Task<byte[]> DumpFrameAsync(byte[] body)
    {
        using var ms = new MemoryStream();
        await FrameFormat.WriteFrameAsync(ms, body);
        return ms.ToArray();
    }

    [Test]
    public async Task Roundtrip_preserves_payload()
    {
        var payload = Encoding.UTF8.GetBytes("hello bun3");
        var wire = await DumpFrameAsync(payload);
        using var ms = new MemoryStream(wire);

        var frame = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);

        Assert.That(frame, Is.EqualTo(payload));
    }

    [Test]
    public async Task Header_is_4_byte_little_endian_length()
    {
        var wire = await DumpFrameAsync(new byte[300]);

        Assert.That(wire.Length, Is.EqualTo(4 + 300));
        Assert.That(wire[0], Is.EqualTo(0x2C)); // 300 = 0x012C
        Assert.That(wire[1], Is.EqualTo(0x01));
        Assert.That(wire[2], Is.EqualTo(0x00));
        Assert.That(wire[3], Is.EqualTo(0x00));
    }

    [Test]
    public async Task Partial_arrival_is_reassembled()
    {
        var payload = Encoding.UTF8.GetBytes("split into tiny chunks");
        var wire = await DumpFrameAsync(payload);
        using var stream = new ChunkedReadStream(wire, chunkSize: 3);

        var frame = await FrameFormat.ReadFrameAsync(stream, MaxFrameSize);

        Assert.That(frame, Is.EqualTo(payload));
    }

    [Test]
    public async Task Merged_arrival_yields_two_frames()
    {
        using var ms = new MemoryStream();
        await FrameFormat.WriteFrameAsync(ms, Encoding.UTF8.GetBytes("one"));
        await FrameFormat.WriteFrameAsync(ms, Encoding.UTF8.GetBytes("two"));
        ms.Position = 0;

        var first = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);
        var second = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);
        var third = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);

        Assert.That(first, Is.EqualTo(Encoding.UTF8.GetBytes("one")));
        Assert.That(second, Is.EqualTo(Encoding.UTF8.GetBytes("two")));
        Assert.That(third, Is.Null); // 프레임 경계의 깨끗한 EOF
    }

    [Test]
    public async Task Zero_length_frame_is_valid_and_empty()
    {
        var wire = await DumpFrameAsync(Array.Empty<byte>());
        using var ms = new MemoryStream(wire);

        var frame = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);

        Assert.That(frame, Is.Not.Null);
        Assert.That(frame, Is.Empty);
    }

    [Test]
    public async Task Frame_at_exactly_max_size_is_accepted()
    {
        var wire = await DumpFrameAsync(new byte[MaxFrameSize]);
        using var ms = new MemoryStream(wire);

        var frame = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);

        Assert.That(frame, Has.Length.EqualTo(MaxFrameSize));
    }

    [Test]
    public async Task Frame_over_max_size_throws_InvalidDataException()
    {
        var wire = await DumpFrameAsync(new byte[MaxFrameSize + 1]);
        using var ms = new MemoryStream(wire);

        Assert.ThrowsAsync<InvalidDataException>(async () => await FrameFormat.ReadFrameAsync(ms, MaxFrameSize));
    }

    [Test]
    public void Negative_length_header_throws_InvalidDataException()
    {
        using var ms = new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }); // -1
        Assert.ThrowsAsync<InvalidDataException>(async () => await FrameFormat.ReadFrameAsync(ms, MaxFrameSize));
    }

    [Test]
    public void Eof_mid_header_throws_EndOfStreamException()
    {
        using var ms = new MemoryStream(new byte[] { 0x05, 0x00 }); // 헤더 2바이트만 도착
        Assert.ThrowsAsync<EndOfStreamException>(async () => await FrameFormat.ReadFrameAsync(ms, MaxFrameSize));
    }

    [Test]
    public void Eof_mid_body_throws_EndOfStreamException()
    {
        using var ms = new MemoryStream(new byte[] { 0x0A, 0x00, 0x00, 0x00, 1, 2, 3 }); // 길이 10, 본문 3바이트만
        Assert.ThrowsAsync<EndOfStreamException>(async () => await FrameFormat.ReadFrameAsync(ms, MaxFrameSize));
    }

    [Test]
    public async Task Empty_stream_returns_null()
    {
        using var ms = new MemoryStream();
        var frame = await FrameFormat.ReadFrameAsync(ms, MaxFrameSize);
        Assert.That(frame, Is.Null);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~FrameFormatTests"`
Expected: 컴파일 에러 — `FrameFormat`이 존재하지 않음 (CS0103/CS0246)

- [ ] **Step 3: 구현**

`server/src/Bun3.Server.Transport.Tcp/FrameFormat.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Transport.Tcp
{
    /// <summary>
    /// 4바이트 리틀엔디언 길이 프리픽스 프레이밍.
    /// 와이어 형식: [length:4(LE)][body:length]
    /// </summary>
    public static class FrameFormat
    {
        public const int HeaderSize = 4;

        public static async ValueTask WriteFrameAsync(
            Stream stream, ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            var length = frame.Length;
            var header = new byte[HeaderSize];
            header[0] = (byte)length;
            header[1] = (byte)(length >> 8);
            header[2] = (byte)(length >> 16);
            header[3] = (byte)(length >> 24);
            await stream.WriteAsync(header.AsMemory(), ct).ConfigureAwait(false);
            if (length > 0)
            {
                await stream.WriteAsync(frame, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 프레임 하나를 읽는다. 프레임 경계에서의 깨끗한 EOF는 null을 반환한다.
        /// 프레임 도중 EOF는 <see cref="EndOfStreamException"/>,
        /// 길이가 음수이거나 maxFrameSize 초과면 <see cref="InvalidDataException"/>.
        /// </summary>
        public static async ValueTask<byte[]?> ReadFrameAsync(
            Stream stream, int maxFrameSize, CancellationToken ct = default)
        {
            var header = new byte[HeaderSize];
            var got = await ReadExactAsync(stream, header, HeaderSize, allowCleanEof: true, ct).ConfigureAwait(false);
            if (got == 0)
            {
                return null;
            }

            var length = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
            if (length < 0 || length > maxFrameSize)
            {
                throw new InvalidDataException($"Frame length {length} is out of range (max {maxFrameSize}).");
            }

            var body = new byte[length];
            if (length > 0)
            {
                await ReadExactAsync(stream, body, length, allowCleanEof: false, ct).ConfigureAwait(false);
            }

            return body;
        }

        private static async ValueTask<int> ReadExactAsync(
            Stream stream, byte[] buffer, int count, bool allowCleanEof, CancellationToken ct)
        {
            var total = 0;
            while (total < count)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(total, count - total), ct).ConfigureAwait(false);
                if (n == 0)
                {
                    if (allowCleanEof && total == 0)
                    {
                        return 0;
                    }

                    throw new EndOfStreamException("Stream ended mid-frame.");
                }

                total += n;
            }

            return total;
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~FrameFormatTests"`
Expected: PASS — 11개 테스트 전부 초록

- [ ] **Step 5: Commit**

```
git add server/src/Bun3.Server.Transport.Tcp/FrameFormat.cs server/tests/Bun3.Server.Tests/
git commit -m "✨ Add length-prefix frame format with unit tests" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Core — Session 액터와 ServerBase — TDD

**Files:**
- Create: `server/src/Bun3.Server.Core/ErrorDecision.cs`
- Create: `server/src/Bun3.Server.Core/SessionOptions.cs`
- Create: `server/src/Bun3.Server.Core/Session.cs`
- Create: `server/src/Bun3.Server.Core/ServerBase.cs`
- Create: `server/tests/Bun3.Server.Tests/Helpers/FakeTransport.cs`
- Test: `server/tests/Bun3.Server.Tests/SessionActorTests.cs`

**Interfaces:**
- Consumes: Task 1의 `IConnection`, `IConnectionHandler`, `ITransportListener`, `IBun3Logger`, `NullBun3Logger.Instance`
- Produces:
  - `enum ErrorDecision { CloseSession, Continue }`
  - `sealed class SessionOptions { int MaxQueuedFrames = 256; }`
  - `abstract class Session` — `protected Session(IConnection connection)`; `long Id => Connection.Id`; `IConnection Connection`; `protected virtual ValueTask OnConnectedAsync()`; `protected abstract ValueTask OnFrameAsync(ReadOnlyMemory<byte> frame)`; `protected virtual ValueTask OnDisconnectedAsync(Exception? error)`; `protected virtual ErrorDecision OnHandlerError(Exception ex)` (기본 CloseSession); `ValueTask SendAsync(ReadOnlyMemory<byte>, CancellationToken = default)`; `void Kick()`; internal: `Initialize(IBun3Logger, SessionOptions)`, `EnqueueFrame(ReadOnlyMemory<byte>)`, `NotifyClosed(Exception?)`, `Task RunAsync()`
  - `abstract class ServerBase<TSession> where TSession : Session` — `protected ServerBase(ITransportListener transport, IBun3Logger? logger = null, SessionOptions? sessionOptions = null)`; `protected abstract TSession CreateSession(IConnection connection)`; `Task StartAsync(CancellationToken = default)`; `Task StopAsync(TimeSpan? drainTimeout = null, CancellationToken = default)`; `bool IsRunning`; `IReadOnlyCollection<TSession> Sessions`
  - Task 5/6/7이 `ServerBase` 상속 + `Session` 상속으로 사용

- [ ] **Step 1: FakeTransport 헬퍼 작성**

`server/tests/Bun3.Server.Tests/Helpers/FakeTransport.cs` (소켓 없이 Core를 검증하는 인메모리 전송 — 전송 추상화가 실제로 성립하는지의 증명이기도 함):

```csharp
using Bun3.Server.Abstractions;

namespace Bun3.Server.Tests.Helpers;

public sealed class FakeTransport : ITransportListener
{
    private IConnectionHandler? _handler;

    public bool Started { get; private set; }
    public bool Stopped { get; private set; }

    public Task StartAsync(IConnectionHandler handler, CancellationToken ct = default)
    {
        _handler = handler;
        Started = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        Stopped = true;
        return Task.CompletedTask;
    }

    /// <summary>클라이언트 접속을 시뮬레이션한다.</summary>
    public FakeConnection Connect(long id)
    {
        var connection = new FakeConnection(id, this);
        _handler!.OnConnected(connection);
        return connection;
    }

    internal void RaiseFrame(FakeConnection connection, byte[] frame) => _handler!.OnFrame(connection, frame);

    internal void RaiseClosed(FakeConnection connection, Exception? error) => _handler!.OnClosed(connection, error);
}

public sealed class FakeConnection : IConnection
{
    private readonly FakeTransport _transport;
    private readonly List<byte[]> _sentFrames = new();
    private int _closed;

    public FakeConnection(long id, FakeTransport transport)
    {
        Id = id;
        _transport = transport;
    }

    public long Id { get; }
    public string? RemoteAddress => "fake";
    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    public IReadOnlyList<byte[]> SentFrames
    {
        get { lock (_sentFrames) return _sentFrames.ToArray(); }
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        if (IsOpen)
        {
            lock (_sentFrames) _sentFrames.Add(frame.ToArray());
        }
        return default;
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _transport.RaiseClosed(this, null);
        }
    }

    /// <summary>원격에서 프레임이 도착한 것을 시뮬레이션한다.</summary>
    public void ReceiveFrame(byte[] frame) => _transport.RaiseFrame(this, frame);

    /// <summary>원격이 오류로 끊긴 것을 시뮬레이션한다.</summary>
    public void FailWith(Exception error)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _transport.RaiseClosed(this, error);
        }
    }
}
```

- [ ] **Step 2: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/SessionActorTests.cs`:

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Tests.Helpers;

namespace Bun3.Server.Tests;

[TestFixture]
public class SessionActorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // ---- 테스트용 세션/서버 ----

    private sealed class ScriptedSession : Session
    {
        private readonly Func<ScriptedSession, ReadOnlyMemory<byte>, ValueTask> _onFrame;
        private readonly Func<Exception, ErrorDecision>? _onError;
        public readonly TaskCompletionSource<Exception?> Disconnected =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ScriptedSession(
            IConnection connection,
            Func<ScriptedSession, ReadOnlyMemory<byte>, ValueTask> onFrame,
            Func<Exception, ErrorDecision>? onError = null)
            : base(connection)
        {
            _onFrame = onFrame;
            _onError = onError;
        }

        protected override ValueTask OnFrameAsync(ReadOnlyMemory<byte> frame) => _onFrame(this, frame);

        protected override ErrorDecision OnHandlerError(Exception ex) =>
            _onError?.Invoke(ex) ?? base.OnHandlerError(ex);

        protected override ValueTask OnDisconnectedAsync(Exception? error)
        {
            Disconnected.TrySetResult(error);
            return default;
        }
    }

    private sealed class TestServer : ServerBase<ScriptedSession>
    {
        private readonly Func<IConnection, ScriptedSession> _factory;

        public TestServer(
            ITransportListener transport,
            Func<IConnection, ScriptedSession> factory,
            SessionOptions? sessionOptions = null)
            : base(transport, logger: null, sessionOptions)
        {
            _factory = factory;
        }

        protected override ScriptedSession CreateSession(IConnection connection) => _factory(connection);
    }

    // ---- 테스트 ----

    [Test]
    public async Task Frames_are_processed_in_order()
    {
        var transport = new FakeTransport();
        var processed = new List<int>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new TestServer(transport, conn => new ScriptedSession(conn, (_, frame) =>
        {
            processed.Add(BitConverter.ToInt32(frame.Span));
            if (processed.Count == 100) done.TrySetResult();
            return default;
        }));
        await server.StartAsync();

        var conn = transport.Connect(1);
        for (var i = 0; i < 100; i++) conn.ReceiveFrame(BitConverter.GetBytes(i));

        await done.Task.WaitAsync(Timeout);
        Assert.That(processed, Is.EqualTo(Enumerable.Range(0, 100)));
    }

    [Test]
    public async Task Handlers_of_one_session_never_overlap()
    {
        var transport = new FakeTransport();
        var concurrent = 0;
        var maxConcurrent = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var server = new TestServer(transport, conn => new ScriptedSession(conn, async (_, _) =>
        {
            var now = Interlocked.Increment(ref concurrent);
            InterlockedExtensions.Max(ref maxConcurrent, now);
            await Task.Delay(1);
            Interlocked.Decrement(ref concurrent);
            if (Interlocked.Increment(ref count) == 50) done.TrySetResult();
        }));
        await server.StartAsync();

        var conn = transport.Connect(1);
        for (var i = 0; i < 50; i++) conn.ReceiveFrame(new byte[] { 1 });

        await done.Task.WaitAsync(Timeout);
        Assert.That(maxConcurrent, Is.EqualTo(1));
    }

    [Test]
    public async Task Inbox_overflow_kicks_the_connection()
    {
        var transport = new FakeTransport();
        var firstFrameEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new TestServer(
            transport,
            conn => new ScriptedSession(conn, async (_, _) =>
            {
                firstFrameEntered.TrySetResult();
                await release.Task; // 첫 프레임에서 블록 → 큐 적체 유도
            }),
            new SessionOptions { MaxQueuedFrames = 8 });
        await server.StartAsync();

        var conn = transport.Connect(1);
        conn.ReceiveFrame(new byte[] { 0 });
        await firstFrameEntered.Task.WaitAsync(Timeout);
        var session = server.Sessions.Single(); // 종료 전에 세션 캡처
        for (var i = 0; i < 20; i++) conn.ReceiveFrame(new byte[] { 1 }); // 8개 초과

        release.TrySetResult(); // 블록 해제 → 루프가 종료 신호를 소비
        await session.Disconnected.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
    }

    [Test]
    public async Task Handler_exception_closes_session_by_default()
    {
        var transport = new FakeTransport();
        var server = new TestServer(transport, conn => new ScriptedSession(conn,
            (_, _) => throw new InvalidOperationException("boom")));
        await server.StartAsync();

        var conn = transport.Connect(1);
        var session = server.Sessions.Single();
        conn.ReceiveFrame(new byte[] { 1 });

        await session.Disconnected.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
    }

    [Test]
    public async Task OnHandlerError_Continue_keeps_session_alive()
    {
        var transport = new FakeTransport();
        var processed = new List<byte>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new TestServer(transport, conn => new ScriptedSession(
            conn,
            (_, frame) =>
            {
                if (frame.Span[0] == 1) throw new InvalidOperationException("boom");
                processed.Add(frame.Span[0]);
                done.TrySetResult();
                return default;
            },
            onError: _ => ErrorDecision.Continue));
        await server.StartAsync();

        var conn = transport.Connect(1);
        conn.ReceiveFrame(new byte[] { 1 }); // 예외 — 무시됨
        conn.ReceiveFrame(new byte[] { 2 }); // 계속 처리되어야 함

        await done.Task.WaitAsync(Timeout);
        Assert.That(processed, Is.EqualTo(new byte[] { 2 }));
        Assert.That(conn.IsOpen, Is.True);
    }

    [Test]
    public async Task Remote_close_fires_OnDisconnected_with_error_and_removes_session()
    {
        var transport = new FakeTransport();
        var server = new TestServer(transport, conn => new ScriptedSession(conn, (_, _) => default));
        await server.StartAsync();

        var conn = transport.Connect(1);
        var session = server.Sessions.Single();
        var error = new IOException("connection reset");
        conn.FailWith(error);

        var received = await session.Disconnected.Task.WaitAsync(Timeout);
        Assert.That(received, Is.SameAs(error));
        Assert.That(server.Sessions, Is.Empty);
    }

    [Test]
    public async Task StopAsync_kicks_all_sessions_and_stops_transport()
    {
        var transport = new FakeTransport();
        var server = new TestServer(transport, conn => new ScriptedSession(conn, (_, _) => default));
        await server.StartAsync();
        var c1 = transport.Connect(1);
        var c2 = transport.Connect(2);
        var sessions = server.Sessions.ToArray();

        await server.StopAsync();

        Assert.That(transport.Stopped, Is.True);
        Assert.That(c1.IsOpen, Is.False);
        Assert.That(c2.IsOpen, Is.False);
        foreach (var s in sessions)
        {
            await s.Disconnected.Task.WaitAsync(Timeout);
        }
        Assert.That(server.IsRunning, Is.False);
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            int current;
            while (value > (current = Volatile.Read(ref location)))
            {
                if (Interlocked.CompareExchange(ref location, value, current) == current) break;
            }
        }
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~SessionActorTests"`
Expected: 컴파일 에러 — `Session`, `ServerBase`, `SessionOptions`, `ErrorDecision` 미정의

- [ ] **Step 4: 구현**

`server/src/Bun3.Server.Core/ErrorDecision.cs`:

```csharp
namespace Bun3.Server.Core
{
    /// <summary>핸들러 예외 발생 시 세션 처리 방침.</summary>
    public enum ErrorDecision
    {
        /// <summary>세션을 종료한다(기본값). 반쯤 적용된 상태를 재접속으로 복구시킨다.</summary>
        CloseSession,

        /// <summary>예외를 무시하고 다음 프레임을 계속 처리한다.</summary>
        Continue,
    }
}
```

`server/src/Bun3.Server.Core/SessionOptions.cs`:

```csharp
namespace Bun3.Server.Core
{
    public sealed class SessionOptions
    {
        /// <summary>세션 수신 큐 상한. 초과 시 연결을 종료해 메모리를 보호한다.</summary>
        public int MaxQueuedFrames { get; set; } = 256;
    }
}
```

`server/src/Bun3.Server.Core/Session.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;

namespace Bun3.Server.Core
{
    /// <summary>
    /// 연결 1개의 서버측 대응물(연결과 수명을 같이한다). 프레임은 세션별 큐에 쌓이고
    /// 단일 소비 루프가 순서대로 처리하므로, 한 세션의 핸들러는 절대 동시에 실행되지 않는다.
    /// </summary>
    public abstract class Session
    {
        private readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private IBun3Logger _logger = NullBun3Logger.Instance;
        private SessionOptions _options = new SessionOptions();
        private volatile bool _closed;
        private Exception? _closeError;
        private int _queuedCount;

        protected Session(IConnection connection)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public long Id => Connection.Id;

        public IConnection Connection { get; }

        /// <summary>연결이 수립되어 소비 루프가 시작될 때 1회 호출된다.</summary>
        protected virtual ValueTask OnConnectedAsync() => default;

        /// <summary>프레임 하나를 처리한다. 같은 세션에서 동시 실행되지 않는다.</summary>
        protected abstract ValueTask OnFrameAsync(ReadOnlyMemory<byte> frame);

        /// <summary>세션 종료 시 1회 호출된다. 정상 종료면 error는 null.</summary>
        protected virtual ValueTask OnDisconnectedAsync(Exception? error) => default;

        /// <summary>
        /// OnConnectedAsync/OnFrameAsync가 던진 예외의 처리 방침. 기본값은 세션 종료.
        /// "이 예외는 무시해도 안전하다"는 지식이 있는 게임만 재정의한다.
        /// </summary>
        protected virtual ErrorDecision OnHandlerError(Exception ex) => ErrorDecision.CloseSession;

        public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default) =>
            Connection.SendAsync(frame, ct);

        /// <summary>서버 주도로 연결을 끊는다. 전송의 OnClosed 통지를 거쳐 세션이 정리된다.</summary>
        public void Kick() => Connection.Close();

        internal void Initialize(IBun3Logger logger, SessionOptions options)
        {
            _logger = logger;
            _options = options;
        }

        internal void EnqueueFrame(ReadOnlyMemory<byte> frame)
        {
            if (_closed)
            {
                return;
            }

            if (Interlocked.Increment(ref _queuedCount) > _options.MaxQueuedFrames)
            {
                Interlocked.Decrement(ref _queuedCount);
                _logger.Log(Bun3LogLevel.Warning,
                    $"Session {Id}: inbox overflow (>{_options.MaxQueuedFrames}); kicking.");
                Kick();
                return;
            }

            _inbox.Enqueue(frame.ToArray()); // 버퍼는 호출 동안만 유효하므로 복사
            _signal.Release();
        }

        internal void NotifyClosed(Exception? error)
        {
            _closeError = error;
            _closed = true;
            _signal.Release(); // 소비 루프를 깨워 종료시킨다
        }

        internal async Task RunAsync()
        {
            try
            {
                try
                {
                    await OnConnectedAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    HandleError(ex);
                }

                while (true)
                {
                    await _signal.WaitAsync().ConfigureAwait(false);
                    if (_closed)
                    {
                        break; // 종료 후 잔여 프레임은 처리하지 않는다
                    }

                    if (!_inbox.TryDequeue(out var frame))
                    {
                        continue;
                    }

                    Interlocked.Decrement(ref _queuedCount);
                    try
                    {
                        await OnFrameAsync(frame).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        HandleError(ex);
                    }
                }
            }
            finally
            {
                try
                {
                    await OnDisconnectedAsync(_closeError).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Log(Bun3LogLevel.Error, $"Session {Id}: OnDisconnectedAsync threw.", ex);
                }
            }
        }

        private void HandleError(Exception ex)
        {
            ErrorDecision decision;
            try
            {
                decision = OnHandlerError(ex);
            }
            catch (Exception hookEx)
            {
                _logger.Log(Bun3LogLevel.Error, $"Session {Id}: OnHandlerError threw.", hookEx);
                decision = ErrorDecision.CloseSession;
            }

            if (decision == ErrorDecision.CloseSession)
            {
                _logger.Log(Bun3LogLevel.Error, $"Session {Id}: handler exception; closing session.", ex);
                Kick();
            }
            else
            {
                _logger.Log(Bun3LogLevel.Warning, $"Session {Id}: handler exception ignored by OnHandlerError.", ex);
            }
        }
    }
}
```

`server/src/Bun3.Server.Core/ServerBase.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;

namespace Bun3.Server.Core
{
    /// <summary>
    /// 전송 리스너 위에서 연결→세션 바인딩과 수명주기를 관리하는 서버 베이스.
    /// 게임 코드와의 결합점은 CreateSession 팩토리 하나다.
    /// </summary>
    public abstract class ServerBase<TSession> where TSession : Session
    {
        private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);

        private readonly ITransportListener _transport;
        private readonly IBun3Logger _logger;
        private readonly SessionOptions _sessionOptions;
        private readonly ConcurrentDictionary<long, SessionEntry> _sessions =
            new ConcurrentDictionary<long, SessionEntry>();
        private readonly Handler _handler;
        private volatile bool _running;

        protected ServerBase(
            ITransportListener transport,
            IBun3Logger? logger = null,
            SessionOptions? sessionOptions = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? NullBun3Logger.Instance;
            _sessionOptions = sessionOptions ?? new SessionOptions();
            _handler = new Handler(this);
        }

        public bool IsRunning => _running;

        public IReadOnlyCollection<TSession> Sessions =>
            _sessions.Values.Select(e => e.Session).ToArray();

        protected abstract TSession CreateSession(IConnection connection);

        public async Task StartAsync(CancellationToken ct = default)
        {
            await _transport.StartAsync(_handler, ct).ConfigureAwait(false);
            _running = true;
            _logger.Log(Bun3LogLevel.Info, "Server started.");
        }

        /// <summary>
        /// 신규 수락을 중단하고 전 세션을 종료한 뒤, 소비 루프들이 끝나기를 drainTimeout까지 기다린다.
        /// </summary>
        public async Task StopAsync(TimeSpan? drainTimeout = null, CancellationToken ct = default)
        {
            _running = false;
            await _transport.StopAsync(ct).ConfigureAwait(false);

            var entries = _sessions.Values.ToArray();
            foreach (var entry in entries)
            {
                entry.Session.Kick();
            }

            var drain = Task.WhenAll(entries.Select(e => e.RunTask));
            var timeout = drainTimeout ?? DefaultDrainTimeout;
            var finished = await Task.WhenAny(drain, Task.Delay(timeout, ct)).ConfigureAwait(false);
            if (finished != drain)
            {
                _logger.Log(Bun3LogLevel.Warning, $"Server stop: {entries.Length} session(s) did not drain within {timeout}.");
            }

            _logger.Log(Bun3LogLevel.Info, "Server stopped.");
        }

        private void HandleConnected(IConnection connection)
        {
            TSession session;
            try
            {
                session = CreateSession(connection);
            }
            catch (Exception ex)
            {
                _logger.Log(Bun3LogLevel.Error, $"CreateSession failed for connection {connection.Id}; closing.", ex);
                connection.Close();
                return;
            }

            session.Initialize(_logger, _sessionOptions);
            var entry = new SessionEntry(session, session.RunAsync());
            _sessions[connection.Id] = entry;
        }

        private void HandleFrame(IConnection connection, ReadOnlyMemory<byte> frame)
        {
            if (_sessions.TryGetValue(connection.Id, out var entry))
            {
                entry.Session.EnqueueFrame(frame);
            }
        }

        private void HandleClosed(IConnection connection, Exception? error)
        {
            if (_sessions.TryRemove(connection.Id, out var entry))
            {
                entry.Session.NotifyClosed(error);
            }
        }

        private sealed class SessionEntry
        {
            public readonly TSession Session;
            public readonly Task RunTask;

            public SessionEntry(TSession session, Task runTask)
            {
                Session = session;
                RunTask = runTask;
            }
        }

        private sealed class Handler : IConnectionHandler
        {
            private readonly ServerBase<TSession> _server;

            public Handler(ServerBase<TSession> server) => _server = server;

            public void OnConnected(IConnection connection) => _server.HandleConnected(connection);

            public void OnFrame(IConnection connection, ReadOnlyMemory<byte> frame) =>
                _server.HandleFrame(connection, frame);

            public void OnClosed(IConnection connection, Exception? error) =>
                _server.HandleClosed(connection, error);
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~SessionActorTests"`
Expected: PASS — 7개 테스트 전부 초록. `FrameFormatTests`도 여전히 초록인지 전체 실행으로 확인: `dotnet test server/tests/Bun3.Server.Tests`

- [ ] **Step 6: Commit**

```
git add server/src/Bun3.Server.Core/ server/tests/Bun3.Server.Tests/
git commit -m "✨ Add session actor core (Session, ServerBase) with fake-transport tests" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: TCP 전송 (TcpTransportListener / TcpConnection) — TDD

**Files:**
- Create: `server/src/Bun3.Server.Transport.Tcp/TcpTransportOptions.cs`
- Create: `server/src/Bun3.Server.Transport.Tcp/TcpConnection.cs`
- Create: `server/src/Bun3.Server.Transport.Tcp/TcpTransportListener.cs`
- Test: `server/tests/Bun3.Server.Tests/TcpTransportTests.cs`

**Interfaces:**
- Consumes: Task 1 계약 전부, Task 2의 `FrameFormat`
- Produces:
  - `sealed class TcpTransportOptions { int Port; int MaxFrameSize = 1024 * 1024; int Backlog = 512; }`
  - `sealed class TcpTransportListener : ITransportListener` — `TcpTransportListener(TcpTransportOptions, IBun3Logger? = null)`; `int? BoundPort` (Port=0으로 시작하면 실제 바인딩된 포트; Task 5/6 테스트가 사용)
  - `internal sealed class TcpConnection : IConnection` (외부 노출 없음)

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/TcpTransportTests.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Bun3.Server.Abstractions;
using Bun3.Server.Transport.Tcp;

namespace Bun3.Server.Tests;

[TestFixture]
public class TcpTransportTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class RecordingHandler : IConnectionHandler
    {
        public readonly TaskCompletionSource<IConnection> Connected =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<Exception?> Closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ConcurrentQueue<byte[]> Frames = new();
        public readonly SemaphoreSlim FrameSignal = new(0);

        public void OnConnected(IConnection connection) => Connected.TrySetResult(connection);

        public void OnFrame(IConnection connection, ReadOnlyMemory<byte> frame)
        {
            Frames.Enqueue(frame.ToArray());
            FrameSignal.Release();
        }

        public void OnClosed(IConnection connection, Exception? error) => Closed.TrySetResult(error);
    }

    private static async Task<(TcpTransportListener listener, RecordingHandler handler)> StartListenerAsync(
        int maxFrameSize = 1024 * 1024)
    {
        var handler = new RecordingHandler();
        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0, MaxFrameSize = maxFrameSize });
        await listener.StartAsync(handler);
        return (listener, handler);
    }

    private static async Task<TcpClient> ConnectAsync(TcpTransportListener listener)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.BoundPort!.Value);
        return client;
    }

    [Test]
    public async Task Start_on_port_zero_reports_bound_port()
    {
        var (listener, _) = await StartListenerAsync();
        try
        {
            Assert.That(listener.BoundPort, Is.Not.Null);
            Assert.That(listener.BoundPort, Is.GreaterThan(0));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Client_connect_raises_OnConnected_with_remote_address()
    {
        var (listener, handler) = await StartListenerAsync();
        try
        {
            using var client = await ConnectAsync(listener);
            var connection = await handler.Connected.Task.WaitAsync(Timeout);

            Assert.That(connection.IsOpen, Is.True);
            Assert.That(connection.Id, Is.GreaterThan(0));
            Assert.That(connection.RemoteAddress, Does.Contain("127.0.0.1"));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Client_frame_reaches_handler_intact()
    {
        var (listener, handler) = await StartListenerAsync();
        try
        {
            using var client = await ConnectAsync(listener);
            await handler.Connected.Task.WaitAsync(Timeout);
            var payload = Encoding.UTF8.GetBytes("ping from client");

            await FrameFormat.WriteFrameAsync(client.GetStream(), payload);

            await handler.FrameSignal.WaitAsync(Timeout);
            Assert.That(handler.Frames.TryDequeue(out var received), Is.True);
            Assert.That(received, Is.EqualTo(payload));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Server_send_reaches_client_intact()
    {
        var (listener, handler) = await StartListenerAsync();
        try
        {
            using var client = await ConnectAsync(listener);
            var connection = await handler.Connected.Task.WaitAsync(Timeout);
            var payload = Encoding.UTF8.GetBytes("pong from server");

            await connection.SendAsync(payload);

            var received = await FrameFormat.ReadFrameAsync(client.GetStream(), 1024 * 1024)
                .AsTask().WaitAsync(Timeout);
            Assert.That(received, Is.EqualTo(payload));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Client_disconnect_raises_OnClosed_with_null_error()
    {
        var (listener, handler) = await StartListenerAsync();
        try
        {
            var client = await ConnectAsync(listener);
            var connection = await handler.Connected.Task.WaitAsync(Timeout);

            client.Close();

            var error = await handler.Closed.Task.WaitAsync(Timeout);
            Assert.That(error, Is.Null);
            Assert.That(connection.IsOpen, Is.False);
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Oversize_frame_closes_connection_with_InvalidDataException()
    {
        var (listener, handler) = await StartListenerAsync(maxFrameSize: 16);
        try
        {
            using var client = await ConnectAsync(listener);
            await handler.Connected.Task.WaitAsync(Timeout);

            await FrameFormat.WriteFrameAsync(client.GetStream(), new byte[17]);

            var error = await handler.Closed.Task.WaitAsync(Timeout);
            Assert.That(error, Is.InstanceOf<InvalidDataException>());
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Send_after_close_is_noop()
    {
        var (listener, handler) = await StartListenerAsync();
        try
        {
            using var client = await ConnectAsync(listener);
            var connection = await handler.Connected.Task.WaitAsync(Timeout);

            connection.Close();
            await handler.Closed.Task.WaitAsync(Timeout);

            Assert.DoesNotThrowAsync(async () => await connection.SendAsync(new byte[] { 1 }));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Two_connections_get_distinct_ids()
    {
        var handler1Seen = new ConcurrentQueue<long>();
        var handler = new MultiConnectionHandler(handler1Seen);
        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
        await listener.StartAsync(handler);
        try
        {
            using var c1 = new TcpClient();
            using var c2 = new TcpClient();
            await c1.ConnectAsync(IPAddress.Loopback, listener.BoundPort!.Value);
            await c2.ConnectAsync(IPAddress.Loopback, listener.BoundPort!.Value);

            await handler.TwoConnected.Task.WaitAsync(Timeout);
            var ids = handler1Seen.ToArray();
            Assert.That(ids, Has.Length.EqualTo(2));
            Assert.That(ids[0], Is.Not.EqualTo(ids[1]));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    private sealed class MultiConnectionHandler : IConnectionHandler
    {
        private readonly ConcurrentQueue<long> _ids;
        public readonly TaskCompletionSource TwoConnected =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MultiConnectionHandler(ConcurrentQueue<long> ids) => _ids = ids;

        public void OnConnected(IConnection connection)
        {
            _ids.Enqueue(connection.Id);
            if (_ids.Count >= 2) TwoConnected.TrySetResult();
        }

        public void OnFrame(IConnection connection, ReadOnlyMemory<byte> frame) { }
        public void OnClosed(IConnection connection, Exception? error) { }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~TcpTransportTests"`
Expected: 컴파일 에러 — `TcpTransportListener`, `TcpTransportOptions` 미정의

- [ ] **Step 3: 구현**

`server/src/Bun3.Server.Transport.Tcp/TcpTransportOptions.cs`:

```csharp
namespace Bun3.Server.Transport.Tcp
{
    public sealed class TcpTransportOptions
    {
        /// <summary>리슨 포트. 0이면 임의 포트에 바인딩된다(BoundPort로 확인).</summary>
        public int Port { get; set; }

        /// <summary>수신 프레임 크기 상한. 초과 시 프로토콜 위반으로 연결을 종료한다.</summary>
        public int MaxFrameSize { get; set; } = 1024 * 1024;

        public int Backlog { get; set; } = 512;
    }
}
```

`server/src/Bun3.Server.Transport.Tcp/TcpConnection.cs`:

```csharp
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;

namespace Bun3.Server.Transport.Tcp
{
    internal sealed class TcpConnection : IConnection
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly TcpTransportOptions _options;
        private readonly IConnectionHandler _handler;
        private readonly IBun3Logger _logger;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private int _closed; // 0 = open, 1 = closed

        internal TcpConnection(
            long id,
            TcpClient client,
            TcpTransportOptions options,
            IConnectionHandler handler,
            IBun3Logger logger)
        {
            Id = id;
            _client = client;
            _options = options;
            _handler = handler;
            _logger = logger;
            _stream = client.GetStream();
            RemoteAddress = client.Client.RemoteEndPoint?.ToString();
        }

        public long Id { get; }

        public string? RemoteAddress { get; }

        public bool IsOpen => Volatile.Read(ref _closed) == 0;

        public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (!IsOpen)
            {
                return; // 계약: 닫힌 연결에 대한 송신은 no-op
            }

            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!IsOpen)
                {
                    return;
                }

                await FrameFormat.WriteFrameAsync(_stream, frame, ct).ConfigureAwait(false);
            }
            catch (Exception) when (!IsOpen)
            {
                // 송신 도중 로컬 Close와 경합 — 계약상 no-op
            }
            catch (Exception ex)
            {
                _logger.Log(Bun3LogLevel.Debug, $"Connection {Id}: send failed; closing.", ex);
                Close();
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return;
            }

            try
            {
                _client.Close(); // 수신 루프의 Read를 깨워 OnClosed 통지로 이어진다
            }
            catch
            {
                // 소켓 정리 중 예외는 무시
            }
        }

        /// <summary>
        /// 수신 루프. 연결당 1개 실행되며, 종료 시 OnClosed를 정확히 1회 통지한다.
        /// </summary>
        internal async Task RunReceiveLoopAsync()
        {
            Exception? error = null;
            try
            {
                while (true)
                {
                    var frame = await FrameFormat.ReadFrameAsync(_stream, _options.MaxFrameSize)
                        .ConfigureAwait(false);
                    if (frame == null)
                    {
                        break; // 원격의 깨끗한 종료
                    }

                    _handler.OnFrame(this, frame);
                }
            }
            catch (Exception) when (!IsOpen)
            {
                // 로컬 Close()가 Read를 깨운 경우 — 정상 종료로 취급 (error = null)
            }
            catch (Exception ex)
            {
                error = ex; // InvalidDataException(프레임 초과), IOException(리셋) 등
            }
            finally
            {
                Close();
                _handler.OnClosed(this, error);
            }
        }
    }
}
```

`server/src/Bun3.Server.Transport.Tcp/TcpTransportListener.cs`:

```csharp
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;

namespace Bun3.Server.Transport.Tcp
{
    /// <summary>순수 Socket 기반 TCP 리스너. 프레이밍은 FrameFormat(4바이트 길이 프리픽스).</summary>
    public sealed class TcpTransportListener : ITransportListener
    {
        private readonly TcpTransportOptions _options;
        private readonly IBun3Logger _logger;
        private TcpListener? _listener;
        private Task? _acceptLoop;
        private long _nextConnectionId;
        private int? _boundPort;
        private volatile bool _stopping;

        public TcpTransportListener(TcpTransportOptions options, IBun3Logger? logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? NullBun3Logger.Instance;
        }

        /// <summary>실제 바인딩된 포트. Options.Port가 0이면 시작 후 여기서 확인한다. Stop 이후에도 유효.</summary>
        public int? BoundPort => _boundPort;

        public Task StartAsync(IConnectionHandler handler, CancellationToken ct = default)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (_listener != null)
            {
                throw new InvalidOperationException("Listener is already started.");
            }

            _listener = new TcpListener(IPAddress.Any, _options.Port);
            _listener.Start(_options.Backlog);
            _boundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _logger.Log(Bun3LogLevel.Info, $"TCP listening on port {BoundPort}.");
            _acceptLoop = Task.Run(() => AcceptLoopAsync(handler));
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken ct = default)
        {
            _stopping = true;
            _listener?.Stop(); // AcceptTcpClientAsync를 깨운다
            if (_acceptLoop != null)
            {
                await _acceptLoop.ConfigureAwait(false);
            }
        }

        private async Task AcceptLoopAsync(IConnectionHandler handler)
        {
            var listener = _listener!;
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception) when (_stopping)
                {
                    break; // Stop()에 의한 정상 종료
                }
                catch (Exception ex)
                {
                    _logger.Log(Bun3LogLevel.Error, "Accept failed.", ex);
                    continue;
                }

                client.NoDelay = true;
                var connection = new TcpConnection(
                    Interlocked.Increment(ref _nextConnectionId), client, _options, handler, _logger);

                // 계약: OnConnected 반환 전에는 OnFrame/OnClosed가 발생하지 않도록
                // 수신 루프는 OnConnected 이후에 시작한다.
                handler.OnConnected(connection);
                _ = Task.Run(connection.RunReceiveLoopAsync);
            }
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~TcpTransportTests"`
Expected: PASS — 8개 테스트 전부 초록. 이후 전체 실행: `dotnet test server/tests/Bun3.Server.Tests` (기존 테스트 포함 전부 초록)

- [ ] **Step 5: Commit**

```
git add server/src/Bun3.Server.Transport.Tcp/ server/tests/Bun3.Server.Tests/
git commit -m "✨ Add pure-socket TCP transport with loopback tests" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: 에코 E2E (Core + TCP 통합, graceful shutdown)

**Files:**
- Test: `server/tests/Bun3.Server.Tests/EchoE2ETests.cs`

**Interfaces:**
- Consumes: Task 3의 `ServerBase<TSession>`/`Session`, Task 4의 `TcpTransportListener`/`TcpTransportOptions`, Task 2의 `FrameFormat`
- Produces: 없음 (통합 검증 전용). 여기의 `EchoSession`/`EchoServer` 패턴이 Task 6/7의 참조 형태가 된다.

- [ ] **Step 1: 실패하는(신규) E2E 테스트 작성**

`server/tests/Bun3.Server.Tests/EchoE2ETests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Transport.Tcp;

namespace Bun3.Server.Tests;

[TestFixture]
public class EchoE2ETests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class EchoSession : Session
    {
        public EchoSession(IConnection connection) : base(connection) { }

        protected override ValueTask OnFrameAsync(ReadOnlyMemory<byte> frame) => SendAsync(frame);
    }

    private sealed class EchoServer : ServerBase<EchoSession>
    {
        public EchoServer(ITransportListener transport) : base(transport) { }

        protected override EchoSession CreateSession(IConnection connection) => new(connection);
    }

    private static async Task<(EchoServer server, TcpTransportListener listener)> StartEchoServerAsync()
    {
        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
        var server = new EchoServer(listener);
        await server.StartAsync();
        return (server, listener);
    }

    private static async Task<TcpClient> ConnectAsync(TcpTransportListener listener)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.BoundPort!.Value);
        return client;
    }

    private static async Task AssertEchoAsync(NetworkStream stream, string message)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        await FrameFormat.WriteFrameAsync(stream, payload);
        var echoed = await FrameFormat.ReadFrameAsync(stream, 1024 * 1024).AsTask().WaitAsync(Timeout);
        Assert.That(echoed, Is.EqualTo(payload));
    }

    [Test]
    public async Task Client_receives_echo_of_each_frame()
    {
        var (server, listener) = await StartEchoServerAsync();
        try
        {
            using var client = await ConnectAsync(listener);
            var stream = client.GetStream();

            await AssertEchoAsync(stream, "hello");
            await AssertEchoAsync(stream, "bun3");
            await AssertEchoAsync(stream, "server");
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task Two_clients_are_echoed_independently()
    {
        var (server, listener) = await StartEchoServerAsync();
        try
        {
            using var clientA = await ConnectAsync(listener);
            using var clientB = await ConnectAsync(listener);

            await AssertEchoAsync(clientA.GetStream(), "from A");
            await AssertEchoAsync(clientB.GetStream(), "from B");
            await AssertEchoAsync(clientA.GetStream(), "A again");

            Assert.That(server.Sessions, Has.Count.EqualTo(2));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task StopAsync_disconnects_client_gracefully()
    {
        var (server, listener) = await StartEchoServerAsync();
        using var client = await ConnectAsync(listener);
        var stream = client.GetStream();
        await AssertEchoAsync(stream, "warm-up"); // 세션 수립 보장

        await server.StopAsync();

        // 서버가 연결을 닫았으므로 클라이언트 읽기는 깨끗한 EOF(null) 또는 IO 예외로 끝난다
        try
        {
            var frame = await FrameFormat.ReadFrameAsync(stream, 1024 * 1024).AsTask().WaitAsync(Timeout);
            Assert.That(frame, Is.Null);
        }
        catch (IOException)
        {
            // RST로 끝나는 플랫폼 변형도 허용
        }
        Assert.That(server.IsRunning, Is.False);
        Assert.That(server.Sessions, Is.Empty);
    }

    [Test]
    public async Task New_connection_after_stop_is_refused()
    {
        var (server, listener) = await StartEchoServerAsync();
        var port = listener.BoundPort!.Value; // Stop 전에 캡처
        await server.StopAsync();

        var late = new TcpClient();
        Assert.ThrowsAsync<SocketException>(async () =>
            await late.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout));
    }
}
```

- [ ] **Step 2: 테스트 실행**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~EchoE2ETests"`
Expected: PASS — 4개 전부 초록. (Task 3·4가 올바르면 신규 코드 없이 통과하는 통합 검증이다. 실패하면 Core/Transport의 버그이므로 **테스트를 고치지 말고 superpowers:systematic-debugging으로 원인을 추적**한다.)

- [ ] **Step 3: 전체 테스트 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests`
Expected: PASS — 전체 초록 (v0 완료 조건인 에코 E2E 달성)

- [ ] **Step 4: Commit**

```
git add server/tests/Bun3.Server.Tests/EchoE2ETests.cs
git commit -m "✅ Add echo E2E tests over real TCP (v0 acceptance)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Hosting 패키지 (AddBun3Server) — TDD

**Files:**
- Create: `server/src/Bun3.Server.Hosting/Bun3ServerOptions.cs`
- Create: `server/src/Bun3.Server.Hosting/Bun3LoggerBridge.cs`
- Create: `server/src/Bun3.Server.Hosting/HostedServer.cs`
- Create: `server/src/Bun3.Server.Hosting/Bun3ServerHostedService.cs`
- Create: `server/src/Bun3.Server.Hosting/Bun3ServerServiceCollectionExtensions.cs`
- Modify: `docs/superpowers/specs/2026-08-04-server-modulize-base-design.md` (§3 의존 표기에 Hosting → Transport.Tcp 반영)
- Test: `server/tests/Bun3.Server.Tests/HostingTests.cs`

**Interfaces:**
- Consumes: Task 3의 `ServerBase`/`Session`/`SessionOptions`, Task 4의 `TcpTransportListener`/`TcpTransportOptions`, Task 1의 `IBun3Logger`
- Produces:
  - `sealed class Bun3ServerOptions { int Port = 20000; int MaxFrameSize = 1024 * 1024; int MaxQueuedFramesPerSession = 256; }` — 구성 섹션 `"Bun3:Server"` 바인딩
  - `static IServiceCollection AddBun3Server<TSession>(this IServiceCollection, Action<Bun3ServerOptions>? configure = null) where TSession : Session` — `TSession`은 `IConnection`을 첫 인자로 받는 public 생성자 필요(DI 서비스 주입 가능). `TcpTransportListener`가 싱글턴으로 등록되므로 테스트에서 `host.Services.GetRequiredService<TcpTransportListener>().BoundPort`로 포트 확인 가능
  - Task 7의 샘플이 사용

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/HostingTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Hosting;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Tests;

[TestFixture]
public class HostingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public sealed class EchoSession : Session
    {
        public EchoSession(IConnection connection) : base(connection) { }

        protected override ValueTask OnFrameAsync(ReadOnlyMemory<byte> frame) => SendAsync(frame);
    }

    [Test]
    public async Task Host_boots_serves_echo_and_stops_gracefully()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddBun3Server<EchoSession>(options => options.Port = 0);
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
    public void Options_bind_from_Bun3_Server_configuration_section()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bun3:Server:Port"] = "0",
            ["Bun3:Server:MaxFrameSize"] = "2048",
            ["Bun3:Server:MaxQueuedFramesPerSession"] = "32",
        });
        builder.Services.AddBun3Server<EchoSession>();
        using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<Bun3ServerOptions>>().Value;

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
        builder.Services.AddBun3Server<EchoSession>(options => options.Port = 0);
        using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<Bun3ServerOptions>>().Value;

        Assert.That(options.Port, Is.EqualTo(0)); // 람다가 나중에 적용되어 우선한다
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~HostingTests"`
Expected: 컴파일 에러 — `AddBun3Server`, `Bun3ServerOptions` 미정의

- [ ] **Step 3: 구현**

`server/src/Bun3.Server.Hosting/Bun3ServerOptions.cs`:

```csharp
namespace Bun3.Server.Hosting;

/// <summary>구성 섹션 "Bun3:Server"에서 바인딩되는 서버 호스팅 옵션.</summary>
public sealed class Bun3ServerOptions
{
    public const string SectionName = "Bun3:Server";

    /// <summary>리슨 포트. 0이면 임의 포트(테스트용).</summary>
    public int Port { get; set; } = 20000;

    public int MaxFrameSize { get; set; } = 1024 * 1024;

    public int MaxQueuedFramesPerSession { get; set; } = 256;
}
```

`server/src/Bun3.Server.Hosting/Bun3LoggerBridge.cs`:

```csharp
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Hosting;

internal sealed class Bun3LoggerBridge : IBun3Logger
{
    private readonly ILogger _logger;

    public Bun3LoggerBridge(ILogger logger) => _logger = logger;

    public void Log(Bun3LogLevel level, string message, Exception? exception = null) =>
        _logger.Log(Map(level), exception, "{Message}", message);

    private static LogLevel Map(Bun3LogLevel level) => level switch
    {
        Bun3LogLevel.Debug => LogLevel.Debug,
        Bun3LogLevel.Info => LogLevel.Information,
        Bun3LogLevel.Warning => LogLevel.Warning,
        _ => LogLevel.Error,
    };
}
```

`server/src/Bun3.Server.Hosting/HostedServer.cs`:

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Core;

namespace Bun3.Server.Hosting;

/// <summary>DI가 제공하는 세션 팩토리로 CreateSession을 구현하는 호스팅용 서버.</summary>
internal sealed class HostedServer<TSession> : ServerBase<TSession> where TSession : Session
{
    private readonly Func<IConnection, TSession> _sessionFactory;

    public HostedServer(
        ITransportListener transport,
        Func<IConnection, TSession> sessionFactory,
        IBun3Logger logger,
        SessionOptions sessionOptions)
        : base(transport, logger, sessionOptions)
    {
        _sessionFactory = sessionFactory;
    }

    protected override TSession CreateSession(IConnection connection) => _sessionFactory(connection);
}
```

`server/src/Bun3.Server.Hosting/Bun3ServerHostedService.cs`:

```csharp
using Bun3.Server.Core;
using Microsoft.Extensions.Hosting;

namespace Bun3.Server.Hosting;

internal sealed class Bun3ServerHostedService<TSession> : IHostedService where TSession : Session
{
    private readonly HostedServer<TSession> _server;

    public Bun3ServerHostedService(HostedServer<TSession> server) => _server = server;

    public Task StartAsync(CancellationToken cancellationToken) => _server.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => _server.StopAsync(ct: cancellationToken);
}
```

`server/src/Bun3.Server.Hosting/Bun3ServerServiceCollectionExtensions.cs`:

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

public static class Bun3ServerServiceCollectionExtensions
{
    /// <summary>
    /// TCP 전송 기반 Bun3 서버를 Generic Host에 등록한다.
    /// TSession은 IConnection을 받는 public 생성자가 필요하며, 나머지 인자는 DI로 주입된다.
    /// </summary>
    public static IServiceCollection AddBun3Server<TSession>(
        this IServiceCollection services,
        Action<Bun3ServerOptions>? configure = null)
        where TSession : Session
    {
        var optionsBuilder = services.AddOptions<Bun3ServerOptions>()
            .BindConfiguration(Bun3ServerOptions.SectionName);
        if (configure != null)
        {
            optionsBuilder.Configure(configure);
        }

        services.AddSingleton<IBun3Logger>(sp =>
        {
            // 최소 구성 호스트(DisableDefaults 등)에서 로깅이 없어도 동작하도록 방어
            var factory = sp.GetService<ILoggerFactory>();
            return factory != null
                ? new Bun3LoggerBridge(factory.CreateLogger("Bun3.Server"))
                : NullBun3Logger.Instance;
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<Bun3ServerOptions>>().Value;
            return new TcpTransportListener(
                new TcpTransportOptions { Port = options.Port, MaxFrameSize = options.MaxFrameSize },
                sp.GetRequiredService<IBun3Logger>());
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<Bun3ServerOptions>>().Value;
            TSession Factory(IConnection connection) =>
                ActivatorUtilities.CreateInstance<TSession>(sp, connection);
            return new HostedServer<TSession>(
                sp.GetRequiredService<TcpTransportListener>(),
                Factory,
                sp.GetRequiredService<IBun3Logger>(),
                new SessionOptions { MaxQueuedFrames = options.MaxQueuedFramesPerSession });
        });

        services.AddHostedService<Bun3ServerHostedService<TSession>>(sp =>
            new Bun3ServerHostedService<TSession>(sp.GetRequiredService<HostedServer<TSession>>()));

        return services;
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~HostingTests"`
Expected: PASS — 3개 전부 초록. 이후 전체: `dotnet test server/tests/Bun3.Server.Tests` 전부 초록

- [ ] **Step 5: 스펙 의존 표기 갱신**

`docs/superpowers/specs/2026-08-04-server-modulize-base-design.md` §3에서
`│   └── Bun3.Server.Hosting/       net10.0 · → Core + Microsoft.Extensions.Hosting` 줄을 다음으로 교체:

```
│   └── Bun3.Server.Hosting/       net10.0 · → Core, Transport.Tcp + Microsoft.Extensions.Hosting
```

같은 섹션 끝의 "의존은 단방향" 문단 뒤에 한 문장 추가:

```
Hosting은 기본 TCP 전송을 조립하는 계층이므로 Transport.Tcp도 참조한다.
```

- [ ] **Step 6: Commit**

```
git add server/src/Bun3.Server.Hosting/ server/tests/Bun3.Server.Tests/HostingTests.cs docs/superpowers/specs/2026-08-04-server-modulize-base-design.md
git commit -m "✨ Add ASP.NET Core hosting integration (AddBun3Server) with host E2E tests" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: EchoServer 샘플 + 스모크 확인

**Files:**
- Create: `server/samples/EchoServer/EchoServer.csproj`
- Create: `server/samples/EchoServer/Program.cs`
- Modify: `Bun3.sln` (`dotnet sln add`)

**Interfaces:**
- Consumes: Task 6의 `AddBun3Server<TSession>`, Task 3의 `Session`
- Produces: 없음 (조립 예제 겸 수동 확인용 실행 파일)

- [ ] **Step 1: 샘플 프로젝트 작성**

`server/samples/EchoServer/EchoServer.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Bun3.Server.Samples.EchoServer</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Bun3.Server.Hosting\Bun3.Server.Hosting.csproj" />
  </ItemGroup>

</Project>
```

`server/samples/EchoServer/Program.cs`:

```csharp
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Hosting;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddBun3Server<EchoSession>(options => options.Port = 20000);
await builder.Build().RunAsync();

/// <summary>받은 프레임을 그대로 돌려주는 최소 세션 — Bun3.Server 조립의 최소 예제.</summary>
public sealed class EchoSession : Session
{
    public EchoSession(IConnection connection) : base(connection) { }

    protected override ValueTask OnFrameAsync(ReadOnlyMemory<byte> frame) => SendAsync(frame);
}
```

- [ ] **Step 2: 솔루션 편입 + 빌드**

Run (레포 루트에서):

```
dotnet sln Bun3.sln add server/samples/EchoServer/EchoServer.csproj
dotnet build Bun3.sln
```

Expected: Build succeeded

- [ ] **Step 3: 스모크 확인 (PowerShell)**

```powershell
$proc = Start-Process dotnet -ArgumentList 'run','--project','server/samples/EchoServer','--no-build' -PassThru
Start-Sleep -Seconds 5
$result = Test-NetConnection -ComputerName localhost -Port 20000
Stop-Process -Id $proc.Id -Force
$result.TcpTestSucceeded
```

Expected: 마지막 출력 `True` (포트 20000 리슨 확인)

- [ ] **Step 4: 전체 테스트 최종 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests; dotnet test common/tests/Bun3.Common.Tests`
Expected: 양쪽 모두 PASS (기존 common 테스트 회귀 없음)

- [ ] **Step 5: Commit**

```
git add server/samples/ Bun3.sln
git commit -m "✨ Add EchoServer sample assembling Hosting + Core + TCP transport" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## 완료 기준 (스펙 §8 대응)

- [ ] `dotnet build Bun3.sln` 성공 (netstandard2.1 3개 + net10.0 2개 + 샘플)
- [ ] 프레이밍 단위 테스트 초록 (분할/병합/경계/초과/EOF)
- [ ] 세션 액터 단위 테스트 초록 (순서/비중첩/적체/에러 정책)
- [ ] 실 TCP 에코 E2E 초록 (다중 클라이언트, graceful shutdown 포함)
- [ ] 호스팅 E2E 초록 (Generic Host 부팅 → 에코 → 정지, 구성 바인딩)
- [ ] EchoServer 샘플 실행 스모크 확인
