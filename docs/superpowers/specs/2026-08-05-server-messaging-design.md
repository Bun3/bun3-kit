# Bun3.Server.Messaging 설계 (v1: 타입 있는 요청/응답 + 서버 푸시)

- 날짜: 2026-08-05
- 상태: 승인 대기
- 범위: `Bun3.Server.Messaging` 패키지 신설 — protobuf 직렬화, 핸들러 등록/디스패치,
  상태코드 응답, 클라이언트 커넥터(`IConnector`/`MessagingClient`), Ping/Pong
- 선행 문서: `2026-08-04-server-modulize-base-design.md` (v0 패킷 전송, §10 로드맵)

## 1. 배경과 입력 결정

v0는 "바이트 패킷이 세션 액터까지 도달"하는 수직 슬라이스를 완성했다. v1은 그
패킷에 타입과 의미를 입힌다: growninja의 7,369줄 수동 switch를 구조로 대체하고,
idlez의 상태코드 응답·코드젠 강제성의 가치를 프레임워크 형태로 일반화한다.

사용자 확인을 거친 입력 결정:

| 질문 | 결정 | 근거 |
|---|---|---|
| 타 언어 소비 | 있음 | 언어 중립 스키마 필수 → protobuf 계열 확정 |
| protobuf 스택 | **Google.Protobuf (스키마 우선)** | 공식 구현, `.proto`가 진실원본, 타 언어 공유는 스키마 전달로 끝. idlez에서 검증 |
| v1 범위 | **요청/응답 + 서버 푸시** | 푸시 없는 게임 서버는 실용 불가. 소켓으로 서버가 먼저 보내는 메시지(모바일 푸시 알림 아님) |
| 디스패치 | **A안: 런타임 등록 + 기동 시 전수 검증** | 코드젠/소스제너레이터 없이 "빠뜨리면 기동 실패"로 idlez 강제성의 실질 확보. 등록 API 뒤가 감춰져 있어 추후 소스 제너레이터로 무파괴 업그레이드 가능 |

## 2. 패키지 구조와 스키마 소유권

```
server/src/
├── Bun3.Server.Messaging/        netstandard2.1 (신규)
│   └ → Core, Abstractions + Google.Protobuf(NuGet)
├── Bun3.Server.Abstractions/     + IConnector (나가는 연결 계약)
├── Bun3.Server.Transport.Tcp/    + TcpConnector, → Bun3.Common 참조 추가
└── (Core/Hosting 기존 유지; Hosting에 AddMessagingServer 확장 추가)

common/src/com.bun3.common/
└── Runtime/Network/PacketFormat.cs   ← Transport.Tcp에서 이동 (와이어 규약은 클라·서버 공유)
```

- **Google.Protobuf 의존은 Messaging에만 격리**된다. v0 패키지들은 protobuf를
  모른다. Unity에서 Messaging을 쓸 때만 NuGetForUnity로 Google.Protobuf 설치가
  필요하다 (NuGet-in-Unity 완화 방침의 두 번째 적용, M.E.L.A에 이어).
  (Hosting은 AddMessagingServer 확장으로 인해 Google.Protobuf를 전이 참조하지만,
  Unity 소비 대상이 아니므로 격리 목표(클라 = Messaging/Transport.Tcp/Abstractions/Common만)는
  유지된다.)
- **proto 스키마는 게임이 소유하고 프레임워크는 스키마를 모른다.** 프레임워크가
  요구하는 규약은 루트 3형뿐:

```proto
message Request  { int64 request_id = 1; oneof body { ... } }   // 클라 → 서버
message Response { int64 request_id = 1; int32 status = 2; oneof body { ... } }
message Update   { oneof body { ... } }                          // 서버 → 클라 (푸시)
```

- 프레임워크는 루트 3형을 제네릭 파라미터로 받아, protobuf 생성 코드에 내장된
  **디스크립터**로 oneof 케이스를 열거해 핸들러와 매핑한다. 게임이 메시지를
  추가해도 프레임워크 재컴파일은 없다.
- 요청/응답 매칭 규약: `Request.body`의 각 케이스는 `Response.body`에 **같은 필드
  이름·번호**의 케이스를 가져야 한다 (기동 검증 대상). 응답할 내용이 없는 요청도
  빈 응답 메시지를 정의한다 — "모든 요청은 응답을 받는다"가 상관·타임아웃 모델의
  전제다.
- protoc 실행 방식은 강제하지 않는다. 서버/테스트는 `Grpc.Tools`(빌드 통합)를
  표준 경로로, Unity는 생성 `.cs` 커밋(idlez 관례)을 권장으로 문서화한다.

## 3. 와이어 포맷과 메시지 흐름

v0 패킷(4바이트 길이 프리픽스) 위에 1바이트 채널:

```
[길이:4] [채널:1] [protobuf 바이트]

0x01 Control   (프레임워크 소유: Ping/Pong — Messaging 내장 control.proto)
0x02 Request   (클라 → 서버)
0x03 Response  (서버 → 클라)
0x04 Update    (서버 → 클라, 푸시)
0x10+ 예약     (미래: 게임 커스텀/고빈도 hot 채널의 확장 지점)
```

- **requestId 상관**: 클라가 단조 증가 id 발급 → pending 딕셔너리에 대기 등록 →
  서버가 응답에 같은 id 에코 → 클라가 매칭해 await를 깨움. 요청별 타임아웃
  (기본 10초, 옵션) 초과 시 해당 요청만 실패, 연결 종료 시 pending 전원 실패 —
  영원히 매달리는 await는 구조적으로 없다.
- **상태코드 대역**: `0` = OK, `1~99` = 프레임워크 예약(1=핸들러 미등록 방어,
  2=핸들러 예외), 음수 = 게임 정의(도메인별 대역 권장, 강제 안 함).
- **에러 정책의 진화 (v0 스펙 §6의 "대안 B" 실현)**: 핸들러 예외는 세션 종료
  대신 `status=2` 응답 + 세션 유지 + 서버 로그. 세션 종료는 **프로토콜 위반**
  (알 수 없는 채널, protobuf 파싱 실패, 방향 위반)에만 남는다. `OnHandlerError`
  훅은 유지되어 게임이 "이 예외는 종료"를 재정의할 수 있다.
- **Ping/Pong (채널 0x01)**: 클라 주기 Ping(기본 30초) → 서버 Pong 에코. 서버는
  마지막 수신 시각 기준 무응답 킥(옵션, 기본 120초). 클라에 레이턴시 노출.

## 4. 서버 API

```csharp
public sealed class MySession : MessagingSession        // 제네릭 없음
{
    public MySession(IConnection connection) : base(connection) { }
    // OnPacketAsync는 프레임워크가 sealed 구현 — 게임은 바이트를 만지지 않는다.
    // OnConnectedAsync/OnDisconnectedAsync/OnHandlerError 훅은 v0 그대로.
}

builder.Services.AddMessagingServer<MySession, Request, Response, Update>(messaging =>
{
    messaging.OnRequest<BuyItemRequest, BuyItemResponse>(async (session, req) =>
    {
        if (/* 골드 부족 */) return Reply.Fail(-1001);
        return new BuyItemResponse { ... };             // 암시 변환 = Reply.Ok
    });
});
```

- **핸들러 등록은 서버 수준이다.** 세션은 접속마다 생성되므로 부팅 순간에는
  0개다 — 등록이 세션 안에 있으면 "전 요청에 핸들러 존재"를 검사할 표 자체가
  부팅 시점에 없고, 검증이 첫 접속으로 밀리며 세션마다 등록이 달라질 수도 있다.
  등록표를 부팅 시 1회 구성되는 서버 소유물로 두어야 기동 검증이 성립한다.
  핸들러 시그니처가 `(세션, 요청) => 응답`이라 요청을 보낸 세션 인스턴스가 첫
  인자로 들어오므로, 세션 상태 접근·인스턴스 메서드 실행은 idlez와 동일하다.

### 핸들러 코드 배치 (권장 관례)

등록(라우팅 표, 요청당 1줄)과 구현(핸들러 본문)을 분리한다. 구현은 idlez처럼
세션 partial 파일 하나에 하나씩:

```
Handlers/ItemHandlers.cs     ← 기능별 Register(m) 묶음 = 그 기능의 목차
Session/MySession.BuyItem.cs ← partial class, 요청 하나의 구현 (idlez WorldPlayer.BuyItem.cs 대응)
```

```csharp
public static class ItemHandlers
{
    public static void Register(MessagingConfig<MySession> m)
    {
        m.OnRequest<BuyItemRequest, BuyItemResponse>((s, req) => s.HandleBuyItem(req));
        m.OnRequest<UseItemRequest, UseItemResponse>((s, req) => s.HandleUseItem(req));
    }
}

public sealed partial class MySession
{
    private async ValueTask<Reply<BuyItemResponse>> HandleBuyItem(BuyItemRequest req) { ... }
}
```

`Program.cs`의 `AddMessagingServer` 본문은 `ItemHandlers.Register(m);` 나열만
남는다 — 파일 비대화는 구조적으로 발생하지 않는다.
- **기동 검증**: `Request.body` 전 케이스에 핸들러 존재, 응답 타입이 매칭 규약
  충족, 중복 등록 없음 — 위반 시 전체 목록을 출력하며 기동 실패(fail-fast).
- **푸시 발신**: `session.SendUpdateAsync(update)` — 디스크립터 맵으로 `Update`
  envelope에 포장, 채널 0x04.

### Reply<TRes>

```csharp
public readonly struct Reply<TRes> where TRes : class, IMessage<TRes>
{
    public int Status { get; }      // 0 = OK
    public TRes? Value { get; }     // 불변식: Status == 0 ⟺ Value != null
    public bool IsOk => Status == 0;
    public static Reply<TRes> Ok(TRes value);      // null 금지
    public static Reply<TRes> Fail(int status);    // 0 금지
    public static implicit operator Reply<TRes>(TRes value);         // = Ok
    public static implicit operator Reply<TRes>(ReplyFailure f);     // Reply.Fail(코드)의 통로
}
public static class Reply { public static ReplyFailure Fail(int status); }
```

- readonly struct — 요청마다 생기는 타입이므로 무할당.
- **실패에는 본문이 없다(v1)**. "실패 + 데이터"는 응답 메시지 필드로 설계하고
  status=0으로 반환하는 것이 규약. 필요가 실증되면 v2에서 실패 본문 검토.
- **판정은 값, 장애는 예외**: 서버가 내린 게임 판정(골드 부족)은 `Reply.Fail`로,
  인프라 실패(타임아웃·연결 끊김)는 클라에서 예외(`TimeoutException`/
  `ConnectionClosedException`)로 구분된다.

## 5. 클라이언트 API

```csharp
var connector = new TcpConnector(new TcpConnectorOptions { Host = "...", Port = 20000 });
var client = await MessagingClient.ConnectAsync<Request, Response, Update>(connector, options, ct);

var reply = await client.RequestAsync<BuyItemResponse>(new BuyItemRequest { ... }, ct);
if (reply.IsOk) Use(reply.Value); else ShowError(reply.Status);

client.OnUpdate<BroadcastedUpdate>(u => ...);
```

- `IConnector`(Abstractions): 나가는 연결 계약. `TcpConnector`(Transport.Tcp)가
  첫 구현. `MessagingClient`는 `IConnection`만 알므로 Steam 커넥터가 생겨도 무변경.
- **Unity 스레딩**: `MessagingClientOptions.UseSynchronizationContext`(기본 on)가
  캡처된 SynchronizationContext로 응답 재개·푸시 콜백을 올린다 — Unity에선 메인
  스레드, 컨텍스트 없는 환경(서버/테스트)에선 스레드풀.
- 클라는 기동 검증을 하지 않는다 — 관심 없는 푸시 무시는 정당한 선택(미등록
  Update는 경고 로그만).
- **IL2CPP 노트**: Unity 배포 빌드는 AOT(IL2CPP)라 실행 중 코드 생성이 불가능
  하다. Google.Protobuf 디스크립터 API의 필드 접근자는 JIT 환경에선 런타임
  컴파일로 빠르지만 IL2CPP에선 인터프리트되어 호출당 크게 느려질 수 있다.
  따라서 디스크립터는 **기동 1회의 맵 구축에만** 쓰고, 메시지마다 도는 hot
  path의 payload 접근은 protobuf가 생성한 일반 C# 프로퍼티(AOT 정상 컴파일)를
  타는 델리게이트를 기동 시 준비해 두는 방식으로 구현한다. 클라 메시지 빈도
  (초당 수십)에선 어느 쪽이든 체감 차 없지만 기본값을 안전한 쪽에 둔다.

## 6. 성능 판단

- Hot path는 "protobuf 파싱 → 생성 코드의 `BodyCase` enum 읽기 → int 키 딕셔너리
  조회 → 델리게이트 호출" — 디스크립터/리플렉션은 기동 1회의 맵 구축에만 쓰인다.
- 지배 비용은 직렬화/할당이며 이 장르 트래픽(요청/응답+푸시)에서 오차범위.
  조일 나사는 준비돼 있다: 수신 버퍼 복사 → Bun3.Common 풀링, 메시지 객체 →
  protobuf 풀링(idlez 선례). 둘 다 측정 후, API 무변경.
- 틱 단위 고빈도 트래픽은 이 계층의 대상이 아니다 — 채널 0x10+가 그 확장 지점
  (idlez의 protobuf/FlatBuffers 이원화와 같은 구도).

## 7. 검증 (v1 완료 조건)

샘플/테스트용 미니 프로토콜: `GetServerTimeRequest/Response`(성공 경로),
`BuyItemRequest/Response`(실패 상태코드 -1001), `BroadcastedUpdate`(푸시).

| 종류 | 대상 |
|---|---|
| 단위 | Reply 불변식 (Ok(null)/Fail(0) 차단) |
| 단위 | 채널 파싱 — 알 수 없는 채널/파싱 실패 → 프로토콜 위반 종료 |
| 단위 | 기동 검증 — 미등록 목록과 함께 기동 실패, 매칭 규약 위반, 중복 거부 |
| 단위 | requestId 상관 — 매칭, 요청별 타임아웃, 연결 종료 시 pending 전원 실패 |
| 단위 | 핸들러 예외 → status=2 + 세션 유지, OnHandlerError 재정의 시 종료 |
| 단위 | Ping/Pong, 무응답 idle kick |
| E2E | 실 TCP: 요청/응답 roundtrip, 실패 상태코드, 푸시 수신, 동시 다중 요청 상관, graceful shutdown |

E2E 5종 통과 = v1 완료.

## 8. v0에 가하는 변경 (최소)

- `PacketFormat` → `common/src/com.bun3.common/Runtime/Network/`로 이동
  (Transport.Tcp에 Bun3.Common 참조 추가; meta 파일은 Unity 관례대로 커밋)
- `IConnector` 계약 추가 (Abstractions), `TcpConnector` 구현 추가 (Transport.Tcp)
- **v0 Core(Session/ServerBase)는 무변경** — MessagingSession이 상속으로 얹힌다

## 9. 비동기 타입 원칙

공개 API의 비동기 타입은 BCL로 고정한다: hot path(메시지·요청 단위)는
`ValueTask`, 수명주기(시작/정지)는 `Task`. UniTask 등 플랫폼 최적화
라이브러리는 프레임워크 계약에 넣지 않는다 — UniTask↔ValueTask 변환이
내장이라 Unity 게임 코드는 마찰 없이 소비 가능하며, UniTask 당의정
(Destroy 연동 취소 등)은 추후 Unity 어댑터 패키지(`com.bun3.unity.net`류)의
몫이다.

## 10. 비범위 (예약)

- 소스 제너레이터(핸들러 등록 자동화 — A안의 무파괴 업그레이드 경로)
- 실패 응답 본문, 압축/암호화, 고빈도 hot 채널(FlatBuffers류)
- Player 계층·세션 재접속(`Bun3.Server.Sessions`), protobuf 메시지 풀링
- 호스트당 다중 서버/세션 타입
- v0 하드닝 백로그(마이너 항목들)는 별도 태스크로 유지
