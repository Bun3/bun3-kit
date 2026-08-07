# v2 관찰성 묶음 설계 (Disconnect 사유 + 수명주기 봉인)

- 날짜: 2026-08-07
- 상태: 승인 대기
- 범위: 절단 사유의 클라 전달(control.proto `Disconnect` + `Kick(int)` + `RpcClient`
  사유 노출), IDisposable 표면(`RpcClient`/`PlayerRegistry`), `TickLoop.StopAsync(ct)`,
  수명주기 봉인(이중 SignIn TOCTOU, `_retired`, dirty 버전 카운터) — v0~Ticking 최종
  리뷰들이 미룬 항목의 일괄 처리
- 선행 문서: `2026-08-05-server-messaging-design.md`(Rpc),
  `2026-08-06-server-players-design.md`(Players), `2026-08-07-server-ticking-design.md`

## 1. 배경과 입력 결정

지금은 중복 로그인 킥/idle 킥/서버 정지가 클라에서 전부 "그냥 끊김"이다.
사용자 확인을 거친 입력 결정:

| 질문 | 결정 |
|---|---|
| 사유 범위 | **전부** — 중복 로그인, 서버 종료/점검, idle, 게임 정의 킥(밴 등) |
| 전달 경로 | **A안 — 컨트롤 채널(0x01) `Disconnect` 메시지** (프레임워크 주도 킥은 게임 proto를 만들 수 없는 지점에서 발생 — 게임 스키마 경유 기각) |
| 코드 필드 | **단일 `int32 code`** — `Reply.Status`와 동일 대역 규약(1~99 프레임워크 예약, 음수 게임 정의). 초안의 code+game_code 이중 필드는 같은 문제의 이중 모델링이라 기각 |
| 네이밍 | `Goodbye` 기각 → **`Disconnect`** (명시적) |
| 느린 세션 킥 | **제외** — 감시 로그로 충분, 코드 대역(6~99)에 자리만 예약 |
| 유예 만료 통지 | **구조적 불가**(이미 끊긴 뒤) — 다음 로그인의 `IsReconnect=false`로 판정 |

## 2. 사유 모델과 와이어

```protobuf
// control.proto (프레임워크 소유, 채널 0x01) — Ping/Pong 옆에 추가
message Disconnect {
  int32 code = 1;   // 1~99 프레임워크 예약, 음수 게임 정의. 0은 와이어에 싣지 않는다
}
```

```csharp
// Bun3.Server.Core — 전 계층 공용 상수 (큐 초과는 Core, idle은 Rpc, 중복 로그인은 Players에서 발생)
public static class DisconnectCode
{
    public const int None = 0;              // 클라 전용 의미: Disconnect 미수신 절단
    public const int ServerShutdown = 1;    // 서버 정지 drain
    public const int DuplicateLogin = 2;    // NewWins — 다른 기기에서 로그인
    public const int IdleKick = 3;          // idle 타임아웃
    public const int QueueOverflow = 4;     // 세션 큐 초과 킥
    public const int ProtocolViolation = 5; // 패킷 크기 초과 등
    // 6~99 예약(예: 미래 SlowSession). 음수 = 게임 정의(밴, 치트 등)
}
```

**전달 규약 (best-effort)**: 사유 있는 킥 = `Disconnect{code}` 송신 → close.
송신 실패/타임아웃(1초 상한)은 무시하고 close 진행 — 송신 막힌 소켓이 close를
붙잡으면 안 된다. 클라의 2단 처리: **수신 = 의도된 킥(안내 UI) / 미수신 = 사고
(재접속 루트)** — best-effort 비대칭이 그대로 UX 분기가 된다.

## 3. 서버 API

```csharp
// Core — Session
public void Kick();                                   // 기존 (사유 없음)
public virtual void Kick(int reasonCode) => Kick();   // 신규 — Core는 와이어를 모름(기본 = 그냥 킥)

// Rpc — RpcSession 재정의
public override void Kick(int reasonCode);
// best-effort: Disconnect{code} 송신(1초 상한) → base close. 예외 무시, 멱등
// (이미 닫힌 연결이면 송신 no-op + close 멱등)
```

OnGateRequest와 같은 최소 침습 패턴 — Core는 개념만, 와이어는 Rpc가 가상 재정의로.

**프레임워크 자동 배선** (게임 코드 무변경으로 사유가 붙는 곳):

| 킥 지점 | 계층 | 코드 |
|---|---|---|
| 세션 큐 초과 (`EnqueuePacket` 오버플로) | Core | QueueOverflow |
| 서버 정지 drain (`ServerBase.StopAsync`) | Core | ServerShutdown |
| idle 타임아웃 | Rpc | IdleKick |
| 프로토콜 위반 — Rpc 계층에서 판정 가능한 것(미지 채널 바이트, 파싱 실패 등) | Rpc | ProtocolViolation |
| 중복 로그인 NewWins (`kickAfterRelease`) | Players | DuplicateLogin |

주의: **패킷 크기 초과는 전송 계층(TCP 길이 프리픽스)에서 즉시 절단**되어 Rpc가
개입할 수 없다 — 이 경우 사유는 전달되지 않는다(클라에선 Code 0). ProtocolViolation은
Rpc 계층이 판정하는 위반에만 붙는다.

게임 주도 킥: `session.Kick(-1)` 등 음수 대역 — 프레임워크는 값에 무관심.
`Player.Kick` 편의 메서드(1줄 위임)와 서버 쪽 사유 재노출(킥한 쪽이 이미 아는
정보)은 만들지 않는다.

## 4. 클라 API (`RpcClient`)

```csharp
public readonly struct DisconnectInfo
{
    public int Code { get; }          // 0 = 사유 미수신(네트워크/자발적 Close)
    public Exception? Error { get; }  // 전송 계층 오류(있으면)
    public bool HasReason => Code != 0;
}

public event Action<DisconnectInfo>? Closed;   // 기존 Action<Exception?> 대체 — 파괴적 변경(0.4.0)
```

- 컨트롤 채널에서 `Disconnect` 수신 → 코드 기억 → 실제 절단 시 그 사유로 통지.
- 파괴적 변경 감수 근거: pre-1.0, 소비자는 자체 테스트/템플릿뿐(템플릿은 클라 미사용).
- 대기 중 요청의 실패 방식은 기존 그대로.
- **IDisposable**: `Dispose()` = `Close()` + 내부 CTS(핑 루프 등) 정리, 멱등 —
  `using var client = await ConnectAsync(...)` 패턴 성립.

## 5. 수명주기 봉인

| # | 문제 | 처치 |
|---|---|---|
| 1 | 이중 SignIn TOCTOU — `Player != null` 검사 비원자(핸들러 밖 동시 호출 시 둘 다 통과) | `PlayerSession`에 `Interlocked.CompareExchange` 사인인 가드 — 두 번째는 즉시 InvalidOperationException, SignIn 실패 시 가드 해제 |
| 2 | 은퇴 후 늦은 SignIn — RetireAll 후 로그인이 새 entry 생성 | 레지스트리 `_retired` volatile 플래그 — RetireAll 진입 시 set, 이후 SignInAsync는 InvalidOperationException(핸들러에서 status 2로 표면화) |
| 3 | dirty 클리어 TOCTOU — 저장 중 MarkDirty가 저장 후 클리어에 지워짐 | 버전 카운터: `MarkDirty`=`_dirtyVersion++`(Interlocked), 저장 성공 시 시작 시점 캡처값을 `_savedVersion`에. `IsDirty => _dirtyVersion != _savedVersion` |
| 4 | PlayerRegistry 수명 | IDisposable — 스윕 CTS cancel+dispose. **Dispose는 은퇴 아님**(저장 안 함 — 우아한 경로는 RetireAllAsync, Dispose는 테스트/비정상 정리용) 문서 명시 |
| 5 | TickLoop.StopAsync 무기한 대기 | `StopAsync(CancellationToken ct = default)` — ct는 "기다림 포기"만(루프는 취소 신호로 현재 잡 후 스스로 종료, 강제 중단 없음 철학 유지). Hosting이 호스트 종료 ct 전달 |
| 6 | 소소 | RpcStatus 문서의 Players 언급 제거, PlayerTicker `captured` 별칭 제거(-2줄) |

## 6. 검증 (완료 조건)

| 대상 | 케이스 |
|---|---|
| 사유 E2E(실 TCP) | 중복 로그인 → 옛 클라 `Closed(DuplicateLogin)`, 게임 킥(음수 코드) 전달, idle 킥 → IdleKick, 서버 정지 → ServerShutdown, 클라 자발 Close → Code 0 |
| Kick(int) | 이미 닫힌 세션 킥 멱등, 송신 불가 상태에서도 close 보장(1초 상한) |
| 봉인 1 | 동시 SignInAsync 두 갈래 — 정확히 한쪽만 성공, 다른 쪽 InvalidOperationException |
| 봉인 2 | RetireAllAsync 후 SignInAsync → InvalidOperationException, 새 entry 없음 |
| 봉인 3 | OnSaveAsync 도중 MarkDirty → 저장 완료 후에도 IsDirty 유지 → 다음 스윕에 재저장 |
| 봉인 4 | Dispose 후 유예 스윕 정지(만료돼도 은퇴 안 일어남), 이중 Dispose 무해 |
| 봉인 5 | StopAsync(취소된 ct) → OperationCanceledException + 루프 태스크는 자체 종료, 정상 경로 무변경 |
| 회귀 | `Closed` 시그니처 변경 반영 후 기존 E2E 전부 초록 |

## 7. 버전

| 패키지 | 버전 | 변경 |
|---|---|---|
| Core | 0.2.0 → **0.3.0** | DisconnectCode, `Kick(int)` 가상, 큐/셧다운 사유 배선 |
| Rpc | 0.3.0 → **0.4.0** | control.proto Disconnect, RpcSession.Kick 재정의, RpcClient `Closed` 파괴적 변경 + IDisposable |
| Players | 0.2.0 → **0.3.0** | NewWins 사유, SignIn 가드, `_retired`, dirty 버전 카운터, Registry IDisposable |
| Ticking | 0.1.0 → **0.2.0** | StopAsync(ct) |
| Hosting | 0.3.0 → **0.4.0** | 버전 추종, 정지 시 ct 전달 |

## 8. 전제와 비범위

**전제**: Disconnect 전달은 best-effort — 보장이 필요한 통지는 이 채널의 몫이 아니다.

비범위(예약): 느린 세션 킥 정책(코드 대역 자리만), 훅 협조적 취소(ct 전달),
유예 만료 실시간 통지(§1 — IsReconnect=false로 대체), 재접속 자동화 헬퍼(Unity
어댑터 몫), 킥 사유의 서버 측 재노출.
