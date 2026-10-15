# Unity 방(Room)·Steam 네트워킹 패키지 설계

날짜: 2026-10-12
상태: 확정 (브레인스토밍 승인 — 쥬라기 낙원 Steam 전환 작업에서 도출)

## 목표

멀티플레이 게임이 반복해서 짜는 "방 만들기 → 코드 공유 → 참가 → 호스트 이탈 시 승계"를
프레임워크가 지원한다. 첫 소비자는 쥬라기 낙원(NGO 리슨 호스트, 최대 8인)이며, Unity
Relay/Lobby(UGS Multiplayer Sessions)를 Steam SDR + Steam 로비로 대체해 릴레이 비용을
없애는 것이 직접 동기다.

- 방 개념(코드, 멤버, 방장, 입장 잠금, 승계 선출)은 넷코드·플랫폼 무관 코어에 둔다.
- 플랫폼(Steam)과 넷코드(NGO)는 각각 어댑터 패키지로 분리하고, 둘을 잇는 코드는 조합
  패키지에만 둔다 (`audio` / `audio.dissonance` / `audio.dissonance.steamaudio` 관례).
- 게임에는 도메인 정책(승계 허용 조건, 입장 규칙, UI 문구)만 남긴다.

## 패키지 구성

```
com.bun3.unity.steam                  Steam 수명 관리                         [BUN3_STEAMWORKS]
 └ com.bun3.unity.steam.ngo           SDR P2P NGO transport                   [BUN3_STEAMWORKS] + NGO
com.bun3.unity.rooms                  방 추상화·방 코드·방장 선출 (NGO/Steam 무관)
 ├ com.bun3.unity.rooms.ngo           방 ↔ NGO 세션, 호스트 승계, 루프백 백엔드  NGO + UTP
 ├ com.bun3.unity.rooms.steam         Steam 로비 백엔드, 초대·게임 참여        [BUN3_STEAMWORKS]
 └ com.bun3.unity.rooms.steam.ngo     로비 ↔ Steam transport 연결             [BUN3_STEAMWORKS] + NGO
```

| 패키지 | 어셈블리 | 의존 |
|---|---|---|
| `steam` | `Bun3.Unity.Steam` (+ `.Editor`) | core(`PlayerLoopSystemHelper`), Steamworks.NET |
| `steam.ngo` | `Bun3.Unity.Steam.Ngo` | steam, NGO 2.13.1 |
| `rooms` | `Bun3.Unity.Rooms` | core(UniTask) |
| `rooms.ngo` | `Bun3.Unity.Rooms.Ngo` | rooms, NGO 2.13.1, Unity Transport |
| `rooms.steam` | `Bun3.Unity.Rooms.Steam` | rooms, steam |
| `rooms.steam.ngo` | `Bun3.Unity.Rooms.Steam.Ngo` | rooms.ngo, steam.ngo (`IRoom` 멤버로 접속을 거른다 — rooms.steam 타입은 쓰지 않는다) |

- 네임스페이스 = 어셈블리 이름. 최소 Unity 6000.3. 패키지 코드 규칙(영어 전용, public XML
  문서, 경고 0, 핫패스 무할당)은 레포 `CLAUDE.md`를 따른다.
- 네트워크 라이브러리 접미사는 `.ngo`다 (`.netcode` 금지 — 레포 `CLAUDE.md` 규칙).

### Steamworks.NET 활성화 (define 게이트)

- Steamworks.NET은 패키지 의존성으로 선언하지 않는다. 게임이 직접 설치한다 (UPM git:
  `https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net`).
- Steam 계열 런타임·테스트 어셈블리는 `defineConstraints: ["BUN3_STEAMWORKS"]`로 막고,
  플랫폼은 Editor + Standalone(Windows/macOS/Linux)으로 제한한다.
- `Bun3.Unity.Steam.Editor`(게이트 없음)가 메뉴를 제공한다:
  `Tools > Bun3 > Steam > Sync Installed Adapters` / `Disable Adapters`.
  Steamworks.NET 어셈블리 정의(`com.rlabrecque.steamworks.net`)와 핵심 타입(`Steamworks.SteamAPI`)이
  로드돼 있을 때만 Standalone 대상에 `BUN3_STEAMWORKS`를 추가하고, 다른 define은 보존한다.
  `-executeMethod`용 정적 메서드도 연다. 도메인 리로드 시 자동 실행하지 않는다.
  (`Bun3.Unity.Audio.Editor.SoundSdkSetup`과 같은 계약) 이미 켜져 있으면 define 순서를 바꾸지 않는다.

## `com.bun3.unity.rooms` — 코어

### RoomCode (readonly struct)

- 길이 6, 알파벳 32자 `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (0/O/1/I 제외). 경우의 수 32⁶ ≈ 10.7억.
- `static RoomCode Generate()` — `System.Security.Cryptography.RandomNumberGenerator` 사용.
- `static bool TryNormalize(ReadOnlySpan<char> input, out RoomCode code)` — 앞뒤 공백 제거,
  대문자화, 길이 6과 알파벳 검사. 실패 시 false.
- `static RoomCode FromValue(uint value)` / `uint Value` — 30비트 이하의 수와 코드를 왕복 변환
  (루프백 백엔드의 포트 인코딩용). 모든 코드는 값 하나에 대응하므로 실패 경로가 없다.
- `IEquatable<RoomCode>`, `TryFormat(Span<char>)`, `ToString()`(저빈도 경로 전용).

### IRoomBackend

```csharp
UniTask<IRoom> CreateAsync(RoomCreateOptions options, CancellationToken cancellationToken);
UniTask<IRoom> JoinByCodeAsync(RoomCode code, RoomJoinOptions options, CancellationToken cancellationToken);
UniTask<IRoom> JoinByIdAsync(string roomId, RoomJoinOptions options, CancellationToken cancellationToken);
event Action<string> JoinRequested;   // 초대 수락·"게임 참여"로 들어온 roomId
bool TryOpenInviteOverlay(IRoom room); // 미지원 백엔드는 false
```

- `RoomCreateOptions`: `MaxMembers`, `Name`, `Version`.
- `RoomJoinOptions`: `Version`. 방의 버전 문자열과 서수 비교로 다르면 방을 떠나고
  `VersionMismatch`로 실패한다. 호환 규칙(예: major.minor만 비교)은 게임이 버전 문자열로 표현한다.
- 한 백엔드 인스턴스는 동시에 한 방만 가진다. 이미 방에 있으면 `InvalidOperationException`.

### IRoom

- 읽기: `Id`, `Code`, `Name`, `Members`(`IReadOnlyList<RoomMember>`), `MaxMembers`(방장 포함 정원, 모르면 0),
  `LocalMemberId`(백엔드가 알기 전에는 null), `OwnerId`, `IsOwner`, `IsJoinable`(방장의 마지막
  `SetJoinableAsync` 값, 생성 시 true), `GetData(string key)`(없으면 null).
- 조작: `SetJoinableAsync(bool)`, `TransferOwnershipAsync(string memberId)`, `SetDataAsync(string key,
  string value)`(셋 다 방장 전용), `LeaveAsync()`(멱등).
- 이벤트: `MembersChanged`, `OwnerChanged`, `DataChanged`, `Closed`. `Closed`는 `LeaveAsync` 없이 방이
  끝났을 때(강퇴, 로비 소멸, 루프백 호스트 이탈)만 발생한다.
- `DataChanged`는 방 데이터나 `IsJoinable`이 바뀌었을 수 있을 때 인자 없이 발생한다. 받는 쪽이 필요한
  키를 다시 읽는다 — Steam은 어떤 키가 바뀌었는지 알려 주지 않으므로 키를 붙이지 않는다.
- `RoomMember`: `Id`(string — 백엔드별 안정 식별자, Steam은 SteamID 10진 문자열),
  `JoinedAt`(long, 백엔드 기준 시각).
- 이벤트는 메인 스레드에서 발생한다.

### 실패

`RoomException(RoomFailure failure)` — `NotFound`, `Full`, `VersionMismatch`,
`ServiceUnavailable`(플랫폼 미실행·미로그인·시간 초과). 사용자 문구는 게임 몫이다.
입장 잠금된 방은 검색되지 않으므로 `NotFound`로 보고된다.

### RoomOwnerElection

`static string Oldest(IReadOnlyList<RoomMember> members, string excludedId)` — `JoinedAt`이 가장
이른 멤버, 동률이면 `Id` 서수 비교. 후보가 없으면 null. 무할당.

## `com.bun3.unity.rooms.ngo` — 방 ↔ NGO

### INgoRoomTransport

```csharp
string PrepareHost(NetworkManager manager, IRoom room);                  // 호스트 주소(endpoint) 반환
void PrepareClient(NetworkManager manager, IRoom room, string endpoint);
```

- 두 메서드는 `manager.NetworkConfig.NetworkTransport`를 자기 transport로 바꾼다. `room`은 접속 허용
  판단(Steam의 멤버 필터)에 쓴다.
- `UtpRoomTransport` — Unity Transport 구현. 호스트는 `ListenPort`(0이면 빈 포트)로 리슨하고
  `127.0.0.1:<port>`를 반환한다. 클라이언트는 `SetConnectionData`로 접속 대상을 설정한다.

### NgoRoomSession

```csharp
NgoRoomSession(NetworkManager manager, IRoom room, INgoRoomTransport transport, NgoRoomSessionOptions options);
UniTask StartAsync(CancellationToken cancellationToken);
UniTask LeaveAsync();
IRoom Room { get; }
RoomSessionState State { get; }       // Idle, Starting, Connected, Migrating, Failed, Closed
event Action<RoomSessionState> StateChanged;
event Action HostMigrated;
```

- `NgoRoomSessionOptions`: `Func<bool> CanMigrate`(기본 false), `TimeSpan MigrationTimeout`(기본 30초),
  `Func<IReadOnlyList<RoomMember>, string, string> ElectHost`(기본 `RoomOwnerElection.Oldest`).
- NGO 시작·재시작은 세션이 맡는다. `NetworkConfig.ConnectionData` 등 입장 데이터는 게임이
  `StartAsync` 전에 설정한다.

### 방 데이터 키

| 키 | 작성자 | 의미 |
|---|---|---|
| `bun3.hostMember` | 방장 | NGO 호스트의 멤버 ID. 호스트가 방을 떠났는지 판단한다 |
| `bun3.host` | 방장 | NGO 호스트 endpoint (`INgoRoomTransport.PrepareHost` 반환값) |
| `bun3.hostEpoch` | 방장 | 호스트 세대 번호. 호스트를 새로 띄울 때마다 1 증가 |

기록 순서는 `bun3.hostMember` → `bun3.host` → `bun3.hostEpoch`다. 세대가 바뀐 것을 본 참가자는 앞의
두 값도 새 값으로 읽는다.

### 시작

1. 방장: `PrepareHost` → `StartHost` → 세 키 기록(세대 1) → `Connected`.
2. 참가자: `bun3.hostEpoch`가 생길 때까지 대기 → `PrepareClient` → `StartClient` → 연결되면 `Connected`.
3. `StartAsync`는 `Connected`가 되어야 끝난다. 시작하지 못하거나 연결 전에 끊기면(입장 거절 포함)
   NGO를 끄고 `Closed`로 바꾼 뒤 `InvalidOperationException`을 던진다. 방 이탈은 호출자가 한다.
   참가자 대기는 `MigrationTimeout`으로 제한한다.

### 호스트 승계

트리거: 방은 살아 있는데 NGO 호스트 연결이 끊김, 또는 `bun3.hostMember`의 멤버가 방을 떠남.
판정은 한 프레임 미룬다 — 승계가 없는 백엔드(루프백)가 같은 프레임에 방을 닫을 기회를 준다.

1. `CanMigrate()`가 false → NGO 종료, `Closed`. 방 이탈은 게임이 결정한다.
2. true → `Migrating`. 모든 멤버가 NGO를 종료한다 (`ShutdownInProgress` 해제까지 대기).
3. 현재 방장이 `ElectHost(members, 떠난 호스트)`의 결과가 아니면 그 멤버에게 방장을 넘기고
   대기자로 남는다 (Steam은 방장을 임의 선정하므로 필요).
4. 선출된 방장: `PrepareHost` → `StartHost` → 세 키 갱신(세대 + 1) → `Connected`, `HostMigrated`.
5. 나머지: `bun3.hostEpoch`가 이전 값보다 커지고 `bun3.hostMember`가 떠난 호스트가 아니게 될 때까지
   대기 → `PrepareClient` → `StartClient` → `Connected`, `HostMigrated`.
6. `MigrationTimeout` 안에 `Connected`가 아니면 NGO를 끄고 `Failed`. 방에는 남는다.

- NGO 재시작으로 스폰된 NetworkObject는 사라진다. 복원은 게임이 `HostMigrated`에서 한다
  (README 계약에 명시).
- 방장은 방장이 떠날 때와 `SetLobbyOwner`로만 바뀐다. 세션이 아닌 경로로 방장과 NGO 호스트가 어긋나는
  경우는 없으므로 되돌리기 규칙은 두지 않는다 (계획된 퇴장의 이전과 충돌한다).

### 계획된 퇴장

`LeaveAsync()`: 방장이고 `CanMigrate()`이고 멤버가 2명 이상이면 `ElectHost(members, 자신)`에게
방장을 넘긴 뒤 NGO 종료 → 방 이탈. 넘기기 실패는 퇴장을 막지 않는다. 멱등. 남은 멤버는 NGO 끊김으로
승계를 시작하고, 이미 방장이 된 선출자가 바로 호스트가 된다.

### LoopbackRoomBackend

에디터·Multiplayer Play Mode처럼 같은 PC의 여러 프로세스(같은 플랫폼 계정)에서 쓰는 백엔드.

- 생성자: `LoopbackRoomBackend(NetworkManager manager, UtpRoomTransport transport)`. 방 데이터는
  서비스 없이 프로세스 로컬에만 있다.
- `CreateAsync`: 빈 UDP 포트를 잡아 `transport.ListenPort`에 설정하고 `RoomCode.FromValue(port)`를
  코드로 쓴다. 코드가 세션 시작 전에 확정돼야 하므로 포트를 생성 시점에 고정한다(같은 패키지 내부
  결합). `bun3.host`는 세션이 `PrepareHost` 결과로 기록한다.
- `JoinByCodeAsync`: 코드를 포트로 되돌려 `bun3.host = 127.0.0.1:<port>`, `bun3.hostMember = local-0`,
  세대 1이 미리 채워진 방 객체를 만든다. 공유 파일·서버 없음. 포트 범위(1~65535) 밖이면 `NotFound`.
  방 ID는 코드 문자열이다(`JoinByIdAsync`도 코드를 받는다).
- 멤버: 호스트는 NGO 연결 목록에서 `local-{clientId}`, `JoinedAt`은 clientId. 클라이언트는 호스트
  (`local-0`)와 자신만 안다. `LocalMemberId`는 NGO 시작 후 확정된다.
- 방장 이전은 아무것도 하지 않는다. 클라이언트의 방은 NGO 클라이언트가 멈추면(호스트 이탈·접속 실패)
  `Closed`를 낸다.
- 승계·초대·버전 검사는 지원하지 않는다 (`TryOpenInviteOverlay` false). 방 이름은 호스트만 안다.
- 정원과 잠금도 호스트만 안다. 클라이언트는 `MaxMembers` 0, `IsJoinable` true로 본다. `DataChanged`는
  호스트의 `SetDataAsync`·`SetJoinableAsync`에서만 발생한다.

## `com.bun3.unity.steam` — Steam 수명 관리

```csharp
static SteamInitResult SteamRuntime.Initialize(SteamRuntimeOptions options, out string error);
static bool SteamRuntime.IsInitialized { get; }
static string SteamRuntime.PersonaName { get; }
static void SteamRuntime.QuitWithError(string title, string message);
static void SteamRuntime.Shutdown();
```

- `SteamRuntimeOptions`: `uint AppId`, `bool RestartThroughSteam`.
- 결과: `Ready`, `RestartingThroughSteam`(게임은 즉시 종료), `Unavailable`(`error`에 Steam의 영어 메시지).
- `RestartThroughSteam`이면 `SteamAPI.RestartAppIfNecessary`를 먼저 호출한다. 에디터에서는 생략.
- 그다음 환경 변수 `SteamAppId`·`SteamGameId`를 App ID로 설정한다. 에디터와 Steam 밖에서 띄운 개발
  빌드도 `steam_appid.txt` 없이 초기화된다. 재실행 판정을 흐리지 않도록 `RestartAppIfNecessary` 뒤에 한다.
- 초기화는 `SteamAPI.InitEx(out string)`(Steamworks.NET 2025.165.0 확인)를 쓴다. `DllNotFoundException`은
  `Unavailable`로 바꾼다.
- `PersonaName`: Steam 프로필 이름(`SteamFriends.GetPersonaName`). 초기화 전에는 빈 문자열. 게임 규칙에
  맞추는 일(길이·금지 문자)은 게임 몫이다.
- `QuitWithError(title, message)`: `Unavailable`일 때 플레이어에게 알리고 끝낸다. 오류 로그를 남긴 뒤
  Windows 플레이어는 OS 오류 창(`MessageBoxW`, 맨 위·포그라운드)을 띄우고, 창을 닫으면 `Application.Quit`.
  에디터는 대화 상자 후 Play 모드를 끝낸다. batch mode와 그 밖의 플랫폼은 창 없이 로그만 남기고 끝낸다
  (무인 실행이 창에 막히지 않게). 문구는 게임이 넘긴다.
- `SteamAPI.RunCallbacks`는 core의 `PlayerLoopSystemHelper`로 Update 단계에 등록한다.
  `Application.quitting`에서 `Shutdown`. 도메인 리로드 비활성 환경을 위해 정적 상태를
  `SubsystemRegistration`에서 초기화한다.

## `com.bun3.unity.steam.ngo` — SteamSocketsTransport

`SteamSocketsTransport : NetworkTransport` (MonoBehaviour)

- 호스트: `CreateListenSocketP2P(virtualPort)` + poll group. 클라이언트:
  `ConnectP2P(TargetSteamId, virtualPort)`. 시작 시 `SteamNetworkingUtils.InitRelayNetworkAccess()`.
- 공개 설정: `ulong TargetSteamId`, `int VirtualPort`(기본 0),
  `Func<ulong, bool> AcceptFilter`(기본: 모두 허용).
- 연결 상태 콜백(`SteamNetConnectionStatusChangedCallback_t`): 들어오는 연결은 `AcceptFilter`가
  true일 때만 `AcceptConnection`, 아니면 닫는다. 연결/해제를 NGO 이벤트 큐로 변환한다.
- 전송 매핑: `Reliable`·`ReliableSequenced`·`ReliableFragmentedSequenced` →
  `k_nSteamNetworkingSend_Reliable`, `Unreliable`·`UnreliableSequenced` →
  `k_nSteamNetworkingSend_Unreliable`. 둘 다 `NoNagle` 추가.
- 수신: 호스트는 poll group, 클라이언트는 연결 단위로 받는다. 수신 버퍼와 메시지 포인터 배열은
  재사용하고 메시지는 즉시 release한다 (프레임당 할당 없음).
- 폴링 transport다: 연결·해제는 큐에 쌓고 `PollEvent`가 큐 → 수신 순서로 돌려준다.
- Steam은 연결의 첫 메시지를 `Connected` 콜백보다 먼저 줄 수 있다. 호스트가 모르는 연결의 데이터를
  받으면 원격 SteamID를 조회해 `Connect`를 먼저 돌려주고 그 데이터는 다음 호출에서 돌려준다. 늦게 온
  `Connected` 콜백은 무시한다 (NGO 접속 요청 유실 방지).
- 클라이언트 쪽 호스트 transport ID는 0(`ServerClientId`), 호스트 쪽 클라이언트 ID는 연결 핸들이다.
- `GetCurrentRtt`는 연결 실시간 상태의 ping을 쓴다. `bool TryGetSteamId(ulong transportId, out ulong steamId)`.
- 테스트 seam: Steam 소켓 호출을 감싼 internal 인터페이스. 실구현은 `SteamNetworkingSockets`
  위임, 테스트는 가짜 구현 (`InternalsVisibleTo` 테스트 어셈블리).

## `com.bun3.unity.rooms.steam` — SteamLobbyBackend

`SteamLobbyBackend : IRoomBackend, IDisposable` — `SteamRuntime.IsInitialized`가 아니면 모든 작업이
`ServiceUnavailable`.

### 로비 데이터 키

| 키 | 종류 | 작성자 | 의미 |
|---|---|---|---|
| `bun3.code` | 로비 | 방장 | 방 코드 |
| `bun3.ver` | 로비 | 방장 | 게임 버전 |
| `bun3.name` | 로비 | 방장 | 방 이름 |
| `bun3.open` | 로비 | 방장 | `0`이면 잠김. 없거나 `1`이면 열림 → `IRoom.IsJoinable` |
| `bun3.joined` | 멤버 | 본인 | 입장 시각 (`SteamUtils.GetServerRealTime`) → `RoomMember.JoinedAt` |

### 동작

- 생성: 코드 생성 → 같은 코드 검색 → 겹치면 재생성(기본 최대 5회, 초과 시 `ServiceUnavailable`) →
  `CreateLobby(k_ELobbyTypeInvisible, MaxMembers)` → 로비 데이터 기록.
- 코드 참가: `AddRequestLobbyListStringFilter(bun3.code, Equal)`,
  `AddRequestLobbyListDistanceFilter(Worldwide)`, `AddRequestLobbyListFilterSlotsAvailable(1)`,
  `AddRequestLobbyListResultCountFilter(1)` → `RequestLobbyList` → `JoinLobby` → 버전 검사.
  결과 0건 `NotFound`, 입장 응답 Full → `Full`, DoesntExist/NotAllowed/Limited → `NotFound`.
- ID 참가: `JoinLobby` → 버전 검사.
- 응답: `JoinLobby`의 Full → `Full`, Error → `ServiceUnavailable`, 그 밖의 실패 → `NotFound`.
  Steam 호출 결과가 15초 안에 오지 않으면 `ServiceUnavailable`.
- 정원: `GetLobbyMemberLimit`.
- 잠금: 방장이 `SetLobbyJoinable(false)` + `bun3.open = 0`. 해제는 반대. 멤버 전원이 `bun3.open`을 보고
  자기 리치 프레즌스 `connect`를 지우거나 되살린다.
- 방장 이전: `SetLobbyOwner`. `LobbyDataUpdate_t`·`LobbyChatUpdate_t` 콜백에서 멤버·방장·이름·잠금을 다시
  읽어 바뀐 것만 `MembersChanged`, `OwnerChanged`로 알린다. 로비 자체의 `LobbyDataUpdate_t`(멤버 ID =
  로비 ID)마다 `DataChanged`를 낸다 — 멤버 데이터 변경은 내지 않는다. 강퇴·로비 소멸·자신의 이탈
  알림은 `Closed`.
- 초대·게임 참여: 방이 열려 있는 동안 각 멤버가 `SteamFriends.SetRichPresence("connect", "+connect_lobby <id>")`.
  `GameLobbyJoinRequested_t`, `GameRichPresenceJoinRequested_t`, 실행 인자 `+connect_lobby <id>`를
  `JoinRequested`로 전달한다. 실행 인자는 구독할 수 있도록 다음 프레임에 낸다.
  `TryOpenInviteOverlay` → `ActivateGameOverlayInviteDialog`.
- 테스트 seam: Steam 로비·친구 호출과 콜백을 감싼 internal 인터페이스와 가짜 구현.

알려진 한계:

- `bun3.joined`는 본인이 기록하므로 악의적 멤버가 이른 시각을 주장해 대기실 승계에서 선출될 수 있다.
  로비 멤버로 이미 허용된 사람만 가능하고 영향이 대기실 호스트 순서에 한정돼 수용한다.
- 잠긴 로비는 검색되지 않으므로 생성 시 코드 중복 검사에서 빠진다. 같은 코드가 생길 확률(약 10억분의
  1)을 수용한다.

## `com.bun3.unity.rooms.steam.ngo` — SteamRoomTransport

`SteamRoomTransport : INgoRoomTransport`

- `PrepareHost(manager, room)`: `AcceptFilter`를 "`room`의 현재 멤버인 SteamID"로 설정, 로컬 SteamID
  문자열(`room.LocalMemberId`) 반환.
- `PrepareClient`: endpoint를 SteamID로 파싱해 `TargetSteamId`에 설정. 0이거나 숫자가 아니면
  `FormatException`.

## 테스트 전략

- `rooms` EditMode: `RoomCode`(생성 알파벳·길이, 정규화, 값 왕복, 경계값), `RoomOwnerElection`
  (최고참, 동률, 제외, 빈 목록).
- `rooms.ngo`
  - PlayMode: 한 프로세스의 NetworkManager 두 개로 `LoopbackRoomBackend` 코드 입장.
  - PlayMode: 테스트 어셈블리 내 메모리 가짜 백엔드로 승계 — 최고참 선출, 비최고참 방장의 이전,
    세대 번호 갱신과 재접속, 시간 초과 `Failed`, `CanMigrate` false 시 `Closed`, 계획된 퇴장 시 이전.
    상태 전이는 이 시나리오들이 함께 검증한다.
- `steam.ngo` EditMode: 가짜 소켓으로 연결·해제 이벤트, ID 매핑, 전송 매핑, 허용 필터, 버퍼 재사용.
- `rooms.steam` EditMode: 가짜 로비로 코드 충돌 재시도, 코드 참가, 실패 매핑, 버전 불일치,
  방장 변경 이벤트, 정원, 잠금과 리치 프레즌스(방장·멤버), 로비 데이터에만 반응하는 `DataChanged`,
  실행 인자 파싱.
- `rooms.ngo` PlayMode 루프백 테스트가 호스트·클라이언트의 정원·잠금·`DataChanged`도 확인한다.
- `steam` EditMode: Sync 메뉴의 define 추가·보존·제거 (define 계산을 순수 함수로 분리해 검증한다.
  Steamworks.NET 감지는 설치 환경에 따라 다르므로 수동 확인). `PersonaName`·`QuitWithError`는 Steam
  클라이언트·OS 창이 필요해 소비 게임의 수동 체크리스트가 확인한다.
- 실제 Steam 연결은 무인 자동화가 불가능하다 — 소비 게임이 PC 2대 수동 체크리스트를 둔다.
- 키트 Unity 프로젝트(`unity/`) manifest에 NGO 2.13.1, Unity Transport, Steamworks.NET을 추가하고
  `BUN3_STEAMWORKS`를 켜서 패키지 테스트를 돌린다. 가짜 seam 테스트는 Steam 클라이언트 없이 실행된다.

## 공개 여부와 공개 방 목록 (2026-10-15 추가)

첫 소비자(jurassic-paradise)가 방을 공개/비공개로 나눈다. 두 종류 모두 코드로 들어갈 수 있고, 공개 방은 목록에서도 고를 수 있다. 방은 비공개로 만들어지고 방장만 바꾼다. 결정 배경과 화면 규칙은 소비 게임 스펙(`jurassic-paradise/docs/superpowers/specs/2026-10-15-room-access-level-design.md`)에 있다.

### 코어 API 추가

```csharp
// IRoom
bool IsPublic { get; }                     // 생성 시 false
UniTask SetPublicAsync(bool isPublic);     // 방장 전용. 바뀌면 DataChanged
// IRoomBackend
UniTask<IReadOnlyList<RoomListing>> ListPublicAsync(RoomListOptions options, CancellationToken cancellationToken = default);
```

- `RoomListOptions`(struct): `Version`(생성 버전과 서수 비교), `MaxResults`(1~50, 0 이하는 50, 50 초과는 50), `DataKeys`(목록과 함께 읽을 방 데이터 키, null이면 없음).
- `RoomListing`(sealed class): `Id`(`JoinByIdAsync`에 넘김), `Name`, `MemberCount`, `MaxMembers`(모르면 0), `GetData(string key)`(요청한 키만, 없으면 null).
- 돌려주는 방: 공개, 열림(`IsJoinable`), 버전 일치, 빈자리 있음(정원을 모르면 포함). 정렬은 백엔드 순서다.
- 방에 있는 동안에도 부를 수 있다. 자기 방이 목록에 들어갈 수 있으며 거르는 것은 게임 몫이다.
- 목록을 받은 뒤 방이 차거나 닫히면 `JoinByIdAsync`가 기존대로 `Full`·`NotFound`로 실패한다.
- 서비스가 없거나 응답하지 않으면 `RoomException(ServiceUnavailable)`.

### Steam

| 키 | 종류 | 작성자 | 의미 |
|---|---|---|---|
| `bun3.public` | 로비 | 방장 | `1`이면 공개. 없거나 `0`이면 비공개 → `IRoom.IsPublic` |
| `bun3.members` | 로비 | 방장 | 현재 인원 |
| `bun3.max` | 로비 | 방장 | 정원 |

- 생성 때 `bun3.public = 0`, `bun3.max`, `bun3.members = 1`을 함께 기록한다.
- 로비 종류는 `k_ELobbyTypeInvisible` 그대로다. 비공개 방도 코드 검색으로 찾힌다.
- 인원·정원을 로비 데이터로 두는 이유: 참가하지 않은 로비에서 `GetNumLobbyMembers`가 값을 주는지 공식 문서로 확인되지 않는다. 방장은 `MembersChanged`와 방장이 되었을 때 `bun3.members`를 다시 쓴다.
- 목록: `bun3.public = 1`, `bun3.ver = Version` 문자열 필터, 빈자리 1, 전 세계, 결과 수 `MaxResults` → `RequestLobbyList` → 결과마다 `bun3.name`, `bun3.members`, `bun3.max`, 요청한 키를 읽는다. 인원이 숫자가 아니거나 코드가 올바르지 않은 로비는 건너뛴다.
- 닫힌 로비(`SetLobbyJoinable(false)`)는 Steam 검색에 나오지 않으므로(코드 참가와 같은 성질) 열림은 따로 거르지 않는다.
- 가짜 seam: `ISteamLobbyApi`의 검색을 여러 문자열 필터와 결과 수를 받는 형태로 일반화한다(코드 검색도 같은 함수를 쓴다).

### 루프백

같은 PC의 여러 프로세스(에디터, Multiplayer Play Mode)가 서로의 공개 방을 보도록 파일 등록부를 둔다.

- 위치: `Application.temporaryCachePath/bun3-rooms` (회사·제품별 경로라 같은 프로젝트의 프로세스끼리 공유된다). 생성자에서 다른 경로를 받을 수 있다(테스트용).
- 공개 방의 호스트는 `<포트>.json`(코드, 이름, 버전, 인원, 정원, 열림, 방 데이터)을 임시 파일에 쓴 뒤 옮겨 바꾼다. 공개가 된 순간과 인원·데이터·열림이 바뀔 때, 그리고 2초마다 쓴다.
- 비공개로 바꾸거나, 방을 떠나거나, 방이 닫히면 파일을 지운다.
- 목록은 등록부를 읽어 6초 넘게 갱신되지 않은 파일(꺼진 프로세스)과 읽을 수 없는 파일을 건너뛰고 Steam과 같은 조건으로 거른다.
- 클라이언트의 방은 호스트 데이터를 모르므로, 자기 코드의 등록 파일이 살아 있는지를 2초마다 확인해 `IsPublic`을 정하고 바뀌면 `DataChanged`를 낸다. 비공개 전환이 클라이언트에 반영되기까지 최대 2초 걸린다.
- 같은 PC 안에서만 보인다.

### 테스트 추가

- `rooms.steam` EditMode(가짜 로비): 생성 때 기록하는 키, 공개 전환과 `DataChanged`, 목록 검색 필터와 결과 수, 목록 결과의 이름·인원·정원·요청 키, 깨진 로비 건너뛰기, 인원 변경·방장 변경 때 `bun3.members` 갱신, 방장이 아닐 때 `SetPublicAsync` 거부.
- `rooms.ngo` PlayMode(루프백, 임시 등록부): 공개 → 목록에 보임 → 비공개 → 사라짐, 오래된 파일·버전 다른 방·꽉 찬 방·닫힌 방 제외, 클라이언트 `IsPublic` 반영, 방 이탈 시 파일 삭제.

## 범위 밖

- 빠른 매칭, 지역 선택, 공개 방 목록의 걸러보기·정렬.
- 승계 데이터 전달 채널 (첫 소비자는 스냅샷 캐시로 해결).
- 매치 중 재접속, 전용 서버(SDR 호스팅 서버 인증서).
- Netcode for Entities·Mirror 등 다른 넷코드 어댑터.

## 백로그 기록

- `com.bun3.unity.audio.dissonance.netcode` → `.ngo` 개명. 소비 게임의 manifest와 define
  (`BUN3_DISSONANCE_NFGO`) 변경을 함께 해야 하는 별도 작업.
