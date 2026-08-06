# Bun3.Server.Auth 설계 (제공자 검증기 — Guest + Steam)

- 날짜: 2026-08-07
- 상태: 승인 대기
- 범위: Players 신원 모델의 1층(자격증명 검증) 실체화 — 검증기 계약 +
  `GuestVerifier` + Steam 검증기 2종(Web API 전용 서버 / 네이티브 위임 클라 호스트)
- 선행 문서: `2026-08-06-server-players-design.md` §2 (3층 신원 모델 — 이 스펙이 그 1층)

## 1. 배경과 입력 결정

첫 게임은 Steam 플랫폼 예정. 사용자 확인을 거친 입력 결정:

| 질문 | 결정 |
|---|---|
| Steam 검증 방식 | **Web API**(`ISteamUserAuth/AuthenticateUserTicket`) — 전용 서버 표준, HttpClient만 필요 |
| 클라 호스트(리슨 서버) 경로 | **이번에 같이 구현** — `BeginAuthSession` 네이티브 위임(델리게이트 시섬). Web API 키는 클라 반입 불가이므로 이 경로가 리슨 서버의 유일한 검증 수단 |
| 실키 보유 | 없음 — 가짜 HTTP 핸들러/가짜 델리게이트로 테스트, 실키 연동은 출시 준비 때 |
| 패키지 구성 | A안 — 계약+Guest 한 패키지(`Bun3.Server.Auth`) + Steam 별도 패키지(`Bun3.Server.Auth.Steam`) |
| 밴(VAC/퍼블리셔) 기본 처리 | **기본 거절**(위조/만료와 동일) — 옵션으로 끄면 성공+플래그. 네이티브 경로가 원래 밴을 실패 코드로 주므로 두 경로 기본 동작 일치 |
| 실패 어휘 | 제공자별 enum이 아니라 **공통 `AuthFailure`** — 게임 핸들러의 실패 매핑 switch가 검증기 교체에도 불변 |
| accountKey 전략 | **acct 방식 권장** — 게임 DB에 계정/연동 테이블, accountKey=`"acct:{id}"` 불변 (§5) |
| Google/Apple | 비범위 — 같은 계약의 후속 모듈로 예약 |

## 2. 위치 — Players 3층 모델의 1층

```
[1층] 자격증명 검증  — 이 스펙: IIdentityVerifier (Guest/Steam/...)
        ↓ ProviderIdentity { provider, subject }
[2층] 계정 매핑      — 게임 DB (연동 테이블, §5 권장 패턴)
        ↓ accountKey (string)
[3층] 수명주기       — Bun3.Server.Players (기존, 무변경)
```

이번 작업은 **Players·Rpc·Hosting 무변경**이다. 검증기는 게임 로그인 핸들러 안에서
조합되는 평범한 객체이며, 프레임워크의 다른 층은 검증기의 존재를 모른다.

## 3. 계약 패키지 — `Bun3.Server.Auth` (netstandard2.1, 의존 0)

```csharp
public readonly struct ProviderIdentity
{
    public string Provider { get; }               // "guest", "steam", ... (소문자 규약)
    public string Subject  { get; }               // 제공자 내 고유 id (SteamID64, device-id, ...)
    public string ToAccountKey() => $"{Provider}:{Subject}";   // Players §2 권장 규약
}

public enum AuthFailure
{
    None = 0,
    InvalidCredential = 1,   // 형식 불량 — 빈 device-id, hex 파싱 실패, 규약 위반
    Rejected = 2,            // 제공자가 거절 — 위조/만료 티켓, 무효 토큰
    Banned = 3,              // 제공자 밴 — VAC/퍼블리셔 밴 (Reject 옵션 켜진 경우)
    IdentityMismatch = 4,    // 예약 — 주장 신원과 검증 결과 불일치 (현 구현 미발생, 미래 제공자용)
    Timeout = 5,             // 검증 응답 시간 초과 (네이티브 콜백 미도착 등)
}

public class AuthResult
{
    public bool Succeeded { get; }
    public ProviderIdentity Identity { get; }     // 성공 시만 유효
    public AuthFailure Failure { get; }           // 실패 시만 유효
    public string? Error { get; }                 // 로그용 설명
}

public interface IIdentityVerifier
{
    string Provider { get; }
    ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default);
}
```

**실패 모델 (Reply 철학과 동일):**

- **검증 거절**(위조 티켓, 빈 device-id 등) = `AuthResult` 실패 **값**. 게임이
  `switch (auth.Failure)`로 자기 proto 에러코드에 매핑 — 이 코드는 와이어에 실리지
  않으며 게임 에러코드와 이름공간이 만나지 않는다.
- **인프라 실패**(Valve API 다운, HTTP 타임아웃) = **예외 전파**. 게임이 안 잡으면
  기존 Rpc 규약대로 status 2(HandlerException). 재시도 정책은 게임 몫.

**`GuestVerifier`** — 검증할 것이 없는 대신 신뢰 경계 검증만 수행:

- credential(device-id)을 trim → 빈 값, 128자 초과, `:` 포함(키 규약 오염)이면
  `InvalidCredential`, 통과 시 `("guest", trimmed)`.
- Guest의 본질은 클라 주장 신뢰(idlez와 동일 신뢰 수준)이며, 같은 계약 뒤에 두는
  이유는 Steam 전환 시 로그인 핸들러가 검증기 한 줄만 바뀌게 하기 위함.

## 4. Steam 패키지 — `Bun3.Server.Auth.Steam` (netstandard2.1, 의존: System.Text.Json)

두 검증기 모두 `Provider = "steam"`, Subject = SteamID64 문자열 — **어느 경로로
검증되든 같은 유저는 같은 accountKey**. ns2.1인 이유: `SteamSessionVerifier`는
Unity 클라 호스트 안에서 돌아야 하고, Web API 쪽 요구는 `HttpClient`(BCL)와
System.Text.Json(ns2.0 타깃 제공, NuGetForUnity 호환)뿐. 단일 패키지에 두 경로를
두는 비용은 반대편 경로의 죽은 코드 몇 KB — 경로별 분리보다 "Steam이면 이 패키지
하나"의 단순함을 택했다.

```csharp
public sealed class SteamAuthResult : AuthResult
{
    public ulong SteamId { get; }
    public ulong OwnerSteamId { get; }        // 패밀리 공유: 빌린 계정이면 소유자가 다름 (네이티브 경로는 SteamId와 동일 값)
    public bool VacBanned { get; }
    public bool PublisherBanned { get; }
    public int ValveErrorCode { get; }        // Valve 원시 코드 (WebApi errorcode / EAuthSessionResponse) — 로그·운영용, 판정에 안 씀
}
```

### 4-A. `SteamWebApiVerifier` — 전용 서버 경로

```csharp
public sealed class SteamWebApiVerifier : IIdentityVerifier   // Provider = "steam"
{
    public SteamWebApiVerifier(HttpClient http, SteamWebApiOptions options);
    // credential = 클라 티켓의 hex 문자열 (GetAuthSessionTicket/GetAuthTicketForWebApi 산출물)
    public ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default);
}

public sealed class SteamWebApiOptions
{
    public uint AppId { get; set; }
    public string WebApiKey { get; set; }         // 비밀 — 환경변수/설정으로만, 커밋 금지
    public string? Identity { get; set; }         // GetAuthTicketForWebApi("identity") 사용 시 쿼리에 동봉
    public bool RejectVacBanned { get; set; } = true;
    public bool RejectPublisherBanned { get; set; } = true;
}
```

- `GET https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/`
  쿼리: `key`, `appid`, `ticket`, (옵션) `identity`. 응답 파싱은 `JsonDocument`
  (DTO 클래스 없음).
- 성공 응답 `response.params` (`result=="OK"`): `steamid`, `ownersteamid`,
  `vacbanned`, `publisherbanned` → `SteamAuthResult`.
  - 밴 + Reject 옵션(기본) → 실패 `Banned` (어느 밴인지는 플래그로).
  - Reject 끔 → 성공 + 플래그 — "티켓이 진짜인가"와 "입장시킬 것인가"의 분리.
- 거절 응답 `response.error`: `errorcode` → 실패 `Rejected` + `ValveErrorCode`.
- credential이 빈 값/hex 아님 → HTTP 호출 없이 `InvalidCredential`.
- HTTP 비성공 상태/네트워크 오류 → 예외 전파 (`EnsureSuccessStatusCode`).
- 테스트: `HttpClient`에 가짜 `HttpMessageHandler` 주입 — 실키 불필요.
- 생성자 검증: `AppId == 0` 또는 `WebApiKey` 빈 값 → `ArgumentException` (부팅 시 즉사).

### 4-B. `SteamSessionVerifier` — 클라 호스트(리슨 서버), 네이티브 위임

프레임워크는 Steamworks C# 바인딩(Steamworks.NET/Facepunch)에 의존하지 않는다.
**네이티브 호출 2개는 게임이 델리게이트로 꽂고, 프레임워크는 상관관계(correlation)
로직만 소유**한다:

```csharp
public sealed class SteamSessionVerifier : IIdentityVerifier   // Provider = "steam"
{
    public SteamSessionVerifier(SteamSessionOptions options);
    // credential = "steamId64:ticketHex" — 클라가 자기 steamId 주장 + 티켓 (BeginAuthSession 서명이 그러함)
    public ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default);

    // 게임 글루가 Steamworks의 ValidateAuthTicketResponse_t 콜백에서 호출
    public void HandleValidateResult(ulong steamId, int authSessionResponse);
    // 플레이어 퇴장 시(OnRetiredAsync 등) 게임이 호출 → EndSession 델리게이트로 전달
    public void EndSession(ulong steamId);
    // 접속 승인 "이후" 도착한 무효화(게임 중 밴, 티켓 취소) — 게임이 구독해서 킥
    public event Action<ulong, int>? SessionInvalidated;   // (steamId, EAuthSessionResponse)
}

public sealed class SteamSessionOptions
{
    public Func<byte[], ulong, int> BeginSession { get; set; }  // SteamUser.BeginAuthSession 래핑 — 즉시 EBeginAuthSessionResult
    public Action<ulong> EndSession { get; set; }               // SteamUser.EndAuthSession 래핑
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);   // 콜백 미도착 대비
}
```

동작 흐름:

1. `VerifyAsync` — credential 파싱 실패(steamId 아님·hex 아님·형식 위반) →
   `InvalidCredential`.
2. 같은 steamId의 검증이 이미 진행 중 → 즉시 실패 `Rejected`
   (Error="verification already pending").
3. `BeginSession` 즉시 결과가 비0(`EBeginAuthSessionResult`) → `Rejected` +
   `ValveErrorCode`.
4. steamId별 pending(`TaskCompletionSource`, `RunContinuationsAsynchronously`)
   등록 후 대기. 콜백은 Unity 메인 스레드에서 오므로 TCS 완료가 Steam 콜백
   스레드를 붙잡지 않게 한다.
5. `HandleValidateResult(steamId, response)` 도착:
   - pending 있음 + `response==0`(OK) → 성공 `SteamAuthResult`
     (OwnerSteamId=SteamId, 밴 플래그 false — 네이티브 경로는 밴이면 아래 실패로 옴).
   - pending 있음 + 비0 → `EAuthSessionResponse` 3(VACBanned)·9(PublisherIssuedBan)
     → `Banned`, 그 외 → `Rejected`. 원시 코드는 `ValveErrorCode`.
   - pending 없음 + 비0 → `SessionInvalidated` 이벤트 (접속 중 밴/티켓 취소 —
     게임이 킥). pending 없음 + OK → 무시.
6. `options.Timeout` 내 콜백 미도착 → `Timeout` 실패.
7. **정리 규약**: 실패·타임아웃 시 검증기가 스스로 `EndSession` 델리게이트 호출.
   성공 시 인증 세션은 열린 채 유지되며(Steam이 수명 추적) 게임이 플레이어 퇴장
   시점에 `verifier.EndSession(steamId)`를 호출할 책임을 진다.

스레딩: pending은 `ConcurrentDictionary<ulong, TaskCompletionSource<AuthResult>>`.
`HandleValidateResult`·`EndSession`·`VerifyAsync`는 어느 스레드에서 호출해도 안전.

취소 의미론(두 검증기 공통): `options.Timeout` 초과는 **값**(`AuthFailure.Timeout`,
네이티브 경로) — 예상 가능한 결과. 호출자가 넘긴 `CancellationToken` 취소는
**예외**(`OperationCanceledException`) — 서버 종료 등 인프라 사정. 생성자 검증:
`BeginSession`/`EndSession` null → `ArgumentException`.

## 5. 게임 통합 가이드

### 로그인 핸들러 — 검증기 교체가 한 줄인 형태

```csharp
// Program.cs — 게임이 직접 등록 (DI 헬퍼 없음, 조건부/다단계 등록이 생기면 그때 추가)
services.AddSingleton<IIdentityVerifier>(new GuestVerifier());
// Steam 전환 시 이 한 줄만:
// services.AddSingleton<IIdentityVerifier>(new SteamWebApiVerifier(httpClient, steamOptions));

players.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
{
    var auth = await verifier.VerifyAsync(req.Credential);
    if (!auth.Succeeded)
        return new LoginResponse { Error = MapAuthFailure(auth.Failure) };  // 게임 proto 코드로 매핑

    var accountKey = await ResolveAccountKeyAsync(auth.Identity);           // §5 acct 방식
    var result = await s.SignInAsync(accountKey);
    return new LoginResponse { ... };
});
```

### accountKey — acct 방식 (확정 권장)

게임 DB에 계정 테이블 + 연동 테이블을 두고 accountKey를 `"acct:{id}"`로 고정한다:

```
accounts:       id=12345 (골드/진행도의 주인)
account_links:  (provider, subject) → account_id
                ("guest", "device-abc") → 12345
                ("steam", "7656119...") → 12345     ← 연동 추가 = 행 1개
```

- `ResolveAccountKeyAsync(identity)`: 연동 테이블 조회(없으면 계정 생성+연동 행
  삽입) → `"acct:{id}"`. 게스트-only 시절에도 1:1로 계정 행을 만들어 두면
  이후 연동 도입 시 키 마이그레이션이 없다.
- 연동 변경(게스트→Steam, Steam 해제→Apple)은 연동 테이블 행 조작뿐 —
  **accountKey 불변이므로 Players 레지스트리·접속 중 Player 무변경**
  (idlez가 `ChangeSnsId`로 인메모리 키를 갈아끼우던 문제의 구조적 제거).
- 충돌 정책(이미 다른 계정에 연동된 신원)은 연동 핸들러에서 게임이 결정.
- `provider:subject` 직결도 동작은 하지만(계약은 불투명 문자열) 연동 도입 시
  키 재바인딩 문제가 생긴다 — 연동 계획이 있으면 처음부터 acct.

### Unity 호스트 글루 (Steamworks.NET 기준, ~10줄)

```csharp
var verifier = new SteamSessionVerifier(new SteamSessionOptions
{
    BeginSession = (ticket, steamId) =>
        (int)SteamUser.BeginAuthSession(ticket, ticket.Length, new CSteamID(steamId)),
    EndSession = steamId => SteamUser.EndAuthSession(new CSteamID(steamId)),
});
Callback<ValidateAuthTicketResponse_t>.Create(r =>
    verifier.HandleValidateResult(r.m_SteamID.m_SteamID, (int)r.m_eAuthSessionResponse));
verifier.SessionInvalidated += (steamId, code) => { /* 해당 플레이어 킥 */ };
```

## 6. 검증 (완료 조건)

기존 NUnit 스위트(net10.0)에 추가. 새 E2E는 없다 — Steam 실검증은 실키/실클라
없이 불가능(가짜 이중이 그 대체)하고, Guest는 기존 Players E2E 경로를 재사용한다.

| 대상 | 케이스 |
|---|---|
| `ProviderIdentity` | ToAccountKey 형식 |
| `GuestVerifier` | 정상 통과, trim, 빈 값·128자 초과·`:` 포함 → InvalidCredential |
| `SteamWebApiVerifier` | OK 응답 파싱(steamid/owner/플래그), Valve 거절 → Rejected+ValveErrorCode, VAC 밴 기본 Banned + 옵션 끄면 성공+플래그, 퍼블리셔 밴 동형, hex 아님 → InvalidCredential(HTTP 미호출), HTTP 500 → 예외, 요청 쿼리 파라미터(key/appid/ticket/identity) 검증 |
| `SteamSessionVerifier` | 파싱 오류 → InvalidCredential, 즉시 실패 코드 → Rejected, OK 콜백 → 성공, 거절 콜백 → Rejected, VAC 콜백(3) → Banned, 타임아웃 → Timeout+EndSession 호출됨, 실패 시 EndSession 호출됨, 동시 중복 verify → Rejected, 접속 후 무효화 → SessionInvalidated, OK인데 pending 없음 → 무시 |
| 통합 | 기존 Players E2E의 인라인 `$"guest:{...}"`를 `GuestVerifier` 경유로 교체 — 로그인 핸들러에서 계약 조립 확인 |

## 7. 전제와 비범위

**전제**: Web API 검증은 전용 서버 전제(키는 서버 비밀). 클라 호스트는
`SteamSessionVerifier` + 게임 글루. 실키 스모크는 출시 준비 때 수동 절차로.

비범위(예약): Google/Apple 검증기(같은 계약의 후속 모듈 — `Bun3.Server.Auth.Google`
등), 연동 테이블 헬퍼(게임 DB 몫), DI 등록 헬퍼, 검증 결과 캐싱/레이트 리밋,
킥 사유 클라 전달(v2 close-reason 묶음).
