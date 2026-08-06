# Bun3.Server.Players 설계 (재접속에 살아남는 Player 계층 + 로그인 수명주기)

- 날짜: 2026-08-06
- 상태: 승인 대기
- 범위: `Bun3.Server.Players` 패키지 신설 — Player 수명주기(생성/재바인딩/유예/중복
  로그인), 미인증 게이트, 호스팅 통합. 로드맵 가칭 "Sessions"의 실체화(이름 변경 근거는 §2)
- 선행 문서: `2026-08-05-server-messaging-design.md` (v1 Rpc — 파일명은 역사적, 내용은 Rpc)

## 1. 배경과 입력 결정

v1 Rpc까지의 세션은 연결과 수명을 같이한다. 실제 게임에는 "와이파이 순단 후
재접속해도 상태가 그대로인 단위" = Player가 필요하다(idlez의 `player.SetSession`
재바인딩 패턴의 일반화).

사용자 확인을 거친 입력 결정:

| 질문 | 결정 |
|---|---|
| 인증 수단 | 게스트/Steam/구글·애플 전부 가능성 있음 → **프레임워크는 특정 수단을 모름**, 검증은 게임 훅 |
| 중복 로그인 기본 | **새 연결 승리** (기존 연결 킥 후 재바인딩; 옵션으로 변경 가능) |
| 재접속 유예 기본 | **60초** (옵션; 0=즉시 정리) |
| 로그인 와이어 | **A안 — 게임 proto의 평범한 요청** (프레임워크 소유 인증 채널 기각: 로그인 응답의 게임 데이터, 타 언어 스키마 가시성, v1 "스키마 게임 소유" 원칙) |
| 모듈명 | **Players** (모듈이 소유하는 것은 세션이 아니라 Player 수명주기 — Messaging→Rpc와 같은 교훈 선반영) |

## 2. 신원 모델 — accountKey는 불투명 문자열

로그인 정보 관리는 3층으로 분리하며, Players는 3층만 소유한다:

```
[1층] 자격증명 검증  — 제공자별 검증기 (미래 모듈: Bun3.Server.Auth.Steam 등)
        ↓ ProviderIdentity { provider, subject }
[2층] 계정 매핑      — 제공자 신원 ↔ 게임 계정 (연동 테이블, 게임 DB 몫)
        ↓ accountKey (string)
[3층] 수명주기       — Bun3.Server.Players (이 스펙)
```

- Players에 제공자 필드를 미리 파지 않는다. 제공자가 늘어도 Players 무변경.
- 키 형식 **권장 규약**(강제 아님): `"provider:subject"` — `"guest:device-abc"`,
  `"steam:7656119..."`, 연동 도입 후 `"acct:12345"`.
- 게임 로그인 핸들러의 진화 경로: 게스트(검증 없음) → 검증기 모듈 추가 →
  연동 테이블 조회 — 모든 단계에서 `SignInAsync(accountKey)` 호출만 동일하게 유지.

## 3. 패키지 구조와 핵심 모델

```
server/src/Bun3.Server.Players/   netstandard2.1 (신규) · → Rpc, Core, Abstractions
├── Player.cs                      게임이 상속하는 베이스
├── PlayerSession.cs               PlayerSession<TPlayer> : RpcSession — 게임 세션의 새 베이스
├── PlayerRegistry.cs              accountKey → Player + 유예 스윕 (RpcServer와 독립 객체)
├── SignInResult.cs                { TPlayer Player; bool IsReconnect; }
└── PlayersConfig.cs               미인증 허용 목록 + OnRequestUnauthenticated 등록 래퍼
```

관계(시간축): `IConnection`(v0, 연결과 소멸) ← 1:1 → `PlayerSession`(연결 1개의
세션) ← N:1 → `Player`(accountKey당 1개, 세션이 죽어도 유예 동안 생존).

```csharp
public sealed class MyPlayer : Player { public long Gold; }          // 상태는 Player에
public sealed partial class MySession : PlayerSession<MyPlayer> { }  // 인증 후 this.Player 사용
```

- 제네릭 증가는 `TPlayer` 하나. 레지스트리는 서버와 독립이며 세션 팩토리에서
  세션에 부착(AttachRuntime과 같은 패턴) — **v1 `RpcServer`/`RpcClient`/와이어 무변경**.
- Players는 DB를 모른다. 로드/저장은 전부 게임 훅(§4).

## 4. SignInAsync 상태기계와 수명주기 훅

게임 로그인 핸들러(미인증 허용으로 등록)가 자격증명 검증 후 호출:

```csharp
players.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
{
    var accountKey = $"guest:{req.DeviceId}";          // 게임 몫 (검증기/연동은 §2)
    var result = await s.SignInAsync(accountKey);      // 프레임워크 몫
    return new LoginResponse { ... };                  // 게임 데이터 자유롭게
});
```

`SignInAsync(accountKey)`의 4갈래 (계정 키 단위 직렬화 — **스트라이프 락 256개**,
같은 키의 동시 로그인 경합에도 로더는 정확히 1회):

| 레지스트리 상태 | 동작 |
|---|---|
| 없음 | 로더 훅 호출 → 부착. IsReconnect=false |
| 유예 중 | 타이머 해제 → 재바인딩. IsReconnect=true |
| 타 세션 접속 중 | 기본정책: 옛 세션 킥 → 재바인딩. IsReconnect=true (정책 옵션: 기존 유지=새 로그인 거부) |
| 이 세션이 이미 인증됨 | InvalidOperationException (게임 버그 — status 2로 표면화) |

훅 전부:

| 훅 | 시점 | 용도 |
|---|---|---|
| 로더 델리게이트 `(accountKey) => ValueTask<TPlayer>` | 신규 키 | DB 로드/신규 생성 |
| `Player.OnAttachedAsync(bool isReconnect)` | 바인딩 직후 | 상태 재전송 등 |
| `Player.OnDetachedAsync()` | 연결 끊김(유예 시작) | 전투 이탈 처리 등 |
| `Player.OnRetiredAsync()` | 유예 만료·RetireAll | **저장 지점** — 이후 제거 (명시 로그아웃 API는 비범위 — 끊기+유예 만료로 충분) |

- `PlayerSession`은 `OnSessionClosedAsync`를 sealed로 받아 detach를 처리하고
  게임에 `OnPlayerSessionClosedAsync(Exception?)`를 재노출한다 (RpcSession 패턴 반복).
- 편의: `Player.PushUpdateAsync(update)` — 접속 중이면 현재 세션으로, 유예 중이면
  no-op(false). `registry.Players` 스냅샷(브로드캐스트용).
- 유예 스윕: 레지스트리당 타이머 루프 1개(주기 = min(유예/2, 15초)).
- `registry.RetireAllAsync()`: 전원 OnRetiredAsync(저장 플러시) 후 제거 —
  호스팅 통합이 서버 정지 뒤 자동 호출, 비호스팅은 게임이 직접 호출.

## 5. 미인증 게이트 — v1 Rpc에 가하는 유일한 변경

- `RpcSession`에 가상 훅 추가: `int OnGateRequest(Type requestType)` — 0=통과,
  비0=그 상태코드로 즉시 응답(핸들러 미도달). `RpcRuntime`이 디스패치 직전 호출.
- **상태코드 3 = 미인증**을 프레임워크 대역에 예약 (v1 스펙 §3 표 갱신).
- `PlayerSession`이 훅 구현: 인증됨 또는 허용 목록(로그인 등)만 통과.
- 등록은 `players.OnRequestUnauthenticated<TReq,TRes>(...)` = 일반 등록 + 허용
  목록 추가. Rpc는 Players를 모른다(범용 게이트 훅일 뿐).
- 구현 노트: 프레임워크의 protected internal virtual 훅을 타 어셈블리에서 오버라이드할 때는 C# 규칙상 protected override로 선언한다(protected internal override는 CS0507).

## 6. 검증 (완료 조건)

E2E "게스트 로그인 수직 슬라이스" (실 TCP): ① 게스트 로그인 → ② Player 상태를
쓰는 요청 → ③ 강제 절단 → ④ 유예 내 재로그인 → 상태 그대로(로더 재호출 없음)
→ ⑤ 같은 계정 두 번째 클라 → 첫 클라 킥. 5단계 초록 = 완료.

| 종류 | 대상 |
|---|---|
| 단위 | SignIn 신규 — 로더 1회, IsReconnect=false |
| 단위 | 게이트 — 미인증 일반 요청 status 3(핸들러 미도달), 허용 목록 통과 |
| 단위 | 유예 재바인딩 — 같은 인스턴스, 로더 재호출 없음, Detached→Attached 순서 |
| 단위 | 유예 만료 — OnRetired + 제거, 재로그인 시 로더 재호출 |
| 단위 | 중복 로그인 — 옛 세션 킥, 같은 Player 재바인딩 |
| 단위 | 동시 로그인 경합 — 로더 정확히 1회 |
| 단위 | 이중 SignIn 예외 / RetireAll 전원 저장 훅 / detached PushUpdate no-op |
| E2E | 수직 슬라이스 5단계 |

호스팅: `AddPlayerServer<TSession, TPlayer, TRequest, TResponse, TUpdate>(loader,
Action<PlayersConfig<...>>, ...)` — 레지스트리 DI 등록 + 정지 시 RetireAll 연결.

## 7. 전제와 비범위

**전제(명시)**: 레지스트리는 프로세스 내 메모리다. 다중 서버 스케일아웃 시 같은
계정의 서버 간 동시 로그인은 막지 못한다 — 단일 게임서버 프로세스가 v2까지의
전제(참고 프로젝트들과 동일), 분산 레지스트리는 필요 실증 후 별도 설계.

비범위(예약): 제공자 검증기 모듈(`Bun3.Server.Auth.*`), 계정 연동 헬퍼(게임 DB
몫), 주기 저장(Ticking과 함께), 킥/만료 사유의 클라 전달(v2 close-reason 묶음과
통합), 오프라인 Player 캐시.
