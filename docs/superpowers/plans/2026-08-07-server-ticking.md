# Bun3.Server.Ticking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 전역 틱 루프 + Player 주기 저장 + 벽시계 스케줄 — "저장 지점이 은퇴뿐"인 데이터 손실 구멍을 닫는다.

**Architecture:** `Bun3.Server.Ticking`(TickLoop, Every/DailyAt) 신설. Player 틱/저장 작업은 `Session.Post`(Core 신규)로 그 Player의 세션 액터에 주입되어 핸들러와 같은 줄에서 순차 실행(락 제로). 세션 큐 항목 전체에 감시(watchdog) 로그. Hosting이 전부 자동 배선. 스펙: `docs/superpowers/specs/2026-08-07-server-ticking-design.md`.

**Tech Stack:** netstandard2.1 + C#9, Microsoft.Bcl.TimeProvider 8.0.1, NUnit 4(net10.0).

## Global Constraints

- 패키지 코드는 `netstandard2.1` + `LangVersion 9.0` + `Nullable enable`, 블록 네임스페이스(파일 스코프 금지). 테스트(net10.0)는 파일 스코프 허용.
- 버전: Ticking **0.1.0**(신규), Core **0.1.0→0.2.0**, Rpc **0.2.0→0.3.0**, Players **0.1.0→0.2.0**, Hosting **0.2.0→0.3.0**. csproj `<Version>` 갱신 필수.
- Ticking 의존: `Microsoft.Bcl.TimeProvider 8.0.1` + ProjectReference `Bun3.Server.Core`(SafeLogger 재사용). 그 외 금지.
- 모든 public 멤버 한국어 XML 문서, `GenerateDocumentationFile true`, **빌드 경고 0**.
- 라이브러리 await 전부 `ConfigureAwait(false)`. net5+ 전용 API 금지(테스트 코드는 허용).
- 새 백그라운드 루프의 로거는 반드시 `SafeLogger`로 감쌀 것(로거 예외가 루프를 죽이는 사고 방지 — v0 교훈).
- 커밋: gitmoji + `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` — **반드시 `git commit -m "<제목>" -m "<트레일러>"` 이중 플래그** (here-string을 bash로 돌리면 메시지가 깨진다).
- 감시/틱 의미론(스펙 §4·§5): 감시는 로그만, 강제 중단 없음(직렬화 유지). Post 작업 예외는 로그 후 세션 유지. Post는 큐 상한/닫힘 시 false(킥 없음 — 패킷 오버플로와 다름). detach 시 dirty면 즉시 저장. DailyAt 캐치업 없음.
- 타이밍 테스트는 여유 마진(대기 시간 ≥ 주기 ×3, 상한 검증은 느슨하게)으로 작성 — CI 흔들림 방지.

---

### Task 1: `Bun3.Server.Ticking` — TickLoop

**Files:**
- Create: `server/src/Bun3.Server.Ticking/Bun3.Server.Ticking.csproj`
- Create: `server/src/Bun3.Server.Ticking/TickingOptions.cs`
- Create: `server/src/Bun3.Server.Ticking/TickLoop.cs`
- Modify: `Bun3.sln` (dotnet sln add)
- Modify: `server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj` (프로젝트 참조 추가)
- Test: `server/tests/Bun3.Server.Tests/TickLoopTests.cs`

**Interfaces:**
- Consumes: `Bun3.Server.Core.SafeLogger(ILogger)` (기존, public)
- Produces (Task 3·4가 사용):
  - `sealed class TickingOptions { TimeSpan TickInterval = 100ms; TimeProvider TimeProvider = TimeProvider.System; }`
  - `sealed class TickLoop`: `TickLoop(TickingOptions? options = null, ILogger? logger = null)`, `void Every(TimeSpan interval, Func<TimeSpan, ValueTask> job, string? name = null)`, `void DailyAt(TimeSpan timeOfDay, TimeSpan utcOffset, Func<ValueTask> job, string? name = null)`, `void Start()`, `Task StopAsync()`, `static DateTimeOffset NextDailyOccurrence(DateTimeOffset nowUtc, TimeSpan timeOfDay, TimeSpan utcOffset)`

- [ ] **Step 1: 프로젝트 생성 + 솔루션/테스트 참조 연결**

`server/src/Bun3.Server.Ticking/Bun3.Server.Ticking.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>Bun3.Server.Ticking</RootNamespace>
    <PackageId>Bun3.Server.Ticking</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-kit</RepositoryUrl>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <Description>전역 틱 루프 — Every/DailyAt 잡, 드리프트 보정, 잡별 예외 격리</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Bcl.TimeProvider" Version="8.0.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Bun3.Server.Core\Bun3.Server.Core.csproj" />
  </ItemGroup>

</Project>
```

Run (레포 루트에서):

```powershell
dotnet sln Bun3.sln add server/src/Bun3.Server.Ticking/Bun3.Server.Ticking.csproj
```

`server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`의 ProjectReference ItemGroup에 추가:

```xml
    <ProjectReference Include="..\..\src\Bun3.Server.Ticking\Bun3.Server.Ticking.csproj" />
```

- [ ] **Step 2: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/TickLoopTests.cs`:

```csharp
using System.Collections.Concurrent;
using Bun3.Server.Ticking;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class TickLoopTests
{
    [Test]
    public async Task Every_job_runs_repeatedly_with_sane_delta()
    {
        var deltas = new ConcurrentQueue<TimeSpan>();
        var loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(20) });
        loop.Every(TimeSpan.FromMilliseconds(50), delta =>
        {
            deltas.Enqueue(delta);
            return default;
        });

        loop.Start();
        await Task.Delay(700);
        await loop.StopAsync();

        Assert.That(deltas.Count, Is.GreaterThanOrEqualTo(3));
        foreach (var delta in deltas)
        {
            Assert.That(delta, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(delta, Is.LessThan(TimeSpan.FromSeconds(2)));   // CI 스톨 대비 느슨한 상한
        }
    }

    [Test]
    public async Task Job_exception_does_not_kill_loop_or_other_jobs()
    {
        var healthyRuns = 0;
        var loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(20) });
        loop.Every(TimeSpan.FromMilliseconds(30), _ => throw new InvalidOperationException("boom"), "bomb");
        loop.Every(TimeSpan.FromMilliseconds(30), _ =>
        {
            Interlocked.Increment(ref healthyRuns);
            return default;
        }, "healthy");

        loop.Start();
        await Task.Delay(400);
        await loop.StopAsync();

        Assert.That(healthyRuns, Is.GreaterThanOrEqualTo(3));   // 폭탄 잡이 루프를 못 죽였다
    }

    [Test]
    public void Registration_after_start_throws()
    {
        var loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(20) });
        loop.Start();
        try
        {
            Assert.Throws<InvalidOperationException>(() => loop.Every(TimeSpan.FromSeconds(1), _ => default));
            Assert.Throws<InvalidOperationException>(() =>
                loop.DailyAt(TimeSpan.FromHours(5), TimeSpan.Zero, () => default));
        }
        finally
        {
            loop.StopAsync().GetAwaiter().GetResult();
        }
    }

    [Test]
    public async Task StopAsync_waits_for_inflight_job()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        var loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(10) });
        loop.Every(TimeSpan.FromMilliseconds(10), async _ =>
        {
            entered.TrySetResult(true);
            await Task.Delay(150);
            Interlocked.Increment(ref completed);
        });

        loop.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await loop.StopAsync();

        Assert.That(completed, Is.GreaterThanOrEqualTo(1));   // 진행 중이던 잡이 끝난 뒤에 정지했다
    }

    // NextDailyOccurrence — 순수 함수 결정적 검증 (가짜 시계 불필요)
    [TestCase("2026-01-15T02:00:00+00:00", 5, 0, "2026-01-15T05:00:00+00:00")]   // 오늘 아직 안 지남
    [TestCase("2026-01-15T06:00:00+00:00", 5, 0, "2026-01-16T05:00:00+00:00")]   // 오늘 지남 → 내일
    [TestCase("2026-01-14T21:00:00+00:00", 5, 9, "2026-01-15T20:00:00+00:00")]   // KST 06:00 → 다음 KST 05:00
    [TestCase("2026-01-15T20:00:00+00:00", 5, 9, "2026-01-16T20:00:00+00:00")]   // 정확히 발생 시각 → 다음날 (전진 보장)
    public void NextDailyOccurrence_computes_next_fire_time(
        string nowIso, int hourOfDay, int offsetHours, string expectedIso)
    {
        var next = TickLoop.NextDailyOccurrence(
            DateTimeOffset.Parse(nowIso),
            TimeSpan.FromHours(hourOfDay),
            TimeSpan.FromHours(offsetHours));

        Assert.That(next, Is.EqualTo(DateTimeOffset.Parse(expectedIso)));
    }

    [Test]
    public async Task DailyAt_fires_when_time_of_day_arrives()
    {
        var now = DateTimeOffset.UtcNow;
        var timeOfDay = now.TimeOfDay + TimeSpan.FromMilliseconds(300);
        if (timeOfDay >= TimeSpan.FromHours(24))
        {
            Assert.Inconclusive("자정 직전 — 재실행하면 통과한다.");
        }

        var fired = 0;
        var loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(20) });
        loop.DailyAt(timeOfDay, TimeSpan.Zero, () =>
        {
            Interlocked.Increment(ref fired);
            return default;
        });

        loop.Start();
        await Task.Delay(1200);
        await loop.StopAsync();

        Assert.That(fired, Is.EqualTo(1));   // 발화 1회 — 다음 발생은 내일이므로 재발화 없음
    }
}
```

- [ ] **Step 3: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~TickLoopTests"`
Expected: 컴파일 오류 (`TickLoop` 미정의)

- [ ] **Step 4: 구현**

`server/src/Bun3.Server.Ticking/TickingOptions.cs`:

```csharp
using System;

namespace Bun3.Server.Ticking
{
    /// <summary>TickLoop 동작 옵션 — 생성자에서 스냅샷되며 이후 변경은 무시된다.</summary>
    public sealed class TickingOptions
    {
        /// <summary>루프 1회전 목표 주기. 잡 실행 시간을 빼고 대기한다(드리프트 보정).</summary>
        public TimeSpan TickInterval { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>시계 — 기본 시스템 시계. 테스트/특수 환경에서 교체 가능.</summary>
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
    }
}
```

`server/src/Bun3.Server.Ticking/TickLoop.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Ticking
{
    /// <summary>
    /// 전역 틱 루프 — 등록된 잡을 한 흐름에서 순차 실행한다. 잡 예외는 격리(로그 후
    /// 계속)되고 대기는 드리프트를 보정한다. 잡은 짧아야 한다 — 오래 걸리면 다른
    /// 잡이 밀린다. 무거운 작업은 잡 안에서 별도 Task로 던질 것.
    /// 등록(Every/DailyAt)은 Start 전에만 허용된다.
    /// </summary>
    public sealed class TickLoop
    {
        private static readonly TimeSpan MinDelay = TimeSpan.FromMilliseconds(10);

        private sealed class Job
        {
            public readonly string Name;
            public readonly Func<DateTimeOffset, ValueTask> Run;
            public readonly Func<DateTimeOffset, DateTimeOffset, DateTimeOffset> Advance;   // (now, 이전 NextAt) → 다음 NextAt
            public DateTimeOffset NextAt;

            public Job(string name, Func<DateTimeOffset, ValueTask> run,
                Func<DateTimeOffset, DateTimeOffset, DateTimeOffset> advance, DateTimeOffset firstAt)
            {
                Name = name;
                Run = run;
                Advance = advance;
                NextAt = firstAt;
            }
        }

        private readonly TimeSpan _tickInterval;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;
        private readonly List<Job> _jobs = new List<Job>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _runTask;

        /// <summary>틱 루프를 구성한다. Start 전까지는 아무것도 돌지 않는다.</summary>
        public TickLoop(TickingOptions? options = null, ILogger? logger = null)
        {
            var effective = options ?? new TickingOptions();
            if (effective.TickInterval <= TimeSpan.Zero)
            {
                throw new ArgumentException("TickInterval은 양수여야 한다.", nameof(options));
            }

            _tickInterval = effective.TickInterval;
            _timeProvider = effective.TimeProvider ?? TimeProvider.System;
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
        }

        /// <summary>고정 간격 잡을 등록한다. job의 인자는 이 잡의 지난 실행 이후 실제 경과 시간.</summary>
        public void Every(TimeSpan interval, Func<TimeSpan, ValueTask> job, string? name = null)
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentException("간격은 양수여야 한다.", nameof(interval));
            }
            if (job == null) throw new ArgumentNullException(nameof(job));
            EnsureNotStarted();

            var lastRunAt = _timeProvider.GetUtcNow();
            _jobs.Add(new Job(
                name ?? string.Format(CultureInfo.InvariantCulture, "every-{0:F0}ms", interval.TotalMilliseconds),
                run: now =>
                {
                    var delta = now - lastRunAt;
                    lastRunAt = now;
                    return job(delta);
                },
                advance: (now, previousNextAt) =>
                {
                    var next = previousNextAt + interval;
                    return next > now ? next : now + interval;   // 밀린 만큼 몰아서 발화하지 않는다
                },
                firstAt: lastRunAt + interval));
        }

        /// <summary>매일 지정 시각(utcOffset 기준 하루 중 시각)에 발화하는 잡을 등록한다.
        /// 서버가 꺼져 있던 사이의 발생은 캐치업하지 않는다 — "오늘 리셋을 받았나"는
        /// 게임 데이터로 판정할 것(스펙 §8 권장 패턴).</summary>
        public void DailyAt(TimeSpan timeOfDay, TimeSpan utcOffset, Func<ValueTask> job, string? name = null)
        {
            if (timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromHours(24))
            {
                throw new ArgumentException("timeOfDay는 [0, 24시간) 범위여야 한다.", nameof(timeOfDay));
            }
            if (job == null) throw new ArgumentNullException(nameof(job));
            EnsureNotStarted();

            _jobs.Add(new Job(
                name ?? string.Format(CultureInfo.InvariantCulture, "daily-{0:hh\\:mm}", timeOfDay),
                run: _ => job(),
                advance: (now, _) => NextDailyOccurrence(now, timeOfDay, utcOffset),
                firstAt: NextDailyOccurrence(_timeProvider.GetUtcNow(), timeOfDay, utcOffset)));
        }

        /// <summary>다음 발생 시각을 계산한다 — nowUtc "이후"의 첫 (utcOffset 기준 timeOfDay).
        /// 정확히 발생 시각과 같으면 다음날로 전진한다(중복 발화 방지).</summary>
        public static DateTimeOffset NextDailyOccurrence(DateTimeOffset nowUtc, TimeSpan timeOfDay, TimeSpan utcOffset)
        {
            var local = nowUtc.ToOffset(utcOffset);
            var todayAt = new DateTimeOffset(local.Date, utcOffset) + timeOfDay;
            return (todayAt > local ? todayAt : todayAt.AddDays(1)).ToUniversalTime();
        }

        /// <summary>루프를 시작한다. 1회만 호출 가능.</summary>
        public void Start()
        {
            if (_runTask != null)
            {
                throw new InvalidOperationException("TickLoop은 이미 시작되었다.");
            }

            _runTask = Task.Run(() => RunAsync(_cts.Token));
        }

        /// <summary>루프를 정지한다 — 진행 중인 틱(잡)이 끝날 때까지 기다린다.</summary>
        public async Task StopAsync()
        {
            _cts.Cancel();
            if (_runTask != null)
            {
                await _runTask.ConfigureAwait(false);
            }
        }

        private void EnsureNotStarted()
        {
            if (_runTask != null)
            {
                throw new InvalidOperationException("잡 등록은 Start 전에만 가능하다.");
            }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var tickStart = _timeProvider.GetUtcNow();
                    foreach (var job in _jobs)
                    {
                        if (job.NextAt > tickStart)
                        {
                            continue;
                        }

                        try
                        {
                            await job.Run(tickStart).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "틱 잡 {Job} 예외 — 루프는 계속된다.", job.Name);
                        }

                        job.NextAt = job.Advance(tickStart, job.NextAt);
                    }

                    var elapsed = _timeProvider.GetUtcNow() - tickStart;
                    var wait = _tickInterval - elapsed;
                    if (wait < MinDelay)
                    {
                        wait = MinDelay;
                    }

                    await _timeProvider.Delay(wait, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // StopAsync에 의한 정상 정지
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "틱 루프 비정상 종료.");
            }
        }
    }
}
```

참고: `_timeProvider.Delay(...)`는 `Microsoft.Bcl.TimeProvider`의
`System.Threading.Tasks.TimeProviderTaskExtensions` 확장이다 — using 추가 불필요
(네임스페이스가 `System.Threading.Tasks`).

- [ ] **Step 5: 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~TickLoopTests"`
Expected: PASS (신규 9개 초록 — TestCase 4개 포함)

- [ ] **Step 6: 커밋**

```powershell
git add server/src/Bun3.Server.Ticking server/tests/Bun3.Server.Tests/TickLoopTests.cs server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj Bun3.sln
git commit -m "✨ Add Bun3.Server.Ticking (TickLoop with Every/DailyAt jobs)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `Session.Post` + 감시(watchdog) — Core 0.2.0 / Rpc 0.3.0

**Files:**
- Modify: `server/src/Bun3.Server.Core/Session.cs`
- Modify: `server/src/Bun3.Server.Core/ServerBase.cs:29-39` (생성자)
- Modify: `server/src/Bun3.Server.Core/ServerBase.cs:99` (Initialize 호출)
- Modify: `server/src/Bun3.Server.Core/Bun3.Server.Core.csproj` (Version 0.2.0)
- Modify: `server/src/Bun3.Server.Rpc/RpcServerOptions.cs` (SlowWorkWarning 추가)
- Modify: `server/src/Bun3.Server.Rpc/RpcServer.cs:35` (base 호출)
- Modify: `server/src/Bun3.Server.Rpc/Bun3.Server.Rpc.csproj` (Version 0.3.0)
- Test: `server/tests/Bun3.Server.Tests/SessionPostTests.cs`

**Interfaces:**
- Consumes: 기존 `Session`/`ServerBase`/`RpcServer` 구조 (위 파일들).
- Produces (Task 3·4가 사용):
  - `public bool Post(Func<ValueTask> work)` on `Session`
  - `RpcServerOptions.SlowWorkWarning { get; set; } = TimeSpan.FromSeconds(1)` (≤0 = 감시 끔)
  - `ServerBase` 생성자 4번째 인자 `TimeSpan? slowWorkWarning = null`(null=1초)

- [ ] **Step 1: 실패하는 테스트 작성**

테스트는 내부 멤버를 쓰지 않고 실제 스택(TCP + RpcServer + RpcClient)으로
public API만 검증한다 — PlayersE2ETests와 같은 하네스 스타일. proto는 기존
`Protos/players_game.proto`의 타입을 재사용한다.

`server/tests/Bun3.Server.Tests/SessionPostTests.cs`:

```csharp
using System.Collections.Concurrent;
using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class SessionPostTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>스레드 안전한 수집 로거 — 감시 경고/작업 예외 로그 검증용.</summary>
    private sealed class CollectingLogger : ILogger
    {
        public readonly ConcurrentQueue<(LogLevel Level, string Message)> Entries = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    private sealed class PostSession : RpcSession
    {
        public PostSession(IConnection connection) : base(connection) { }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public readonly CollectingLogger Logger = new();
        public readonly ConcurrentQueue<string> Order = new();
        public RpcServer<PostSession, PlayersRequest, PlayersResponse, PlayersUpdate> Server = null!;
        public TcpTransportListener Listener = null!;

        public static async Task<Harness> StartAsync(TimeSpan? slowWorkWarning = null, int maxQueued = 256)
        {
            var h = new Harness();
            var config = new RpcConfig<PostSession>();
            config.OnRequest<GetGoldRequest, GetGoldResponse>((s, req) =>
            {
                h.Order.Enqueue("handler");
                return new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse { Gold = 1 });
            });
            h.Listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
            h.Server = new RpcServer<PostSession, PlayersRequest, PlayersResponse, PlayersUpdate>(
                h.Listener, conn => new PostSession(conn), config,
                new RpcServerOptions
                {
                    MaxQueuedPackets = maxQueued,
                    SlowWorkWarning = slowWorkWarning ?? TimeSpan.FromSeconds(1),
                },
                h.Logger);
            await h.Server.StartAsync();
            return h;
        }

        public ValueTask<RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>> ConnectAsync() =>
            RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(
                new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = Listener.BoundPort!.Value }));

        public async Task<PostSession> GetSessionAsync()
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                foreach (var session in Server.Sessions)
                {
                    return session;
                }
                await Task.Delay(10);
            }
            throw new TimeoutException("세션 미생성");
        }

        public async ValueTask DisposeAsync() => await Server.StopAsync();
    }

    [Test]
    public async Task Posted_work_interleaves_in_order_with_packets()
    {
        await using var h = await Harness.StartAsync();
        var client = await h.ConnectAsync();
        var session = await h.GetSessionAsync();

        var workDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.That(session.Post(() =>
        {
            h.Order.Enqueue("work");
            workDone.TrySetResult(true);
            return default;
        }), Is.True);

        var reply = await client.RequestAsync<GetGoldResponse>(new GetGoldRequest()).AsTask().WaitAsync(Timeout);
        Assert.That(reply.IsOk, Is.True);
        await workDone.Task.WaitAsync(Timeout);

        // Post가 요청보다 먼저 큐에 들어갔으므로 실행도 먼저다 (같은 큐, 순차)
        Assert.That(h.Order.ToArray(), Is.EqualTo(new[] { "work", "handler" }));
        client.Close();
    }

    [Test]
    public async Task Post_returns_false_after_session_closed()
    {
        await using var h = await Harness.StartAsync();
        var client = await h.ConnectAsync();
        var session = await h.GetSessionAsync();

        client.Close();
        var deadline = DateTime.UtcNow + Timeout;
        while (session.Connection.IsOpen && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        await Task.Delay(100);   // NotifyClosed 전파 여유

        Assert.That(session.Post(() => default), Is.False);
    }

    [Test]
    public async Task Post_returns_false_when_queue_is_full()
    {
        await using var h = await Harness.StartAsync(maxQueued: 4);
        var client = await h.ConnectAsync();
        var session = await h.GetSessionAsync();

        // 소비 루프를 막는 작업 하나 + 큐를 상한까지 채운다
        var block = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.That(session.Post(async () => await block.Task), Is.True);
        await Task.Delay(100);   // blocker가 dequeue되어 실행(대기) 상태로 들어갈 시간

        var accepted = 0;
        var sawFalse = false;
        for (var i = 0; i < 10; i++)
        {
            if (session.Post(() => default)) accepted++;
            else { sawFalse = true; break; }
        }

        Assert.That(sawFalse, Is.True);
        Assert.That(accepted, Is.LessThanOrEqualTo(4));
        block.TrySetResult(true);   // 정리
        client.Close();
    }

    [Test]
    public async Task Posted_work_exception_is_logged_and_session_survives()
    {
        await using var h = await Harness.StartAsync();
        var client = await h.ConnectAsync();
        var session = await h.GetSessionAsync();

        Assert.That(session.Post(() => throw new InvalidOperationException("posted-boom")), Is.True);

        // 예외 이후에도 같은 세션에서 요청이 정상 처리된다 — 세션 생존 증명
        var reply = await client.RequestAsync<GetGoldResponse>(new GetGoldRequest()).AsTask().WaitAsync(Timeout);
        Assert.That(reply.IsOk, Is.True);
        Assert.That(h.Logger.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains("posted work")),
            Is.True, "작업 예외 로그가 남아야 한다");
        client.Close();
    }

    [Test]
    public async Task Slow_work_triggers_watchdog_warning_and_completes()
    {
        await using var h = await Harness.StartAsync(slowWorkWarning: TimeSpan.FromMilliseconds(50));
        var client = await h.ConnectAsync();
        var session = await h.GetSessionAsync();

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Post(async () =>
        {
            await Task.Delay(250);
            done.TrySetResult(true);
        });

        await done.Task.WaitAsync(Timeout);   // 감시는 로그만 — 작업은 끝까지 실행된다
        Assert.That(h.Logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("blocked")),
            Is.True, "감시 경고 로그가 남아야 한다");
        client.Close();
    }

    [Test]
    public async Task Fast_work_produces_no_watchdog_warning()
    {
        await using var h = await Harness.StartAsync(slowWorkWarning: TimeSpan.FromMilliseconds(200));
        var client = await h.ConnectAsync();
        var session = await h.GetSessionAsync();

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Post(() => { done.TrySetResult(true); return default; });
        await done.Task.WaitAsync(Timeout);
        await Task.Delay(300);   // 잘못된 지연 경고가 있다면 이 사이 떠야 한다

        Assert.That(h.Logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("blocked")), Is.False);
        client.Close();
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~SessionPostTests"`
Expected: 컴파일 오류 (`Post`/`SlowWorkWarning` 미정의)

- [ ] **Step 3: 구현 — Session.cs**

`server/src/Bun3.Server.Core/Session.cs` 변경 사항:

1. 인박스 타입 교체 — `_inbox`를 `ConcurrentQueue<object>`로 (byte[] 또는 Func&lt;ValueTask&gt;):

```csharp
        private readonly ConcurrentQueue<object> _inbox = new ConcurrentQueue<object>();
```

2. 필드 추가 (기존 `_maxQueuedPackets` 옆):

```csharp
        private TimeSpan _slowWorkWarning = TimeSpan.FromSeconds(1);
```

3. `Initialize` 서명 확장:

```csharp
        internal void Initialize(ILogger logger, int maxQueuedPackets, TimeSpan slowWorkWarning)
        {
            _logger = logger;
            _maxQueuedPackets = maxQueuedPackets;
            _slowWorkWarning = slowWorkWarning;
        }
```

4. `Post` 추가 (`Kick()` 아래):

```csharp
        /// <summary>
        /// 세션 액터 큐에 작업을 주입한다 — 패킷 처리와 같은 줄에서 순차 실행되므로
        /// 핸들러와 같은 상태를 락 없이 만질 수 있다. 세션이 닫혔거나 큐가 상한이면
        /// false(작업 미실행). 작업의 미처리 예외는 로그만 남기고 세션은 유지된다.
        /// 종료 직전 경합 시 true를 반환하고도 실행되지 않을 수 있다(최선 노력).
        /// </summary>
        public bool Post(Func<ValueTask> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (_closed)
            {
                return false;
            }

            if (Interlocked.Increment(ref _queuedCount) > _maxQueuedPackets)
            {
                Interlocked.Decrement(ref _queuedCount);
                return false;   // 패킷 오버플로와 달리 킥하지 않는다 — 호출자가 스킵을 판단
            }

            _inbox.Enqueue(work);
            _signal.Release();
            return true;
        }
```

5. `EnqueuePacket`의 큐 삽입은 그대로 (`_inbox.Enqueue(packet.ToArray());` — object 큐에 byte[] 삽입).

6. `RunAsync`의 소비 부분을 항목 타입으로 분기 + 감시 적용 (기존 `TryDequeue`~`HandleError` 블록 교체):

```csharp
                    var dequeued = _inbox.TryDequeue(out var item);
                    System.Diagnostics.Debug.Assert(dequeued, "signal/inbox invariant broken");

                    Interlocked.Decrement(ref _queuedCount);
                    if (item is byte[] packet)
                    {
                        try
                        {
                            await WatchAsync(() => OnPacketAsync(packet), "handler").ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            HandleError(ex);
                        }
                    }
                    else
                    {
                        var work = (Func<ValueTask>)item!;
                        try
                        {
                            await WatchAsync(work, "posted work").ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Session {SessionId}: posted work threw; session continues.", Id);
                        }
                    }
```

7. 감시 헬퍼 추가 (`HandleError` 위):

```csharp
        private async ValueTask WatchAsync(Func<ValueTask> action, string kind)
        {
            if (_slowWorkWarning <= TimeSpan.Zero)
            {
                await action().ConfigureAwait(false);
                return;
            }

            var pending = action();
            if (pending.IsCompleted)
            {
                await pending.ConfigureAwait(false);   // 동기 완료 — 감시 불필요 (fast path, 무할당)
                return;
            }

            var task = pending.AsTask();
            using var delayCts = new CancellationTokenSource();
            var delay = Task.Delay(_slowWorkWarning, delayCts.Token);
            if (await Task.WhenAny(task, delay).ConfigureAwait(false) != task)
            {
                _logger.LogWarning(
                    "Session {SessionId}: {Kind} running longer than {Threshold} — queue is blocked.",
                    Id, kind, _slowWorkWarning);
            }

            delayCts.Cancel();   // 타이머 즉시 정리 (고부하에서 타이머 적체 방지)
            await task.ConfigureAwait(false);
        }
```

- [ ] **Step 4: 구현 — ServerBase / Rpc 배선 + 버전**

`server/src/Bun3.Server.Core/ServerBase.cs` — 생성자에 매개변수 추가(기본값이라 기존 호출부 비파괴):

```csharp
        private readonly TimeSpan _slowWorkWarning;

        /// <summary>서버 베이스를 구성한다. transport는 시작 시 handler를 바인딩받는다.
        /// slowWorkWarning: 세션 큐 항목이 이 시간을 넘기면 경고 로그(null=1초, 0 이하=끔).</summary>
        protected ServerBase(
            ITransportListener transport,
            ILogger? logger = null,
            int maxQueuedPackets = 256,
            TimeSpan? slowWorkWarning = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
            _maxQueuedPackets = maxQueuedPackets;
            _slowWorkWarning = slowWorkWarning ?? TimeSpan.FromSeconds(1);
            _handler = new Handler(this);
        }
```

같은 파일의 `session.Initialize(_logger, _maxQueuedPackets);` 를:

```csharp
            session.Initialize(_logger, _maxQueuedPackets, _slowWorkWarning);
```

`server/src/Bun3.Server.Rpc/RpcServerOptions.cs` — 속성 추가:

```csharp
        /// <summary>세션 큐 항목(핸들러·Post 작업)이 이 시간을 넘기면 경고 로그를 남긴다.
        /// 강제 중단은 하지 않는다(직렬화 유지). 0 이하 = 감시 끔.</summary>
        public TimeSpan SlowWorkWarning { get; set; } = TimeSpan.FromSeconds(1);
```

`server/src/Bun3.Server.Rpc/RpcServer.cs:35` — base 호출에 전달:

```csharp
            : base(transport, logger, (options ??= new RpcServerOptions()).MaxQueuedPackets, options.SlowWorkWarning)
```

버전: `Bun3.Server.Core.csproj`의 `<Version>`을 `0.2.0`으로,
`Bun3.Server.Rpc.csproj`의 `<Version>`을 `0.3.0`으로.

- [ ] **Step 5: 통과 확인 + 전체 회귀**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~SessionPostTests"`
Expected: PASS (신규 6개 초록)

Run: `dotnet test server/tests/Bun3.Server.Tests`
Expected: 전체 PASS (기존 138 + Task 1 9 + Task 2 6 = 153) — 인박스 타입 교체가 기존 경로를 깨지 않았는지 확인

- [ ] **Step 6: 커밋**

```powershell
git add server/src/Bun3.Server.Core server/src/Bun3.Server.Rpc server/tests/Bun3.Server.Tests/SessionPostTests.cs
git commit -m "✨ Add Session.Post work injection and slow-work watchdog" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Players 통합 — Player 틱/저장 훅 + PlayerTicker (Players 0.2.0)

**Files:**
- Modify: `server/src/Bun3.Server.Players/Player.cs`
- Modify: `server/src/Bun3.Server.Players/PlayersOptions.cs`
- Modify: `server/src/Bun3.Server.Players/PlayerRegistry.cs` (Attach 리셋 + detach 저장 + SaveInterval 스냅샷)
- Create: `server/src/Bun3.Server.Players/PlayerTicker.cs`
- Modify: `server/src/Bun3.Server.Players/Bun3.Server.Players.csproj` (Ticking 참조 + Version 0.2.0)
- Test: `server/tests/Bun3.Server.Tests/PlayerTickingTests.cs`

**Interfaces:**
- Consumes: Task 1 `TickLoop.Every(TimeSpan, Func<TimeSpan, ValueTask>, string?)`, Task 2 `Session.Post(Func<ValueTask>)`
- Produces (Task 4가 사용):
  - `Player`: `protected internal virtual ValueTask OnTickAsync(TimeSpan delta)`, `protected internal virtual ValueTask OnSaveAsync()`, `public void MarkDirty()`, `public bool IsDirty { get; }`, `internal ValueTask TrySaveAsync(ILogger)`
  - `PlayersOptions`: `PlayerTickInterval = 1s`, `SaveInterval = 30s`
  - `sealed class PlayerTicker<TPlayer>`: `PlayerTicker(PlayerRegistry<TPlayer> registry, PlayersOptions? options = null, ILogger? logger = null)`, `void Register(TickLoop loop)`

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/PlayerTickingTests.cs` (PlayersE2ETests와 같은 실스택 하네스 — proto는 `players_game.proto` 재사용):

```csharp
using System.Collections.Concurrent;
using Bun3.Server.Abstractions;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Ticking;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerTickingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class TickPlayer : Player
    {
        public long Gold = 100;
        public readonly ConcurrentQueue<TimeSpan> TickDeltas = new();
        public int SaveCalls;
        public volatile bool FailNextSave;
        private int _concurrent;
        public readonly ConcurrentQueue<string> Violations = new();

        protected override async ValueTask OnTickAsync(TimeSpan delta)
        {
            Enter("tick");
            TickDeltas.Enqueue(delta);
            await Task.Delay(1);
            Exit();
        }

        protected override ValueTask OnSaveAsync()
        {
            Interlocked.Increment(ref SaveCalls);
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new InvalidOperationException("save-fail");
            }
            return default;
        }

        public void Enter(string who)
        {
            if (Interlocked.Increment(ref _concurrent) != 1)
            {
                Violations.Enqueue(who);
            }
        }

        public void Exit() => Interlocked.Decrement(ref _concurrent);
    }

    private sealed class TickSession : PlayerSession<TickPlayer>
    {
        public TickSession(IConnection connection) : base(connection) { }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public PlayerRegistry<TickPlayer> Registry = null!;
        public RpcServer<TickSession, PlayersRequest, PlayersResponse, PlayersUpdate> Server = null!;
        public TcpTransportListener Listener = null!;
        public TickLoop Loop = null!;

        public static async Task<Harness> StartAsync(
            TimeSpan? tickInterval = null, TimeSpan? saveInterval = null)
        {
            var h = new Harness();
            var playersOptions = new PlayersOptions
            {
                PlayerTickInterval = tickInterval ?? TimeSpan.FromMilliseconds(40),
                SaveInterval = saveInterval ?? TimeSpan.FromMilliseconds(150),
            };
            h.Registry = new PlayerRegistry<TickPlayer>(
                _ => new ValueTask<TickPlayer>(new TickPlayer()), playersOptions);

            var config = new PlayersConfig<TickSession>();
            config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
            {
                var result = await s.SignInAsync($"guest:{req.DeviceId}");
                return new LoginResponse { Gold = result.Player.Gold, IsReconnect = result.IsReconnect };
            });
            config.OnRequest<AddGoldRequest, AddGoldResponse>(async (s, req) =>
            {
                var player = s.Player!;
                player.Enter("handler");
                player.Gold += req.Amount;
                player.MarkDirty();
                await Task.Delay(1);
                player.Exit();
                return new AddGoldResponse { Gold = player.Gold };
            });

            h.Listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
            h.Server = new RpcServer<TickSession, PlayersRequest, PlayersResponse, PlayersUpdate>(
                h.Listener, h.Registry.Wrap(config, conn => new TickSession(conn)), config.Rpc);
            await h.Server.StartAsync();

            h.Loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(20) });
            new PlayerTicker<TickPlayer>(h.Registry, playersOptions).Register(h.Loop);
            h.Loop.Start();
            return h;
        }

        public ValueTask<RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>> ConnectAsync() =>
            RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(
                new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = Listener.BoundPort!.Value }));

        public async Task<RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>> LoginAsync(string device)
        {
            var client = await ConnectAsync();
            var reply = await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = device })
                .AsTask().WaitAsync(Timeout);
            Assert.That(reply.IsOk, Is.True);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await Loop.StopAsync();
            await Server.StopAsync();
            await Registry.RetireAllAsync();
        }
    }

    [Test]
    public async Task Tick_hook_runs_while_connected_with_sane_delta()
    {
        await using var h = await Harness.StartAsync();
        var client = await h.LoginAsync("t1");
        var player = h.Registry.TryGet("guest:t1")!;

        await Task.Delay(500);

        Assert.That(player.TickDeltas.Count, Is.GreaterThanOrEqualTo(3));
        foreach (var delta in player.TickDeltas)
        {
            Assert.That(delta, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(delta, Is.LessThan(TimeSpan.FromSeconds(1)));
        }
        Assert.That(player.Violations, Is.Empty);
        client.Close();
    }

    [Test]
    public async Task Tick_and_handlers_never_run_concurrently()
    {
        await using var h = await Harness.StartAsync(tickInterval: TimeSpan.FromMilliseconds(20));
        var client = await h.LoginAsync("t2");
        var player = h.Registry.TryGet("guest:t2")!;

        for (var i = 0; i < 50; i++)
        {
            var reply = await client.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 1 })
                .AsTask().WaitAsync(Timeout);
            Assert.That(reply.IsOk, Is.True);
        }

        Assert.That(player.Violations, Is.Empty, "틱 훅과 핸들러가 동시에 실행되면 안 된다");
        Assert.That(player.TickDeltas.Count, Is.GreaterThanOrEqualTo(1));
        client.Close();
    }

    [Test]
    public async Task Ticks_pause_during_grace_and_resume_after_relogin()
    {
        await using var h = await Harness.StartAsync();
        var client1 = await h.LoginAsync("t3");
        var player = h.Registry.TryGet("guest:t3")!;
        await Task.Delay(150);

        client1.Close();
        await Task.Delay(200);                       // detach 전파 대기
        var countDuringGraceStart = player.TickDeltas.Count;
        await Task.Delay(500);                       // 유예 중 — 오프라인 구간
        Assert.That(player.TickDeltas.Count, Is.LessThanOrEqualTo(countDuringGraceStart + 1),
            "유예 중에는 틱이 멈춰야 한다 (전파 경계의 1회 오차 허용)");

        var client2 = await h.LoginAsync("t3");      // 재바인딩
        await Task.Delay(300);
        Assert.That(player.TickDeltas.Count, Is.GreaterThan(countDuringGraceStart + 1), "재접속 후 틱 재개");
        // delta 리셋 — 오프라인 500ms가 delta에 합산되지 않았다
        foreach (var delta in player.TickDeltas)
        {
            Assert.That(delta, Is.LessThan(TimeSpan.FromMilliseconds(450)));
        }
        client2.Close();
    }

    [Test]
    public async Task Periodic_save_flushes_dirty_then_stays_quiet_when_clean()
    {
        await using var h = await Harness.StartAsync(saveInterval: TimeSpan.FromMilliseconds(150));
        var client = await h.LoginAsync("t4");
        var player = h.Registry.TryGet("guest:t4")!;

        await client.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 5 }).AsTask().WaitAsync(Timeout);
        await Task.Delay(600);
        Assert.That(player.SaveCalls, Is.GreaterThanOrEqualTo(1), "dirty면 주기 저장");
        Assert.That(player.IsDirty, Is.False, "저장 성공 시 dirty 해제");

        var saved = player.SaveCalls;
        await Task.Delay(500);
        Assert.That(player.SaveCalls, Is.EqualTo(saved), "클린이면 저장하지 않는다");
        client.Close();
    }

    [Test]
    public async Task Failed_save_keeps_dirty_and_retries_next_period()
    {
        await using var h = await Harness.StartAsync(saveInterval: TimeSpan.FromMilliseconds(150));
        var client = await h.LoginAsync("t5");
        var player = h.Registry.TryGet("guest:t5")!;

        player.FailNextSave = true;
        await client.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 5 }).AsTask().WaitAsync(Timeout);
        await Task.Delay(800);

        Assert.That(player.SaveCalls, Is.GreaterThanOrEqualTo(2), "실패 후 dirty 유지로 재시도되어야 한다");
        Assert.That(player.IsDirty, Is.False, "재시도 성공 후 클린");
        client.Close();
    }

    [Test]
    public async Task Detach_saves_dirty_immediately()
    {
        // 저장 주기를 아주 길게 — 주기 스윕이 아니라 detach 경로의 저장임을 보장
        await using var h = await Harness.StartAsync(saveInterval: TimeSpan.FromSeconds(60));
        var client = await h.LoginAsync("t6");
        var player = h.Registry.TryGet("guest:t6")!;

        await client.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 5 }).AsTask().WaitAsync(Timeout);
        Assert.That(player.SaveCalls, Is.Zero);

        client.Close();
        var deadline = DateTime.UtcNow + Timeout;
        while (player.SaveCalls == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.That(player.SaveCalls, Is.EqualTo(1), "detach 시 즉시 저장 1회");
        Assert.That(player.IsDirty, Is.False);
    }

    [Test]
    public async Task Duplicate_login_under_ticking_stays_consistent()
    {
        await using var h = await Harness.StartAsync();
        var client1 = await h.LoginAsync("t7");
        var player = h.Registry.TryGet("guest:t7")!;
        await Task.Delay(150);

        var client2 = await h.LoginAsync("t7");      // NewWins — client1 킥
        await Task.Delay(400);

        Assert.That(player.Violations, Is.Empty, "소유권 이전 경합에서도 동시 실행 금지 유지");
        var before = player.TickDeltas.Count;
        await Task.Delay(300);
        Assert.That(player.TickDeltas.Count, Is.GreaterThan(before), "새 세션에서 틱 계속");
        client2.Close();
        client1.Close();
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~PlayerTickingTests"`
Expected: 컴파일 오류 (`OnTickAsync`/`PlayerTicker` 미정의)

- [ ] **Step 3: 구현 — Player / PlayersOptions**

`server/src/Bun3.Server.Players/Player.cs` — using에 `System`, `Microsoft.Extensions.Logging` 추가 후 클래스에 멤버 추가 (`PushUpdateAsync` 아래):

```csharp
        private bool _dirty;
        internal long LastTickAtTicksUtc;    // PlayerTicker 전용 — Attach 시 리셋
        internal long NextSaveAtTicksUtc;    // PlayerTicker 전용 — Attach 시 재무장

        /// <summary>접속 중일 때 주기 호출되는 틱 훅 — 현재 세션 액터 안에서 실행되므로
        /// 요청 핸들러와 동시에 실행되지 않는다. delta는 지난 틱 이후 실제 경과
        /// (재바인딩 시 리셋 — 오프라인 구간은 OnAttachedAsync에서 게임이 처리).
        /// 제약은 핸들러와 동일: 짧게, 자기/타 세션 완료를 동기 대기하지 말 것.</summary>
        protected internal virtual ValueTask OnTickAsync(TimeSpan delta) => default;

        /// <summary>저장 훅 — 게임이 DB 쓰기를 구현한다. 주기 스윕(dirty일 때)과
        /// 연결 끊김(detach) 시 호출된다. 유예 만료의 최종 지점은 OnRetiredAsync.</summary>
        protected internal virtual ValueTask OnSaveAsync() => default;

        /// <summary>상태 변경 후 호출 — 다음 저장 주기의 대상으로 표시한다.</summary>
        public void MarkDirty() => _dirty = true;

        /// <summary>저장 대기 중인 변경이 있는지 여부.</summary>
        public bool IsDirty => _dirty;

        internal async ValueTask TrySaveAsync(ILogger logger)
        {
            try
            {
                await OnSaveAsync().ConfigureAwait(false);
                _dirty = false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OnSaveAsync 실패 — dirty 유지, 다음 주기에 재시도 (Player {AccountKey})", AccountKey);
            }
        }
```

`server/src/Bun3.Server.Players/PlayersOptions.cs` — 속성 추가:

```csharp
        /// <summary>접속 중 Player의 OnTickAsync 호출 주기.</summary>
        public TimeSpan PlayerTickInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>dirty Player의 주기 저장 간격 — 크래시 시 손실 상한.</summary>
        public TimeSpan SaveInterval { get; set; } = TimeSpan.FromSeconds(30);
```

- [ ] **Step 4: 구현 — PlayerRegistry (Attach 리셋 + detach 저장)**

`server/src/Bun3.Server.Players/PlayerRegistry.cs` 변경 3곳:

1. 필드/생성자 — SaveInterval 스냅샷 (기존 `_duplicatePolicy` 옆):

```csharp
        private readonly TimeSpan _saveInterval;
```

생성자에서 (기존 스냅샷 줄들 옆):

```csharp
            _saveInterval = effectiveOptions.SaveInterval;
```

2. `Attach`에 틱/저장 시계 리셋 추가:

```csharp
        private void Attach(Entry entry, PlayerSession<TPlayer> session)
        {
            entry.Session = session;
            entry.Player.CurrentSession = session;
            var now = DateTime.UtcNow.Ticks;
            entry.Player.LastTickAtTicksUtc = now;                          // delta 리셋 — 오프라인 구간 미합산
            entry.Player.NextSaveAtTicksUtc = now + _saveInterval.Ticks;    // 저장 주기 재무장
            session.SetPlayer(entry.Player);
        }
```

3. `HandleSessionClosedAsync` — `OnDetachedAsync` 훅 호출 직후, 유예/즉시은퇴 분기 **앞**에 추가:

```csharp
                if (player.IsDirty)
                {
                    await player.TrySaveAsync(_logger).ConfigureAwait(false);   // detach 즉시 저장 → 유예 중 = 항상 저장됨
                }
```

- [ ] **Step 5: 구현 — PlayerTicker + csproj**

`server/src/Bun3.Server.Players/PlayerTicker.cs`:

```csharp
using System;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Bun3.Server.Ticking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Players
{
    /// <summary>
    /// 접속 중 Player의 틱/주기 저장을 구동하는 TickLoop 잡. 순회(틱 루프 스레드)는
    /// 포스팅만 하고, 실행은 각 Player의 현재 세션 액터 안에서 일어난다(락 제로).
    /// 유예 중 Player는 건너뛴다 — detach 시 즉시 저장되므로 항상 저장된 상태다.
    /// 호스팅(AddPlayerServer)은 자동 배선하며, 비호스팅은 Register를 직접 호출한다.
    /// </summary>
    public sealed class PlayerTicker<TPlayer> where TPlayer : Player
    {
        private readonly PlayerRegistry<TPlayer> _registry;
        private readonly TimeSpan _tickInterval;
        private readonly TimeSpan _saveInterval;
        private readonly ILogger _logger;

        /// <summary>레지스트리와 옵션으로 티커를 구성한다. 옵션은 스냅샷된다.</summary>
        public PlayerTicker(PlayerRegistry<TPlayer> registry, PlayersOptions? options = null, ILogger? logger = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            var effective = options ?? new PlayersOptions();
            _tickInterval = effective.PlayerTickInterval;
            _saveInterval = effective.SaveInterval;
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
        }

        /// <summary>틱 루프에 Player 틱 잡을 등록한다. loop.Start 전에 호출할 것.</summary>
        public void Register(TickLoop loop)
        {
            if (loop == null) throw new ArgumentNullException(nameof(loop));
            loop.Every(_tickInterval, TickAsync, "players");
        }

        internal ValueTask TickAsync(TimeSpan _)
        {
            foreach (var player in _registry.Players)   // 스냅샷 — 순회 중 추가/제거 안전
            {
                var session = player.CurrentSession;
                if (session == null)
                {
                    continue;   // 유예 중 — 틱 없음
                }

                var captured = player;
                var posted = session.Post(async () =>
                {
                    if (!ReferenceEquals(captured.CurrentSession, session))
                    {
                        return;   // 실행 시점 재확인 — NewWins 이전/킥 경합 방어
                    }

                    var now = DateTime.UtcNow.Ticks;
                    var delta = TimeSpan.FromTicks(Math.Max(0, now - captured.LastTickAtTicksUtc));
                    captured.LastTickAtTicksUtc = now;
                    try
                    {
                        await captured.OnTickAsync(delta).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "OnTickAsync 예외 (Player {AccountKey})", captured.AccountKey);
                    }

                    if (now >= captured.NextSaveAtTicksUtc && captured.IsDirty)
                    {
                        captured.NextSaveAtTicksUtc = now + _saveInterval.Ticks;
                        await captured.TrySaveAsync(_logger).ConfigureAwait(false);
                    }
                });

                if (!posted)
                {
                    // 닫히는 중이거나 큐 포화 — 이번 틱 스킵, 다음 틱이 온다 (종료 경합은 정상 경로라 Debug)
                    _logger.LogDebug("Player {AccountKey}: 세션 큐 포화/종료로 이번 틱 스킵", player.AccountKey);
                }
            }

            return default;
        }
    }
}
```

`server/src/Bun3.Server.Players/Bun3.Server.Players.csproj` — `<Version>`을 `0.2.0`으로, ProjectReference 추가:

```xml
    <ProjectReference Include="..\Bun3.Server.Ticking\Bun3.Server.Ticking.csproj" />
```

- [ ] **Step 6: 통과 확인 + 전체 회귀**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~PlayerTickingTests"`
Expected: PASS (신규 7개 초록)

Run: `dotnet test server/tests/Bun3.Server.Tests`
Expected: 전체 PASS (153 + 7 = 160) — 기존 Players 테스트(Attach 변경 영향) 회귀 확인

- [ ] **Step 7: 커밋**

```powershell
git add server/src/Bun3.Server.Players server/tests/Bun3.Server.Tests/PlayerTickingTests.cs
git commit -m "✨ Add Player tick hook and dirty periodic save (PlayerTicker)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Hosting 자동 배선 (0.3.0) + 최종 검증

**Files:**
- Modify: `server/src/Bun3.Server.Hosting/ServerOptions.cs` (SlowWorkWarning)
- Modify: `server/src/Bun3.Server.Hosting/RpcServiceCollectionExtensions.cs` (SlowWorkWarning 전달)
- Modify: `server/src/Bun3.Server.Hosting/PlayersServiceCollectionExtensions.cs` (ticking/jobs 배선 + 수명 순서)
- Modify: `server/src/Bun3.Server.Hosting/Bun3.Server.Hosting.csproj` (Ticking 참조 + Version 0.3.0)
- Test: `server/tests/Bun3.Server.Tests/PlayersHostingTests.cs` (테스트 1개 추가)

**Interfaces:**
- Consumes: Task 1 `TickLoop`/`TickingOptions`, Task 3 `PlayerTicker<TPlayer>`, Task 2 `RpcServerOptions.SlowWorkWarning`
- Produces: `AddPlayerServer(..., Action<TickingOptions>? ticking = null, Action<TickLoop>? jobs = null)`

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/PlayersHostingTests.cs`에 테스트 추가 (기존 하네스/using 관례를 그 파일에서 따라갈 것 — 아래는 추가할 테스트 본문):

```csharp
    [Test]
    public async Task AddPlayerServer_starts_tick_loop_and_runs_registered_jobs()
    {
        var jobRuns = 0;
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Bun3:Server:Port"] = "0";
        builder.Services.AddPlayerServer<HostSession, HostPlayer, PlayersRequest, PlayersResponse, PlayersUpdate>(
            loader: (_, _) => new ValueTask<HostPlayer>(new HostPlayer()),
            configure: players =>
            {
                players.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
                {
                    var result = await s.SignInAsync($"guest:{req.DeviceId}");
                    return new LoginResponse { Gold = result.Player.Gold, IsReconnect = result.IsReconnect };
                });
            },
            ticking: o => o.TickInterval = TimeSpan.FromMilliseconds(20),
            jobs: loop => loop.Every(TimeSpan.FromMilliseconds(50), _ =>
            {
                Interlocked.Increment(ref jobRuns);
                return default;
            }, "test-job"));

        using var host = builder.Build();
        await host.StartAsync();
        await Task.Delay(400);
        await host.StopAsync();

        Assert.That(jobRuns, Is.GreaterThanOrEqualTo(3), "AddPlayerServer만으로 틱 루프가 돌고 jobs가 실행된다");
    }
```

참고: `HostSession`/`HostPlayer`는 기존 PlayersHostingTests의 세션/플레이어 타입을
재사용한다(파일에 이미 정의돼 있음 — 실제 이름이 다르면 그 이름을 쓸 것).

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~PlayersHostingTests"`
Expected: 컴파일 오류 (`ticking`/`jobs` 매개변수 미정의)

- [ ] **Step 3: 구현**

`server/src/Bun3.Server.Hosting/ServerOptions.cs` — 속성 추가:

```csharp
    /// <summary>세션 큐 항목(핸들러·Post 작업)이 이 시간을 넘기면 경고 로그. 0 이하 = 끔.</summary>
    public TimeSpan SlowWorkWarning { get; set; } = TimeSpan.FromSeconds(1);
```

`server/src/Bun3.Server.Hosting/RpcServiceCollectionExtensions.cs` — `rpcServerOptions.MaxQueuedPackets = ...` 줄 다음에:

```csharp
            rpcServerOptions.SlowWorkWarning = options.SlowWorkWarning;
```

`server/src/Bun3.Server.Hosting/PlayersServiceCollectionExtensions.cs` — 전체 수정:

1. using 추가: `using Bun3.Server.Ticking;`
2. `AddPlayerServer` 서명에 매개변수 2개 추가:

```csharp
    public static IServiceCollection AddPlayerServer<TSession, TPlayer, TRequest, TResponse, TUpdate>(
        this IServiceCollection services,
        Func<IServiceProvider, string, ValueTask<TPlayer>> loader,
        Action<PlayersConfig<TSession>> configure,
        Action<ServerOptions>? serverOptions = null,
        Action<PlayersOptions>? playersOptions = null,
        Action<TickingOptions>? ticking = null,
        Action<TickLoop>? jobs = null)
```

3. 본문에서 PlayersOptions를 한 번만 구성해 레지스트리·티커가 공유하도록 호이스팅
   (기존 레지스트리 팩토리 안의 `effectivePlayersOptions` 생성을 밖으로):

```csharp
        var effectivePlayersOptions = new PlayersOptions();
        playersOptions?.Invoke(effectivePlayersOptions);

        services.AddServerTransport(serverOptions);

        services.AddSingleton(sp => new PlayerRegistry<TPlayer>(
            key => loader(sp, key),
            effectivePlayersOptions,
            ServerServiceCollectionExtensions.ResolveLogger(sp)));

        services.AddSingleton(sp =>
        {
            var tickingOptions = new TickingOptions();
            ticking?.Invoke(tickingOptions);
            var loop = new TickLoop(tickingOptions, ServerServiceCollectionExtensions.ResolveLogger(sp));
            new PlayerTicker<TPlayer>(
                    sp.GetRequiredService<PlayerRegistry<TPlayer>>(),
                    effectivePlayersOptions,
                    ServerServiceCollectionExtensions.ResolveLogger(sp))
                .Register(loop);
            jobs?.Invoke(loop);   // 게임 전역 잡 — Start 전 등록 규약 충족
            return loop;
        });
```

4. RpcServer 팩토리의 `RpcServerOptions` 생성에 SlowWorkWarning 추가:

```csharp
                new RpcServerOptions
                {
                    MaxQueuedPackets = options.MaxQueuedPacketsPerSession,
                    SlowWorkWarning = options.SlowWorkWarning,
                },
```

5. `PlayersLifetimeService`에 TickLoop 주입 + 수명 순서 (서명·생성자·Start/Stop 수정):

```csharp
        services.AddHostedService(sp => new PlayersLifetimeService<TSession, TPlayer, TRequest, TResponse, TUpdate>(
            sp.GetRequiredService<RpcServer<TSession, TRequest, TResponse, TUpdate>>(),
            sp.GetRequiredService<PlayerRegistry<TPlayer>>(),
            sp.GetRequiredService<TickLoop>(),
            sp.GetRequiredService<IOptions<ServerOptions>>()));
```

```csharp
    private readonly TickLoop _tickLoop;

    public PlayersLifetimeService(
        RpcServer<TSession, TRequest, TResponse, TUpdate> server,
        PlayerRegistry<TPlayer> registry,
        TickLoop tickLoop,
        IOptions<ServerOptions> options)
    {
        _server = server;
        _registry = registry;
        _tickLoop = tickLoop;
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _server.StartAsync(cancellationToken).ConfigureAwait(false);
        _tickLoop.Start();   // 서버가 받은 뒤 틱 시작
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _tickLoop.StopAsync().ConfigureAwait(false);   // 틱 먼저 정지 — 정지 중 새 틱 작업 유입 차단
        await _server.StopAsync(_options.Value.DrainTimeout, cancellationToken).ConfigureAwait(false);
        await _registry.RetireAllAsync(cancellationToken).ConfigureAwait(false);   // 최종 저장
    }
```

`server/src/Bun3.Server.Hosting/Bun3.Server.Hosting.csproj` — `<Version>`을 `0.3.0`으로, ProjectReference 추가:

```xml
    <ProjectReference Include="..\Bun3.Server.Ticking\Bun3.Server.Ticking.csproj" />
```

- [ ] **Step 4: 통과 확인 + 최종 전체 검증**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~PlayersHostingTests"`
Expected: PASS (기존 2 + 신규 1)

Run: `dotnet build Bun3.sln --no-incremental` → Expected: **경고 0**
Run: `dotnet test server/tests/Bun3.Server.Tests` → Expected: 전체 PASS (160 + 1 = **161**)
Run: `dotnet test common/tests/Bun3.Common.Tests` → Expected: 28 PASS (무변경 확인)

- [ ] **Step 5: 커밋**

```powershell
git add server/src/Bun3.Server.Hosting server/tests/Bun3.Server.Tests/PlayersHostingTests.cs
git commit -m "✨ Wire TickLoop and PlayerTicker into AddPlayerServer" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```
