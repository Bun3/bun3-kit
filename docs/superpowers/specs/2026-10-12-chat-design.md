# 텍스트 채팅 패키지 설계 (`com.bun3.chat` + `com.bun3.unity.chat.ngo`)

- 날짜: 2026-10-12
- 상태: 확정 — 구현 완료 (`com.bun3.chat` 0.2.0, `com.bun3.unity.chat.ngo` 0.1.0)
- 범위: 전송 계층과 무관한 채팅 코어(`common/src/com.bun3.chat` 0.1.0)와 NGO named message 어댑터(`unity/Packages/com.bun3.unity.chat.ngo` 0.1.0)
- 첫 소비자: jurassic-paradise(NGO 리슨 호스트, 대기실·회의 채팅). 게임 쪽 설계는 JP 레포 `docs/superpowers/specs/2026-10-12-chat-system-design.md`
- 참고: idlez-server, growdaemon, growninja 채팅 구조(실수요 파악용, 코드 미복사)

## 1. 실수요 분석과 경계

세 레거시 게임은 모두 채팅을 가졌지만 코드는 패킷·DB·싱글턴과 강결합돼 재사용할 수 없다. 공통으로 반복된 것은 구조다.

| 항목 | 소속 | 근거 |
|---|---|---|
| 서버가 찍는 송신자(ID·이름) | 프레임워크 | 세 게임 모두 세션의 플레이어로 송신자를 결정. 클라이언트가 보낸 이름을 믿지 않는다 |
| 안정 플레이어 ID와 연결 ID 분리 | 프레임워크 | 레거시의 playerId/unitId 분리와 같다. 연결 ID는 재접속·호스트 승계 때 다시 매겨지므로, "내 글"·차단·신고는 바뀌지 않는 플레이어 ID로 판단한다 |
| 본문 정리(trim·제어문자·길이) | 프레임워크 | 세 게임 모두 서버에서 같은 일을 함(255/500자 상한, 탭·개행 제거) |
| 송신자별 도배 제한 | 프레임워크 | 세 게임 모두 **클라이언트에만** 있어 우회 가능했다(3초 3개 → 5초 차단). 서버/호스트 몫으로 내린다 |
| 수신자 판정 | **게임** (정책 훅) | 월드/클랜/귓속말/회의/사망자 등 채널 규칙이 게임마다 완전히 다름 |
| 최근 N개 로그·안 읽음 수 | 프레임워크 | 세 게임 모두 채널별 버퍼(100~1000개) 보유. growdaemon은 무제한이라 누수 |
| 서버 메모리 기록과 신규 접속자 재전송 | 프레임워크(선택) | 세 게임 모두 서버 버퍼의 최근 메시지를 새로 들어온 사람에게 보여줌. 게임마다 끄고 켜고 개수를 정한다 |
| 영속화·읽음 커서·귓속말·차단·푸시 | **게임**/후속 서버 어댑터 | 전용 서버 게임에만 필요. `Bun3.Server.Chat`을 만들 때 그 어댑터 몫 |
| 비속어 필터 | 후속 | `com.bun3.badwords`(별도 브랜치에서 설계 중)가 들어오면 릴레이 파이프라인에 단계로 추가 |
| UI | **게임** | 외형이 게임마다 다름. 두 번째 Unity 소비자가 생기면 재검토 |

## 2. 패키지 구조

```
common/src/com.bun3.chat/
  package.json                 // com.bun3.chat 0.1.0, unity 2022.3, 의존성 없음
  Bun3.Chat.asmdef             // noEngineReferences, autoReferenced
  Bun3.Chat.csproj             // netstandard2.1, PackageId Bun3.Chat, InternalsVisibleTo Bun3.Chat.Tests
  README.md
  Runtime/                     // namespace Bun3.Chat (평평하게)
    ChatMessage.cs
    ChatText.cs
    ChatRateLimiter.cs
    ChatRelay.cs               // ChatRelay + ChatRelayOptions + ChatRejectReason
    IChatPolicy.cs
    ChatLog.cs
  Tests/Editor/                // NUnit 공유 소스 (Unity Test Runner + dotnet)
common/tests/Bun3.Chat.Tests/  // net10.0 NUnit, Tests/Editor/*.cs 링크, Bun3.sln 추가
unity/Packages/manifest.json   // "com.bun3.chat": "file:../../common/src/com.bun3.chat" + testables

unity/Packages/com.bun3.unity.chat.ngo/
  package.json                 // 0.1.0, deps: com.bun3.chat 0.1.0, com.unity.netcode.gameobjects 2.13.1
  Runtime/Bun3.Unity.Chat.Ngo.asmdef   // refs: Bun3.Chat, Unity.Netcode.Runtime, Unity.Collections
  Runtime/NgoChatScope.cs      // namespace Bun3.Unity.Chat.Ngo
  Tests/PlayMode/              // 같은 프로세스 호스트+클라이언트
```

NGO는 UPM 의존성이므로 Dissonance 어댑터처럼 define constraint를 두지 않는다. 다른 넷코드(Mirror, FishNet, 자체 서버 Rpc)는 같은 코어에 각자 어댑터 패키지를 둔다.

## 3. 코어 공개 API (`Bun3.Chat`)

```csharp
public readonly struct ChatMessage
{
    public const int MaxIdentityBytes = 448;  // UTF-8 bytes of SenderPlayerId + SenderName
    public ChatMessage(ulong senderId, string senderPlayerId, string senderName, string text, byte channel, long timestampMs);
    public ulong SenderId { get; }          // transport connection id: routing only, reassigned on reconnect
    public string SenderPlayerId { get; }   // stable player identity from the policy: use for "mine", block, report
    public string SenderName { get; }
    public string Text { get; }
    public byte Channel { get; }      // meaning is defined by the game policy
    public long TimestampMs { get; }  // stamped by the relay host (Unix ms)
}

public static class ChatText
{
    public static bool TryNormalize(string? input, int maxLength, out string text);
}

public sealed class ChatRateLimiter
{
    public ChatRateLimiter(int burst, long windowMs);
    public bool TryAcquire(ulong senderId, long nowMs);
    public void Forget(ulong senderId);
    public void Clear();
}

public interface IChatPolicy
{
    bool TryGetSender(ulong senderId, out string playerId, out string name);
    bool TryGetChannel(ulong senderId, out byte channel);
    bool CanReceive(ulong recipientId, ulong senderId, byte channel);
}

public enum ChatRejectReason : byte { None, InvalidText, UnknownSender, NotAllowed, RateLimited }

public sealed class ChatRelayOptions
{
    public int MaxLength { get; set; } = 100;        // text elements
    public int Burst { get; set; } = 5;
    public long BurstWindowMs { get; set; } = 5000;
    public int HistoryCapacity { get; set; }         // 0 = host keeps no history
    public int HistoryReplayCount { get; set; }      // 0 = new peers see only messages sent after they join
}

public sealed class ChatRelay
{
    public ChatRelay(IChatPolicy policy, ChatRelayOptions? options = null);
    public int MaxLength { get; }
    public int HistoryReplayCount { get; }
    public ChatLog? History { get; }                 // null when HistoryCapacity == 0
    public ChatRejectReason Submit(ulong senderId, string? text, long nowMs, out ChatMessage message);
    public void CollectRecipients(in ChatMessage message, IReadOnlyList<ulong> peers, List<ulong> result);
    public void CollectReplay(ulong recipientId, List<ChatMessage> result);
    public void SeedHistory(ChatLog source);          // copy a local log into History; no-op when History is null
    public void Forget(ulong senderId);
    public void Clear();                             // rate limiter + history
}

public sealed class ChatLog
{
    public ChatLog(int capacity);
    public int Capacity { get; }
    public int Count { get; }
    public ChatMessage this[int index] { get; }  // 0 = oldest
    public int Unread { get; }
    public long TotalAdded { get; }              // 생성 이후 Add 누적, Clear로 초기화되지 않음
    public event Action? Changed;
    public void Add(in ChatMessage message);
    public void MarkRead();
    public void Clear();
}
```

### 3.1 `ChatText.TryNormalize`

입력 → 출력 순서:

1. `null`은 빈 문자열로 취급한다.
2. `UnicodeCategory.Control`(CR·LF·탭 포함)은 공백으로 바꾼다. 채팅은 한 줄이다.
3. `UnicodeCategory.Format`(zero-width, bidi override 등 표시 위장 문자)은 제거한다.
4. 앞뒤 공백을 trim하고 NFC로 정규화한다. 잘못된 서로게이트로 정규화가 실패(`ArgumentException`)하면 `false`.
5. 비어 있거나, 공백·결합 문자(`NonSpacingMark`)만 남았거나, `StringInfo.LengthInTextElements > maxLength`이면 `false`.
6. UTF-8 바이트 수가 `maxLength × 4`를 넘으면 `false`. 텍스트 요소 하나에 결합 문자를 수천 개 붙이는 입력(zalgo)이 글자 수 검사를 통과해 메모리와 전송 크기를 키우는 것을 막는다. 이 상한 덕분에 메시지 하나의 전송 크기가 `MaxLength`로 정해진다.

거부보다 정리를 우선한다. 붙여넣은 개행이나 보이지 않는 문자 때문에 메시지 전체가 사라지지 않게 한다. 리치 텍스트 태그 무력화는 표시 계층(TMP 등) 몫이라 코어에서 다루지 않는다.

### 3.2 `ChatRateLimiter`

송신자마다 길이 `burst`의 타임스탬프 링을 둔다. `windowMs` 안에 이미 `burst`개를 보냈으면 `false`. 가장 오래된 기록보다 `nowMs`가 작으면(호스트 시계가 뒤로 감) 창이 지난 것으로 본다. 시계 보정 한 번에 모든 송신자가 그 차이만큼 막히지 않게 하기 위해서다. 링은 송신자의 첫 메시지에서 한 번 할당하고 이후 재사용한다. 연결 종료 시 어댑터가 `Forget`을 호출한다.

### 3.3 `ChatRelay.Submit` 파이프라인 (호스트/서버 전용)

1. `policy.TryGetSender` 실패, 빈 플레이어 ID, 또는 플레이어 ID와 이름의 UTF-8 바이트 합이 `ChatMessage.MaxIdentityBytes`(448)를 넘음 → `UnknownSender`. 어댑터의 메시지 크기 계산(§4)이 이 상한에 기댄다
2. `ChatText.TryNormalize(text, MaxLength)` 실패 → `InvalidText`
3. `policy.TryGetChannel` 실패 → `NotAllowed` (지금 채팅할 수 없는 상태)
4. `ChatRelayOptions.TextFilter`가 있으면 정규화한 본문을 넘겨 바꾼 본문을 받는다(예: 비속어 가리기). 결과를 다시 `ChatText.TryNormalize`하고 실패하면 `InvalidText`. 0.2.0(2026-10-15)에 추가했다
5. `ChatRateLimiter.TryAcquire` 실패 → `RateLimited`
6. 성공 → `message = new ChatMessage(senderId, playerId, name, normalized, channel, nowMs)`. `History`가 있으면 추가한다.

순서 근거: 거부된 메시지는 도배 제한 예산을 소모하지 않는다. 그래서 본문 바꾸기도 도배 제한 앞에 둔다. 비속어 사전은 게임마다 다르므로 코어는 `com.bun3.badwords`에 의존하지 않고 델리게이트만 받는다. 송신자 이름과 플레이어 ID는 정책에서만 온다. `Submit` 페이로드에는 둘 다 없다.

`CollectRecipients`는 `result`를 비우고, `peers` 중 `policy.CanReceive(peer, message.SenderId, message.Channel)`가 참인 것만 담는다. 호출자가 소유한 리스트를 재사용하므로 할당이 없다. 송신자 자신을 받게 할지도 정책이 정한다(보통 참).

### 3.4 `ChatLog`

용량 고정 링 버퍼다. 가득 차면 가장 오래된 메시지를 덮는다. `Unread`는 마지막 `MarkRead` 이후 추가된 수이며 `Capacity`를 넘지 않는다. `Add`, `MarkRead`(값이 바뀔 때), `Clear`가 `Changed`를 발생시킨다. `TotalAdded`는 `Add` 누적 횟수이며 `Clear`가 초기화하지 않는다. UI는 이 값과 `Count`의 변화로 "끝에 추가된 수"와 "앞에서 밀려난 수"를 구해 가상화 리스트에 증분으로 알린다. 스레드 안전하지 않다. 메인 스레드 소비를 전제한다.

### 3.5 호스트 기록과 재전송

- `ChatRelayOptions`는 생성 시 검증한다. `MaxLength`·`Burst`·`BurstWindowMs`는 1 이상이고, `0 ≤ HistoryCapacity`, `0 ≤ HistoryReplayCount ≤ HistoryCapacity`여야 한다. 어기면 `ArgumentOutOfRangeException`이다.
- `HistoryCapacity > 0`이면 릴레이가 통과시킨 모든 메시지를 `History`(같은 `ChatLog` 링 버퍼)에 담는다. 채널 구분 없이 한 버퍼라서, 넘치면 채널과 무관하게 가장 오래된 것부터 밀려난다. 게임은 `History`를 읽어 신고·관전 등에 쓸 수 있다.
- `CollectReplay(recipientId, result)`는 `result`를 비우고 기록을 최신부터 거꾸로 훑는다. `policy.CanReceive(recipientId, m.SenderId, m.Channel)`가 참인 메시지를 최대 `HistoryReplayCount`개 모은 뒤, 오래된 순으로 담는다. 판정은 **지금** 정책 기준이다. 그래서 정책은 `channel`(보낸 시점의 송신자 상태)로 판단해야 하고, 송신자의 현재 상태로 판단하면 안 된다.
- 조합:
  - `HistoryCapacity = 0`: 기록 없음.
  - `HistoryCapacity > 0, HistoryReplayCount = 0`: 저장만 한다. 늦게 들어온 사람은 접속 이후 메시지만 본다.
  - 둘 다 > 0: 레거시 방식. 늦게 들어온 사람도 볼 수 있는 최근 메시지를 받는다.
- `Clear()`는 도배 제한 상태와 기록을 비운다. 서버가 멈출 때 어댑터가 부른다. 영속화(디스크·DB)는 하지 않는다.
- `SeedHistory(source)`는 `source`의 메시지를 오래된 순으로 `History`에 추가한다. 넘치면 최신 것이 남는다. 호스트 승계처럼 서버 역할이 다른 피어로 넘어갈 때, 새 서버가 자기 로컬 로그로 기록을 이어받는 데 쓴다. `History`가 없으면(기록 꺼짐) 아무것도 하지 않는다. 그래서 게임은 항상 부르고, 켜고 끄기는 설정값만으로 정한다.
- 기록 안 메시지의 `SenderId`는 보낸 시점의 연결 ID라서 재접속 뒤에는 다른 사람을 가리킬 수 있다. 정책의 `CanReceive`와 게임의 표시 판정은 `Channel`과 `SenderPlayerId`를 써야 한다.

## 4. NGO 어댑터 (`Bun3.Unity.Chat.Ngo`)

```csharp
public sealed class NgoChatScope : IDisposable
{
    public NgoChatScope(NetworkManager manager, ChatRelay relay);
    public event Action<ChatMessage>? Received;          // live messages
    public event Action<ChatMessage>? HistoryReceived;   // replay sent on connect
    public bool Send(string text);
    public void Dispose();
}
```

- **메시지 3개**:
  - `Bun3.Chat.Submit`: 클라이언트 → 서버, 본문 `string`
  - `Bun3.Chat.Message`: 서버 → 수신자, `SenderId`, `Channel`, `TimestampMs`, `SenderPlayerId`, `SenderName`, `Text`
  - `Bun3.Chat.History`: 서버 → 새로 접속한 클라이언트 1명, `ushort` 개수 + 메시지 필드 반복
  - 문자열은 `ushort` 바이트 수 + UTF-8로 쓴다. NGO의 `WriteValueSafe(string)`은 UTF-16이라 크기 상한이 두 배가 되기 때문이다. 읽을 때 바이트 수가 상한(본문 `MaxLength × 4`, 신원 448)을 넘으면 버린다
- **전송 방식**: 세 메시지 모두 `NetworkDelivery.ReliableFragmentedSequenced`다. 같은 파이프라인이라 기록과 실시간 메시지의 순서가 섞이지 않는다. NGO 기본 비분할 상한(약 1.26KB)을 넘는 기록 묶음도 보낼 수 있다.
- **크기 상한**: 기록 묶음은 4KB 이하로 자른다. UnityTransport `MaxPayloadSize` 기본값이 6KB이기 때문이다. 메시지 하나가 묶음 하나에 들어가야 하므로, 생성자에서 `relay.MaxLength × 4 + 512 ≤ 4096`을 검사하고 어기면 `ArgumentOutOfRangeException`이다(`MaxLength` 896까지). 512바이트는 플레이어 ID·이름·고정 필드 몫이다.
- **등록 수명**: NGO는 시작할 때마다 `CustomMessagingManager`를 새로 만든다. 스코프는 생성 시점에 이미 리스닝 중이면 즉시 등록한다. 그 외에는 `OnClientStarted`/`OnServerStarted`에서 등록하고, `OnClientStopped`/`OnServerStopped`에서 해제하며 릴레이 상태를 비운다. `Submit` 핸들러는 서버일 때만 등록한다. `Dispose`는 이벤트 구독과 핸들러를 모두 해제한다.
- **호스트 처리**: `Submit`을 받으면 `relay.Submit(senderClientId, text, nowMs)`를 호출한다. 성공하면 `CollectRecipients(message, 준비된 피어 목록, …)` 후 목록 전송 오버로드로 한 번에 보낸다. 준비된 피어는 서버 시작 때의 호스트 자신과, `OnClientConnectedCallback`에서 재전송을 넣은 뒤의 클라이언트다. 연결 해제·서버 정지 때 뺀다. NGO 씬 관리가 켜져 있으면 승인 뒤 씬 동기화가 끝날 때까지 `ConnectedClientsIds`에는 있지만 연결 콜백은 아직 오지 않으므로, 그 사이 실시간 메시지를 보내면 재전송과 중복되고 순서가 뒤집히기 때문이다. `nowMs`는 `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`. 거부는 조용히 버린다(송신자에게 사유를 돌려주지 않음, §7).
- **루프백 불필요**: NGO 2.13.1의 `SendNamedMessage`는 호스트가 `LocalClientId`로 보내면 로컬에서 바로 호출한다(`CustomMessageManager.cs` host 분기). 호스트의 `Send`도 서버 ID로 보내는 같은 경로를 탄다.
- **신뢰 경계**:
  - `Message` 핸들러는 `senderClientId != NetworkManager.ServerClientId`이면 버린다.
  - 페이로드 읽기의 `OverflowException`은 잡아서 버린다.
  - `Submit` 본문 길이는 릴레이가 재검증한다.
  - 송신자 ID는 NGO가 인증한 `senderClientId`만 쓴다.
- **연결 종료**: 서버에서 `OnClientDisconnectCallback` → `relay.Forget(clientId)`.
- **재전송**: 서버에서 `OnClientConnectedCallback(clientId)`가 오면, `relay.HistoryReplayCount > 0`이고 호스트 자신이 아닐 때 `CollectReplay` 결과를 4KB 묶음으로 그 클라이언트에게 보낸다. 이후 실시간 메시지는 같은 파이프라인에서 그 뒤에 도착한다. 클라이언트는 `History`의 메시지를 순서대로 `HistoryReceived`로 올린다. 실시간 `Received`와 나누는 이유는, 호스트 승계 뒤 재접속한 피어도 재전송을 받기 때문이다. 이미 로그를 가진 피어는 게임이 이 이벤트를 무시해 중복을 막는다. NGO 2.13 UnityTransport의 송신 큐는 필요에 따라 커지므로 10,000개(약 1.5MB)도 한 번에 넣을 수 있다. 접속 순간 부하가 문제 되면 틱마다 나눠 보내도록 바꾼다고 `ponytail:` 주석을 남긴다.
- **`Send`**: 리스닝 중이 아니거나 `ChatText.TryNormalize`에 실패하면 `false`를 돌려주고 보내지 않는다. 성공하면 정리된 본문을 서버로 보낸다. 표시는 서버가 되돌려준 `Message`로만 한다. 낙관적 표시를 하지 않으므로 거부된 글은 화면에 나타나지 않는다.
- **할당**: 채팅은 저빈도 경로다. 수신 문자열 할당과 `FastBufferWriter`(`Allocator.Temp`, `using`)는 허용한다. 수신자 리스트는 스코프가 보유해 재사용한다.

## 5. 게임 연결 예 (README에 수록)

```csharp
var relay = new ChatRelay(new MyPolicy(), new ChatRelayOptions { HistoryCapacity = 1000, HistoryReplayCount = 100 });
var scope = new NgoChatScope(networkManager, relay);
var log = new ChatLog(1000);
scope.Received += m => log.Add(m);
scope.HistoryReceived += m => log.Add(m);   // skip this when the peer already has a log (reconnect)
networkManager.OnServerStarted += () => relay.SeedHistory(log);   // a new host inherits its own log
scope.Send(input.text);
```

## 6. 테스트

- **코어**: NUnit 공유 소스를 `dotnet test`와 Unity EditMode 양쪽에서 실행한다. 가짜 `IChatPolicy`를 사용한다.
  - `ChatText`: 개행·탭 → 공백, zero-width·bidi 제거, NFC, 앞뒤 trim, 빈 문자열·공백만·결합 문자만 거부, 텍스트 요소 기준 길이(한글·이모지 서로게이트), 잘못된 서로게이트 거부, 결합 문자 폭탄의 바이트 상한 거부.
  - `ChatRateLimiter`: burst까지 허용, 초과 거부, 창이 지나면 회복, `Forget` 후 초기화, 송신자별 독립.
  - `ChatRelay`: 파이프라인 단계별 거부 사유, 거부 시 예산 미소모, 이름과 플레이어 ID는 정책에서만 온다, `CollectRecipients` 필터링과 리스트 재사용.
  - 기록: 통과한 메시지만 저장, 용량 초과 덮어쓰기, `CollectReplay`의 정책 필터·개수 상한·오래된 순 정렬, `Clear`, 옵션 검증, `SeedHistory`(순서 유지, 넘치면 최신 유지, 기록 꺼짐이면 무시).
  - `ChatLog`: 순서, 용량 초과 시 덮어쓰기, 인덱싱, `Unread` 상한, `MarkRead`, `Clear`, `Changed` 발생 조건, `TotalAdded` 누적과 `Clear` 후 유지.
- **어댑터 PlayMode** (bun3-kit Unity 프로젝트, UnityTransport 127.0.0.1 빈 포트, 같은 프로세스 호스트+클라이언트):
  1. 클라이언트 `Send` → 양쪽이 호스트 정책의 이름과 플레이어 ID가 붙은 메시지를 받는다.
  2. 호스트 `Send`(로컬 경로) → 양쪽이 받는다.
  3. 정책 `CanReceive`가 클라이언트를 제외하면 호스트만 받는다.
  4. 정책 `TryGetChannel` 거부 → 아무도 받지 않는다.
  5. 종료 후 재시작해도 핸들러가 다시 등록돼 동작한다.
  6. `Dispose` 후에는 수신 이벤트가 없다.
  7. 재전송: 호스트가 메시지 3개를 보낸 뒤 클라이언트가 접속 → 최근 `HistoryReplayCount`개를 `HistoryReceived`로 순서대로 받고, 이어진 실시간 메시지가 `Received`로 그 뒤에 온다.
  8. `HistoryReplayCount = 0` → 늦게 접속한 클라이언트는 이전 메시지를 받지 않는다.
  9. 정책이 막은 채널의 기록은 재전송되지 않는다.
  10. 4KB를 넘는 기록이 여러 묶음으로 나뉘어 순서대로 도착한다.
  11. 서버를 재시작하면 기록이 비어 있다.

## 7. 비범위 (v1)

- 거부 사유를 송신자에게 회신. 필요해지면 `Bun3.Chat.Reject` 메시지를 추가한다.
- 서버 쪽 채널 레지스트리, 멤버/구독자 집합, 채널별 기록 상한, 영속화, 읽음 커서, 귓속말. `Bun3.Server.Chat`을 만들 때 다룬다.
- 채팅 UI 컴포넌트. 범용 가변 높이 목록은 `com.bun3.unity.ui`의 `VirtualList`(스펙 `2026-10-12-unity-virtual-list-design.md`)로 제공한다.

## 8. 완료 기준

- `dotnet build` 경고 0, `dotnet test` 통과.
- bun3-kit Unity 프로젝트가 두 패키지를 경고 없이 컴파일하고, EditMode·PlayMode 테스트가 통과하며, `.meta`가 커밋돼 있다.
- 모든 public 멤버에 영어 XML 문서. README에 사용 예와 정책 계약을 적는다.
- JP가 `#Bun3/jp-chat` 브랜치로 두 패키지를 참조해 통합 테스트를 통과한다.
