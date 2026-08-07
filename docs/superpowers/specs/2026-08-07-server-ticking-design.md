# Bun3.Server.Ticking 설계 (틱 루프 + 주기 저장 + 벽시계 스케줄)

- 날짜: 2026-08-07
- 상태: 승인 대기
- 범위: `Bun3.Server.Ticking` 신설(전역 틱 루프, Every/DailyAt 잡) + Players 통합
  (Player 틱 훅, dirty 주기 저장, detach 저장) + `Session.Post`(Core) + 감시 로그
- 선행 문서: `2026-08-06-server-players-design.md` (Player 수명주기 — 이 스펙이
  "저장 지점이 은퇴뿐"인 데이터 손실 구멍을 닫는다)

## 1. 배경과 입력 결정

idlez 패턴(전역 `Run()` 루프 + 플레이어별 루프/세마포어 + 30초 주기 저장 +
`day_reset_at`)의 일반화. 사용자 확인을 거친 입력 결정:

| 질문 | 결정 |
|---|---|
| 범위 | **전부** — 서버 틱 + Player 주기 저장 + 벽시계 스케줄 세 층 |
| Player 틱 훅 | **제공** (`Player.OnTickAsync`) — 스태미나 회복류 per-player 로직의 자리 |
| 동시성 모델 | **A안** — 공유 틱 루프 1개 + Player 작업은 세션 액터로 포스팅(락 제로). idlez식 플레이어별 루프/세마포어(B안), 게임 책임(C안)은 기각 — CLAUDE.md 프레임워크-우선 원칙 |
| 느린 작업 대책 | **감시 로그(기본 켜짐), 강제 중단 없음** — WaitAsync식 포기는 버려진 작업과 다음 핸들러가 같은 Player를 동시에 만져 직렬화 보장을 깨므로 기각. 협조적 취소/킥 정책은 v2 |
| 시계 | **TimeProvider 채택** (`Microsoft.Bcl.TimeProvider`, ns2.0 호환) — DailyAt 결정적 테스트 |
| 시간대 | **UTC 전용** (사용자 결정 — 다중 시간대 비희망). idlez의 `world.utc_offset_hours`(월드별 지역 시간대 인프라, 실제로는 단일 월드 운용)는 일반화하지 않는다. DailyAt은 UTC 시각만 받는다 |

## 2. 패키지 구조와 버전

```
Bun3.Server.Ticking  신규 0.1.0 (ns2.1) — 의존: Microsoft.Bcl.TimeProvider, Core 참조(SafeLogger 재사용 — 로거 예외가 루프를 죽이지 못하게)
├── TickLoop.cs          전역 루프 1개 — 드리프트 보정, 잡별 예외 격리
├── TickingOptions.cs    { TickInterval=100ms, TimeProvider=System }
└── (잡 등록: Every / DailyAt — cron 파서 없음. DailyAt 발생 시각 계산은
    공개 순수 함수 NextDailyOccurrence로 노출 — 결정적 테스트용)

Bun3.Server.Core     0.2.0 — Session.Post(작업 주입) + 세션 큐 감시 로그
Bun3.Server.Rpc      0.3.0 — RpcServerOptions.SlowWorkWarning 추가(감시 threshold를 ServerBase로 전달)
Bun3.Server.Players  0.2.0 — Ticking 참조 추가. Player 틱/저장 훅, PlayerTicker
Bun3.Server.Hosting  0.3.0 — AddPlayerServer가 TickLoop+PlayerTicker 자동 배선
```

- 의존 방향: `Players → Ticking`. Ticking은 Players를 모른다(월드/보드 로직만
  필요한 서버가 단독 사용 가능). 순환 없음.
- Players의 Ticking 참조를 별도 통합 패키지로 쪼개지 않는 근거: 주기 저장은
  Players 사용 경험의 일부여야 한다 — "설치 안 해서 저장이 안 돌던" 사고 방지.
- 전 패키지 버전 범프(재퍼블리시 금지 원칙).

## 3. TickLoop — 전역 루프

```
loop:
  now = TimeProvider.GetUtcNow()
  due 잡 순차 실행 — 잡마다 try/catch: 예외는 잡 이름과 함께 로그, 루프·다른 잡 계속
  드리프트 보정 대기: max(10ms, TickInterval - 이번 틱 실행 시간)      ← idlez Run()과 동일
```

- 대기는 TimeProvider 경유(Bcl `TimeProviderTaskExtensions.Delay`) — 테스트에서
  가짜 시계로 전진 가능.
- **Every(interval, job, name?)**: 잡별 nextAt — 지났으면 실행, `nextAt += interval`.
  job은 `Func<TimeSpan, ValueTask>` — delta = 이 잡의 지난 실행 이후 실제 경과.
- **DailyAt(timeOfDay, job, name?)**: 다음 발생 시각 계산(오늘 남았으면 오늘,
  지났으면 내일). **UTC 전용** — 지역별 시각이 필요하면 게임이 환산해 넘긴다.
  **놓친 발생 캐치업 없음** — 서버가 꺼져 있던 사이의 리셋은 발화하지 않는다.
  "이 플레이어가 오늘 리셋을 받았나"는 게임 데이터 몫(§8 권장: idlez
  `day_reset_at` 패턴). `TimeOnly`는 net6+라 `TimeSpan` 사용.
- 등록은 **Start 전에만** — 이후 호출은 InvalidOperationException(부팅 검증 정신).
- `Start()` / `Task StopAsync()` — 정지는 진행 중 틱 완료까지 대기.
- 잡은 루프에서 순차 실행 — 짧아야 한다(문서화). 무거운 작업은 잡 안에서 게임이
  별도 Task로 던진다.

## 4. Session.Post와 감시 — Core에 가하는 유일한 변경

```csharp
/// 세션 액터 큐에 작업을 주입한다 — 패킷 처리와 같은 줄에서 순차 실행.
/// 세션이 닫혔거나 큐 상한이면 false(작업 미실행).
public bool Post(Func<ValueTask> work);
```

- 세션 inbox가 "패킷 또는 작업"을 담게 확장(순서 보장 그대로). public — 게임도
  "특정 세션 맥락에서 실행"의 공식 통로로 사용 가능.
- **Post 작업의 미처리 예외는 로그 후 세션 유지** — 응답 상대가 없는 백그라운드
  작업이라 요청 핸들러(OnHandlerError 정책)와 달리 킥하지 않는다.
- **감시(watchdog)**: 세션 액터가 큐 항목(핸들러·Post 작업 공통)을 실행할 때
  `Task.WhenAny(work, Delay(threshold))`로 행을 감지 — threshold 초과 시 경고
  로그 1회 후 **끝까지 기다린다**(직렬화 유지, 강제 중단 없음).
- threshold 소유: Core는 세션 생성 인자(기존 MaxQueuedPackets 전달 경로와 동일)로
  받고, Hosting의 `ServerOptions.SlowWorkWarning`(기본 1초, "Bun3:Server" 섹션)이
  구성 표면. 0 이하 = 감시 끔.

## 5. Players 통합 — Player 틱과 주기 저장

### Player 신규 멤버

```csharp
protected internal virtual ValueTask OnTickAsync(TimeSpan delta) => default;  // 접속 중에만
protected internal virtual ValueTask OnSaveAsync() => default;                // 게임이 DB 쓰기 구현
public void MarkDirty();
public bool IsDirty { get; }
```

(크로스 어셈블리 오버라이드는 `protected override` — Players 스펙 §5 관례 동일)

### PlayersOptions 추가

```csharp
public TimeSpan PlayerTickInterval { get; set; } = TimeSpan.FromSeconds(1);
public TimeSpan SaveInterval { get; set; } = TimeSpan.FromSeconds(30);   // idlez와 동일 기본
```

### PlayerTicker — 순회는 포스팅만, 실행은 세션 액터

```
PlayerTicker 잡 (PlayerTickInterval마다, 틱 루프 스레드):
  foreach player in registry.Players 스냅샷:            // 스냅샷 — 순회 중 추가/제거 안전
    session = player.CurrentSession — 없으면(유예 중) 스킵
    session.Post(async () => {
        if (player.CurrentSession != session) return;   // 실행 시점 재확인 — NewWins/킥 경합 방어
        await player.OnTickAsync(delta);                // delta = Player별 lastTickAt 기준 실제 경과
        if (저장 주기 도달 && player.IsDirty)
            await SaveAsync(player);                    // 성공 시 dirty 해제, 실패 시 로그+유지(재시도)
    })
```

- **락 제로**: 틱 작업과 요청 핸들러가 같은 세션 큐에서 순차 실행 — 게임 코드는
  기존처럼 Player를 자유롭게 만진다.
- 순회 중 로그인(스냅샷 밖) → 다음 틱부터. 순회 중 제거/이전 → 실행 시점
  재확인이 걸러냄. **틱 작업 안에서 레지스트리를 바꾸는 일은 금지되지 않는다.**
- 세션 큐 상한으로 Post가 false면 그 틱 스킵+로그 — 다음 틱이 온다.
- 재바인딩 시 lastTickAt 리셋 — 유예/오프라인 구간 델타는 프레임워크가 계산하지
  않는다(오프라인 진행은 `OnAttachedAsync(isReconnect)`에서 게임 몫).
- 비호스팅 사용: `new PlayerTicker<TPlayer>(registry, playersOptions).Register(tickLoop)`.

### 틱 훅의 실행 컨텍스트 규약 (핸들러와 동일)

1. 자기 세션 큐 완료를 동기 대기하면 데드락.
2. 다른 세션의 처리 완료를 동기 대기하지 말 것 — 교차 통신은 `PushUpdateAsync`.
3. 오래 걸리는 작업 금지 — 같은 큐의 요청 처리가 밀린다(감시 로그가 잡아준다).

### 저장 시점 전체 지도

| 시점 | 저장 |
|---|---|
| 접속 중 | 주기 스윕(SaveInterval, dirty만) — 크래시 손실 최대 = 저장 주기 |
| 연결 끊김(detach) | 즉시 1회(dirty면) → 유예 중 = 항상 저장된 상태(스윕 제외 근거) |
| 유예 만료·서버 정지 | `OnRetiredAsync` (기존 계약 불변 — dirty 무관 호출) |

## 6. Hosting 배선

```csharp
AddPlayerServer<...>(loader, configure,
    serverOptions: ..., playersOptions: ...,
    ticking: o => o.TickInterval = ...,   // 신규(선택)
    jobs: loop => loop.DailyAt(TimeSpan.FromHours(20), ResetDaily));  // 신규(선택) — UTC 20:00 = KST 05:00
```

- TickLoop 싱글턴 + PlayerTicker 자동 등록 — **옵션 없이도 틱+주기 저장이 기본
  동작**(프레임워크-우선). `jobs`는 Start 전에 호출되는 게임 전역 잡 등록 지점.
- 수명 순서: 시작 = 서버 → TickLoop. 정지 = **TickLoop → drain → RetireAll** —
  틱이 먼저 멈춰 정지 중 세션 큐에 새 틱 작업이 흘러들지 않는다.

## 7. 검증 (완료 조건)

| 대상 | 케이스 |
|---|---|
| TickLoop | Every 실행+delta(짧은 실간격), 잡 예외 격리(한 잡 throw → 다른 잡 계속+로그), Start 후 등록 예외, StopAsync 진행 중 틱 완료 대기, DailyAt 다음 발생 — 오늘 남음/지남(내일)/정확히 같음(전진)/비UTC now 정규화, 캐치업 없음 |
| Session.Post | 패킷과 순서 인터리브 보장, 닫힌 세션 false, 큐 상한 false, 작업 예외 → 로그+세션 생존 |
| 감시 | threshold 초과 → 경고 로그 1회, 통과 → 무로그 (threshold 수십 ms) |
| Player 틱 | 세션 액터에서 실행(재진입 카운터로 동시 실행 없음 검증), 유예 중 스킵, 재바인딩 후 재개+delta 리셋, NewWins 직후 옛 세션 포스트 스킵 |
| 주기 저장 | MarkDirty → 주기 도달 시 OnSaveAsync+dirty 해제, 클린 미호출, 저장 실패 dirty 유지 재시도, detach 즉시 저장 1회, OnRetiredAsync 불변 |
| Hosting | AddPlayerServer만으로 틱+저장 동작, 정지 순서(TickLoop→drain→RetireAll) |
| E2E | 수직 슬라이스 확장: 로그인 → MarkDirty → (SaveInterval 단축) 주기 저장 → 절단 → detach 저장 → 재접속 상태 확인 |

시계 전략: 루프 역학은 짧은 실간격(Players 유예 테스트 방식). DailyAt 발생 시각
계산은 순수 함수 `NextDailyOccurrence(now, timeOfDay, offset)`를 직접 검증 —
가짜 시계 패키지 불필요. `TickingOptions.TimeProvider`는 런타임 주입점으로 유지.

## 8. 권장 패턴 — 일일 리셋 (게임 몫)

DailyAt 잡은 "지금 접속 중" 상태만 만질 수 있다. 접속 안 한 플레이어까지 포함한
일일 리셋의 정석은 idlez 패턴:

- Player 데이터에 `dayResetAt`(마지막 리셋 기준 시각) 저장.
- 로그인(`OnAttachedAsync`)과 DailyAt 잡 양쪽에서 "기준 시각이 지났으면 리셋 후
  갱신" — 서버가 꺼져 있었어도 로그인 경로가 보정하므로 캐치업이 필요 없다.

## 9. 전제와 비범위

**전제**: 단일 틱 루프(잡은 짧다), 단일 프로세스(Players 스펙과 동일).

비범위(예약): 협조적 취소(훅 ct)·느린 세션 킥 정책(v2 close-reason 묶음),
DailyAt 캐치업(§8로 대체), 오프라인 진행 계산(게임 몫), 틱 루프 샤딩(병목 실증
후), cron 표현식, 주기 백업/스냅샷 인프라.
