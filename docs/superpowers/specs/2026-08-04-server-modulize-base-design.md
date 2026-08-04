# Bun3 서버 모듈 베이스 설계 (v0: 부팅 + 에코 E2E)

- 날짜: 2026-08-04
- 상태: 승인 대기
- 범위: `server/` 하위 모듈 라이브러리의 첫 수직 슬라이스 — 전송 추상화,
  세션 코어, TCP 전송, ASP.NET Core 호스팅, 에코 E2E 검증
- 선행 문서: `2026-07-26-monorepo-structure-design.md` (6장 서버 재사용 전략)

## 1. 배경과 목표

목표는 서버 프로젝트를 시작할 때 **빠르게 구조를 잡는 것**이다. 재사용 방식은
선행 문서에서 결정한 대로 fork가 아니라 **모듈 라이브러리(NuGet) + 얇은
template 레포**다. 이번 작업은 그 결정의 첫 실체화로, 참고 프로젝트 분석에서
얻은 교훈을 반영한다.

### 참고 프로젝트 분석 요약

| 프로젝트 | 교훈 |
|---|---|
| growdaemon → growninja | fork 모델의 실증 실패 사례. 두 fork가 발산해 베이스 개선이 전파되지 못함(growninja는 EOL된 netcoreapp3.1에 잔류). 프레임워크/게임 경계 부재 — 패킷 처리부가 7,369줄 단일 switch. 예외를 침묵 삼킴 → 수년간 손으로 키운 예외 무시 목록 |
| idlez-server | 가장 좋은 출발점. 3계층(공유 로직 → 프레임워크 → 게임) 분리, 제네릭 Server/Player, 플레이어별 직렬화된 액터 루프, 요청 수준 상태코드 응답. 결함: 프레임워크↔게임 partial class 순환 참조, static 싱글턴 남발, DotNetty 의존, 테스트 0개 |

새 설계는 idlez-server의 `Server` 계층을 정제·패키지화한 것에 가깝다.

### 방향을 결정한 요구사항

1. **Steam 네트워킹 / Unity 내부 호스팅이 가까운 미래** → 핵심 로직은
   netstandard2.1 + 외부 의존 0으로 Unity 안에서도 실행 가능해야 한다.
2. **실시간 세션 + HTTP API가 한 호스트에 공존**하는 형태가 기본.
3. v0 성공 기준은 **부팅 + 에코 E2E**: 클라이언트가 접속해 프레임을 보내고
   같은 프레임을 돌려받는 수직 슬라이스가 실제로 동작.

## 2. 결정 사항 요약

| 항목 | 결정 |
|---|---|
| 접근 방식 | A안 — 바이트 레벨 전송 추상화 + 계층 패키지 (단일 패키지·메시징 포함안 기각) |
| 핵심 계층 TFM | netstandard2.1 (Abstractions, Core, Transport.Tcp) — 기존 `Bun3.Server.Core`(net10.0)를 재타겟 |
| 호스팅 계층 TFM | net10.0 (Hosting만 ASP.NET Core 의존 허용) |
| 네트워크 라이브러리 | DotNetty 배제, 순수 `Socket` + async. 이유: 프로젝트 사장(아카이브, 2020년 이후 릴리스 없음), 가치가 현대 BCL에 흡수됨(epoll/IOCP 내장), Unity 비호환, libuv 네이티브 바이너리 배포 부담 |
| 프레이밍 | 4바이트 길이 프리픽스 (전송이 소유; Steam 등 메시지 단위 전송은 프레이밍 불필요) |
| 동시성 모델 | 세션 액터 — 세션별 수신 큐 + 단일 소비 루프. 한 세션의 로직은 동시 실행되지 않음 |
| 소비 루프 구동 | 이벤트 구동 (`ConcurrentQueue` + `SemaphoreSlim` 신호). 고정 주기 tick은 추후 `Bun3.Server.Ticking` 모듈 |
| 에러 정책 | 기본 세션 종료(fail-fast) + `OnHandlerError` 가상 훅으로 게임별 재정의. 상태코드 응답(대안 B)은 v1 메시징에서 |
| 로깅 | 자체 최소 계약 `IBun3Logger`(Abstractions). Hosting에서 `ILogger`로 브리지 |
| 버퍼 소유권 | `OnFrame`의 메모리는 호출 동안만 유효. v0은 복사 우선, 풀링 최적화는 측정 후 |
| 직렬화/디스패치 | v0 비범위. 프레임 = 불투명 byte 덩어리. v1 `Bun3.Server.Messaging`에서 결정 |
| 테스트 | NUnit 4 (common/tests 관례), 단위 + 실 TCP E2E |

## 3. 패키지 구조

```
server/
├── Directory.Build.props          (기존 + 공통 패키징 설정 추가)
├── src/
│   ├── Bun3.Server.Abstractions/  netstandard2.1 · 의존성 0
│   │   └ IConnection, IConnectionHandler, ITransportListener,
│   │     IBun3Logger, 옵션 타입
│   ├── Bun3.Server.Core/          netstandard2.1 · → Abstractions, Bun3.Common
│   │   └ ServerBase<TSession>, Session, 세션 레지스트리,
│   │     수명주기(Start/Stop, graceful shutdown)
│   ├── Bun3.Server.Transport.Tcp/ netstandard2.1 · → Abstractions
│   │   └ Socket 리스너 + accept/수신 루프 + 길이 프리픽스 프레이밍
│   └── Bun3.Server.Hosting/       net10.0 · → Core + Microsoft.Extensions.Hosting
│       └ AddBun3Server(), BackgroundService 통합, ILogger 브리지, IOptions 바인딩
├── samples/
│   └── EchoServer/                net10.0 콘솔 · IsPackable=false · 조립 예제 겸 수동 확인
└── tests/
    └── Bun3.Server.Tests/         net10.0 · NUnit 4
```

의존은 단방향: Hosting → Core → Abstractions ← Transport.Tcp.
Transport와 Core는 서로를 모르며 Abstractions로만 만난다. 새 전송(Steam,
인프로세스)은 `IConnection`/`ITransportListener` 구현 추가로 끝나고 Core는
무변경이다. Unity 호스팅 시에는 Hosting을 제외한 세 패키지만 가져간다.

## 4. 핵심 계약 (Abstractions)

```csharp
public interface IConnection
{
    long Id { get; }                 // 프로세스 내 유일 연결 식별자(단조 증가). 로그 상관·레지스트리 키 용도.
                                     // 계정/플레이어 ID 아님 — 재접속 시 새 값
    string? RemoteAddress { get; }   // TCP는 IP, Steam은 SteamID — 전송별 세부는 문자열로
    bool IsOpen { get; }
    ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default);
    void Close();
}

public interface IConnectionHandler      // Core가 구현, Transport가 호출
{
    void OnConnected(IConnection connection);
    void OnFrame(IConnection connection, ReadOnlyMemory<byte> frame);
    void OnClosed(IConnection connection, Exception? error);   // 정상 종료면 null
}

public interface ITransportListener
{
    Task StartAsync(IConnectionHandler handler, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
```

- `OnFrame`의 버퍼는 호출 반환 후 재사용될 수 있다(호출 동안만 유효).
- 닫힌 연결에 대한 `SendAsync`는 no-op (종료 경합은 정상 상황이므로 예외 금지).
- 나가는 연결 계약(`IConnector`)은 v0 비범위 — E2E 테스트는 raw `TcpClient` 사용,
  정식 클라이언트 계약은 v1 메시징과 함께 도입.

## 5. 세션 모델 (Core)

**Session은 "연결 1개"의 서버측 대응물이며 연결과 수명을 같이한다** (끊기면
소멸, 재접속 = 새 Session). 인증을 거쳐 얻는 "플레이어"(재접속에도 살아남는
단위)는 별개 개념으로, v0에는 존재하지 않는다 — 로그인 모듈(로드맵의
`Bun3.Server.Sessions`)이 이 위에 Player 계층과 세션 재바인딩(idlez의
`player.SetSession` 패턴)을 도입한다. 에코 수준의 서버는 Session만으로 충분하다.

```csharp
public abstract class Session
{
    public long Id { get; }
    public IConnection Connection { get; }

    protected virtual ValueTask OnConnectedAsync() => default;
    protected abstract ValueTask OnFrameAsync(ReadOnlyMemory<byte> frame);
    protected virtual ValueTask OnDisconnectedAsync(Exception? error) => default;
    protected virtual ErrorDecision OnHandlerError(Exception ex) => ErrorDecision.CloseSession;

    public ValueTask SendAsync(ReadOnlyMemory<byte> frame);
    public void Kick();
}

public abstract class ServerBase<TSession> where TSession : Session
{
    protected abstract TSession CreateSession(IConnection connection);
    public Task StartAsync(CancellationToken ct = default);
    public Task StopAsync(CancellationToken ct = default);
    public IReadOnlyCollection<TSession> Sessions { get; }
}
```

- **액터 루프**: IO 스레드는 도착 프레임을 세션 큐에 복사·투입하고 신호만 올린 뒤
  즉시 반환. 세션별 소비 루프(스레드풀)가 깨어나 순서대로 `OnFrameAsync` 호출.
  결과적으로 한 세션의 게임 로직은 절대 동시 실행되지 않아 lock이 불필요하다.
- **게임 코드 결합점은 `CreateSession` 팩토리 하나.** DI 없이도 (Unity에서도)
  `new MyServer(new TcpTransportListener(...))`로 조립 가능.
- growninja식 중복 패킷·해킹 감지는 게임별 정책이므로 프레임워크에 넣지 않는다
  (훅으로 확장할 자리만 유지).

## 6. 에러 처리와 방어선

| 상황 | 정책 |
|---|---|
| `OnFrameAsync` 예외 | 기본: 로그 + 해당 세션만 종료. `OnHandlerError` 재정의로 게임이 유지 선택 가능 |
| 수신 중 소켓 오류/원격 종료 | `OnClosed(error)` → `OnDisconnectedAsync(error)` → 세션 정리 |
| 닫힌 연결에 `SendAsync` | no-op |
| 프레임 크기 초과 (기본 1MB, 옵션) | 프로토콜 위반 — 즉시 연결 종료 |
| 세션 큐 적체 (기본 256프레임, 옵션) | 연결 종료 — 메모리 보호 |
| `StopAsync` | accept 중단 → 전 세션 종료 통지 → 소비 루프 종료 대기(타임아웃) |

기본값을 세션 종료로 두는 근거: 핸들러가 도중에 죽으면 반쯤 적용된 상태일 수
있고, 재접속 시 저장된 상태에서 새로 로드되며 일관성이 자연 복구된다(사실상
supervisor 재시작의 실용판 — 진실원본은 DB). 상태코드 응답으로 세션을 유지하는
정책(idlez 방식)은 요청-응답 개념이 생기는 v1 메시징에서 도입한다.

## 7. 호스팅 (net10.0)

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddBun3Server<MySession>(options =>
{
    options.Port = 20000;            // appsettings "Bun3:Server" 섹션 바인딩 지원
    options.MaxFrameSize = 1024 * 1024;
});
await builder.Build().RunAsync();    // SIGTERM/Ctrl+C → graceful shutdown
```

- 내용물: `ServerBase`의 `BackgroundService` 래핑, `IBun3Logger`→`ILogger` 브리지,
  `IOptions` 바인딩. **그 이상의 로직이 쌓이면 Core로 내려야 한다는 신호.**
- 같은 Generic Host에 ASP.NET Core 엔드포인트를 추가하면 실시간 + HTTP 공존.
- Unity 호스팅 시나리오에서는 이 패키지를 사용하지 않는다.

## 8. 검증 (v0 완료 조건)

- `samples/EchoServer`: Hosting API로 조립, `OnFrameAsync`에서 받은 프레임을
  그대로 반환.
- `tests/Bun3.Server.Tests`:

| 종류 | 대상 |
|---|---|
| 단위 | 프레이밍 코덱 — 분할 도착, 병합 도착, 경계값, 최대 크기 초과 |
| 단위 | 세션 액터 — 처리 순서 보장, 동시 실행 없음, 큐 적체 시 종료 |
| 단위 | 에러 정책 — 예외 → 세션 종료, `OnHandlerError` 재정의 시 유지 |
| E2E | 실 TCP: 기동(임의 포트) → `TcpClient` 접속 → 송신 → 에코 수신 → graceful shutdown |

E2E 통과 = v0 완료.

## 9. 버전/솔루션 편입

- 새 프로젝트들을 루트 `Bun3.sln`에 편입.
- 패키지별 SemVer 0.1.0 시작, 태그는 `Bun3.Server.Core/v0.1.0` 형식(기존 규약).
- `PackageId`/`RootNamespace` = 프로젝트명 일치(기존 규약).

## 10. 로드맵 (비범위, 예약)

| 단계 | 모듈 | 내용 |
|---|---|---|
| v1 | `Bun3.Server.Messaging` | 직렬화 추상 + 타입 있는 요청/응답 핸들러 + 상태코드 + 클라이언트 커넥터(`IConnector`). 직렬화 포맷(protobuf vs MemoryPack 등)은 이때 결정 |
| v2 | `Bun3.Server.Ticking` | 고정 타임스텝 tick 루프 |
| v2+ | `Bun3.Server.Sessions`(인증/로그인), `Bun3.Server.Transport.Steam`, Unity 호스트 패키지, `bun3-server-template` 레포 | 필요 순서대로 |

## 11. 이번 작업에서 하지 않는 것

- 직렬화/메시지 디스패치 (v1)
- 인증/로그인, DB/영속성, 랭킹·채팅 등 기능 모듈
- Steam/Unity 전송 구현 (계약만 대비)
- 성능 최적화(버퍼 풀링 등) — 측정 후
- CI/NuGet publish 자동화, template 레포 생성
