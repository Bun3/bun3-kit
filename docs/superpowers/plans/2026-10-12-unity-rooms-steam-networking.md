# Unity 방(Room)·Steam 네트워킹 패키지 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 방 코드·멤버·방장 승계를 넷코드·플랫폼과 무관하게 제공하고, NGO 어댑터와 Steam(SDR P2P + 로비) 어댑터를 bun3-kit Unity 패키지 6개로 만든다.

**Architecture:** `rooms`(코어) ← `rooms.ngo`(NGO 세션·승계·루프백) / `steam`(수명) ← `steam.ngo`(NGO transport) / `rooms.steam`(로비 백엔드) / `rooms.steam.ngo`(로비 멤버 필터 transport). Steam 계열은 `BUN3_STEAMWORKS` define으로 막고, Steam 호출은 internal seam 뒤에 둬서 가짜 구현으로 Steam 클라이언트 없이 테스트한다.

**Tech Stack:** Unity 6000.3.14f1(키트 프로젝트), NGO 2.13.1(키트에 이미 전이 설치), Unity Transport, UniTask, Steamworks.NET 2025.165.0(UPM git), NUnit/Unity Test Framework 1.6.

**Spec:** `docs/superpowers/specs/2026-10-12-unity-rooms-steam-networking-design.md` — 실행자는 플랜과 스펙을 함께 읽는다.

## Global Constraints

- 패키지 위치: `unity/Packages/com.bun3.unity.<name>/` (embedded 패키지 — 키트 Test Runner가 자동으로 잡는다).
- 패키지 6개: `com.bun3.unity.rooms`, `com.bun3.unity.rooms.ngo`, `com.bun3.unity.steam`, `com.bun3.unity.steam.ngo`, `com.bun3.unity.rooms.steam`, `com.bun3.unity.rooms.steam.ngo`. 모두 `"version": "0.1.0"`, `"unity": "6000.3"`.
- 네트워크 라이브러리 접미사는 `.ngo`다 (`.netcode` 금지 — 레포 `CLAUDE.md`).
- 어셈블리 이름 = 루트 네임스페이스 = `Bun3.Unity.Rooms`, `Bun3.Unity.Rooms.Ngo`, `Bun3.Unity.Steam`(+ `Bun3.Unity.Steam.Editor`), `Bun3.Unity.Steam.Ngo`, `Bun3.Unity.Rooms.Steam`, `Bun3.Unity.Rooms.Steam.Ngo`. 폴더 = 네임스페이스, 타입 종류별 폴더 금지.
- Steam 계열 런타임·테스트 어셈블리: `"defineConstraints": ["BUN3_STEAMWORKS"]`(테스트는 `UNITY_INCLUDE_TESTS`도), 런타임 `"includePlatforms": ["Editor", "LinuxStandalone64", "macOSStandalone", "WindowsStandalone64"]`. `Bun3.Unity.Steam.Editor`는 게이트 없음.
- Steamworks.NET은 패키지 `dependencies`에 넣지 않는다. 키트 프로젝트 manifest에만 `"com.rlabrecque.steamworks.net": "https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net"`(핀 없음). 어셈블리 이름 `com.rlabrecque.steamworks.net`.
- 코드·주석·README·패키지 메타데이터는 영어만. public 멤버 전부 영어 XML 문서. 레거시·타 패키지 언급 금지. 주석은 코드가 못 보여 주는 제약만.
- 컴파일 경고 0 (우리 패키지 경로 기준). 프레임마다 도는 경로(transport 수신·송신)는 할당 0.
- `.meta`는 Unity가 생성한다. 손으로 쓰지 않고, Unity 실행 후 생성된 `.meta`를 함께 커밋한다.
- 커밋: gitmoji, 그리고 `git commit -m "<제목>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"` 이중 플래그. 푸시는 사용자 승인 후(Task 8).
- 메인 체크아웃 `E:/Projects/bun3-kit`은 건드리지 않는다. 모든 작업은 워크트리 `E:/Projects/orca/workspace/bun3-kit/jp-steam-networking`에서 한다.

## Review Focus

자동 테스트가 직접 재현하지 못하지만 사용자가 겪을 가능성이 큰 조건. 각 줄의 고정 테스트를 표시한 Task에 넣었다.

1. **같은 PC의 여러 인스턴스(같은 Steam 계정)** — Steam P2P로 자기 자신에게 붙을 수 없다. 기대: 루프백 백엔드로 같은 PC 테스트가 된다 → Task 2 `TypedCodeConnectsToTheLoopbackHost`.
2. **Steam 미실행·미로그인** — 모든 방 작업이 즉시 `ServiceUnavailable`, transport는 시작하지 않는다(멈추지 않음) → Task 4 `NothingStartsWithoutSteam`, Task 5 `UnavailableSteamAndSecondRoomAreRejected`. 실제 Steam 호출 결과 지연은 15초 제한(코드 리뷰 대상).
3. **연결의 첫 메시지가 `Connected` 콜백보다 먼저 도착** — NGO 접속 요청이 사라지면 참가자가 영원히 접속 대기한다. 기대: `Connect` 후 같은 데이터 → Task 4 `FirstMessageBeforeConnectedCallbackStillFollowsAConnectEvent`.
4. **승계 중 아무도 호스트가 되지 못함(방장 이전 실패)** — 기대: 제한 시간 뒤 `Failed`, NGO 정지, 방은 유지 → Task 2 `MigrationFailsWhenNoHostAppearsInTime`.
5. **나가는 방장이 방을 넘겼는데 새 방장이 이전을 되돌림** — 승계가 꼬인다. 기대: 되돌리기 규칙 없음, 선출자가 바로 호스트 → Task 2 `LeavingOwnerHandsTheRoomToTheOldestMember`.
6. **매치 중(잠긴 방) 방장이 아닌 멤버의 친구가 "게임 참여"를 누름** — 기대: 멤버도 잠금을 보고 리치 프레즌스를 지워 버튼이 보이지 않는다 → Task 5 `MembersFollowTheLockAndDataChangesComeFromTheLobby`.

## 테스트 실행 (모든 Task 공통)

키트 Unity 프로젝트를 다른 에디터가 열고 있으면 batchmode가 실패한다. 첫 실행은 `Library`를 새로 만들어 10분을 넘길 수 있다 — 그때는 `run_in_background`로 돌리고 끝날 때까지 기다린다. 아래 블록의 첫 줄(`$p` 플랫폼, `$a` 어셈블리 목록(세미콜론 구분), `$n` 결과 이름)만 Step마다 바꾼다.

```powershell
$p='EditMode'; $a='Bun3.Unity.Rooms.Tests'; $n='rooms-edit'
$proj='E:\Projects\orca\workspace\bun3-kit\jp-steam-networking\unity'
& 'E:\Unitys\6000.3.14f1\Editor\Unity.exe' -batchmode -projectPath $proj -runTests -testPlatform $p -assemblyNames $a -testResults "$proj\Logs\$n.xml" -logFile "$proj\Logs\$n.log" | Out-Null
if (Test-Path "$proj\Logs\$n.xml") { (Select-String -Path "$proj\Logs\$n.xml" -Pattern '<test-run [^>]*>').Matches.Value } else { 'NO RESULT XML' }
Select-String -Path "$proj\Logs\$n.log" -Pattern 'com\.bun3\.unity\.(rooms|steam)[^\r\n]*(error|warning) CS' | Select-Object -First 20
```

통과 기준: `<test-run ... result="Passed" ... failed="0">`이고 마지막 명령 출력이 비어 있다(우리 패키지의 오류·경고 0). `NO RESULT XML`이면 컴파일 오류다 — 로그에서 `error CS`를 찾는다.

## 파일 구조

| 파일 | 책임 |
|---|---|
| `com.bun3.unity.rooms/Runtime/RoomCode.cs` | 6자 방 코드 생성·정규화·값 변환 |
| `com.bun3.unity.rooms/Runtime/IRoom.cs` | `IRoom`, `RoomMember` |
| `com.bun3.unity.rooms/Runtime/IRoomBackend.cs` | `IRoomBackend`, `RoomCreateOptions`, `RoomJoinOptions` |
| `com.bun3.unity.rooms/Runtime/RoomException.cs` | `RoomException`, `RoomFailure` |
| `com.bun3.unity.rooms/Runtime/RoomOwnerElection.cs` | 최고참 선출 |
| `com.bun3.unity.rooms.ngo/Runtime/INgoRoomTransport.cs` | 방 ↔ NGO transport 준비 계약 |
| `com.bun3.unity.rooms.ngo/Runtime/UtpRoomTransport.cs` | 루프백 UTP 구현, 빈 포트 |
| `com.bun3.unity.rooms.ngo/Runtime/NgoRoomSession.cs` | `NgoRoomSession`, `NgoRoomSessionOptions`, `RoomSessionState` — 시작·승계·퇴장 |
| `com.bun3.unity.rooms.ngo/Runtime/LoopbackRoomBackend.cs` | 같은 PC 백엔드(+ private `LoopbackRoom`) |
| `com.bun3.unity.steam/Runtime/SteamRuntime.cs` | `SteamRuntime`, `SteamRuntimeOptions`, `SteamInitResult` |
| `com.bun3.unity.steam/Editor/SteamSdkSetup.cs` | `BUN3_STEAMWORKS` Sync/Disable 메뉴 |
| `com.bun3.unity.steam.ngo/Runtime/ISteamSockets.cs` | internal Steam 소켓 seam + `SteamConnectionStatus` |
| `com.bun3.unity.steam.ngo/Runtime/SteamSockets.cs` | seam 실구현(Steamworks.NET 위임, 수신 배치) |
| `com.bun3.unity.steam.ngo/Runtime/SteamSocketsTransport.cs` | NGO `NetworkTransport` |
| `com.bun3.unity.rooms.steam/Runtime/ISteamLobbyApi.cs` | internal 로비·친구 seam |
| `com.bun3.unity.rooms.steam/Runtime/SteamLobbyApi.cs` | seam 실구현(콜백·CallResult) |
| `com.bun3.unity.rooms.steam/Runtime/SteamLobbyBackend.cs` | 로비 백엔드 |
| `com.bun3.unity.rooms.steam/Runtime/SteamLobbyRoom.cs` | 로비 하나의 `IRoom` |
| `com.bun3.unity.rooms.steam.ngo/Runtime/SteamRoomTransport.cs` | 멤버 필터 + SteamID endpoint |

---

### Task 0: 설계 문서 커밋

**Files:**
- Commit: `CLAUDE.md`(이미 수정됨 — `.ngo` 접미사 규칙), `docs/superpowers/specs/2026-10-12-unity-rooms-steam-networking-design.md`, `docs/superpowers/plans/2026-10-12-unity-rooms-steam-networking.md`

- [ ] **Step 1: 변경 확인**

Run: `git -C E:/Projects/orca/workspace/bun3-kit/jp-steam-networking status --short`
Expected: ` M CLAUDE.md`, `?? docs/superpowers/specs/2026-10-12-unity-rooms-steam-networking-design.md`, `?? docs/superpowers/plans/2026-10-12-unity-rooms-steam-networking.md` 세 줄뿐.

- [ ] **Step 2: Commit**

```bash
cd E:/Projects/orca/workspace/bun3-kit/jp-steam-networking
git add CLAUDE.md docs/superpowers/specs/2026-10-12-unity-rooms-steam-networking-design.md docs/superpowers/plans/2026-10-12-unity-rooms-steam-networking.md
git commit -m "📝 Add rooms and Steam networking package design and plan" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 1: `com.bun3.unity.rooms` — 방 코드·방 계약·최고참 선출

**Files:**
- Create: `unity/Packages/com.bun3.unity.rooms/package.json`, `README.md`, `CHANGELOG.md`
- Create: `unity/Packages/com.bun3.unity.rooms/Runtime/Bun3.Unity.Rooms.asmdef`, `RoomCode.cs`, `IRoom.cs`, `IRoomBackend.cs`, `RoomException.cs`, `RoomOwnerElection.cs`
- Test: `unity/Packages/com.bun3.unity.rooms/Tests/Editor/Bun3.Unity.Rooms.Tests.asmdef`, `RoomCodeTests.cs`, `RoomOwnerElectionTests.cs`

**Interfaces:**
- Consumes: 없음 (UniTask만)
- Produces (이후 모든 Task가 사용):
  - `readonly struct RoomCode : IEquatable<RoomCode>` — `const int Length = 6`, `const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"`, `const uint MaxValue`, `uint Value`, `static RoomCode Generate()`, `static RoomCode FromValue(uint)`, `static bool TryNormalize(ReadOnlySpan<char>, out RoomCode)`, `bool TryFormat(Span<char>, out int)`, `ToString()`
  - `readonly struct RoomMember(string id, long joinedAt)` — `Id`, `JoinedAt`
  - `interface IRoom` — `Id`, `Code`, `Name`, `Members`, `int MaxMembers`, `LocalMemberId`, `OwnerId`, `IsOwner`, `bool IsJoinable`, `string GetData(string)`, `UniTask SetDataAsync(string, string)`, `UniTask SetJoinableAsync(bool)`, `UniTask TransferOwnershipAsync(string)`, `UniTask LeaveAsync()`, `event Action MembersChanged`, `event Action OwnerChanged`, `event Action DataChanged`, `event Action Closed`
  - `interface IRoomBackend` — `CreateAsync(RoomCreateOptions, CancellationToken = default)`, `JoinByCodeAsync(RoomCode, RoomJoinOptions, CancellationToken = default)`, `JoinByIdAsync(string, RoomJoinOptions, CancellationToken = default)` (모두 `UniTask<IRoom>`), `event Action<string> JoinRequested`, `bool TryOpenInviteOverlay(IRoom)`
  - `struct RoomCreateOptions { int MaxMembers; string Name; string Version; }`, `struct RoomJoinOptions { string Version; }`
  - `enum RoomFailure { NotFound, Full, VersionMismatch, ServiceUnavailable }`, `sealed class RoomException(RoomFailure)` — `Failure`
  - `static string RoomOwnerElection.Oldest(IReadOnlyList<RoomMember> members, string excludedId)`

- [ ] **Step 1: 패키지 골격과 실패하는 테스트 작성**

`unity/Packages/com.bun3.unity.rooms/package.json`:

```json
{
  "name": "com.bun3.unity.rooms",
  "displayName": "Bun3 Rooms",
  "version": "0.1.0",
  "unity": "6000.3",
  "description": "Netcode- and platform-agnostic multiplayer rooms: short room codes, members, ownership and host election.",
  "author": {
    "name": "Bun3",
    "url": "https://github.com/Bun3",
    "email": "bun3.dev@gmail.com"
  },
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"
  }
}
```

`unity/Packages/com.bun3.unity.rooms/Runtime/Bun3.Unity.Rooms.asmdef`:

```json
{
    "name": "Bun3.Unity.Rooms",
    "rootNamespace": "Bun3.Unity.Rooms",
    "references": [ "UniTask" ],
    "autoReferenced": true
}
```

`unity/Packages/com.bun3.unity.rooms/Tests/Editor/Bun3.Unity.Rooms.Tests.asmdef`:

```json
{
  "name": "Bun3.Unity.Rooms.Tests",
  "rootNamespace": "Bun3.Unity.Rooms.Tests",
  "references": ["Bun3.Unity.Rooms", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"]
}
```

`unity/Packages/com.bun3.unity.rooms/Tests/Editor/RoomCodeTests.cs`:

```csharp
using System;
using NUnit.Framework;

namespace Bun3.Unity.Rooms.Tests
{
    public class RoomCodeTests
    {
        [Test]
        public void GeneratedCodesUseTheAlphabetAndRoundTripThroughInput()
        {
            for (int i = 0; i < 1000; i++)
            {
                var code = RoomCode.Generate();
                string text = code.ToString();
                Assert.That(text, Has.Length.EqualTo(RoomCode.Length));
                foreach (char c in text)
                    Assert.That(RoomCode.Alphabet.IndexOf(c), Is.GreaterThanOrEqualTo(0), text);
                Assert.That(RoomCode.TryNormalize(text, out var parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(code));
            }
        }

        [TestCase(" abc234 ", "ABC234")]
        [TestCase("zzzz99", "ZZZZ99")]
        public void InputIsTrimmedAndUpperCased(string input, string expected)
        {
            Assert.That(RoomCode.TryNormalize(input, out var code), Is.True);
            Assert.That(code.ToString(), Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("ABC23")]
        [TestCase("ABC2345")]
        [TestCase("ABC20O")]
        [TestCase("ABC21I")]
        [TestCase("AB C23")]
        public void MalformedInputIsRejected(string input) =>
            Assert.That(RoomCode.TryNormalize(input, out _), Is.False);

        [Test]
        public void NullInputIsRejected() => Assert.That(RoomCode.TryNormalize(null, out _), Is.False);

        [TestCase(0u, "AAAAAA")]
        [TestCase(RoomCode.MaxValue, "999999")]
        [TestCase(17973u, "AAATTX")]
        public void ValuesRoundTripThroughText(uint value, string expected)
        {
            var code = RoomCode.FromValue(value);
            Assert.That(code.ToString(), Is.EqualTo(expected));
            Assert.That(RoomCode.TryNormalize(expected, out var parsed), Is.True);
            Assert.That(parsed.Value, Is.EqualTo(value));
        }

        [Test]
        public void ValuesAboveThirtyBitsAreRejected() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomCode.FromValue(RoomCode.MaxValue + 1));

        [Test]
        public void TryFormatNeedsRoomForSixCharacters()
        {
            var code = RoomCode.FromValue(1);
            var small = new char[5];
            Assert.That(code.TryFormat(small, out int written), Is.False);
            Assert.That(written, Is.EqualTo(0));
            var exact = new char[6];
            Assert.That(code.TryFormat(exact, out written), Is.True);
            Assert.That(new string(exact), Is.EqualTo("AAAAAB"));
            Assert.That(written, Is.EqualTo(6));
        }
    }
}
```

기대값 근거: `17973 = 17×32² + 17×32 + 21` → 문자 인덱스 `[0,0,0,17,17,21]`. 알파벳을 0부터 세면 17 = `T`, 21 = `X` → `"AAATTX"`. `MaxValue`는 모든 자리가 31(`9`)이다.

`unity/Packages/com.bun3.unity.rooms/Tests/Editor/RoomOwnerElectionTests.cs`:

```csharp
using NUnit.Framework;

namespace Bun3.Unity.Rooms.Tests
{
    public class RoomOwnerElectionTests
    {
        private static RoomMember M(string id, long joinedAt) => new RoomMember(id, joinedAt);

        [Test]
        public void EarliestJoinWins() =>
            Assert.That(RoomOwnerElection.Oldest(new[] { M("a", 5), M("b", 1), M("c", 3) }, null), Is.EqualTo("b"));

        [Test]
        public void TiesBreakByOrdinalId() =>
            Assert.That(RoomOwnerElection.Oldest(new[] { M("b", 1), M("a", 1), M("A", 1) }, null), Is.EqualTo("A"));

        [Test]
        public void ExcludedMemberIsSkipped() =>
            Assert.That(RoomOwnerElection.Oldest(new[] { M("b", 1), M("c", 3) }, "b"), Is.EqualTo("c"));

        [Test]
        public void NoCandidateReturnsNull()
        {
            Assert.That(RoomOwnerElection.Oldest(new RoomMember[0], null), Is.Null);
            Assert.That(RoomOwnerElection.Oldest(new[] { M("a", 1) }, "a"), Is.Null);
        }
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Run: 공통 블록, 첫 줄 `$p='EditMode'; $a='Bun3.Unity.Rooms.Tests'; $n='rooms-edit-red'`
Expected: `NO RESULT XML` 또는 실패. 로그에 `error CS0246: The type or namespace name 'RoomCode'`.

- [ ] **Step 3: 구현**

`Runtime/RoomCode.cs`:

```csharp
using System;
using System.Security.Cryptography;

namespace Bun3.Unity.Rooms
{
    /// <summary>
    /// Six-character room code. The alphabet leaves out look-alike characters (0/O, 1/I) so a code
    /// survives being read aloud. Each character carries five bits; a code packs a 30-bit value.
    /// </summary>
    public readonly struct RoomCode : IEquatable<RoomCode>
    {
        /// <summary>Number of characters in a code.</summary>
        public const int Length = 6;

        /// <summary>Characters a code may contain, in value order.</summary>
        public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        /// <summary>Largest value a code can encode.</summary>
        public const uint MaxValue = (1u << (5 * Length)) - 1;

        private RoomCode(uint value) => Value = value;

        /// <summary>The 30-bit value this code encodes.</summary>
        public uint Value { get; }

        /// <summary>Creates a uniformly random code from a cryptographic random source.</summary>
        public static RoomCode Generate()
        {
            var bytes = new byte[4];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            return new RoomCode(BitConverter.ToUInt32(bytes, 0) & MaxValue);
        }

        /// <summary>Encodes <paramref name="value"/> as a code.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> exceeds <see cref="MaxValue"/>.</exception>
        public static RoomCode FromValue(uint value)
        {
            if (value > MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
            return new RoomCode(value);
        }

        /// <summary>
        /// Parses user input. Surrounding whitespace is ignored and letters are upper-cased; anything
        /// else must be exactly <see cref="Length"/> alphabet characters.
        /// </summary>
        public static bool TryNormalize(ReadOnlySpan<char> input, out RoomCode code)
        {
            code = default;
            input = input.Trim();
            if (input.Length != Length) return false;
            uint value = 0;
            for (int i = 0; i < Length; i++)
            {
                int digit = Alphabet.IndexOf(char.ToUpperInvariant(input[i]));
                if (digit < 0) return false;
                value = (value << 5) | (uint)digit;
            }
            code = new RoomCode(value);
            return true;
        }

        /// <summary>Writes the code's characters without allocating.</summary>
        public bool TryFormat(Span<char> destination, out int charsWritten)
        {
            charsWritten = 0;
            if (destination.Length < Length) return false;
            for (int i = 0; i < Length; i++)
                destination[i] = Alphabet[(int)((Value >> (5 * (Length - 1 - i))) & 31)];
            charsWritten = Length;
            return true;
        }

        /// <summary>Returns the code's characters. Allocates; prefer <see cref="TryFormat"/> on hot paths.</summary>
        public override string ToString()
        {
            Span<char> buffer = stackalloc char[Length];
            TryFormat(buffer, out _);
            return new string(buffer);
        }

        /// <inheritdoc/>
        public bool Equals(RoomCode other) => Value == other.Value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RoomCode other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (int)Value;

        /// <summary>Compares codes by value.</summary>
        public static bool operator ==(RoomCode left, RoomCode right) => left.Equals(right);

        /// <summary>Compares codes by value.</summary>
        public static bool operator !=(RoomCode left, RoomCode right) => !left.Equals(right);
    }
}
```

`Runtime/IRoom.cs`:

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.Rooms
{
    /// <summary>A member of a room.</summary>
    public readonly struct RoomMember
    {
        /// <summary>Creates a member record.</summary>
        public RoomMember(string id, long joinedAt)
        {
            Id = id;
            JoinedAt = joinedAt;
        }

        /// <summary>Backend-stable member identifier. A Steam backend uses the decimal SteamID.</summary>
        public string Id { get; }

        /// <summary>Join time in backend units; earlier joins have smaller values.</summary>
        public long JoinedAt { get; }
    }

    /// <summary>
    /// A joined room. Members, ownership and data come from the backend. Events are raised on the main thread.
    /// </summary>
    public interface IRoom
    {
        /// <summary>Backend room identifier, accepted by <see cref="IRoomBackend.JoinByIdAsync"/>.</summary>
        string Id { get; }

        /// <summary>Code players type to join.</summary>
        RoomCode Code { get; }

        /// <summary>Display name given at creation; null when the backend does not know it.</summary>
        string Name { get; }

        /// <summary>Current members, owner included.</summary>
        IReadOnlyList<RoomMember> Members { get; }

        /// <summary>Member capacity including the owner; 0 when the backend does not know it.</summary>
        int MaxMembers { get; }

        /// <summary>Id of the local member; null until the backend knows it.</summary>
        string LocalMemberId { get; }

        /// <summary>Id of the member allowed to change the room.</summary>
        string OwnerId { get; }

        /// <summary>Whether the local member owns the room.</summary>
        bool IsOwner { get; }

        /// <summary>Whether the room accepts new members: the owner's latest <see cref="SetJoinableAsync"/>, true when created.</summary>
        bool IsJoinable { get; }

        /// <summary>Room data for <paramref name="key"/>; null when absent.</summary>
        string GetData(string key);

        /// <summary>Stores room data. Owner only.</summary>
        UniTask SetDataAsync(string key, string value);

        /// <summary>Opens or closes the room to new members. Closed rooms are not found by code. Owner only.</summary>
        UniTask SetJoinableAsync(bool joinable);

        /// <summary>Makes <paramref name="memberId"/> the owner. Owner only.</summary>
        UniTask TransferOwnershipAsync(string memberId);

        /// <summary>Leaves the room. Idempotent; does not raise <see cref="Closed"/>.</summary>
        UniTask LeaveAsync();

        /// <summary>Raised when members join, leave or change their join records.</summary>
        event Action MembersChanged;

        /// <summary>Raised when <see cref="OwnerId"/> changes.</summary>
        event Action OwnerChanged;

        /// <summary>
        /// Raised when room data or <see cref="IsJoinable"/> may have changed. Read the keys you need again;
        /// backends do not report which key changed.
        /// </summary>
        event Action DataChanged;

        /// <summary>Raised when the room ends without <see cref="LeaveAsync"/>: kicked, destroyed or host gone.</summary>
        event Action Closed;
    }
}
```

`Runtime/IRoomBackend.cs`:

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.Rooms
{
    /// <summary>Options for <see cref="IRoomBackend.CreateAsync"/>.</summary>
    public struct RoomCreateOptions
    {
        /// <summary>Member capacity including the owner.</summary>
        public int MaxMembers;

        /// <summary>Display name stored with the room.</summary>
        public string Name;

        /// <summary>Compatibility string. Joiners must pass an identical value.</summary>
        public string Version;
    }

    /// <summary>Options for joining a room.</summary>
    public struct RoomJoinOptions
    {
        /// <summary>Compared ordinally with the room's <see cref="RoomCreateOptions.Version"/>.</summary>
        public string Version;
    }

    /// <summary>
    /// Creates and joins rooms on one platform. One instance holds at most one room at a time;
    /// creating or joining while in a room throws <see cref="InvalidOperationException"/>.
    /// Failures the player should see are <see cref="RoomException"/>.
    /// </summary>
    public interface IRoomBackend
    {
        /// <summary>Creates a room owned by the local member.</summary>
        UniTask<IRoom> CreateAsync(RoomCreateOptions options, CancellationToken cancellationToken = default);

        /// <summary>Joins the joinable room with <paramref name="code"/>.</summary>
        UniTask<IRoom> JoinByCodeAsync(RoomCode code, RoomJoinOptions options, CancellationToken cancellationToken = default);

        /// <summary>Joins a room by backend id, as delivered by <see cref="JoinRequested"/>.</summary>
        UniTask<IRoom> JoinByIdAsync(string roomId, RoomJoinOptions options, CancellationToken cancellationToken = default);

        /// <summary>Raised with a room id when the player accepts an invite or joins a friend from the platform.</summary>
        event Action<string> JoinRequested;

        /// <summary>Opens the platform invite UI for <paramref name="room"/>; false when unsupported.</summary>
        bool TryOpenInviteOverlay(IRoom room);
    }
}
```

`Runtime/RoomException.cs`:

```csharp
using System;

namespace Bun3.Unity.Rooms
{
    /// <summary>Why a room operation failed. Player-facing text is the game's choice.</summary>
    public enum RoomFailure
    {
        /// <summary>No joinable room matches. Closed (locked) rooms report this too.</summary>
        NotFound,

        /// <summary>The room has no free member slot.</summary>
        Full,

        /// <summary>The room's version string differs from the joiner's.</summary>
        VersionMismatch,

        /// <summary>The platform service is not running, not signed in, or did not answer in time.</summary>
        ServiceUnavailable,
    }

    /// <summary>A room operation failure the player should be told about.</summary>
    public sealed class RoomException : Exception
    {
        /// <summary>Creates the exception; the message is the failure name.</summary>
        public RoomException(RoomFailure failure) : base(failure.ToString()) => Failure = failure;

        /// <summary>What went wrong.</summary>
        public RoomFailure Failure { get; }
    }
}
```

`Runtime/RoomOwnerElection.cs`:

```csharp
using System.Collections.Generic;

namespace Bun3.Unity.Rooms
{
    /// <summary>Host election shared by backends and sessions.</summary>
    public static class RoomOwnerElection
    {
        /// <summary>
        /// The member who joined first, ignoring <paramref name="excludedId"/>. Ties break by ordinal id.
        /// Returns null when no candidate remains. Does not allocate.
        /// </summary>
        public static string Oldest(IReadOnlyList<RoomMember> members, string excludedId)
        {
            string oldest = null;
            long joinedAt = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (member.Id == excludedId) continue;
                if (oldest == null || member.JoinedAt < joinedAt ||
                    member.JoinedAt == joinedAt && string.CompareOrdinal(member.Id, oldest) < 0)
                {
                    oldest = member.Id;
                    joinedAt = member.JoinedAt;
                }
            }
            return oldest;
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: 공통 블록, `$p='EditMode'; $a='Bun3.Unity.Rooms.Tests'; $n='rooms-edit'`
Expected: `result="Passed"`, `failed="0"`, 경고·오류 출력 없음.

- [ ] **Step 5: README와 CHANGELOG**

`unity/Packages/com.bun3.unity.rooms/README.md`:

````markdown
# Bun3 Rooms

Multiplayer rooms without a netcode or platform dependency: six-character room codes, members, ownership, room data and host election. Backends (`com.bun3.unity.rooms.steam`, the loopback backend in `com.bun3.unity.rooms.ngo`) implement `IRoomBackend`; netcode adapters run a session on top of `IRoom`.

## Installation

```json
{
  "com.bun3.unity.rooms": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.rooms#Bun3/jp-steam-networking"
}
```

The fragment selects a moving integration branch. UniTask must also be installed. The runtime assembly is `Bun3.Unity.Rooms`.

## Contracts

- `RoomCode` uses `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (no 0/O/1/I). `TryNormalize` trims and upper-cases input. `FromValue`/`Value` map codes to 30-bit numbers.
- One backend instance holds one room at a time. Player-facing failures are `RoomException` with `RoomFailure.NotFound`, `Full`, `VersionMismatch` or `ServiceUnavailable`; the game owns the wording. Locked rooms report `NotFound`.
- `RoomJoinOptions.Version` is compared ordinally with the room's version. Express compatibility in the string you pass (for example `major.minor`).
- `IRoom` events are raised on the main thread. `Closed` means the room ended without `LeaveAsync`. `DataChanged` carries no key: re-read the data (and `IsJoinable`) you care about. `MaxMembers` is 0 when the backend does not know the capacity.
- `RoomOwnerElection.Oldest` picks the earliest `JoinedAt`, breaking ties by ordinal id, and does not allocate.
````

`unity/Packages/com.bun3.unity.rooms/CHANGELOG.md`:

```markdown
# Changelog

## [0.1.0] - 2026-10-12

- Add room codes, the room and backend contracts, room failures and oldest-member election.
```

- [ ] **Step 6: Commit**

Unity가 만든 `.meta`(패키지 폴더, `Runtime/`, `Tests/`, 각 파일)가 있는지 `git status`로 확인한 뒤:

```bash
cd E:/Projects/orca/workspace/bun3-kit/jp-steam-networking
git add unity/Packages/com.bun3.unity.rooms
git commit -m "✨ Add room codes, room contracts and oldest-member election" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `com.bun3.unity.rooms.ngo` — NGO 세션·호스트 승계·루프백 백엔드

**Files:**
- Create: `unity/Packages/com.bun3.unity.rooms.ngo/package.json`, `README.md`, `CHANGELOG.md`
- Create: `unity/Packages/com.bun3.unity.rooms.ngo/Runtime/Bun3.Unity.Rooms.Ngo.asmdef`, `INgoRoomTransport.cs`, `UtpRoomTransport.cs`, `NgoRoomSession.cs`, `LoopbackRoomBackend.cs`
- Test: `unity/Packages/com.bun3.unity.rooms.ngo/Tests/Runtime/Bun3.Unity.Rooms.Ngo.Tests.asmdef`, `NgoTestPeers.cs`, `FakeRoomHub.cs`, `LoopbackRoomBackendTests.cs`, `NgoRoomSessionMigrationTests.cs`

**Interfaces:**
- Consumes: Task 1 전부.
- Produces:
  - `interface INgoRoomTransport` — `string PrepareHost(NetworkManager manager, IRoom room)`, `void PrepareClient(NetworkManager manager, IRoom room, string endpoint)`
  - `sealed class UtpRoomTransport(UnityTransport transport) : INgoRoomTransport` — `UnityTransport Transport`, `ushort ListenPort { get; set; }`, `internal static ushort FreeUdpPort()`
  - `enum RoomSessionState { Idle, Starting, Connected, Migrating, Failed, Closed }`
  - `sealed class NgoRoomSessionOptions` — `Func<bool> CanMigrate`(기본 false), `TimeSpan MigrationTimeout`(기본 30초), `Func<IReadOnlyList<RoomMember>, string, string> ElectHost`(기본 `RoomOwnerElection.Oldest`)
  - `sealed class NgoRoomSession(NetworkManager, IRoom, INgoRoomTransport, NgoRoomSessionOptions = null)` — `const string HostKey = "bun3.host"`, `HostEpochKey = "bun3.hostEpoch"`, `HostMemberKey = "bun3.hostMember"`, `IRoom Room`, `RoomSessionState State`, `event Action<RoomSessionState> StateChanged`, `event Action HostMigrated`, `UniTask StartAsync(CancellationToken = default)`, `UniTask LeaveAsync()`
  - `sealed class LoopbackRoomBackend(NetworkManager, UtpRoomTransport) : IRoomBackend` — 멤버 ID `local-{clientId}`. 호스트의 방은 생성 정원·잠금·`DataChanged`를 가진다. 클라이언트의 방은 `MaxMembers` 0, `IsJoinable` true, `DataChanged` 없음

- [ ] **Step 1: 패키지 골격과 실패하는 테스트 작성**

`package.json`:

```json
{
  "name": "com.bun3.unity.rooms.ngo",
  "displayName": "Bun3 Rooms NGO Adapter",
  "version": "0.1.0",
  "unity": "6000.3",
  "description": "Runs Netcode for GameObjects for a room: owner hosts, members connect, host migration and a same-machine loopback backend.",
  "author": {
    "name": "Bun3",
    "url": "https://github.com/Bun3",
    "email": "bun3.dev@gmail.com"
  },
  "dependencies": {
    "com.bun3.unity.rooms": "0.1.0",
    "com.unity.netcode.gameobjects": "2.13.1",
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"
  }
}
```

`Runtime/Bun3.Unity.Rooms.Ngo.asmdef`:

```json
{
    "name": "Bun3.Unity.Rooms.Ngo",
    "rootNamespace": "Bun3.Unity.Rooms.Ngo",
    "references": [ "Bun3.Unity.Rooms", "Unity.Netcode.Runtime", "UniTask" ],
    "autoReferenced": true
}
```

`Tests/Runtime/Bun3.Unity.Rooms.Ngo.Tests.asmdef` (PlayMode — `includePlatforms` 비움):

```json
{
    "name": "Bun3.Unity.Rooms.Ngo.Tests",
    "rootNamespace": "Bun3.Unity.Rooms.Ngo.Tests",
    "references": [
        "Bun3.Unity.Rooms",
        "Bun3.Unity.Rooms.Ngo",
        "Unity.Netcode.Runtime",
        "UniTask",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [],
    "overrideReferences": true,
    "precompiledReferences": [ "nunit.framework.dll" ],
    "autoReferenced": false,
    "defineConstraints": [ "UNITY_INCLUDE_TESTS" ]
}
```

`Tests/Runtime/NgoTestPeers.cs`:

```csharp
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Rooms.Ngo.Tests
{
    internal static class NgoTestPeers
    {
        public static NetworkManager Create(string name)
        {
            var root = new GameObject(name);
            var utp = root.AddComponent<UnityTransport>();
            var manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig { NetworkTransport = utp, EnableSceneManagement = false };
            return manager;
        }

        public static UtpRoomTransport Transport(NetworkManager manager) =>
            new UtpRoomTransport(manager.GetComponent<UnityTransport>());

        public static async UniTask DestroyAsync(params NetworkManager[] managers)
        {
            foreach (var manager in managers)
                if (manager != null && manager.IsListening) manager.Shutdown();
            await UniTask.DelayFrame(3);
            foreach (var manager in managers)
                if (manager != null) Object.Destroy(manager.gameObject);
            await UniTask.DelayFrame(1);
        }
    }
}
```

`Tests/Runtime/FakeRoomHub.cs`:

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.Rooms.Ngo.Tests
{
    // In-memory room shared by local members. Losing the owner hands ownership to the newest member,
    // like a platform picking an arbitrary owner, so sessions must converge on the elected host.
    internal sealed class FakeRoomHub
    {
        public readonly List<RoomMember> Members = new List<RoomMember>();
        public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
        public readonly List<FakeRoom> Views = new List<FakeRoom>();
        public string OwnerId;
        public bool IgnoreTransfers;
        private long clock;

        public FakeRoom Join(string memberId)
        {
            Members.Add(new RoomMember(memberId, ++clock));
            OwnerId ??= memberId;
            var view = new FakeRoom(this, memberId);
            Views.Add(view);
            RaiseMembersChanged();
            return view;
        }

        public void Remove(string memberId)
        {
            Members.RemoveAll(member => member.Id == memberId);
            Views.RemoveAll(view => view.LocalMemberId == memberId);
            bool ownerLeft = OwnerId == memberId;
            if (ownerLeft) OwnerId = Members.Count > 0 ? Members[Members.Count - 1].Id : null;
            RaiseMembersChanged();
            if (ownerLeft) RaiseOwnerChanged();
        }

        public void SetOwner(string memberId)
        {
            if (IgnoreTransfers) return;
            OwnerId = memberId;
            RaiseOwnerChanged();
        }

        private void RaiseMembersChanged()
        {
            foreach (var view in Views.ToArray()) view.RaiseMembersChanged();
        }

        private void RaiseOwnerChanged()
        {
            foreach (var view in Views.ToArray()) view.RaiseOwnerChanged();
        }
    }

    internal sealed class FakeRoom : IRoom
    {
        private readonly FakeRoomHub hub;

        public FakeRoom(FakeRoomHub hub, string memberId)
        {
            this.hub = hub;
            LocalMemberId = memberId;
        }

        public string Id => "fake";
        public RoomCode Code => RoomCode.FromValue(1);
        public string Name => "Fake";
        public IReadOnlyList<RoomMember> Members => hub.Members;
        public string LocalMemberId { get; }
        public string OwnerId => hub.OwnerId;
        public bool IsOwner => hub.OwnerId == LocalMemberId;
        public int MaxMembers => 0;
        public bool IsJoinable => true;
        public event Action MembersChanged;
        public event Action OwnerChanged;
        public event Action DataChanged { add { } remove { } }
        public event Action Closed;

        public string GetData(string key) => hub.Data.TryGetValue(key, out string value) ? value : null;

        public UniTask SetDataAsync(string key, string value)
        {
            RequireOwner();
            hub.Data[key] = value;
            return UniTask.CompletedTask;
        }

        public UniTask SetJoinableAsync(bool joinable) => UniTask.CompletedTask;

        public UniTask TransferOwnershipAsync(string memberId)
        {
            RequireOwner();
            hub.SetOwner(memberId);
            return UniTask.CompletedTask;
        }

        public UniTask LeaveAsync()
        {
            hub.Remove(LocalMemberId);
            return UniTask.CompletedTask;
        }

        public void RaiseMembersChanged() => MembersChanged?.Invoke();
        public void RaiseOwnerChanged() => OwnerChanged?.Invoke();
        public void RaiseClosed() => Closed?.Invoke();

        private void RequireOwner()
        {
            if (!IsOwner) throw new InvalidOperationException("Not the owner.");
        }
    }
}
```

`Tests/Runtime/LoopbackRoomBackendTests.cs`:

```csharp
using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Rooms.Ngo.Tests
{
    public class LoopbackRoomBackendTests
    {
        [UnityTest]
        public IEnumerator TypedCodeConnectsToTheLoopbackHost() => UniTask.ToCoroutine(async () =>
        {
            var host = NgoTestPeers.Create("Host");
            var client = NgoTestPeers.Create("Client");
            try
            {
                var hostTransport = NgoTestPeers.Transport(host);
                IRoom hostRoom = await new LoopbackRoomBackend(host, hostTransport)
                    .CreateAsync(new RoomCreateOptions { MaxMembers = 4, Name = "Host" });
                var hostSession = new NgoRoomSession(host, hostRoom, hostTransport);
                await hostSession.StartAsync();
                Assert.That(hostSession.State, Is.EqualTo(RoomSessionState.Connected));
                Assert.That(hostTransport.Transport.ConnectionData.Port, Is.EqualTo((ushort)hostRoom.Code.Value));
                Assert.That(hostRoom.GetData(NgoRoomSession.HostKey), Is.EqualTo("127.0.0.1:" + hostRoom.Code.Value));
                Assert.That(hostRoom.Name, Is.EqualTo("Host"));
                Assert.That(hostRoom.MaxMembers, Is.EqualTo(4));
                int dataChanges = 0;
                hostRoom.DataChanged += () => dataChanges++;
                await hostRoom.SetJoinableAsync(false);
                Assert.That(hostRoom.IsJoinable, Is.False);
                Assert.That(dataChanges, Is.EqualTo(1));
                await hostRoom.SetJoinableAsync(true);

                Assert.That(RoomCode.TryNormalize(hostRoom.Code.ToString().ToLowerInvariant(), out var typed), Is.True);
                var clientTransport = NgoTestPeers.Transport(client);
                IRoom clientRoom = await new LoopbackRoomBackend(client, clientTransport).JoinByCodeAsync(typed, default);
                var clientSession = new NgoRoomSession(client, clientRoom, clientTransport);
                await clientSession.StartAsync().Timeout(TimeSpan.FromSeconds(10));

                Assert.That(clientSession.State, Is.EqualTo(RoomSessionState.Connected));
                Assert.That(clientRoom.LocalMemberId, Is.EqualTo("local-1"));
                Assert.That(clientRoom.MaxMembers, Is.EqualTo(0), "A loopback client does not know the capacity.");
                Assert.That(clientRoom.IsJoinable, Is.True);
                await UniTask.WaitUntil(() => hostRoom.Members.Count == 2).Timeout(TimeSpan.FromSeconds(5));
                Assert.That(hostRoom.Members[1].Id, Is.EqualTo(clientRoom.LocalMemberId));
                Assert.That(clientRoom.Members[0].Id, Is.EqualTo(hostRoom.LocalMemberId));
                Assert.That(hostRoom.LocalMemberId, Is.EqualTo("local-0"));

                bool closed = false;
                clientRoom.Closed += () => closed = true;
                await hostSession.LeaveAsync();
                await UniTask.WaitUntil(() => clientSession.State == RoomSessionState.Closed).Timeout(TimeSpan.FromSeconds(10));
                Assert.That(closed, Is.True);
            }
            finally
            {
                await NgoTestPeers.DestroyAsync(host, client);
            }
        });

        [Test]
        public void CodesOutsideThePortRangeAreNotFound()
        {
            var manager = NgoTestPeers.Create("Loopback");
            try
            {
                var backend = new LoopbackRoomBackend(manager, NgoTestPeers.Transport(manager));
                var zero = Assert.Throws<RoomException>(() => backend.JoinByCodeAsync(RoomCode.FromValue(0), default));
                Assert.That(zero.Failure, Is.EqualTo(RoomFailure.NotFound));
                var high = Assert.Throws<RoomException>(() => backend.JoinByCodeAsync(RoomCode.FromValue(70000), default));
                Assert.That(high.Failure, Is.EqualTo(RoomFailure.NotFound));
                var id = Assert.Throws<RoomException>(() => backend.JoinByIdAsync("not-a-code", default));
                Assert.That(id.Failure, Is.EqualTo(RoomFailure.NotFound));
            }
            finally
            {
                Object.Destroy(manager.gameObject);
            }
        }
    }
}
```

`Tests/Runtime/NgoRoomSessionMigrationTests.cs`:

```csharp
using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace Bun3.Unity.Rooms.Ngo.Tests
{
    public class NgoRoomSessionMigrationTests
    {
        private sealed class Peer
        {
            public NetworkManager Manager;
            public FakeRoom Room;
            public NgoRoomSession Session;
            public int Migrations;
        }

        private static NgoRoomSessionOptions Options(bool canMigrate, double timeoutSeconds = 15) => new NgoRoomSessionOptions
        {
            CanMigrate = () => canMigrate,
            MigrationTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        private static async UniTask<Peer[]> StartRoomAsync(FakeRoomHub hub, NgoRoomSessionOptions options, params string[] ids)
        {
            var peers = new Peer[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                var manager = NgoTestPeers.Create(ids[i]);
                var room = hub.Join(ids[i]);
                var peer = new Peer { Manager = manager, Room = room };
                peer.Session = new NgoRoomSession(manager, room, NgoTestPeers.Transport(manager), options);
                peer.Session.HostMigrated += () => peer.Migrations++;
                peers[i] = peer;
                await peer.Session.StartAsync().Timeout(TimeSpan.FromSeconds(10));
            }
            return peers;
        }

        private static UniTask WaitAsync(Func<bool> condition, double seconds = 20) =>
            UniTask.WaitUntil(condition).Timeout(TimeSpan.FromSeconds(seconds));

        private static NetworkManager[] Managers(Peer[] peers) => Array.ConvertAll(peers, peer => peer.Manager);

        [UnityTest]
        public IEnumerator CrashedHostIsReplacedByOldestRemainingMember() => UniTask.ToCoroutine(async () =>
        {
            var hub = new FakeRoomHub();
            Peer[] peers = null;
            try
            {
                peers = await StartRoomAsync(hub, Options(true), "A", "B", "C");
                Assert.That(hub.Data[NgoRoomSession.HostEpochKey], Is.EqualTo("1"));
                Assert.That(hub.Data[NgoRoomSession.HostMemberKey], Is.EqualTo("A"));

                hub.Remove("A");               // ownership falls to the newest member, C
                peers[0].Manager.Shutdown();   // crash without LeaveAsync

                await WaitAsync(() => peers[1].Migrations == 1 && peers[2].Migrations == 1 &&
                    peers[1].Session.State == RoomSessionState.Connected && peers[2].Session.State == RoomSessionState.Connected);
                Assert.That(hub.OwnerId, Is.EqualTo("B"), "The newest member hands ownership to the oldest.");
                Assert.That(hub.Data[NgoRoomSession.HostMemberKey], Is.EqualTo("B"));
                Assert.That(hub.Data[NgoRoomSession.HostEpochKey], Is.EqualTo("2"));
                Assert.That(peers[1].Manager.IsHost, Is.True);
                await WaitAsync(() => peers[1].Manager.ConnectedClientsIds.Count == 2);
                Assert.That(peers[2].Manager.IsConnectedClient, Is.True);
            }
            finally
            {
                if (peers != null) await NgoTestPeers.DestroyAsync(Managers(peers));
            }
        });

        [UnityTest]
        public IEnumerator LeavingOwnerHandsTheRoomToTheOldestMember() => UniTask.ToCoroutine(async () =>
        {
            var hub = new FakeRoomHub();
            Peer[] peers = null;
            try
            {
                peers = await StartRoomAsync(hub, Options(true), "A", "B", "C");
                await peers[0].Session.LeaveAsync();
                Assert.That(peers[0].Session.State, Is.EqualTo(RoomSessionState.Closed));
                Assert.That(hub.Members.Exists(member => member.Id == "A"), Is.False);
                Assert.That(hub.OwnerId, Is.EqualTo("B"));

                await WaitAsync(() => peers[1].Migrations == 1 && peers[2].Migrations == 1 &&
                    peers[2].Session.State == RoomSessionState.Connected);
                Assert.That(hub.OwnerId, Is.EqualTo("B"), "Nobody may hand the room back to the member who left.");
                Assert.That(peers[1].Manager.IsHost, Is.True);
                Assert.That(hub.Data[NgoRoomSession.HostEpochKey], Is.EqualTo("2"));
            }
            finally
            {
                if (peers != null) await NgoTestPeers.DestroyAsync(Managers(peers));
            }
        });

        [UnityTest]
        public IEnumerator HostLossClosesSessionsWhenMigrationIsNotAllowed() => UniTask.ToCoroutine(async () =>
        {
            var hub = new FakeRoomHub();
            Peer[] peers = null;
            try
            {
                peers = await StartRoomAsync(hub, Options(false), "A", "B");
                peers[0].Manager.Shutdown();
                await WaitAsync(() => peers[1].Session.State == RoomSessionState.Closed);
                Assert.That(peers[1].Manager.IsListening, Is.False);
                Assert.That(peers[1].Migrations, Is.EqualTo(0));
                Assert.That(hub.Members.Exists(member => member.Id == "B"), Is.True, "Leaving the room stays the game's decision.");
            }
            finally
            {
                if (peers != null) await NgoTestPeers.DestroyAsync(Managers(peers));
            }
        });

        [UnityTest]
        public IEnumerator MigrationFailsWhenNoHostAppearsInTime() => UniTask.ToCoroutine(async () =>
        {
            var hub = new FakeRoomHub();
            Peer[] peers = null;
            try
            {
                peers = await StartRoomAsync(hub, Options(true, 3), "A", "B", "C");
                hub.IgnoreTransfers = true;
                hub.Remove("A");
                peers[0].Manager.Shutdown();

                await WaitAsync(() => peers[1].Session.State == RoomSessionState.Failed &&
                    peers[2].Session.State == RoomSessionState.Failed, 12);
                Assert.That(peers[1].Manager.IsListening, Is.False);
                Assert.That(peers[2].Manager.IsListening, Is.False);
                Assert.That(hub.Members.Count, Is.EqualTo(2), "A failed migration keeps everyone in the room.");
            }
            finally
            {
                if (peers != null) await NgoTestPeers.DestroyAsync(Managers(peers));
            }
        });
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Run: 공통 블록, `$p='PlayMode'; $a='Bun3.Unity.Rooms.Ngo.Tests'; $n='rooms-ngo-red'`
Expected: `NO RESULT XML`, 로그에 `error CS0246: ... 'LoopbackRoomBackend'` 또는 `'NgoRoomSession'`.

- [ ] **Step 3: 구현**

`Runtime/INgoRoomTransport.cs`:

```csharp
using Unity.Netcode;

namespace Bun3.Unity.Rooms.Ngo
{
    /// <summary>Points a NetworkManager at the transport and peer for a room.</summary>
    public interface INgoRoomTransport
    {
        /// <summary>
        /// Makes this transport the manager's transport, prepares it to host <paramref name="room"/>,
        /// and returns the endpoint clients connect to.
        /// </summary>
        string PrepareHost(NetworkManager manager, IRoom room);

        /// <summary>Makes this transport the manager's transport and targets the host at <paramref name="endpoint"/>.</summary>
        void PrepareClient(NetworkManager manager, IRoom room, string endpoint);
    }
}
```

`Runtime/UtpRoomTransport.cs`:

```csharp
using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace Bun3.Unity.Rooms.Ngo
{
    /// <summary>Unity Transport on the loopback interface. Endpoints are <c>127.0.0.1:port</c>.</summary>
    public sealed class UtpRoomTransport : INgoRoomTransport
    {
        private const string Loopback = "127.0.0.1";

        /// <summary>Wraps the manager's <see cref="UnityTransport"/>.</summary>
        public UtpRoomTransport(UnityTransport transport) =>
            Transport = transport != null ? transport : throw new ArgumentNullException(nameof(transport));

        /// <summary>The wrapped transport.</summary>
        public UnityTransport Transport { get; }

        /// <summary>Port the host listens on; 0 picks a free port in <see cref="PrepareHost"/>.</summary>
        public ushort ListenPort { get; set; }

        /// <inheritdoc/>
        public string PrepareHost(NetworkManager manager, IRoom room)
        {
            ushort port = ListenPort != 0 ? ListenPort : FreeUdpPort();
            Transport.SetConnectionData(Loopback, port, Loopback);
            manager.NetworkConfig.NetworkTransport = Transport;
            return string.Concat(Loopback, ":", port.ToString(CultureInfo.InvariantCulture));
        }

        /// <inheritdoc/>
        /// <exception cref="FormatException"><paramref name="endpoint"/> is not <c>host:port</c>.</exception>
        public void PrepareClient(NetworkManager manager, IRoom room, string endpoint)
        {
            int split = endpoint != null ? endpoint.LastIndexOf(':') : -1;
            if (split <= 0 || !ushort.TryParse(endpoint.Substring(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out ushort port))
                throw new FormatException("The host endpoint is not host:port.");
            Transport.SetConnectionData(endpoint.Substring(0, split), port);
            manager.NetworkConfig.NetworkTransport = Transport;
        }

        internal static ushort FreeUdpPort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return (ushort)((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }
    }
}
```

`Runtime/NgoRoomSession.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace Bun3.Unity.Rooms.Ngo
{
    /// <summary>Lifecycle of an <see cref="NgoRoomSession"/>.</summary>
    public enum RoomSessionState
    {
        /// <summary>Created; <see cref="NgoRoomSession.StartAsync"/> not called yet.</summary>
        Idle,

        /// <summary>Starting NGO for the first time.</summary>
        Starting,

        /// <summary>NGO runs as host or connected client.</summary>
        Connected,

        /// <summary>The NGO host was lost and a replacement is being started.</summary>
        Migrating,

        /// <summary>Migration did not finish in time. NGO is stopped; the room is still joined.</summary>
        Failed,

        /// <summary>NGO is stopped for good: left, room closed, or migration not allowed.</summary>
        Closed,
    }

    /// <summary>Policy hooks for <see cref="NgoRoomSession"/>.</summary>
    public sealed class NgoRoomSessionOptions
    {
        /// <summary>Whether the room survives losing its NGO host right now. Default: never.</summary>
        public Func<bool> CanMigrate { get; set; } = () => false;

        /// <summary>Time allowed to reach <see cref="RoomSessionState.Connected"/> after starting or after losing the host.</summary>
        public TimeSpan MigrationTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Picks the next host from the members, given the departed host's id.</summary>
        public Func<IReadOnlyList<RoomMember>, string, string> ElectHost { get; set; } = RoomOwnerElection.Oldest;
    }

    /// <summary>
    /// Runs NGO for a room. The owner hosts and publishes its endpoint in room data; members connect to it.
    /// When the host is lost and <see cref="NgoRoomSessionOptions.CanMigrate"/> allows it, the elected member
    /// takes ownership and hosts, and the others reconnect. Restarting NGO destroys spawned NetworkObjects:
    /// restore game state in <see cref="HostMigrated"/>. Set connection data such as
    /// <c>NetworkConfig.ConnectionData</c> before <see cref="StartAsync"/>. Main thread only.
    /// </summary>
    public sealed class NgoRoomSession
    {
        /// <summary>Room data key holding the NGO host endpoint.</summary>
        public const string HostKey = "bun3.host";

        /// <summary>Room data key holding the host generation; it grows by one each time a host starts.</summary>
        public const string HostEpochKey = "bun3.hostEpoch";

        /// <summary>Room data key holding the member id of the NGO host.</summary>
        public const string HostMemberKey = "bun3.hostMember";

        private readonly NetworkManager manager;
        private readonly INgoRoomTransport transport;
        private readonly NgoRoomSessionOptions options;
        private string hostMember;
        private int epoch;
        private bool clientStopped;
        private bool checkScheduled;
        private bool left;

        /// <summary>Creates a session; nothing starts until <see cref="StartAsync"/>.</summary>
        public NgoRoomSession(NetworkManager manager, IRoom room, INgoRoomTransport transport, NgoRoomSessionOptions options = null)
        {
            this.manager = manager != null ? manager : throw new ArgumentNullException(nameof(manager));
            Room = room ?? throw new ArgumentNullException(nameof(room));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.options = options ?? new NgoRoomSessionOptions();
        }

        /// <summary>The room this session runs NGO for.</summary>
        public IRoom Room { get; }

        /// <summary>Current lifecycle state.</summary>
        public RoomSessionState State { get; private set; }

        /// <summary>Raised whenever <see cref="State"/> changes.</summary>
        public event Action<RoomSessionState> StateChanged;

        /// <summary>Raised after NGO reconnects under a new host. Spawned objects from before are gone.</summary>
        public event Action HostMigrated;

        /// <summary>
        /// Hosts when the local member owns the room, otherwise waits for the host endpoint and connects.
        /// Completes once <see cref="State"/> is <see cref="RoomSessionState.Connected"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">NGO did not start or connect; the session is closed and the room is still joined.</exception>
        public async UniTask StartAsync(CancellationToken cancellationToken = default)
        {
            if (State != RoomSessionState.Idle) throw new InvalidOperationException("The session has already started.");
            SetState(RoomSessionState.Starting);
            Subscribe();
            double deadline = Deadline();
            try
            {
                bool connected;
                if (Room.IsOwner)
                {
                    await StartHostAsync(1);
                    connected = true;
                }
                else
                {
                    connected = await WaitAsync(() => ReadEpoch() > 0, deadline, cancellationToken) &&
                        await ConnectClientAsync(deadline, cancellationToken);
                }
                if (!connected || State != RoomSessionState.Starting)
                    throw new InvalidOperationException("The room's NGO host could not be reached.");
                SetState(RoomSessionState.Connected);
            }
            catch
            {
                Close();
                await StopNgoAsync();
                throw;
            }
        }

        /// <summary>
        /// Stops NGO and leaves the room. An owner first hands the room to the elected member when
        /// <see cref="NgoRoomSessionOptions.CanMigrate"/> allows it; hand-off failures never block leaving. Idempotent.
        /// </summary>
        public async UniTask LeaveAsync()
        {
            if (left) return;
            left = true;
            bool handOff = Room.IsOwner && Room.Members.Count > 1 && options.CanMigrate();
            Close();
            if (handOff)
            {
                string next = options.ElectHost(Room.Members, Room.LocalMemberId);
                if (next != null)
                {
                    try { await Room.TransferOwnershipAsync(next); }
                    catch (Exception error) { Debug.LogException(error); }
                }
            }
            await StopNgoAsync();
            await Room.LeaveAsync();
        }

        private async UniTask StartHostAsync(int newEpoch)
        {
            string endpoint = transport.PrepareHost(manager, Room);
            if (!manager.StartHost()) throw new InvalidOperationException("NetworkManager.StartHost failed.");
            hostMember = Room.LocalMemberId;
            epoch = newEpoch;
            await Room.SetDataAsync(HostMemberKey, hostMember);
            await Room.SetDataAsync(HostKey, endpoint);
            await Room.SetDataAsync(HostEpochKey, newEpoch.ToString(CultureInfo.InvariantCulture));
        }

        private async UniTask<bool> ConnectClientAsync(double deadline, CancellationToken cancellationToken)
        {
            hostMember = Room.GetData(HostMemberKey);
            epoch = ReadEpoch();
            transport.PrepareClient(manager, Room, Room.GetData(HostKey));
            clientStopped = false;
            if (!manager.StartClient()) return false;
            bool connected = await WaitAsync(() => manager.IsConnectedClient || clientStopped, deadline, cancellationToken) &&
                manager.IsConnectedClient;
            if (!connected) await StopNgoAsync();
            return connected;
        }

        private void Subscribe()
        {
            manager.OnClientStopped += OnClientStopped;
            Room.MembersChanged += ScheduleHostCheck;
            Room.Closed += OnRoomClosed;
        }

        private void Unsubscribe()
        {
            manager.OnClientStopped -= OnClientStopped;
            Room.MembersChanged -= ScheduleHostCheck;
            Room.Closed -= OnRoomClosed;
        }

        private void OnClientStopped(bool wasHost)
        {
            clientStopped = true;
            if (!wasHost) ScheduleHostCheck();
        }

        private void OnRoomClosed()
        {
            Close();
            StopNgoAsync().Forget();
        }

        // Deferred one frame so a backend without migration can report its own closure first.
        private void ScheduleHostCheck()
        {
            if (checkScheduled || State != RoomSessionState.Connected) return;
            checkScheduled = true;
            CheckHostAsync().Forget();
        }

        private async UniTaskVoid CheckHostAsync()
        {
            await UniTask.Yield();
            checkScheduled = false;
            if (State != RoomSessionState.Connected || manager == null || manager.IsHost) return;
            if (manager.IsConnectedClient && IsMember(hostMember)) return;
            if (!options.CanMigrate())
            {
                Close();
                await StopNgoAsync();
                return;
            }
            await MigrateAsync();
        }

        private async UniTask MigrateAsync()
        {
            SetState(RoomSessionState.Migrating);
            string departed = hostMember;
            int knownEpoch = epoch;
            int attemptedEpoch = epoch;
            double deadline = Deadline();
            bool handedOver = false;
            try
            {
                await StopNgoAsync();
                while (State == RoomSessionState.Migrating)
                {
                    if (Time.realtimeSinceStartupAsDouble >= deadline)
                    {
                        Fail();
                        return;
                    }
                    string elected = options.ElectHost(Room.Members, departed);
                    if (elected != null && elected == Room.LocalMemberId)
                    {
                        if (Room.IsOwner)
                        {
                            await StartHostAsync(Math.Max(knownEpoch, ReadEpoch()) + 1);
                            await FinishMigrationAsync();
                            return;
                        }
                    }
                    else if (elected != null && Room.IsOwner && !handedOver)
                    {
                        handedOver = true;
                        await Room.TransferOwnershipAsync(elected);
                    }
                    int published = ReadEpoch();
                    if (published > attemptedEpoch && Room.GetData(HostMemberKey) != departed)
                    {
                        attemptedEpoch = published;
                        if (await ConnectClientAsync(deadline, CancellationToken.None))
                        {
                            await FinishMigrationAsync();
                            return;
                        }
                    }
                    await UniTask.Yield();
                }
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (State == RoomSessionState.Migrating) Fail();
                await StopNgoAsync();
            }
        }

        private async UniTask FinishMigrationAsync()
        {
            if (State != RoomSessionState.Migrating)
            {
                await StopNgoAsync();
                return;
            }
            SetState(RoomSessionState.Connected);
            HostMigrated?.Invoke();
        }

        private void Fail()
        {
            Unsubscribe();
            SetState(RoomSessionState.Failed);
            StopNgoAsync().Forget();
        }

        private void Close()
        {
            if (State == RoomSessionState.Closed) return;
            Unsubscribe();
            SetState(RoomSessionState.Closed);
        }

        private async UniTask StopNgoAsync()
        {
            if (manager == null) return;
            if (manager.IsListening && !manager.ShutdownInProgress) manager.Shutdown();
            while (manager != null && manager.ShutdownInProgress) await UniTask.Yield();
        }

        private void SetState(RoomSessionState value)
        {
            if (State == value) return;
            State = value;
            StateChanged?.Invoke(value);
        }

        private double Deadline() => Time.realtimeSinceStartupAsDouble + options.MigrationTimeout.TotalSeconds;

        private async UniTask<bool> WaitAsync(Func<bool> condition, double deadline, CancellationToken cancellationToken)
        {
            while (!condition())
            {
                if (State == RoomSessionState.Closed || Time.realtimeSinceStartupAsDouble >= deadline) return false;
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
            return true;
        }

        private int ReadEpoch() =>
            int.TryParse(Room.GetData(HostEpochKey), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;

        private bool IsMember(string memberId)
        {
            var members = Room.Members;
            for (int i = 0; i < members.Count; i++)
                if (members[i].Id == memberId) return true;
            return false;
        }
    }
}
```

`Runtime/LoopbackRoomBackend.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;

namespace Bun3.Unity.Rooms.Ngo
{
    /// <summary>
    /// Rooms for processes on one machine (Editor, Multiplayer Play Mode) without a platform service.
    /// The room code encodes the host's UDP port, so joining needs no lookup, and the room id is the code.
    /// Room data lives only in the local process. The host sees every NGO connection as a member
    /// (<c>local-{clientId}</c>); a client sees the host and itself. Capacity and the lock are known only to the
    /// host; a client reports a capacity of 0 and a joinable room. No migration, invites or version checks:
    /// a client's room closes when its NGO client stops.
    /// </summary>
    public sealed class LoopbackRoomBackend : IRoomBackend
    {
        private const string HostMemberId = "local-0";
        private readonly NetworkManager manager;
        private readonly UtpRoomTransport transport;
        private LoopbackRoom current;

        /// <summary>Creates a backend; the code of a created room is the port set on <paramref name="transport"/>.</summary>
        public LoopbackRoomBackend(NetworkManager manager, UtpRoomTransport transport)
        {
            this.manager = manager != null ? manager : throw new ArgumentNullException(nameof(manager));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        /// <summary>Never raised: loopback rooms have no invites.</summary>
        public event Action<string> JoinRequested { add { } remove { } }

        /// <inheritdoc/>
        public bool TryOpenInviteOverlay(IRoom room) => false;

        /// <summary>Reserves a free UDP port as <see cref="UtpRoomTransport.ListenPort"/>; the code is that port.</summary>
        public UniTask<IRoom> CreateAsync(RoomCreateOptions options, CancellationToken cancellationToken = default)
        {
            EnsureNoRoom();
            ushort port = UtpRoomTransport.FreeUdpPort();
            transport.ListenPort = port;
            current = new LoopbackRoom(this, manager, RoomCode.FromValue(port), options.Name, options.MaxMembers, true);
            return UniTask.FromResult<IRoom>(current);
        }

        /// <summary>Builds a room pointing at <c>127.0.0.1:{code value}</c>. Codes outside the port range are not found.</summary>
        public UniTask<IRoom> JoinByCodeAsync(RoomCode code, RoomJoinOptions options, CancellationToken cancellationToken = default)
        {
            EnsureNoRoom();
            if (code.Value == 0 || code.Value > ushort.MaxValue) throw new RoomException(RoomFailure.NotFound);
            var room = new LoopbackRoom(this, manager, code, null, 0, false);
            room.Preset(NgoRoomSession.HostMemberKey, HostMemberId);
            room.Preset(NgoRoomSession.HostKey, string.Concat("127.0.0.1:", code.Value.ToString(CultureInfo.InvariantCulture)));
            room.Preset(NgoRoomSession.HostEpochKey, "1");
            current = room;
            return UniTask.FromResult<IRoom>(room);
        }

        /// <summary>Loopback room ids are their codes.</summary>
        public UniTask<IRoom> JoinByIdAsync(string roomId, RoomJoinOptions options, CancellationToken cancellationToken = default) =>
            RoomCode.TryNormalize(roomId, out var code)
                ? JoinByCodeAsync(code, options, cancellationToken)
                : throw new RoomException(RoomFailure.NotFound);

        private void EnsureNoRoom()
        {
            if (current != null) throw new InvalidOperationException("Leave the current room first.");
        }

        private sealed class LoopbackRoom : IRoom
        {
            private readonly LoopbackRoomBackend backend;
            private readonly NetworkManager manager;
            private readonly Dictionary<string, string> data = new Dictionary<string, string>();
            private readonly Dictionary<ulong, string> memberIds = new Dictionary<ulong, string>();
            private readonly List<RoomMember> members = new List<RoomMember>();
            private bool released;

            public LoopbackRoom(LoopbackRoomBackend backend, NetworkManager manager, RoomCode code, string name, int maxMembers, bool owner)
            {
                this.backend = backend;
                this.manager = manager;
                Code = code;
                Name = name;
                MaxMembers = maxMembers;
                IsOwner = owner;
                Id = code.ToString();
                manager.OnConnectionEvent += OnConnectionEvent;
                manager.OnClientStopped += OnClientStopped;
            }

            public string Id { get; }
            public RoomCode Code { get; }
            public string Name { get; }
            public bool IsOwner { get; }
            public int MaxMembers { get; }
            public bool IsJoinable { get; private set; } = true;
            public string OwnerId => HostMemberId;

            public string LocalMemberId =>
                manager.IsListening && (manager.IsServer || manager.IsConnectedClient) ? MemberId(manager.LocalClientId) : null;

            public IReadOnlyList<RoomMember> Members
            {
                get
                {
                    members.Clear();
                    if (manager.IsServer)
                    {
                        var ids = manager.ConnectedClientsIds;
                        for (int i = 0; i < ids.Count; i++)
                            members.Add(new RoomMember(MemberId(ids[i]), (long)ids[i]));
                    }
                    else if (manager.IsConnectedClient)
                    {
                        members.Add(new RoomMember(HostMemberId, 0));
                        members.Add(new RoomMember(MemberId(manager.LocalClientId), (long)manager.LocalClientId));
                    }
                    return members;
                }
            }

            public event Action MembersChanged;
            public event Action OwnerChanged { add { } remove { } }
            public event Action DataChanged;
            public event Action Closed;

            public string GetData(string key) => data.TryGetValue(key, out string value) ? value : null;

            public UniTask SetDataAsync(string key, string value)
            {
                RequireOwner();
                data[key] = value;
                DataChanged?.Invoke();
                return UniTask.CompletedTask;
            }

            // Only the host process sees the lock; admission stays the game's job.
            public UniTask SetJoinableAsync(bool joinable)
            {
                RequireOwner();
                IsJoinable = joinable;
                DataChanged?.Invoke();
                return UniTask.CompletedTask;
            }

            // Ownership is fixed; a client's room closes when its host leaves.
            public UniTask TransferOwnershipAsync(string memberId)
            {
                RequireOwner();
                return UniTask.CompletedTask;
            }

            public UniTask LeaveAsync()
            {
                Release();
                return UniTask.CompletedTask;
            }

            public void Preset(string key, string value) => data[key] = value;

            private void OnConnectionEvent(NetworkManager source, ConnectionEventData eventData)
            {
                if (manager.IsServer) MembersChanged?.Invoke();
            }

            private void OnClientStopped(bool wasHost)
            {
                if (IsOwner || released) return;
                Release();
                Closed?.Invoke();
            }

            private void Release()
            {
                if (released) return;
                released = true;
                manager.OnConnectionEvent -= OnConnectionEvent;
                manager.OnClientStopped -= OnClientStopped;
                if (backend.current == this) backend.current = null;
            }

            private void RequireOwner()
            {
                if (!IsOwner) throw new InvalidOperationException("Only the room owner can change a loopback room.");
            }

            private string MemberId(ulong clientId)
            {
                if (!memberIds.TryGetValue(clientId, out string id))
                {
                    id = string.Concat("local-", clientId.ToString(CultureInfo.InvariantCulture));
                    memberIds[clientId] = id;
                }
                return id;
            }
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: 공통 블록, `$p='PlayMode'; $a='Bun3.Unity.Rooms.Ngo.Tests'; $n='rooms-ngo'`
Expected: 6개 통과(`failed="0"`), 경고·오류 없음. 승계 테스트가 시간 초과로 실패하면 로그에서 `[Netcode]` 경고와 상태 전이를 확인한다(NGO 종료가 다음 프레임에 끝나므로 `StopNgoAsync`가 `ShutdownInProgress` 해제를 기다리는지 확인).

- [ ] **Step 5: README와 CHANGELOG**

`unity/Packages/com.bun3.unity.rooms.ngo/README.md`:

````markdown
# Bun3 Rooms NGO Adapter

Runs Netcode for GameObjects (NGO) for an `IRoom`: the owner hosts, members connect to the endpoint the host publishes in room data, and the room can survive losing its host.

## Installation

```json
{
  "com.bun3.unity.rooms": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.rooms#Bun3/jp-steam-networking",
  "com.bun3.unity.rooms.ngo": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.rooms.ngo#Bun3/jp-steam-networking"
}
```

Requires NGO 2.13.1 and UniTask. The runtime assembly is `Bun3.Unity.Rooms.Ngo`.

## Session

```csharp
var session = new NgoRoomSession(manager, room, transport, new NgoRoomSessionOptions { CanMigrate = () => inWaitingRoom });
manager.NetworkConfig.ConnectionData = payload; // the game's admission data
await session.StartAsync();                     // Connected when this returns
session.HostMigrated += RestoreLobbyState;      // spawned objects are gone after a migration
await session.LeaveAsync();
```

- `StartAsync` hosts when the local member owns the room, otherwise waits for `bun3.hostEpoch` and connects. It throws `InvalidOperationException` and closes the session when NGO cannot start or connect (including admission rejection); leaving the room is the caller's call.
- Room data written by the host, in this order: `bun3.hostMember`, `bun3.host` (endpoint), `bun3.hostEpoch` (generation, +1 per host start).
- Host loss (NGO disconnect, or the host member leaving the room) is judged one frame later. If `CanMigrate()` is false the session closes. Otherwise the member chosen by `ElectHost` (default `RoomOwnerElection.Oldest`) takes ownership and hosts, the others reconnect when the generation grows, and `HostMigrated` fires. Without `Connected` within `MigrationTimeout` (default 30 s) the session is `Failed`; the room stays joined.
- `LeaveAsync` from the owner hands the room to the elected member first when `CanMigrate()` allows it.
- Main thread only. Restarting NGO destroys spawned NetworkObjects; restore state in `HostMigrated`.

## Transports

`INgoRoomTransport.PrepareHost/PrepareClient` assign `NetworkConfig.NetworkTransport`. `UtpRoomTransport` listens on `127.0.0.1` (`ListenPort`, 0 = free port) and returns `127.0.0.1:port`.

## Loopback backend

`LoopbackRoomBackend(manager, utpRoomTransport)` serves processes on one machine (Editor, Multiplayer Play Mode), which share one platform account and cannot reach each other through a platform relay. The room code is the host's UDP port; the room id is the code. The host lists every NGO connection as `local-{clientId}`; a client lists the host and itself. Capacity, the lock and `DataChanged` exist only on the host; a client reports `MaxMembers` 0 and `IsJoinable` true. There is no migration, invite or version check, and a client's room closes when its NGO client stops.
````

`CHANGELOG.md`:

```markdown
# Changelog

## [0.1.0] - 2026-10-12

- Add the NGO room session with host migration, the Unity Transport room transport and the loopback backend.
```

- [ ] **Step 6: Commit**

```bash
cd E:/Projects/orca/workspace/bun3-kit/jp-steam-networking
git add unity/Packages/com.bun3.unity.rooms.ngo
git commit -m "✨ Add NGO room session with host migration and loopback backend" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: `com.bun3.unity.steam` — Steam 수명 관리와 define 동기화

**Files:**
- Modify: `unity/Packages/manifest.json` (Steamworks.NET 추가), `unity/Packages/packages-lock.json`(Unity가 갱신), `unity/ProjectSettings/ProjectSettings.asset`(Sync가 갱신)
- Create: `unity/Packages/com.bun3.unity.steam/package.json`, `README.md`, `CHANGELOG.md`
- Create: `unity/Packages/com.bun3.unity.steam/Runtime/Bun3.Unity.Steam.asmdef`, `SteamRuntime.cs`
- Create: `unity/Packages/com.bun3.unity.steam/Editor/Bun3.Unity.Steam.Editor.asmdef`, `AssemblyInfo.cs`, `SteamSdkSetup.cs`
- Test: `unity/Packages/com.bun3.unity.steam/Tests/Editor/Bun3.Unity.Steam.Editor.Tests.asmdef`, `SteamSdkSetupTests.cs`

**Interfaces:**
- Consumes: `Bun3.Unity.Core.PlayerLoop.PlayerLoopSystemHelper.InsertSystemBefore(Type marker, PlayerLoopSystem.UpdateFunction update, Type insertBefore)`, `TryRemoveSystem(Type)`.
- Produces:
  - `enum SteamInitResult { Ready, RestartingThroughSteam, Unavailable }`
  - `struct SteamRuntimeOptions { uint AppId; bool RestartThroughSteam; }`
  - `static class SteamRuntime` — `bool IsInitialized`, `string PersonaName`(초기화 전 빈 문자열), `SteamInitResult Initialize(SteamRuntimeOptions options, out string error)`, `void QuitWithError(string title, string message)`, `void Shutdown()`
  - `static class Bun3.Unity.Steam.Editor.SteamSdkSetup` — `const string Symbol = "BUN3_STEAMWORKS"`, `SyncInstalledAdapters()`, `DisableAdapters()`, `internal static string[] WithSymbol(string[] current, bool enabled)`

- [ ] **Step 1: 키트 프로젝트에 Steamworks.NET 추가**

`unity/Packages/manifest.json`의 `dependencies`에 알파벳 순서 위치(`com.mrdav30.fixedmathsharp.lean` 다음)로 한 줄 추가:

```json
    "com.rlabrecque.steamworks.net": "https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net",
```

- [ ] **Step 2: 패키지 골격과 실패하는 테스트 작성**

`package.json`:

```json
{
  "name": "com.bun3.unity.steam",
  "displayName": "Bun3 Steam",
  "version": "0.1.0",
  "unity": "6000.3",
  "description": "Steamworks lifetime for Unity: initialization, relaunch through Steam, callback pumping and shutdown. Requires a separately installed Steamworks.NET.",
  "author": {
    "name": "Bun3",
    "url": "https://github.com/Bun3",
    "email": "bun3.dev@gmail.com"
  },
  "dependencies": {
    "com.bun3.unity.core": "0.5.2"
  }
}
```

`Runtime/Bun3.Unity.Steam.asmdef`:

```json
{
    "name": "Bun3.Unity.Steam",
    "rootNamespace": "Bun3.Unity.Steam",
    "references": [ "Bun3.Unity.Core", "com.rlabrecque.steamworks.net" ],
    "includePlatforms": [ "Editor", "LinuxStandalone64", "macOSStandalone", "WindowsStandalone64" ],
    "autoReferenced": true,
    "defineConstraints": [ "BUN3_STEAMWORKS" ]
}
```

`Editor/Bun3.Unity.Steam.Editor.asmdef` (게이트 없음 — Steamworks.NET 없이도 컴파일돼야 Sync 메뉴가 뜬다):

```json
{
    "name": "Bun3.Unity.Steam.Editor",
    "rootNamespace": "Bun3.Unity.Steam.Editor",
    "references": [],
    "includePlatforms": [ "Editor" ],
    "autoReferenced": true
}
```

`Editor/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Bun3.Unity.Steam.Editor.Tests")]
```

`Tests/Editor/Bun3.Unity.Steam.Editor.Tests.asmdef`:

```json
{
  "name": "Bun3.Unity.Steam.Editor.Tests",
  "rootNamespace": "Bun3.Unity.Steam.Editor.Tests",
  "references": ["Bun3.Unity.Steam.Editor", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"]
}
```

`Tests/Editor/SteamSdkSetupTests.cs`:

```csharp
using NUnit.Framework;

namespace Bun3.Unity.Steam.Editor.Tests
{
    public class SteamSdkSetupTests
    {
        [Test]
        public void EnablingAppendsTheSymbolAndKeepsOthers() =>
            Assert.That(SteamSdkSetup.WithSymbol(new[] { "A", "B" }, true), Is.EqualTo(new[] { "A", "B", SteamSdkSetup.Symbol }));

        [Test]
        public void EnablingTwiceKeepsTheExistingOrder()
        {
            var current = new[] { SteamSdkSetup.Symbol, "A" };
            Assert.That(SteamSdkSetup.WithSymbol(current, true), Is.SameAs(current));
        }

        [Test]
        public void DisablingRemovesOnlyTheSymbol() =>
            Assert.That(SteamSdkSetup.WithSymbol(new[] { "A", SteamSdkSetup.Symbol, "B" }, false), Is.EqualTo(new[] { "A", "B" }));

        [Test]
        public void DisablingWhenAbsentChangesNothing()
        {
            var current = new[] { "A" };
            Assert.That(SteamSdkSetup.WithSymbol(current, false), Is.SameAs(current));
        }
    }
}
```

- [ ] **Step 3: 테스트가 실패하는지 확인**

Run: 공통 블록, `$p='EditMode'; $a='Bun3.Unity.Steam.Editor.Tests'; $n='steam-edit-red'`. 이 실행이 Steamworks.NET을 처음 내려받는다.
Expected: `NO RESULT XML`, 로그에 `error CS0103: The name 'SteamSdkSetup' does not exist`.

- [ ] **Step 4: 구현**

`Editor/SteamSdkSetup.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Bun3.Unity.Steam.Editor
{
    /// <summary>
    /// Explicitly enables the Bun3 Steam adapters on Standalone once Steamworks.NET is compiled, or disables
    /// them before removing it. Never runs on domain reload.
    /// </summary>
    public static class SteamSdkSetup
    {
        /// <summary>Scripting define that gates every Bun3 Steam assembly.</summary>
        public const string Symbol = "BUN3_STEAMWORKS";

        private const string SdkAssembly = "com.rlabrecque.steamworks.net";

        /// <summary>Adds <see cref="Symbol"/> when the Steamworks.NET assembly definition and its loaded types exist; removes it otherwise.</summary>
        [MenuItem("Tools/Bun3/Steam/Sync Installed Adapters")]
        public static void SyncInstalledAdapters()
        {
            bool installed = IsSteamworksInstalled();
            Apply(installed);
            Debug.Log(installed
                ? "Bun3 Steam adapters enabled on Standalone."
                : "Steamworks.NET was not found; Bun3 Steam adapters are disabled on Standalone. Let the SDK finish compiling before synchronizing.");
        }

        /// <summary>Removes <see cref="Symbol"/> from Standalone before Steamworks.NET is removed.</summary>
        [MenuItem("Tools/Bun3/Steam/Disable Adapters")]
        public static void DisableAdapters()
        {
            Apply(false);
            Debug.Log("Bun3 Steam adapters disabled on Standalone.");
        }

        internal static string[] WithSymbol(string[] current, bool enabled)
        {
            if (enabled == (Array.IndexOf(current, Symbol) >= 0)) return current;
            var updated = new List<string>(current);
            if (enabled) updated.Add(Symbol);
            else updated.RemoveAll(symbol => symbol == Symbol);
            return updated.ToArray();
        }

        private static void Apply(bool enabled)
        {
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone, out string[] current);
            string[] updated = WithSymbol(current, enabled);
            if (ReferenceEquals(updated, current)) return;
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, updated);
            AssetDatabase.SaveAssets();
        }

        private static bool IsSteamworksInstalled()
        {
            bool definition = false;
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)) continue;
                var asset = AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>(path);
                if (asset != null && JsonUtility.FromJson<AssemblyDefinitionName>(asset.text)?.name == SdkAssembly)
                {
                    definition = true;
                    break;
                }
            }
            return definition && Type.GetType("Steamworks.SteamAPI, " + SdkAssembly, false) != null;
        }

        [Serializable]
        private sealed class AssemblyDefinitionName
        {
            public string name = string.Empty;
        }
    }
}
```

`Runtime/SteamRuntime.cs`:

```csharp
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Bun3.Unity.Core.PlayerLoop;
using Steamworks;
using UnityEngine;

namespace Bun3.Unity.Steam
{
    /// <summary>Outcome of <see cref="SteamRuntime.Initialize"/>.</summary>
    public enum SteamInitResult
    {
        /// <summary>Steam is initialized and callbacks run every frame.</summary>
        Ready,

        /// <summary>Steam is relaunching the game; quit this process now.</summary>
        RestartingThroughSteam,

        /// <summary>Steam is not running, not signed in, or the native library is missing.</summary>
        Unavailable,
    }

    /// <summary>Options for <see cref="SteamRuntime.Initialize"/>.</summary>
    public struct SteamRuntimeOptions
    {
        /// <summary>The game's Steam App ID.</summary>
        public uint AppId;

        /// <summary>Relaunch through Steam when started outside it. Ignored in the Editor; use for release builds.</summary>
        public bool RestartThroughSteam;
    }

    /// <summary>
    /// Owns the Steamworks lifetime: initialization, relaunch through Steam, running callbacks before
    /// <c>Update</c>, and shutdown when the application quits. Main thread only.
    /// </summary>
    public static class SteamRuntime
    {
        private static class CallbackSystem
        {
        }

        /// <summary>Whether Steamworks is initialized.</summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>The signed-in user's Steam profile name; empty before initialization.</summary>
        public static string PersonaName => IsInitialized ? SteamFriends.GetPersonaName() : string.Empty;

        /// <summary>
        /// Initializes Steamworks once. The App ID is also published through the <c>SteamAppId</c> and
        /// <c>SteamGameId</c> environment variables so Editor and development runs need no <c>steam_appid.txt</c>.
        /// </summary>
        /// <param name="options">App ID and relaunch policy.</param>
        /// <param name="error">Steam's English failure message when the result is <see cref="SteamInitResult.Unavailable"/>; otherwise empty.</param>
        public static SteamInitResult Initialize(SteamRuntimeOptions options, out string error)
        {
            error = string.Empty;
            if (IsInitialized) return SteamInitResult.Ready;
            try
            {
                if (options.RestartThroughSteam && !Application.isEditor && SteamAPI.RestartAppIfNecessary(new AppId_t(options.AppId)))
                    return SteamInitResult.RestartingThroughSteam;
                // Set after the relaunch check so the variables cannot mask a launch outside Steam.
                string appId = options.AppId.ToString(CultureInfo.InvariantCulture);
                Environment.SetEnvironmentVariable("SteamAppId", appId);
                Environment.SetEnvironmentVariable("SteamGameId", appId);
                if (SteamAPI.InitEx(out error) != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                    return SteamInitResult.Unavailable;
            }
            catch (DllNotFoundException missing)
            {
                error = missing.Message;
                return SteamInitResult.Unavailable;
            }
            error = string.Empty;
            IsInitialized = true;
            PlayerLoopSystemHelper.InsertSystemBefore(typeof(CallbackSystem), SteamAPI.RunCallbacks,
                typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate));
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;
            return SteamInitResult.Ready;
        }

        /// <summary>
        /// Tells the player why the game cannot continue, then quits. Logs <paramref name="message"/> as an error;
        /// Windows players also get a blocking native error dialog, and the Editor shows a dialog and leaves
        /// Play Mode. Batch mode and other platforms only log, so unattended runs never wait on a window.
        /// Use when <see cref="Initialize"/> returns <see cref="SteamInitResult.Unavailable"/>.
        /// </summary>
        /// <param name="title">Dialog title, usually the product name.</param>
        /// <param name="message">Player-facing text in the player's language.</param>
        public static void QuitWithError(string title, string message)
        {
            Debug.LogError(message);
#if UNITY_EDITOR
            if (!Application.isBatchMode) UnityEditor.EditorUtility.DisplayDialog(title, message, "OK");
            UnityEditor.EditorApplication.ExitPlaymode();
#else
#if UNITY_STANDALONE_WIN
            if (!Application.isBatchMode) MessageBoxW(IntPtr.Zero, message, title, ErrorDialogFlags);
#endif
            Application.Quit();
#endif
        }

        /// <summary>Stops running callbacks and shuts Steamworks down. Called automatically on quit; idempotent.</summary>
        public static void Shutdown()
        {
            Application.quitting -= Shutdown;
            if (!IsInitialized) return;
            PlayerLoopSystemHelper.TryRemoveSystem(typeof(CallbackSystem));
            SteamAPI.Shutdown();
            IsInitialized = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => IsInitialized = false;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        // MB_OK | MB_ICONERROR | MB_SETFOREGROUND | MB_TOPMOST: keeps the dialog above a fullscreen game window.
        private const uint ErrorDialogFlags = 0x00000010 | 0x00010000 | 0x00040000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
#endif
    }
}
```

- [ ] **Step 5: 에디터 테스트 통과 확인**

Run: 공통 블록, `$p='EditMode'; $a='Bun3.Unity.Steam.Editor.Tests'; $n='steam-edit'`
Expected: 4개 통과, 경고·오류 없음.

- [ ] **Step 6: Sync 메뉴로 `BUN3_STEAMWORKS` 켜기**

```powershell
$proj='E:\Projects\orca\workspace\bun3-kit\jp-steam-networking\unity'
& 'E:\Unitys\6000.3.14f1\Editor\Unity.exe' -batchmode -quit -projectPath $proj -executeMethod Bun3.Unity.Steam.Editor.SteamSdkSetup.SyncInstalledAdapters -logFile "$proj\Logs\steam-sync.log" | Out-Null
Select-String -Path "$proj\Logs\steam-sync.log" -Pattern 'Bun3 Steam adapters'
Select-String -Path "$proj\ProjectSettings\ProjectSettings.asset" -Pattern '^\s+Standalone: '
```

Expected: `Bun3 Steam adapters enabled on Standalone.`, Standalone 줄 끝이 `;BUN3_STEAMWORKS`. 다른 플랫폼 줄은 그대로다(`git diff unity/ProjectSettings/ProjectSettings.asset`이 Standalone 한 줄만 바뀌었는지 확인).

- [ ] **Step 7: 게이트된 런타임이 컴파일되는지 확인**

```powershell
$proj='E:\Projects\orca\workspace\bun3-kit\jp-steam-networking\unity'
& 'E:\Unitys\6000.3.14f1\Editor\Unity.exe' -batchmode -quit -projectPath $proj -logFile "$proj\Logs\steam-compile.log" | Out-Null
Select-String -Path "$proj\Logs\steam-compile.log" -Pattern 'com\.bun3\.unity\.steam[^\r\n]*(error|warning) CS'
Test-Path "$proj\Library\ScriptAssemblies\Bun3.Unity.Steam.dll"
```

Expected: 첫 Select-String 출력 없음, 마지막 줄 `True`(게이트가 풀려 `Bun3.Unity.Steam.dll`이 생성됨).

- [ ] **Step 8: README와 CHANGELOG**

`README.md`:

````markdown
# Bun3 Steam

Steamworks lifetime for Unity: initialization, relaunch through Steam, callback pumping and shutdown. Adapter packages (`com.bun3.unity.steam.ngo`, `com.bun3.unity.rooms.steam`, `com.bun3.unity.rooms.steam.ngo`) build on it.

## Installation and activation

Steamworks.NET is not a package dependency. Install it in the game:

```json
{
  "com.rlabrecque.steamworks.net": "https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net",
  "com.bun3.unity.steam": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.steam#Bun3/jp-steam-networking"
}
```

`com.bun3.unity.core` must also be installed. Every Bun3 Steam runtime and test assembly requires the `BUN3_STEAMWORKS` define and is limited to the Editor and Standalone platforms. After Steamworks.NET finishes compiling, run **Tools > Bun3 > Steam > Sync Installed Adapters** (or `-executeMethod Bun3.Unity.Steam.Editor.SteamSdkSetup.SyncInstalledAdapters`). It adds the define to Standalone only when the Steamworks.NET assembly definition and its types are present, and keeps other defines in place. Before removing Steamworks.NET, run **Tools > Bun3 > Steam > Disable Adapters**. Nothing runs automatically on domain reload.

## Usage

```csharp
var result = SteamRuntime.Initialize(new SteamRuntimeOptions { AppId = 480, RestartThroughSteam = !Debug.isDebugBuild }, out string error);
if (result == SteamInitResult.RestartingThroughSteam) Application.Quit();
else if (result == SteamInitResult.Unavailable) SteamRuntime.QuitWithError(Application.productName, "Start Steam, sign in, and launch the game again.");
string defaultNickname = SteamRuntime.PersonaName; // empty before initialization; apply your own name rules
```

- `RestartThroughSteam` calls `SteamAPI.RestartAppIfNecessary` outside the Editor. Use it for release builds.
- The App ID is then published through the `SteamAppId`/`SteamGameId` environment variables, so the Editor and development builds started outside Steam need no `steam_appid.txt`.
- `Unavailable` means Steam is not running, not signed in, or the native library is missing; `error` carries Steam's English message.
- `QuitWithError(title, message)` logs the message, shows a native error dialog on Windows players (topmost, foreground) or an Editor dialog, then quits (the Editor leaves Play Mode). Batch mode and other platforms skip the dialog. The text is yours, so localize it.
- `PersonaName` is the signed-in user's Steam profile name, or empty before initialization.
- When ready, `SteamAPI.RunCallbacks` runs every frame before `Update` scripts, and Steamworks shuts down on `Application.quitting`. Main thread only.
````

`CHANGELOG.md`:

```markdown
# Changelog

## [0.1.0] - 2026-10-12

- Add the Steamworks lifetime runtime, the persona name, the quit-with-error dialog and the explicit `BUN3_STEAMWORKS` adapter activation menu.
```

- [ ] **Step 9: Commit**

```bash
cd E:/Projects/orca/workspace/bun3-kit/jp-steam-networking
git add unity/Packages/com.bun3.unity.steam unity/Packages/manifest.json unity/Packages/packages-lock.json unity/ProjectSettings/ProjectSettings.asset
git commit -m "✨ Add Steam lifetime runtime and BUN3_STEAMWORKS activation" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: `com.bun3.unity.steam.ngo` — SteamSocketsTransport

**Files:**
- Create: `unity/Packages/com.bun3.unity.steam.ngo/package.json`, `README.md`, `CHANGELOG.md`
- Create: `unity/Packages/com.bun3.unity.steam.ngo/Runtime/Bun3.Unity.Steam.Ngo.asmdef`, `AssemblyInfo.cs`, `ISteamSockets.cs`, `SteamSockets.cs`, `SteamSocketsTransport.cs`
- Test: `unity/Packages/com.bun3.unity.steam.ngo/Tests/Editor/Bun3.Unity.Steam.Ngo.Tests.asmdef`, `FakeSteamSockets.cs`, `SteamSocketsTransportTests.cs`

**Interfaces:**
- Consumes: `SteamRuntime.IsInitialized` (Task 3).
- Produces:
  - `sealed class SteamSocketsTransport : NetworkTransport` — `ulong TargetSteamId`, `int VirtualPort`, `Func<ulong, bool> AcceptFilter`, `bool TryGetSteamId(ulong transportId, out ulong steamId)`, `ServerClientId == 0`, `internal ISteamSockets Sockets { get; set; }`, `internal static int SendFlags(NetworkDelivery)`
  - internal `ISteamSockets`, `SteamConnectionStatus`

- [ ] **Step 1: 패키지 골격과 실패하는 테스트 작성**

`package.json`:

```json
{
  "name": "com.bun3.unity.steam.ngo",
  "displayName": "Bun3 Steam NGO Transport",
  "version": "0.1.0",
  "unity": "6000.3",
  "description": "Netcode for GameObjects transport over Steam Datagram Relay peer-to-peer sockets.",
  "author": {
    "name": "Bun3",
    "url": "https://github.com/Bun3",
    "email": "bun3.dev@gmail.com"
  },
  "dependencies": {
    "com.bun3.unity.steam": "0.1.0",
    "com.unity.netcode.gameobjects": "2.13.1"
  }
}
```

`Runtime/Bun3.Unity.Steam.Ngo.asmdef`:

```json
{
    "name": "Bun3.Unity.Steam.Ngo",
    "rootNamespace": "Bun3.Unity.Steam.Ngo",
    "references": [ "Bun3.Unity.Steam", "com.rlabrecque.steamworks.net", "Unity.Netcode.Runtime" ],
    "includePlatforms": [ "Editor", "LinuxStandalone64", "macOSStandalone", "WindowsStandalone64" ],
    "autoReferenced": true,
    "defineConstraints": [ "BUN3_STEAMWORKS" ]
}
```

`Runtime/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Bun3.Unity.Steam.Ngo.Tests")]
```

`Tests/Editor/Bun3.Unity.Steam.Ngo.Tests.asmdef`:

```json
{
  "name": "Bun3.Unity.Steam.Ngo.Tests",
  "rootNamespace": "Bun3.Unity.Steam.Ngo.Tests",
  "references": ["Bun3.Unity.Steam.Ngo", "com.rlabrecque.steamworks.net", "Unity.Netcode.Runtime", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS", "BUN3_STEAMWORKS"]
}
```

`Tests/Editor/FakeSteamSockets.cs`:

```csharp
using System;
using System.Collections.Generic;
using Steamworks;

namespace Bun3.Unity.Steam.Ngo.Tests
{
    internal sealed class FakeSteamSockets : ISteamSockets
    {
        public readonly List<uint> Accepted = new List<uint>();
        public readonly List<uint> Closed = new List<uint>();
        public readonly Dictionary<uint, uint> Groups = new Dictionary<uint, uint>();
        public readonly Dictionary<uint, ulong> RemoteIds = new Dictionary<uint, ulong>();
        public readonly Queue<(uint Connection, byte[] Data)> Inbox = new Queue<(uint Connection, byte[] Data)>();
        public readonly List<(uint Connection, int Flags, byte[] Data)> Sent = new List<(uint Connection, int Flags, byte[] Data)>();
        public uint ListenSocket = 10;
        public uint PollGroup = 20;
        public uint OutgoingConnection = 30;
        public int RelayInits;
        public bool ListenSocketClosed;
        public ulong ConnectedTo;

        public bool IsAvailable { get; set; } = true;
        public event Action<SteamConnectionStatus> StatusChanged;

        public void InitRelayNetworkAccess() => RelayInits++;
        public uint CreateListenSocket(int virtualPort) => ListenSocket;
        public void CloseListenSocket(uint socket) => ListenSocketClosed = true;
        public uint CreatePollGroup() => PollGroup;
        public void DestroyPollGroup(uint group) { }

        public uint Connect(ulong steamId, int virtualPort)
        {
            ConnectedTo = steamId;
            return OutgoingConnection;
        }

        public bool Accept(uint connection)
        {
            Accepted.Add(connection);
            return true;
        }

        public void SetPollGroup(uint connection, uint group) => Groups[connection] = group;
        public void Close(uint connection, bool linger) => Closed.Add(connection);
        public ulong GetRemoteSteamId(uint connection) => RemoteIds.TryGetValue(connection, out ulong id) ? id : 0;

        public bool Send(uint connection, ArraySegment<byte> payload, int flags)
        {
            Sent.Add((connection, flags, payload.ToArray()));
            return true;
        }

        public bool TryReceive(uint source, bool pollGroup, ref byte[] buffer, out int size, out uint connection)
        {
            size = 0;
            connection = 0;
            if (Inbox.Count == 0) return false;
            var (from, data) = Inbox.Dequeue();
            if (buffer.Length < data.Length) buffer = new byte[data.Length];
            Buffer.BlockCopy(data, 0, buffer, 0, data.Length);
            size = data.Length;
            connection = from;
            return true;
        }

        public void DiscardReceived() => Inbox.Clear();
        public int GetPing(uint connection) => 42;

        public void Raise(uint connection, ulong remote, ESteamNetworkingConnectionState state, uint listenSocket) =>
            StatusChanged?.Invoke(new SteamConnectionStatus(connection, listenSocket, remote, state));
    }
}
```

`Tests/Editor/SteamSocketsTransportTests.cs`:

```csharp
using System;
using NUnit.Framework;
using Steamworks;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Steam.Ngo.Tests
{
    public class SteamSocketsTransportTests
    {
        private const ESteamNetworkingConnectionState Connecting = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting;
        private const ESteamNetworkingConnectionState Connected = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected;
        private const ESteamNetworkingConnectionState ClosedByPeer = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer;
        private const ESteamNetworkingConnectionState ProblemDetectedLocally = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally;

        private SteamSocketsTransport transport;
        private FakeSteamSockets sockets;

        [SetUp]
        public void SetUp()
        {
            transport = new GameObject("Steam transport").AddComponent<SteamSocketsTransport>();
            sockets = new FakeSteamSockets();
            transport.Sockets = sockets;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(transport.gameObject);

        private NetworkEvent Poll(out ulong id, out ArraySegment<byte> payload) => transport.PollEvent(out id, out payload, out _);

        private void ConnectPeer(uint connection, ulong steamId)
        {
            sockets.Raise(connection, steamId, Connecting, sockets.ListenSocket);
            sockets.Raise(connection, steamId, Connected, sockets.ListenSocket);
            Assert.That(Poll(out ulong id, out _), Is.EqualTo(NetworkEvent.Connect));
            Assert.That(id, Is.EqualTo((ulong)connection));
        }

        [Test]
        public void HostAcceptsAllowedPeersAndReportsThemByConnection()
        {
            transport.AcceptFilter = steamId => steamId == 7;
            Assert.That(transport.StartServer(), Is.True);
            Assert.That(sockets.RelayInits, Is.EqualTo(1));

            sockets.Raise(101, 7, Connecting, sockets.ListenSocket);
            sockets.Raise(102, 8, Connecting, sockets.ListenSocket);
            Assert.That(sockets.Accepted, Is.EqualTo(new uint[] { 101 }));
            Assert.That(sockets.Closed, Is.EqualTo(new uint[] { 102 }));
            Assert.That(sockets.Groups[101], Is.EqualTo(sockets.PollGroup));

            sockets.Raise(101, 7, Connected, sockets.ListenSocket);
            Assert.That(Poll(out ulong id, out _), Is.EqualTo(NetworkEvent.Connect));
            Assert.That(id, Is.EqualTo(101ul));
            Assert.That(transport.TryGetSteamId(101, out ulong steamId), Is.True);
            Assert.That(steamId, Is.EqualTo(7ul));
            Assert.That(Poll(out _, out _), Is.EqualTo(NetworkEvent.Nothing));
        }

        [Test]
        public void ReceivedMessagesShareOneBuffer()
        {
            transport.StartServer();
            ConnectPeer(101, 7);
            sockets.Inbox.Enqueue((101, new byte[] { 1, 2, 3 }));
            sockets.Inbox.Enqueue((101, new byte[] { 4, 5 }));

            Assert.That(Poll(out ulong id, out var first), Is.EqualTo(NetworkEvent.Data));
            Assert.That(id, Is.EqualTo(101ul));
            Assert.That(first.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            byte[] array = first.Array;

            Assert.That(Poll(out _, out var second), Is.EqualTo(NetworkEvent.Data));
            Assert.That(second.ToArray(), Is.EqualTo(new byte[] { 4, 5 }));
            Assert.That(second.Array, Is.SameAs(array));
        }

        [Test]
        public void FirstMessageBeforeConnectedCallbackStillFollowsAConnectEvent()
        {
            transport.StartServer();
            sockets.Raise(101, 7, Connecting, sockets.ListenSocket);
            sockets.RemoteIds[101] = 7;
            sockets.Inbox.Enqueue((101, new byte[] { 9 }));

            Assert.That(Poll(out ulong id, out _), Is.EqualTo(NetworkEvent.Connect));
            Assert.That(id, Is.EqualTo(101ul));
            Assert.That(Poll(out id, out var payload), Is.EqualTo(NetworkEvent.Data));
            Assert.That(id, Is.EqualTo(101ul));
            Assert.That(payload.ToArray(), Is.EqualTo(new byte[] { 9 }));

            sockets.Raise(101, 7, Connected, sockets.ListenSocket);
            Assert.That(Poll(out _, out _), Is.EqualTo(NetworkEvent.Nothing), "A late Connected callback must not connect twice.");
            Assert.That(transport.TryGetSteamId(101, out ulong steamId), Is.True);
            Assert.That(steamId, Is.EqualTo(7ul));
        }

        [Test]
        public void PeerClosingAfterConnectRaisesOneDisconnect()
        {
            transport.StartServer();
            ConnectPeer(101, 7);
            sockets.Raise(101, 7, ClosedByPeer, sockets.ListenSocket);
            Assert.That(Poll(out ulong id, out _), Is.EqualTo(NetworkEvent.Disconnect));
            Assert.That(id, Is.EqualTo(101ul));
            Assert.That(transport.DisconnectEvent, Is.EqualTo(NetworkTransport.DisconnectEvents.ClosedByRemote));
            Assert.That(sockets.Closed, Does.Contain(101u));

            sockets.Raise(103, 9, ClosedByPeer, sockets.ListenSocket);
            Assert.That(Poll(out _, out _), Is.EqualTo(NetworkEvent.Nothing), "A connection that never connected has nothing to report.");
        }

        [Test]
        public void ClientConnectsToTheTargetAndTalksToTheHost()
        {
            Assert.That(transport.StartClient(), Is.False, "No target yet.");
            transport.TargetSteamId = 55;
            Assert.That(transport.StartClient(), Is.True);
            Assert.That(sockets.ConnectedTo, Is.EqualTo(55ul));

            sockets.Raise(sockets.OutgoingConnection, 55, Connected, 0);
            Assert.That(Poll(out ulong id, out _), Is.EqualTo(NetworkEvent.Connect));
            Assert.That(id, Is.EqualTo(transport.ServerClientId));
            Assert.That(transport.TryGetSteamId(transport.ServerClientId, out ulong host), Is.True);
            Assert.That(host, Is.EqualTo(55ul));

            transport.Send(transport.ServerClientId, new ArraySegment<byte>(new byte[] { 1, 2 }, 1, 1), NetworkDelivery.Unreliable);
            Assert.That(sockets.Sent[0].Connection, Is.EqualTo(sockets.OutgoingConnection));
            Assert.That(sockets.Sent[0].Data, Is.EqualTo(new byte[] { 2 }));

            sockets.Inbox.Enqueue((sockets.OutgoingConnection, new byte[] { 8 }));
            Assert.That(Poll(out id, out var payload), Is.EqualTo(NetworkEvent.Data));
            Assert.That(id, Is.EqualTo(transport.ServerClientId));
            Assert.That(payload.ToArray(), Is.EqualTo(new byte[] { 8 }));
            Assert.That(transport.GetCurrentRtt(transport.ServerClientId), Is.EqualTo(42ul));

            sockets.Raise(sockets.OutgoingConnection, 55, ProblemDetectedLocally, 0);
            Assert.That(Poll(out id, out _), Is.EqualTo(NetworkEvent.Disconnect));
            Assert.That(id, Is.EqualTo(transport.ServerClientId));
            Assert.That(transport.DisconnectEvent, Is.EqualTo(NetworkTransport.DisconnectEvents.ProtocolTimeout));
        }

        [TestCase(NetworkDelivery.Reliable, true)]
        [TestCase(NetworkDelivery.ReliableSequenced, true)]
        [TestCase(NetworkDelivery.ReliableFragmentedSequenced, true)]
        [TestCase(NetworkDelivery.Unreliable, false)]
        [TestCase(NetworkDelivery.UnreliableSequenced, false)]
        public void DeliveryMapsToReliableOrUnreliableWithoutNagle(NetworkDelivery delivery, bool reliable)
        {
            int flags = SteamSocketsTransport.SendFlags(delivery);
            Assert.That((flags & Constants.k_nSteamNetworkingSend_Reliable) != 0, Is.EqualTo(reliable));
            Assert.That(flags & Constants.k_nSteamNetworkingSend_NoNagle, Is.Not.Zero);
        }

        [Test]
        public void NothingStartsWithoutSteam()
        {
            sockets.IsAvailable = false;
            Assert.That(transport.StartServer(), Is.False);
            transport.TargetSteamId = 1;
            Assert.That(transport.StartClient(), Is.False);
            Assert.That(sockets.RelayInits, Is.Zero);
        }

        [Test]
        public void ShutdownClosesConnectionsAndTheListenSocket()
        {
            transport.StartServer();
            ConnectPeer(101, 7);
            transport.Shutdown();
            Assert.That(sockets.Closed, Does.Contain(101u));
            Assert.That(sockets.ListenSocketClosed, Is.True);
            Assert.That(transport.TryGetSteamId(101, out _), Is.False);
            Assert.That(transport.StartServer(), Is.True, "A shut down transport can host again.");
        }
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Run: 공통 블록, `$p='EditMode'; $a='Bun3.Unity.Steam.Ngo.Tests'; $n='steam-ngo-red'`
Expected: `NO RESULT XML`, 로그에 `error CS0246: ... 'ISteamSockets'` 또는 `'SteamSocketsTransport'`.

- [ ] **Step 3: 구현**

`Runtime/ISteamSockets.cs`:

```csharp
using System;
using Steamworks;

namespace Bun3.Unity.Steam.Ngo
{
    // Steam networking behind a seam so transport logic runs without a Steam client in tests.
    internal interface ISteamSockets
    {
        bool IsAvailable { get; }
        event Action<SteamConnectionStatus> StatusChanged;
        void InitRelayNetworkAccess();
        uint CreateListenSocket(int virtualPort);
        void CloseListenSocket(uint socket);
        uint CreatePollGroup();
        void DestroyPollGroup(uint group);
        uint Connect(ulong steamId, int virtualPort);
        bool Accept(uint connection);
        void SetPollGroup(uint connection, uint group);
        void Close(uint connection, bool linger);
        ulong GetRemoteSteamId(uint connection);
        bool Send(uint connection, ArraySegment<byte> payload, int flags);
        // Copies the next message from a poll group or a connection into buffer, growing it when needed.
        bool TryReceive(uint source, bool pollGroup, ref byte[] buffer, out int size, out uint connection);
        void DiscardReceived();
        int GetPing(uint connection);
    }

    internal readonly struct SteamConnectionStatus
    {
        public SteamConnectionStatus(uint connection, uint listenSocket, ulong remoteSteamId, ESteamNetworkingConnectionState state)
        {
            Connection = connection;
            ListenSocket = listenSocket;
            RemoteSteamId = remoteSteamId;
            State = state;
        }

        public uint Connection { get; }
        public uint ListenSocket { get; }
        public ulong RemoteSteamId { get; }
        public ESteamNetworkingConnectionState State { get; }
    }
}
```

`Runtime/SteamSockets.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using Steamworks;

namespace Bun3.Unity.Steam.Ngo
{
    internal sealed class SteamSockets : ISteamSockets, IDisposable
    {
        private const int BatchSize = 64;
        private static readonly int DataOffset = (int)Marshal.OffsetOf<SteamNetworkingMessage_t>(nameof(SteamNetworkingMessage_t.m_pData));
        private static readonly int SizeOffset = (int)Marshal.OffsetOf<SteamNetworkingMessage_t>(nameof(SteamNetworkingMessage_t.m_cbSize));
        private static readonly int ConnectionOffset = (int)Marshal.OffsetOf<SteamNetworkingMessage_t>(nameof(SteamNetworkingMessage_t.m_conn));
        private readonly IntPtr[] batch = new IntPtr[BatchSize];
        private int batchCount;
        private int batchIndex;
        private Callback<SteamNetConnectionStatusChangedCallback_t> statusCallback;

        public bool IsAvailable => SteamRuntime.IsInitialized;
        public event Action<SteamConnectionStatus> StatusChanged;

        public void InitRelayNetworkAccess()
        {
            statusCallback ??= Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
            SteamNetworkingUtils.InitRelayNetworkAccess();
        }

        public uint CreateListenSocket(int virtualPort) =>
            SteamNetworkingSockets.CreateListenSocketP2P(virtualPort, 0, null).m_HSteamListenSocket;

        public void CloseListenSocket(uint socket) => SteamNetworkingSockets.CloseListenSocket(new HSteamListenSocket(socket));

        public uint CreatePollGroup() => SteamNetworkingSockets.CreatePollGroup().m_HSteamNetPollGroup;

        public void DestroyPollGroup(uint group) => SteamNetworkingSockets.DestroyPollGroup(new HSteamNetPollGroup(group));

        public uint Connect(ulong steamId, int virtualPort)
        {
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID64(steamId);
            return SteamNetworkingSockets.ConnectP2P(ref identity, virtualPort, 0, null).m_HSteamNetConnection;
        }

        public bool Accept(uint connection) =>
            SteamNetworkingSockets.AcceptConnection(new HSteamNetConnection(connection)) == EResult.k_EResultOK;

        public void SetPollGroup(uint connection, uint group) =>
            SteamNetworkingSockets.SetConnectionPollGroup(new HSteamNetConnection(connection), new HSteamNetPollGroup(group));

        public void Close(uint connection, bool linger) =>
            SteamNetworkingSockets.CloseConnection(new HSteamNetConnection(connection), 0, null, linger);

        public ulong GetRemoteSteamId(uint connection) =>
            SteamNetworkingSockets.GetConnectionInfo(new HSteamNetConnection(connection), out SteamNetConnectionInfo_t info)
                ? info.m_identityRemote.GetSteamID64()
                : 0;

        public bool Send(uint connection, ArraySegment<byte> payload, int flags)
        {
            var pinned = GCHandle.Alloc(payload.Array, GCHandleType.Pinned);
            try
            {
                return SteamNetworkingSockets.SendMessageToConnection(new HSteamNetConnection(connection),
                    pinned.AddrOfPinnedObject() + payload.Offset, (uint)payload.Count, flags, out _) == EResult.k_EResultOK;
            }
            finally
            {
                pinned.Free();
            }
        }

        public bool TryReceive(uint source, bool pollGroup, ref byte[] buffer, out int size, out uint connection)
        {
            if (batchIndex >= batchCount)
            {
                batchIndex = 0;
                batchCount = pollGroup
                    ? SteamNetworkingSockets.ReceiveMessagesOnPollGroup(new HSteamNetPollGroup(source), batch, BatchSize)
                    : SteamNetworkingSockets.ReceiveMessagesOnConnection(new HSteamNetConnection(source), batch, BatchSize);
                if (batchCount <= 0)
                {
                    batchCount = 0;
                    size = 0;
                    connection = 0;
                    return false;
                }
            }
            IntPtr message = batch[batchIndex++];
            try
            {
                size = Marshal.ReadInt32(message, SizeOffset);
                connection = (uint)Marshal.ReadInt32(message, ConnectionOffset);
                if (buffer.Length < size) buffer = new byte[Math.Max(size, buffer.Length * 2)];
                Marshal.Copy(Marshal.ReadIntPtr(message, DataOffset), buffer, 0, size);
            }
            finally
            {
                SteamNetworkingMessage_t.Release(message);
            }
            return true;
        }

        public void DiscardReceived()
        {
            while (batchIndex < batchCount) SteamNetworkingMessage_t.Release(batch[batchIndex++]);
            batchIndex = 0;
            batchCount = 0;
        }

        public int GetPing(uint connection)
        {
            var status = new SteamNetConnectionRealTimeStatus_t();
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();
            return SteamNetworkingSockets.GetConnectionRealTimeStatus(new HSteamNetConnection(connection), ref status, 0, ref lanes) == EResult.k_EResultOK
                ? status.m_nPing
                : 0;
        }

        public void Dispose()
        {
            DiscardReceived();
            statusCallback?.Dispose();
            statusCallback = null;
        }

        private void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t data) =>
            StatusChanged?.Invoke(new SteamConnectionStatus(data.m_hConn.m_HSteamNetConnection,
                data.m_info.m_hListenSocket.m_HSteamListenSocket, data.m_info.m_identityRemote.GetSteamID64(), data.m_info.m_eState));
    }
}
```

`Runtime/SteamSocketsTransport.cs`:

```csharp
using System;
using System.Collections.Generic;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace Bun3.Unity.Steam.Ngo
{
    /// <summary>
    /// NGO transport over Steam Datagram Relay peer-to-peer sockets. The host listens on <see cref="VirtualPort"/>;
    /// clients connect to <see cref="TargetSteamId"/>. Requires <see cref="SteamRuntime.IsInitialized"/>.
    /// Receiving reuses one buffer and allocates nothing per frame. Reliable deliveries map to Steam reliable,
    /// unreliable ones to Steam unreliable, both without Nagle delay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SteamSocketsTransport : NetworkTransport
    {
        private const ulong HostTransportId = 0;
        private readonly Queue<PendingEvent> events = new Queue<PendingEvent>();
        private readonly Dictionary<ulong, ulong> steamIds = new Dictionary<ulong, ulong>();
        private byte[] buffer = new byte[1200];
        private ISteamSockets sockets;
        private uint listenSocket;
        private uint pollGroup;
        private uint hostConnection;
        private uint pendingConnection;
        private int pendingSize;
        private bool hosting;
        private bool joined;

        /// <summary>SteamID of the host a client connects to.</summary>
        public ulong TargetSteamId { get; set; }

        /// <summary>Steam virtual port shared by host and clients.</summary>
        public int VirtualPort { get; set; }

        /// <summary>Decides whether the host accepts a connecting SteamID. Null accepts everyone.</summary>
        public Func<ulong, bool> AcceptFilter { get; set; }

        /// <summary>A client's transport id for its host.</summary>
        public override ulong ServerClientId => HostTransportId;

        internal ISteamSockets Sockets
        {
            get => sockets ??= new SteamSockets();
            set => sockets = value;
        }

        /// <summary>SteamID behind a transport id: a host's clients, or the host from a client.</summary>
        public bool TryGetSteamId(ulong transportId, out ulong steamId) => steamIds.TryGetValue(transportId, out steamId);

        /// <inheritdoc/>
        public override void Initialize(NetworkManager networkManager = null)
        {
        }

        /// <inheritdoc/>
        public override bool StartServer()
        {
            if (hosting || joined || !Sockets.IsAvailable) return false;
            Sockets.StatusChanged += OnStatusChanged;
            Sockets.InitRelayNetworkAccess();
            hosting = true;
            listenSocket = Sockets.CreateListenSocket(VirtualPort);
            pollGroup = Sockets.CreatePollGroup();
            if (listenSocket != 0 && pollGroup != 0) return true;
            Shutdown();
            return false;
        }

        /// <inheritdoc/>
        public override bool StartClient()
        {
            if (hosting || joined || TargetSteamId == 0 || !Sockets.IsAvailable) return false;
            Sockets.StatusChanged += OnStatusChanged;
            Sockets.InitRelayNetworkAccess();
            joined = true;
            hostConnection = Sockets.Connect(TargetSteamId, VirtualPort);
            if (hostConnection == 0)
            {
                Shutdown();
                return false;
            }
            steamIds[HostTransportId] = TargetSteamId;
            return true;
        }

        /// <inheritdoc/>
        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            uint connection = joined ? hostConnection : (uint)clientId;
            if (connection != 0) Sockets.Send(connection, payload, SendFlags(networkDelivery));
        }

        /// <inheritdoc/>
        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            receiveTime = Time.realtimeSinceStartup;
            payload = default;
            clientId = 0;
            if (events.Count > 0)
            {
                var next = events.Dequeue();
                clientId = next.TransportId;
                return next.Kind;
            }
            if (pendingConnection != 0)
            {
                clientId = pendingConnection;
                payload = new ArraySegment<byte>(buffer, 0, pendingSize);
                pendingConnection = 0;
                return NetworkEvent.Data;
            }
            uint source = hosting ? pollGroup : hostConnection;
            if (source == 0 || !Sockets.TryReceive(source, hosting, ref buffer, out int size, out uint connection))
                return NetworkEvent.Nothing;
            if (!hosting)
            {
                clientId = HostTransportId;
                payload = new ArraySegment<byte>(buffer, 0, size);
                return NetworkEvent.Data;
            }
            clientId = connection;
            // Steam can deliver a connection's first message before its Connected callback runs.
            if (!steamIds.ContainsKey(connection))
            {
                steamIds[connection] = Sockets.GetRemoteSteamId(connection);
                pendingConnection = connection;
                pendingSize = size;
                return NetworkEvent.Connect;
            }
            payload = new ArraySegment<byte>(buffer, 0, size);
            return NetworkEvent.Data;
        }

        /// <inheritdoc/>
        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (steamIds.Remove(clientId)) Sockets.Close((uint)clientId, true);
        }

        /// <inheritdoc/>
        public override void DisconnectLocalClient()
        {
            if (hostConnection != 0) Sockets.Close(hostConnection, true);
            hostConnection = 0;
        }

        /// <inheritdoc/>
        public override ulong GetCurrentRtt(ulong clientId)
        {
            uint connection = joined ? hostConnection : (uint)clientId;
            return connection == 0 ? 0 : (ulong)Math.Max(0, Sockets.GetPing(connection));
        }

        /// <inheritdoc/>
        public override void Shutdown()
        {
            if (sockets == null) return;
            sockets.StatusChanged -= OnStatusChanged;
            foreach (var pair in steamIds)
                if (pair.Key != HostTransportId) sockets.Close((uint)pair.Key, true);
            if (hostConnection != 0) sockets.Close(hostConnection, true);
            if (listenSocket != 0) sockets.CloseListenSocket(listenSocket);
            if (pollGroup != 0) sockets.DestroyPollGroup(pollGroup);
            sockets.DiscardReceived();
            steamIds.Clear();
            events.Clear();
            listenSocket = 0;
            pollGroup = 0;
            hostConnection = 0;
            pendingConnection = 0;
            hosting = false;
            joined = false;
        }

        internal static int SendFlags(NetworkDelivery delivery) =>
            (delivery == NetworkDelivery.Unreliable || delivery == NetworkDelivery.UnreliableSequenced
                ? Constants.k_nSteamNetworkingSend_Unreliable
                : Constants.k_nSteamNetworkingSend_Reliable) | Constants.k_nSteamNetworkingSend_NoNagle;

        private void OnDestroy() => (sockets as IDisposable)?.Dispose();

        private void OnStatusChanged(SteamConnectionStatus status)
        {
            switch (status.State)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    if (!hosting || status.ListenSocket != listenSocket) return;
                    if ((AcceptFilter == null || AcceptFilter(status.RemoteSteamId)) && Sockets.Accept(status.Connection))
                        Sockets.SetPollGroup(status.Connection, pollGroup);
                    else
                        Sockets.Close(status.Connection, false);
                    return;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    if (hosting && status.ListenSocket == listenSocket)
                    {
                        if (steamIds.ContainsKey(status.Connection)) return;
                        steamIds[status.Connection] = status.RemoteSteamId;
                        events.Enqueue(new PendingEvent(NetworkEvent.Connect, status.Connection));
                    }
                    else if (joined && status.Connection == hostConnection)
                    {
                        events.Enqueue(new PendingEvent(NetworkEvent.Connect, HostTransportId));
                    }
                    return;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    Sockets.Close(status.Connection, false);
                    var reason = status.State == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                        ? DisconnectEvents.ClosedByRemote
                        : DisconnectEvents.ProtocolTimeout;
                    if (hosting && steamIds.Remove(status.Connection))
                    {
                        SetDisconnectEvent(reason);
                        events.Enqueue(new PendingEvent(NetworkEvent.Disconnect, status.Connection));
                    }
                    else if (joined && status.Connection == hostConnection)
                    {
                        hostConnection = 0;
                        SetDisconnectEvent(reason);
                        events.Enqueue(new PendingEvent(NetworkEvent.Disconnect, HostTransportId));
                    }
                    return;
            }
        }

        private readonly struct PendingEvent
        {
            public PendingEvent(NetworkEvent kind, ulong transportId)
            {
                Kind = kind;
                TransportId = transportId;
            }

            public NetworkEvent Kind { get; }
            public ulong TransportId { get; }
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: 공통 블록, `$p='EditMode'; $a='Bun3.Unity.Steam.Ngo.Tests'; $n='steam-ngo'`
Expected: 12개(케이스 포함) 통과, 경고·오류 없음. 열거형 멤버 이름 오류가 나면 설치된 `Library/PackageCache/com.rlabrecque.steamworks.net*/Runtime/autogen/SteamEnums.cs`의 `ESteamNetworkingConnectionState`에서 이름을 확인한다.

- [ ] **Step 5: README와 CHANGELOG**

`README.md`:

````markdown
# Bun3 Steam NGO Transport

Netcode for GameObjects transport over Steam Datagram Relay (`ISteamNetworkingSockets` peer-to-peer). Relay traffic is routed through Valve's network, so players never exchange IP addresses.

## Installation

Install and activate `com.bun3.unity.steam` first (Steamworks.NET plus `BUN3_STEAMWORKS`), then:

```json
{
  "com.bun3.unity.steam.ngo": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.steam.ngo#Bun3/jp-steam-networking"
}
```

Requires NGO 2.13.1. The runtime assembly is `Bun3.Unity.Steam.Ngo` (Editor and Standalone, `BUN3_STEAMWORKS`).

## Usage

Add `SteamSocketsTransport` and assign it as `NetworkConfig.NetworkTransport` before starting NGO (`com.bun3.unity.rooms.steam.ngo` does this per room).

- Host: `StartHost()` opens a P2P listen socket on `VirtualPort`. `AcceptFilter` decides which SteamIDs may connect; null accepts everyone.
- Client: set `TargetSteamId` to the host's SteamID, then `StartClient()`.
- `TryGetSteamId(transportId, out steamId)` maps NGO transport ids to SteamIDs. A client's id for its host is 0.
- Reliable deliveries use Steam reliable messages; `Unreliable` and `UnreliableSequenced` use unreliable messages (Steam does not drop older unreliable messages). All sends disable Nagle delay because NGO already batches per frame.
- Steam may hand over a connection's first message before its `Connected` callback; the transport reports `Connect` first so NGO's connection request is never lost.
- `SteamRuntime` must be initialized; otherwise `StartHost`/`StartClient` return false. Receiving reuses one buffer and releases Steam messages immediately.
````

`CHANGELOG.md`:

```markdown
# Changelog

## [0.1.0] - 2026-10-12

- Add the Steam Datagram Relay peer-to-peer NGO transport.
```

- [ ] **Step 6: Commit**

```bash
cd E:/Projects/orca/workspace/bun3-kit/jp-steam-networking
git add unity/Packages/com.bun3.unity.steam.ngo
git commit -m "✨ Add Steam Datagram Relay NGO transport" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: `com.bun3.unity.rooms.steam` — SteamLobbyBackend

**Files:**
- Create: `unity/Packages/com.bun3.unity.rooms.steam/package.json`, `README.md`, `CHANGELOG.md`
- Create: `unity/Packages/com.bun3.unity.rooms.steam/Runtime/Bun3.Unity.Rooms.Steam.asmdef`, `AssemblyInfo.cs`, `ISteamLobbyApi.cs`, `SteamLobbyApi.cs`, `SteamLobbyBackend.cs`, `SteamLobbyRoom.cs`
- Test: `unity/Packages/com.bun3.unity.rooms.steam/Tests/Editor/Bun3.Unity.Rooms.Steam.Tests.asmdef`, `FakeSteamLobbyApi.cs`, `SteamLobbyBackendTests.cs`

**Interfaces:**
- Consumes: Task 1 전부, `SteamRuntime.IsInitialized`(Task 3).
- Produces:
  - `sealed class SteamLobbyBackend : IRoomBackend, IDisposable` — `SteamLobbyBackend()`, `internal SteamLobbyBackend(ISteamLobbyApi)`, `const string CodeKey = "bun3.code"`, `VersionKey = "bun3.ver"`, `NameKey = "bun3.name"`, `OpenKey = "bun3.open"`, `JoinedKey = "bun3.joined"`, `internal const string ConnectKey = "connect"`, `internal const string ConnectCommand = "+connect_lobby"`, `internal static bool TryParseConnect(string text, out string lobbyId)`
  - internal `ISteamLobbyApi`, `SteamLobbyRoom : IRoom`

- [ ] **Step 1: 패키지 골격과 실패하는 테스트 작성**

`package.json`:

```json
{
  "name": "com.bun3.unity.rooms.steam",
  "displayName": "Bun3 Rooms Steam Backend",
  "version": "0.1.0",
  "unity": "6000.3",
  "description": "Rooms as invisible Steam lobbies: worldwide room-code search, ownership, friend invites and Join Game.",
  "author": {
    "name": "Bun3",
    "url": "https://github.com/Bun3",
    "email": "bun3.dev@gmail.com"
  },
  "dependencies": {
    "com.bun3.unity.rooms": "0.1.0",
    "com.bun3.unity.steam": "0.1.0",
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"
  }
}
```

`Runtime/Bun3.Unity.Rooms.Steam.asmdef`:

```json
{
    "name": "Bun3.Unity.Rooms.Steam",
    "rootNamespace": "Bun3.Unity.Rooms.Steam",
    "references": [ "Bun3.Unity.Rooms", "Bun3.Unity.Steam", "com.rlabrecque.steamworks.net", "UniTask" ],
    "includePlatforms": [ "Editor", "LinuxStandalone64", "macOSStandalone", "WindowsStandalone64" ],
    "autoReferenced": true,
    "defineConstraints": [ "BUN3_STEAMWORKS" ]
}
```

`Runtime/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Bun3.Unity.Rooms.Steam.Tests")]
```

`Tests/Editor/Bun3.Unity.Rooms.Steam.Tests.asmdef`:

```json
{
  "name": "Bun3.Unity.Rooms.Steam.Tests",
  "rootNamespace": "Bun3.Unity.Rooms.Steam.Tests",
  "references": ["Bun3.Unity.Rooms", "Bun3.Unity.Rooms.Steam", "com.rlabrecque.steamworks.net", "UniTask", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS", "BUN3_STEAMWORKS"]
}
```

`Tests/Editor/FakeSteamLobbyApi.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Steamworks;

namespace Bun3.Unity.Rooms.Steam.Tests
{
    internal sealed class FakeSteamLobbyApi : ISteamLobbyApi
    {
        internal sealed class Lobby
        {
            public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
            public readonly Dictionary<(ulong, string), string> MemberData = new Dictionary<(ulong, string), string>();
            public readonly List<ulong> Members = new List<ulong>();
            public ulong Owner;
            public int MaxMembers;
            public bool Joinable = true;
        }

        private const uint Success = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
        public readonly Dictionary<ulong, Lobby> Lobbies = new Dictionary<ulong, Lobby>();
        public readonly Dictionary<string, string> RichPresence = new Dictionary<string, string>();
        public readonly List<ulong> Left = new List<ulong>();
        private readonly List<ulong> results = new List<ulong>();
        private ulong nextLobby = 109775240000000001;
        public int CollisionsRemaining;
        public int Searches;
        public bool LastSearchRequiredSlot;
        public uint JoinResponse = Success;
        public ulong InviteDialogLobby;

        public bool IsAvailable { get; set; } = true;
        public ulong LocalUserId { get; set; }
        public uint ServerRealTime { get; set; } = 1000;
        public string CommandLine { get; set; } = string.Empty;

        public event Action<ulong, ulong> LobbyDataUpdated;
        public event Action<ulong, ulong, uint> LobbyChatUpdated;
        public event Action<ulong> LobbyKicked;
        public event Action<ulong> LobbyJoinRequested;
        public event Action<string> RichPresenceJoinRequested;

        public ulong AddLobby(ulong owner, string code, string version, string ownerJoined, int maxMembers = 8)
        {
            ulong id = nextLobby++;
            var lobby = new Lobby { Owner = owner, MaxMembers = maxMembers };
            lobby.Members.Add(owner);
            lobby.Data[SteamLobbyBackend.CodeKey] = code;
            lobby.Data[SteamLobbyBackend.VersionKey] = version;
            lobby.Data[SteamLobbyBackend.NameKey] = "Friend";
            lobby.MemberData[(owner, SteamLobbyBackend.JoinedKey)] = ownerJoined;
            Lobbies[id] = lobby;
            return id;
        }

        public UniTask<ulong> CreateLobbyAsync(int maxMembers, CancellationToken cancellationToken)
        {
            ulong id = nextLobby++;
            var lobby = new Lobby { Owner = LocalUserId, MaxMembers = maxMembers };
            lobby.Members.Add(LocalUserId);
            Lobbies[id] = lobby;
            return UniTask.FromResult(id);
        }

        public UniTask<int> FindLobbiesAsync(string key, string value, bool requireOpenSlot, CancellationToken cancellationToken)
        {
            Searches++;
            LastSearchRequiredSlot = requireOpenSlot;
            results.Clear();
            if (CollisionsRemaining > 0)
            {
                CollisionsRemaining--;
                results.Add(1);
                return UniTask.FromResult(1);
            }
            foreach (var pair in Lobbies)
            {
                var lobby = pair.Value;
                if (lobby.Joinable && lobby.Data.TryGetValue(key, out string found) && found == value &&
                    (!requireOpenSlot || lobby.Members.Count < lobby.MaxMembers))
                {
                    results.Add(pair.Key);
                    break;
                }
            }
            return UniTask.FromResult(results.Count);
        }

        public ulong GetLobbyByIndex(int index) => results[index];

        public UniTask<uint> JoinLobbyAsync(ulong lobby, CancellationToken cancellationToken)
        {
            if (JoinResponse != Success) return UniTask.FromResult(JoinResponse);
            if (!Lobbies.TryGetValue(lobby, out var entry))
                return UniTask.FromResult((uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist);
            entry.Members.Add(LocalUserId);
            return UniTask.FromResult(Success);
        }

        public void LeaveLobby(ulong lobby)
        {
            Left.Add(lobby);
            if (Lobbies.TryGetValue(lobby, out var entry)) entry.Members.Remove(LocalUserId);
        }

        public string GetLobbyData(ulong lobby, string key) =>
            Lobbies.TryGetValue(lobby, out var entry) && entry.Data.TryGetValue(key, out string value) ? value : string.Empty;

        public bool SetLobbyData(ulong lobby, string key, string value)
        {
            if (!Lobbies.TryGetValue(lobby, out var entry) || entry.Owner != LocalUserId) return false;
            entry.Data[key] = value;
            return true;
        }

        public string GetMemberData(ulong lobby, ulong member, string key) =>
            Lobbies.TryGetValue(lobby, out var entry) && entry.MemberData.TryGetValue((member, key), out string value) ? value : string.Empty;

        public void SetMemberData(ulong lobby, string key, string value) => Lobbies[lobby].MemberData[(LocalUserId, key)] = value;
        public int GetMemberCount(ulong lobby) => Lobbies[lobby].Members.Count;
        public int GetMemberLimit(ulong lobby) => Lobbies[lobby].MaxMembers;
        public ulong GetMemberByIndex(ulong lobby, int index) => Lobbies[lobby].Members[index];
        public ulong GetOwner(ulong lobby) => Lobbies.TryGetValue(lobby, out var entry) ? entry.Owner : 0;

        public bool SetOwner(ulong lobby, ulong member)
        {
            var entry = Lobbies[lobby];
            if (entry.Owner != LocalUserId || !entry.Members.Contains(member)) return false;
            entry.Owner = member;
            return true;
        }

        public bool SetJoinable(ulong lobby, bool joinable)
        {
            var entry = Lobbies[lobby];
            if (entry.Owner != LocalUserId) return false;
            entry.Joinable = joinable;
            return true;
        }

        public void SetRichPresence(string key, string value)
        {
            if (string.IsNullOrEmpty(value)) RichPresence.Remove(key);
            else RichPresence[key] = value;
        }

        public void OpenInviteDialog(ulong lobby) => InviteDialogLobby = lobby;

        public void RaiseDataUpdated(ulong lobby, ulong member) => LobbyDataUpdated?.Invoke(lobby, member);
        public void RaiseChatUpdated(ulong lobby, ulong user, uint change) => LobbyChatUpdated?.Invoke(lobby, user, change);
        public void RaiseKicked(ulong lobby) => LobbyKicked?.Invoke(lobby);
        public void RaiseLobbyJoinRequested(ulong lobby) => LobbyJoinRequested?.Invoke(lobby);
        public void RaiseRichPresenceJoinRequested(string connect) => RichPresenceJoinRequested?.Invoke(connect);
    }
}
```

`Tests/Editor/SteamLobbyBackendTests.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Steamworks;
using UnityEngine.TestTools;

namespace Bun3.Unity.Rooms.Steam.Tests
{
    public class SteamLobbyBackendTests
    {
        private const ulong Me = 76561198000000001;
        private const ulong Friend = 76561198000000002;
        private FakeSteamLobbyApi api;
        private SteamLobbyBackend backend;

        [SetUp]
        public void SetUp()
        {
            api = new FakeSteamLobbyApi { LocalUserId = Me };
            backend = new SteamLobbyBackend(api);
        }

        [TearDown]
        public void TearDown() => backend.Dispose();

        private static RoomCreateOptions Create => new RoomCreateOptions { MaxMembers = 8, Name = "Host", Version = "0.1" };

        private static RoomJoinOptions Join(string version = "0.1") => new RoomJoinOptions { Version = version };

        private static RoomCode Code(string text)
        {
            Assert.That(RoomCode.TryNormalize(text, out var code), Is.True);
            return code;
        }

        private static string[] Ids(IReadOnlyList<RoomMember> members)
        {
            var ids = new string[members.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = members[i].Id;
            return ids;
        }

        private static async UniTask<RoomFailure> FailureAsync(Func<UniTask<IRoom>> call)
        {
            try { await call(); }
            catch (RoomException error) { return error.Failure; }
            Assert.Fail("Expected a RoomException.");
            return default;
        }

        [UnityTest]
        public IEnumerator CreatedLobbyPublishesCodeVersionNameAndInvite() => UniTask.ToCoroutine(async () =>
        {
            var room = await backend.CreateAsync(Create);
            ulong id = ulong.Parse(room.Id);
            var lobby = api.Lobbies[id];
            Assert.That(lobby.Data[SteamLobbyBackend.CodeKey], Is.EqualTo(room.Code.ToString()));
            Assert.That(lobby.Data[SteamLobbyBackend.VersionKey], Is.EqualTo("0.1"));
            Assert.That(room.Name, Is.EqualTo("Host"));
            Assert.That(lobby.MaxMembers, Is.EqualTo(8));
            Assert.That(room.MaxMembers, Is.EqualTo(8));
            Assert.That(room.IsJoinable, Is.True);
            Assert.That(room.IsOwner, Is.True);
            Assert.That(room.LocalMemberId, Is.EqualTo(Me.ToString()));
            Assert.That(lobby.MemberData[(Me, SteamLobbyBackend.JoinedKey)], Is.EqualTo("1000"));
            Assert.That(api.RichPresence["connect"], Is.EqualTo("+connect_lobby " + room.Id));
            Assert.That(api.LastSearchRequiredSlot, Is.False);
            Assert.That(backend.TryOpenInviteOverlay(room), Is.True);
            Assert.That(api.InviteDialogLobby, Is.EqualTo(id));
        });

        [UnityTest]
        public IEnumerator CodeCollisionsRetryThenGiveUp() => UniTask.ToCoroutine(async () =>
        {
            api.CollisionsRemaining = 2;
            var room = await backend.CreateAsync(Create);
            Assert.That(api.Searches, Is.EqualTo(3));
            await room.LeaveAsync();

            api.CollisionsRemaining = 5;
            api.Searches = 0;
            Assert.That(await FailureAsync(() => backend.CreateAsync(Create)), Is.EqualTo(RoomFailure.ServiceUnavailable));
            Assert.That(api.Searches, Is.EqualTo(5));
        });

        [UnityTest]
        public IEnumerator JoiningByCodeEntersTheMatchingLobby() => UniTask.ToCoroutine(async () =>
        {
            ulong lobby = api.AddLobby(Friend, "ABC234", "0.1", "900");
            var room = await backend.JoinByCodeAsync(Code("abc234"), Join());
            Assert.That(room.Id, Is.EqualTo(lobby.ToString()));
            Assert.That(api.LastSearchRequiredSlot, Is.True);
            Assert.That(room.IsOwner, Is.False);
            Assert.That(room.OwnerId, Is.EqualTo(Friend.ToString()));
            Assert.That(room.Name, Is.EqualTo("Friend"));
            Assert.That(Ids(room.Members), Is.EqualTo(new[] { Friend.ToString(), Me.ToString() }));
            Assert.That(RoomOwnerElection.Oldest(room.Members, null), Is.EqualTo(Friend.ToString()));
        });

        [UnityTest]
        public IEnumerator JoinFailuresMapToRoomFailures() => UniTask.ToCoroutine(async () =>
        {
            Assert.That(await FailureAsync(() => backend.JoinByCodeAsync(Code("ABC234"), Join())), Is.EqualTo(RoomFailure.NotFound));

            ulong lobby = api.AddLobby(Friend, "ABC234", "0.2", "900");
            Assert.That(await FailureAsync(() => backend.JoinByCodeAsync(Code("ABC234"), Join("0.1"))), Is.EqualTo(RoomFailure.VersionMismatch));
            Assert.That(api.Left, Does.Contain(lobby));

            api.JoinResponse = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseFull;
            Assert.That(await FailureAsync(() => backend.JoinByIdAsync(lobby.ToString(), Join("0.2"))), Is.EqualTo(RoomFailure.Full));
            api.JoinResponse = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseLimited;
            Assert.That(await FailureAsync(() => backend.JoinByIdAsync(lobby.ToString(), Join("0.2"))), Is.EqualTo(RoomFailure.NotFound));
            api.JoinResponse = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseError;
            Assert.That(await FailureAsync(() => backend.JoinByIdAsync(lobby.ToString(), Join("0.2"))), Is.EqualTo(RoomFailure.ServiceUnavailable));
            api.JoinResponse = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
            Assert.That(await FailureAsync(() => backend.JoinByIdAsync("not-a-lobby", Join())), Is.EqualTo(RoomFailure.NotFound));

            var room = await backend.JoinByIdAsync(lobby.ToString(), Join("0.2"));
            Assert.That(room.Id, Is.EqualTo(lobby.ToString()), "Failed joins must not leave the backend holding a room.");
        });

        [UnityTest]
        public IEnumerator LobbyUpdatesRaiseOnlyRealChanges() => UniTask.ToCoroutine(async () =>
        {
            ulong lobby = api.AddLobby(Friend, "ABC234", "0.1", "900");
            var room = await backend.JoinByIdAsync(lobby.ToString(), Join());
            int ownerChanges = 0, memberChanges = 0;
            room.OwnerChanged += () => ownerChanges++;
            room.MembersChanged += () => memberChanges++;

            api.Lobbies[lobby].Owner = Me;
            api.RaiseDataUpdated(lobby, lobby);
            Assert.That(ownerChanges, Is.EqualTo(1));
            Assert.That(room.IsOwner, Is.True);
            api.RaiseDataUpdated(lobby, lobby);
            Assert.That(ownerChanges, Is.EqualTo(1), "Unchanged data must not repeat the event.");
            Assert.That(memberChanges, Is.EqualTo(0));

            api.Lobbies[lobby].Members.Remove(Friend);
            api.RaiseChatUpdated(lobby, Friend, (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft);
            Assert.That(memberChanges, Is.EqualTo(1));
            Assert.That(Ids(room.Members), Is.EqualTo(new[] { Me.ToString() }));
        });

        [UnityTest]
        public IEnumerator LockingHidesTheRoomAndTheInvite() => UniTask.ToCoroutine(async () =>
        {
            var room = await backend.CreateAsync(Create);
            await room.SetJoinableAsync(false);
            var lobby = api.Lobbies[ulong.Parse(room.Id)];
            Assert.That(lobby.Joinable, Is.False);
            Assert.That(lobby.Data[SteamLobbyBackend.OpenKey], Is.EqualTo("0"), "Members learn about the lock from lobby data.");
            Assert.That(room.IsJoinable, Is.False);
            Assert.That(api.RichPresence.ContainsKey("connect"), Is.False);

            var other = new SteamLobbyBackend(api);
            Assert.That(await FailureAsync(() => other.JoinByCodeAsync(room.Code, Join())), Is.EqualTo(RoomFailure.NotFound));
            other.Dispose();

            await room.SetJoinableAsync(true);
            Assert.That(room.IsJoinable, Is.True);
            Assert.That(lobby.Data[SteamLobbyBackend.OpenKey], Is.EqualTo("1"));
            Assert.That(api.RichPresence["connect"], Is.EqualTo("+connect_lobby " + room.Id));
        });

        [UnityTest]
        public IEnumerator MembersFollowTheLockAndDataChangesComeFromTheLobby() => UniTask.ToCoroutine(async () =>
        {
            ulong lobby = api.AddLobby(Friend, "ABC234", "0.1", "900", maxMembers: 6);
            var room = await backend.JoinByIdAsync(lobby.ToString(), Join());
            int dataChanges = 0;
            room.DataChanged += () => dataChanges++;
            Assert.That(room.MaxMembers, Is.EqualTo(6));
            Assert.That(room.IsJoinable, Is.True);
            Assert.That(api.RichPresence["connect"], Is.EqualTo("+connect_lobby " + room.Id));

            api.Lobbies[lobby].Data[SteamLobbyBackend.OpenKey] = "0";
            api.RaiseDataUpdated(lobby, lobby);
            Assert.That(room.IsJoinable, Is.False);
            Assert.That(api.RichPresence.ContainsKey("connect"), Is.False, "Friends of members must not see Join Game while locked.");
            Assert.That(dataChanges, Is.EqualTo(1));

            api.RaiseDataUpdated(lobby, Friend);
            Assert.That(dataChanges, Is.EqualTo(1), "Member data changes are not room data changes.");

            api.Lobbies[lobby].Data[SteamLobbyBackend.OpenKey] = "1";
            api.RaiseDataUpdated(lobby, lobby);
            Assert.That(room.IsJoinable, Is.True);
            Assert.That(api.RichPresence["connect"], Is.EqualTo("+connect_lobby " + room.Id));
            Assert.That(dataChanges, Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator KickClosesTheRoomAndFreesTheBackend() => UniTask.ToCoroutine(async () =>
        {
            ulong lobby = api.AddLobby(Friend, "ABC234", "0.1", "900");
            var room = await backend.JoinByIdAsync(lobby.ToString(), Join());
            bool closed = false;
            room.Closed += () => closed = true;
            api.RaiseKicked(lobby);
            Assert.That(closed, Is.True);
            Assert.That(api.RichPresence.ContainsKey("connect"), Is.False);
            Assert.That(api.Left, Does.Contain(lobby));
            Assert.That(await backend.CreateAsync(Create), Is.Not.Null);
        });

        [Test]
        public void OverlayAndJoinGameRequestsReachJoinRequested()
        {
            var ids = new List<string>();
            backend.JoinRequested += ids.Add;
            api.RaiseLobbyJoinRequested(123);
            api.RaiseRichPresenceJoinRequested("+connect_lobby 456");
            api.RaiseRichPresenceJoinRequested("something else");
            Assert.That(ids, Is.EqualTo(new[] { "123", "456" }));
        }

        [TestCase("game.exe +connect_lobby 109775240000000001", "109775240000000001")]
        [TestCase("+connect_lobby   42 -other", "42")]
        public void ConnectCommandYieldsTheLobbyId(string text, string expected)
        {
            Assert.That(SteamLobbyBackend.TryParseConnect(text, out string lobby), Is.True);
            Assert.That(lobby, Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("game.exe")]
        [TestCase("+connect_lobby")]
        [TestCase("+connect_lobby 0")]
        [TestCase("+connect_lobby abc")]
        [TestCase("+connect_lobby 99999999999999999999999")]
        public void OtherTextHasNoLobby(string text) =>
            Assert.That(SteamLobbyBackend.TryParseConnect(text, out _), Is.False);

        [UnityTest]
        public IEnumerator UnavailableSteamAndSecondRoomAreRejected() => UniTask.ToCoroutine(async () =>
        {
            api.IsAvailable = false;
            Assert.That(await FailureAsync(() => backend.CreateAsync(Create)), Is.EqualTo(RoomFailure.ServiceUnavailable));
            Assert.That(await FailureAsync(() => backend.JoinByIdAsync("1", Join())), Is.EqualTo(RoomFailure.ServiceUnavailable));
            api.IsAvailable = true;
            await backend.CreateAsync(Create);
            try
            {
                await backend.CreateAsync(Create);
                Assert.Fail("A backend holds one room at a time.");
            }
            catch (InvalidOperationException)
            {
            }
        });
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Run: 공통 블록, `$p='EditMode'; $a='Bun3.Unity.Rooms.Steam.Tests'; $n='rooms-steam-red'`
Expected: `NO RESULT XML`, 로그에 `error CS0246: ... 'ISteamLobbyApi'`.

- [ ] **Step 3: 구현**

`Runtime/ISteamLobbyApi.cs`:

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.Rooms.Steam
{
    // Steam lobby, friends and utility calls behind a seam so backend logic runs without a Steam client in tests.
    internal interface ISteamLobbyApi
    {
        bool IsAvailable { get; }
        ulong LocalUserId { get; }
        uint ServerRealTime { get; }
        string CommandLine { get; }
        UniTask<ulong> CreateLobbyAsync(int maxMembers, CancellationToken cancellationToken);
        // Returns the match count (at most one), or a negative number when the request failed.
        UniTask<int> FindLobbiesAsync(string key, string value, bool requireOpenSlot, CancellationToken cancellationToken);
        ulong GetLobbyByIndex(int index);
        UniTask<uint> JoinLobbyAsync(ulong lobby, CancellationToken cancellationToken);
        void LeaveLobby(ulong lobby);
        string GetLobbyData(ulong lobby, string key);
        bool SetLobbyData(ulong lobby, string key, string value);
        string GetMemberData(ulong lobby, ulong member, string key);
        void SetMemberData(ulong lobby, string key, string value);
        int GetMemberCount(ulong lobby);
        int GetMemberLimit(ulong lobby);
        ulong GetMemberByIndex(ulong lobby, int index);
        ulong GetOwner(ulong lobby);
        bool SetOwner(ulong lobby, ulong member);
        bool SetJoinable(ulong lobby, bool joinable);
        // A null or empty value removes the key.
        void SetRichPresence(string key, string value);
        void OpenInviteDialog(ulong lobby);
        event Action<ulong, ulong> LobbyDataUpdated;
        event Action<ulong, ulong, uint> LobbyChatUpdated;
        event Action<ulong> LobbyKicked;
        event Action<ulong> LobbyJoinRequested;
        event Action<string> RichPresenceJoinRequested;
    }
}
```

`Runtime/SteamLobbyApi.cs`:

```csharp
using System;
using System.Threading;
using Bun3.Unity.Steam;
using Cysharp.Threading.Tasks;
using Steamworks;

namespace Bun3.Unity.Rooms.Steam
{
    internal sealed class SteamLobbyApi : ISteamLobbyApi, IDisposable
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
        private readonly Callback<LobbyDataUpdate_t> dataUpdated;
        private readonly Callback<LobbyChatUpdate_t> chatUpdated;
        private readonly Callback<LobbyKicked_t> kicked;
        private readonly Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
        private readonly Callback<GameRichPresenceJoinRequested_t> presenceJoinRequested;

        public SteamLobbyApi()
        {
            dataUpdated = Callback<LobbyDataUpdate_t>.Create(data => LobbyDataUpdated?.Invoke(data.m_ulSteamIDLobby, data.m_ulSteamIDMember));
            chatUpdated = Callback<LobbyChatUpdate_t>.Create(data =>
                LobbyChatUpdated?.Invoke(data.m_ulSteamIDLobby, data.m_ulSteamIDUserChanged, data.m_rgfChatMemberStateChange));
            kicked = Callback<LobbyKicked_t>.Create(data => LobbyKicked?.Invoke(data.m_ulSteamIDLobby));
            lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(data => LobbyJoinRequested?.Invoke(data.m_steamIDLobby.m_SteamID));
            presenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(data => RichPresenceJoinRequested?.Invoke(data.m_rgchConnect));
        }

        public bool IsAvailable => SteamRuntime.IsInitialized;
        public ulong LocalUserId => SteamUser.GetSteamID().m_SteamID;
        public uint ServerRealTime => SteamUtils.GetServerRealTime();
        public string CommandLine => Environment.CommandLine;

        public event Action<ulong, ulong> LobbyDataUpdated;
        public event Action<ulong, ulong, uint> LobbyChatUpdated;
        public event Action<ulong> LobbyKicked;
        public event Action<ulong> LobbyJoinRequested;
        public event Action<string> RichPresenceJoinRequested;

        public UniTask<ulong> CreateLobbyAsync(int maxMembers, CancellationToken cancellationToken) =>
            CallAsync<LobbyCreated_t, ulong>(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeInvisible, maxMembers),
                (data, ioFailure) => ioFailure || data.m_eResult != EResult.k_EResultOK ? 0 : data.m_ulSteamIDLobby, cancellationToken);

        public UniTask<int> FindLobbiesAsync(string key, string value, bool requireOpenSlot, CancellationToken cancellationToken)
        {
            SteamMatchmaking.AddRequestLobbyListStringFilter(key, value, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            if (requireOpenSlot) SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(1);
            return CallAsync<LobbyMatchList_t, int>(SteamMatchmaking.RequestLobbyList(),
                (data, ioFailure) => ioFailure ? -1 : (int)data.m_nLobbiesMatching, cancellationToken);
        }

        public ulong GetLobbyByIndex(int index) => SteamMatchmaking.GetLobbyByIndex(index).m_SteamID;

        public UniTask<uint> JoinLobbyAsync(ulong lobby, CancellationToken cancellationToken) =>
            CallAsync<LobbyEnter_t, uint>(SteamMatchmaking.JoinLobby(new CSteamID(lobby)),
                (data, ioFailure) => ioFailure ? (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseError : data.m_EChatRoomEnterResponse,
                cancellationToken);

        public void LeaveLobby(ulong lobby) => SteamMatchmaking.LeaveLobby(new CSteamID(lobby));
        public string GetLobbyData(ulong lobby, string key) => SteamMatchmaking.GetLobbyData(new CSteamID(lobby), key);
        public bool SetLobbyData(ulong lobby, string key, string value) => SteamMatchmaking.SetLobbyData(new CSteamID(lobby), key, value);

        public string GetMemberData(ulong lobby, ulong member, string key) =>
            SteamMatchmaking.GetLobbyMemberData(new CSteamID(lobby), new CSteamID(member), key);

        public void SetMemberData(ulong lobby, string key, string value) => SteamMatchmaking.SetLobbyMemberData(new CSteamID(lobby), key, value);
        public int GetMemberCount(ulong lobby) => SteamMatchmaking.GetNumLobbyMembers(new CSteamID(lobby));
        public int GetMemberLimit(ulong lobby) => SteamMatchmaking.GetLobbyMemberLimit(new CSteamID(lobby));
        public ulong GetMemberByIndex(ulong lobby, int index) => SteamMatchmaking.GetLobbyMemberByIndex(new CSteamID(lobby), index).m_SteamID;
        public ulong GetOwner(ulong lobby) => SteamMatchmaking.GetLobbyOwner(new CSteamID(lobby)).m_SteamID;
        public bool SetOwner(ulong lobby, ulong member) => SteamMatchmaking.SetLobbyOwner(new CSteamID(lobby), new CSteamID(member));
        public bool SetJoinable(ulong lobby, bool joinable) => SteamMatchmaking.SetLobbyJoinable(new CSteamID(lobby), joinable);
        public void SetRichPresence(string key, string value) => SteamFriends.SetRichPresence(key, value ?? string.Empty);
        public void OpenInviteDialog(ulong lobby) => SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(lobby));

        public void Dispose()
        {
            dataUpdated.Dispose();
            chatUpdated.Dispose();
            kicked.Dispose();
            lobbyJoinRequested.Dispose();
            presenceJoinRequested.Dispose();
        }

        private static async UniTask<TResult> CallAsync<TCallback, TResult>(SteamAPICall_t call, Func<TCallback, bool, TResult> map,
            CancellationToken cancellationToken)
        {
            var source = new UniTaskCompletionSource<TResult>();
            using (var result = CallResult<TCallback>.Create((data, ioFailure) => source.TrySetResult(map(data, ioFailure))))
            {
                result.Set(call);
                try
                {
                    return await source.Task.Timeout(RequestTimeout).AttachExternalCancellation(cancellationToken);
                }
                catch (TimeoutException)
                {
                    throw new RoomException(RoomFailure.ServiceUnavailable);
                }
            }
        }
    }
}
```

`Runtime/SteamLobbyBackend.cs`:

```csharp
using System;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Steamworks;

namespace Bun3.Unity.Rooms.Steam
{
    /// <summary>
    /// Rooms as invisible Steam lobbies. Room codes are searched worldwide through lobby data; friends join
    /// through the overlay invite or "Join Game", which arrive as <see cref="JoinRequested"/>. Construct after
    /// <c>SteamRuntime.Initialize</c>. While Steam is not initialized every call fails with
    /// <see cref="RoomFailure.ServiceUnavailable"/>. Main thread only.
    /// </summary>
    public sealed class SteamLobbyBackend : IRoomBackend, IDisposable
    {
        /// <summary>Lobby data key holding the room code.</summary>
        public const string CodeKey = "bun3.code";

        /// <summary>Lobby data key holding the room version string.</summary>
        public const string VersionKey = "bun3.ver";

        /// <summary>Lobby data key holding the room name.</summary>
        public const string NameKey = "bun3.name";

        /// <summary>Lobby data key the owner sets to <c>"0"</c> while the room is closed to new members.</summary>
        public const string OpenKey = "bun3.open";

        /// <summary>Member data key holding each member's join time (Steam server time).</summary>
        public const string JoinedKey = "bun3.joined";

        internal const string ConnectKey = "connect";
        internal const string ConnectCommand = "+connect_lobby";
        private const int MaxCodeAttempts = 5;
        private readonly ISteamLobbyApi api;
        private SteamLobbyRoom current;

        /// <summary>Creates a backend on the initialized Steam client.</summary>
        public SteamLobbyBackend() : this(new SteamLobbyApi())
        {
        }

        internal SteamLobbyBackend(ISteamLobbyApi api)
        {
            this.api = api;
            api.LobbyJoinRequested += OnLobbyJoinRequested;
            api.RichPresenceJoinRequested += OnRichPresenceJoinRequested;
            // Raised next frame so the creator can subscribe first.
            if (TryParseConnect(api.CommandLine, out string launchLobby))
                UniTask.Post(() => JoinRequested?.Invoke(launchLobby));
        }

        /// <inheritdoc/>
        public event Action<string> JoinRequested;

        /// <summary>Creates an invisible lobby with an unused code. Gives up after five code collisions.</summary>
        public async UniTask<IRoom> CreateAsync(RoomCreateOptions options, CancellationToken cancellationToken = default)
        {
            EnsureReady();
            RoomCode code = default;
            bool unique = false;
            for (int attempt = 0; attempt < MaxCodeAttempts && !unique; attempt++)
            {
                code = RoomCode.Generate();
                int found = await api.FindLobbiesAsync(CodeKey, code.ToString(), false, cancellationToken);
                if (found < 0) throw new RoomException(RoomFailure.ServiceUnavailable);
                unique = found == 0;
            }
            if (!unique) throw new RoomException(RoomFailure.ServiceUnavailable);
            ulong lobby = await api.CreateLobbyAsync(options.MaxMembers, cancellationToken);
            if (lobby == 0) throw new RoomException(RoomFailure.ServiceUnavailable);
            api.SetLobbyData(lobby, CodeKey, code.ToString());
            api.SetLobbyData(lobby, VersionKey, options.Version ?? string.Empty);
            api.SetLobbyData(lobby, NameKey, options.Name ?? string.Empty);
            return Enter(lobby, code);
        }

        /// <inheritdoc/>
        public async UniTask<IRoom> JoinByCodeAsync(RoomCode code, RoomJoinOptions options, CancellationToken cancellationToken = default)
        {
            EnsureReady();
            int found = await api.FindLobbiesAsync(CodeKey, code.ToString(), true, cancellationToken);
            if (found < 0) throw new RoomException(RoomFailure.ServiceUnavailable);
            if (found == 0) throw new RoomException(RoomFailure.NotFound);
            return await JoinAsync(api.GetLobbyByIndex(0), options, cancellationToken);
        }

        /// <summary>Joins by decimal lobby SteamID, as delivered by <see cref="JoinRequested"/>.</summary>
        public UniTask<IRoom> JoinByIdAsync(string roomId, RoomJoinOptions options, CancellationToken cancellationToken = default)
        {
            EnsureReady();
            return ulong.TryParse(roomId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong lobby) && lobby != 0
                ? JoinAsync(lobby, options, cancellationToken)
                : throw new RoomException(RoomFailure.NotFound);
        }

        /// <summary>Opens the Steam overlay invite dialog for the current room.</summary>
        public bool TryOpenInviteOverlay(IRoom room)
        {
            if (!(room is SteamLobbyRoom lobby) || lobby != current || !api.IsAvailable) return false;
            api.OpenInviteDialog(lobby.Lobby);
            return true;
        }

        /// <summary>Stops listening for Steam callbacks.</summary>
        public void Dispose()
        {
            api.LobbyJoinRequested -= OnLobbyJoinRequested;
            api.RichPresenceJoinRequested -= OnRichPresenceJoinRequested;
            (api as IDisposable)?.Dispose();
        }

        internal static bool TryParseConnect(string text, out string lobbyId)
        {
            lobbyId = null;
            if (string.IsNullOrEmpty(text)) return false;
            int start = text.IndexOf(ConnectCommand, StringComparison.Ordinal);
            if (start < 0) return false;
            start += ConnectCommand.Length;
            while (start < text.Length && text[start] == ' ') start++;
            int end = start;
            while (end < text.Length && text[end] >= '0' && text[end] <= '9') end++;
            if (end == start ||
                !ulong.TryParse(text.Substring(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out ulong lobby) ||
                lobby == 0)
                return false;
            lobbyId = lobby.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        internal void Release(SteamLobbyRoom room)
        {
            if (current == room) current = null;
        }

        private async UniTask<IRoom> JoinAsync(ulong lobby, RoomJoinOptions options, CancellationToken cancellationToken)
        {
            uint response = await api.JoinLobbyAsync(lobby, cancellationToken);
            if (response != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
                throw new RoomException(
                    response == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseFull ? RoomFailure.Full :
                    response == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseError ? RoomFailure.ServiceUnavailable :
                    RoomFailure.NotFound);
            if (!string.Equals(api.GetLobbyData(lobby, VersionKey), options.Version ?? string.Empty, StringComparison.Ordinal))
            {
                api.LeaveLobby(lobby);
                throw new RoomException(RoomFailure.VersionMismatch);
            }
            if (!RoomCode.TryNormalize(api.GetLobbyData(lobby, CodeKey), out RoomCode code))
            {
                api.LeaveLobby(lobby);
                throw new RoomException(RoomFailure.NotFound);
            }
            return Enter(lobby, code);
        }

        private IRoom Enter(ulong lobby, RoomCode code)
        {
            api.SetMemberData(lobby, JoinedKey, api.ServerRealTime.ToString(CultureInfo.InvariantCulture));
            current = new SteamLobbyRoom(this, api, lobby, code);
            return current;
        }

        private void EnsureReady()
        {
            if (!api.IsAvailable) throw new RoomException(RoomFailure.ServiceUnavailable);
            if (current != null) throw new InvalidOperationException("Leave the current room first.");
        }

        private void OnLobbyJoinRequested(ulong lobby) => JoinRequested?.Invoke(lobby.ToString(CultureInfo.InvariantCulture));

        private void OnRichPresenceJoinRequested(string connect)
        {
            if (TryParseConnect(connect, out string lobby)) JoinRequested?.Invoke(lobby);
        }
    }
}
```

`Runtime/SteamLobbyRoom.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using Steamworks;

namespace Bun3.Unity.Rooms.Steam
{
    internal sealed class SteamLobbyRoom : IRoom
    {
        private const uint LeftMask =
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft |
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeDisconnected |
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeKicked |
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeBanned;

        private readonly SteamLobbyBackend backend;
        private readonly ISteamLobbyApi api;
        private readonly List<RoomMember> members = new List<RoomMember>();
        private readonly string connectValue;
        private ulong owner;
        private bool released;

        public SteamLobbyRoom(SteamLobbyBackend backend, ISteamLobbyApi api, ulong lobby, RoomCode code)
        {
            this.backend = backend;
            this.api = api;
            Lobby = lobby;
            Code = code;
            Id = lobby.ToString(CultureInfo.InvariantCulture);
            LocalMemberId = api.LocalUserId.ToString(CultureInfo.InvariantCulture);
            connectValue = string.Concat(SteamLobbyBackend.ConnectCommand, " ", Id);
            api.LobbyDataUpdated += OnLobbyDataUpdated;
            api.LobbyChatUpdated += OnLobbyChatUpdated;
            api.LobbyKicked += OnLobbyKicked;
            Refresh();
            PublishInvite();
        }

        internal ulong Lobby { get; }
        public string Id { get; }
        public RoomCode Code { get; }
        public string Name { get; private set; }
        public IReadOnlyList<RoomMember> Members => members;
        public int MaxMembers => api.GetMemberLimit(Lobby);
        public string LocalMemberId { get; }
        public string OwnerId { get; private set; }
        public bool IsOwner => OwnerId == LocalMemberId;
        public bool IsJoinable { get; private set; } = true;
        public event Action MembersChanged;
        public event Action OwnerChanged;
        public event Action DataChanged;
        public event Action Closed;

        public string GetData(string key)
        {
            string value = api.GetLobbyData(Lobby, key);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        public UniTask SetDataAsync(string key, string value)
        {
            RequireOwner();
            if (!api.SetLobbyData(Lobby, key, value ?? string.Empty)) throw new RoomException(RoomFailure.ServiceUnavailable);
            return UniTask.CompletedTask;
        }

        // Steam has no getter for the joinable flag, so the owner mirrors it in lobby data for every member.
        public UniTask SetJoinableAsync(bool joinable)
        {
            RequireOwner();
            if (!api.SetJoinable(Lobby, joinable) || !api.SetLobbyData(Lobby, SteamLobbyBackend.OpenKey, joinable ? "1" : "0"))
                throw new RoomException(RoomFailure.ServiceUnavailable);
            if (IsJoinable != joinable)
            {
                IsJoinable = joinable;
                PublishInvite();
            }
            return UniTask.CompletedTask;
        }

        public UniTask TransferOwnershipAsync(string memberId)
        {
            RequireOwner();
            if (!ulong.TryParse(memberId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong member) || !api.SetOwner(Lobby, member))
                throw new RoomException(RoomFailure.ServiceUnavailable);
            return UniTask.CompletedTask;
        }

        public UniTask LeaveAsync()
        {
            if (Release()) api.LeaveLobby(Lobby);
            return UniTask.CompletedTask;
        }

        // member == lobby means lobby data changed; otherwise it is that member's data.
        private void OnLobbyDataUpdated(ulong lobby, ulong member)
        {
            if (lobby != Lobby) return;
            Refresh();
            if (member == lobby) DataChanged?.Invoke();
        }

        private void OnLobbyChatUpdated(ulong lobby, ulong user, uint change)
        {
            if (lobby != Lobby) return;
            if (user == api.LocalUserId && (change & LeftMask) != 0) RemoveLocal();
            else Refresh();
        }

        private void OnLobbyKicked(ulong lobby)
        {
            if (lobby == Lobby) RemoveLocal();
        }

        private void RemoveLocal()
        {
            if (!Release()) return;
            api.LeaveLobby(Lobby);
            Closed?.Invoke();
        }

        private bool Release()
        {
            if (released) return false;
            released = true;
            api.LobbyDataUpdated -= OnLobbyDataUpdated;
            api.LobbyChatUpdated -= OnLobbyChatUpdated;
            api.LobbyKicked -= OnLobbyKicked;
            api.SetRichPresence(SteamLobbyBackend.ConnectKey, null);
            backend.Release(this);
            return true;
        }

        private void Refresh()
        {
            Name = GetData(SteamLobbyBackend.NameKey);
            bool joinable = GetData(SteamLobbyBackend.OpenKey) != "0";
            if (joinable != IsJoinable)
            {
                IsJoinable = joinable;
                PublishInvite();
            }
            bool membersChanged = RefreshMembers();
            ulong latestOwner = api.GetOwner(Lobby);
            bool ownerChanged = latestOwner != owner || OwnerId == null;
            if (ownerChanged)
            {
                owner = latestOwner;
                OwnerId = latestOwner.ToString(CultureInfo.InvariantCulture);
            }
            if (membersChanged) MembersChanged?.Invoke();
            if (ownerChanged) OwnerChanged?.Invoke();
        }

        private bool RefreshMembers()
        {
            int count = api.GetMemberCount(Lobby);
            bool changed = count != members.Count;
            for (int i = 0; i < count; i++)
            {
                ulong steamId = api.GetMemberByIndex(Lobby, i);
                long joinedAt = long.TryParse(api.GetMemberData(Lobby, steamId, SteamLobbyBackend.JoinedKey),
                    NumberStyles.None, CultureInfo.InvariantCulture, out long value) ? value : long.MaxValue;
                string id = steamId.ToString(CultureInfo.InvariantCulture);
                if (i < members.Count)
                {
                    if (members[i].Id == id && members[i].JoinedAt == joinedAt) continue;
                    members[i] = new RoomMember(id, joinedAt);
                    changed = true;
                }
                else
                {
                    members.Add(new RoomMember(id, joinedAt));
                }
            }
            if (members.Count > count) members.RemoveRange(count, members.Count - count);
            return changed;
        }

        // Friends see "Join Game" only while the room is open.
        private void PublishInvite() => api.SetRichPresence(SteamLobbyBackend.ConnectKey, IsJoinable ? connectValue : null);

        private void RequireOwner()
        {
            if (!IsOwner) throw new InvalidOperationException("Only the lobby owner can change the room.");
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: 공통 블록, `$p='EditMode'; $a='Bun3.Unity.Rooms.Steam.Tests'; $n='rooms-steam'`
Expected: 모든 케이스 통과, 경고·오류 없음.

- [ ] **Step 5: README와 CHANGELOG**

`README.md`:

````markdown
# Bun3 Rooms Steam Backend

`IRoomBackend` on Steam lobbies. Rooms are invisible lobbies (found by search, hidden from friends lists) whose six-character code lives in lobby data, so players keep typing short codes while friends can also join from the Steam overlay.

## Installation

Install and activate `com.bun3.unity.steam` first (Steamworks.NET plus `BUN3_STEAMWORKS`), plus `com.bun3.unity.rooms`, then:

```json
{
  "com.bun3.unity.rooms.steam": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.rooms.steam#Bun3/jp-steam-networking"
}
```

The runtime assembly is `Bun3.Unity.Rooms.Steam` (Editor and Standalone, `BUN3_STEAMWORKS`).

## Behaviour

- Construct `SteamLobbyBackend` after `SteamRuntime.Initialize` returns `Ready`, and dispose it on shutdown. While Steam is not initialized every operation fails with `ServiceUnavailable`.
- Lobby data: `bun3.code`, `bun3.ver`, `bun3.name`, `bun3.open` (owner; `"0"` while locked); member data `bun3.joined` (each member's Steam server time, used as `RoomMember.JoinedAt`). Room and member ids are decimal SteamIDs. `MaxMembers` is the lobby member limit.
- Create: a new code is checked against existing joinable lobbies (up to five attempts), then an invisible lobby is created.
- Join by code: worldwide search on `bun3.code` with a free slot. No match → `NotFound`; enter response Full → `Full`, Error → `ServiceUnavailable`, others → `NotFound`. A different `bun3.ver` leaves the lobby and reports `VersionMismatch`. Steam requests time out after 15 seconds (`ServiceUnavailable`).
- `SetJoinableAsync(false)` hides the lobby from search and stores `bun3.open = "0"`. Every member reads it into `IsJoinable` and clears its own `connect` rich presence, so no friend sees "Join Game" during a match. Locked rooms report `NotFound`.
- `DataChanged` fires on each lobby data update (not on member data updates); re-read the keys you need.
- Invites: while in an open room each member publishes rich presence `connect = "+connect_lobby <lobby id>"`. Overlay invites (`GameLobbyJoinRequested_t`), "Join Game" (`GameRichPresenceJoinRequested_t`) and a `+connect_lobby <id>` launch argument raise `JoinRequested` with the lobby id; the launch argument is raised on the next frame. `TryOpenInviteOverlay` opens the Steam invite dialog.
- Kicks, lobby loss and the local member's own leave notification raise `Closed`.

Known limit: members write their own `bun3.joined`, so a lobby member could claim an early time to win waiting-room host election.
````

`CHANGELOG.md`:

```markdown
# Changelog

## [0.1.0] - 2026-10-12

- Add the Steam lobby room backend with code search, ownership, capacity, a lock every member sees, data change events, invites and Join Game.
```

- [ ] **Step 6: Commit**

```bash
cd E:/Projects/orca/workspace/bun3-kit/jp-steam-networking
git add unity/Packages/com.bun3.unity.rooms.steam
git commit -m "✨ Add Steam lobby room backend with invites" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: `com.bun3.unity.rooms.steam.ngo` — SteamRoomTransport

**Files:**
- Create: `unity/Packages/com.bun3.unity.rooms.steam.ngo/package.json`, `README.md`, `CHANGELOG.md`
- Create: `unity/Packages/com.bun3.unity.rooms.steam.ngo/Runtime/Bun3.Unity.Rooms.Steam.Ngo.asmdef`, `AssemblyInfo.cs`, `SteamRoomTransport.cs`
- Test: `unity/Packages/com.bun3.unity.rooms.steam.ngo/Tests/Editor/Bun3.Unity.Rooms.Steam.Ngo.Tests.asmdef`, `SteamRoomTransportTests.cs`

**Interfaces:**
- Consumes: `IRoom`(Task 1), `INgoRoomTransport`(Task 2), `SteamSocketsTransport`(Task 4).
- Produces: `sealed class SteamRoomTransport(SteamSocketsTransport) : INgoRoomTransport`, `internal static bool IsMember(IRoom, ulong)`, `internal static ulong ParseEndpoint(string)`

- [ ] **Step 1: 패키지 골격과 실패하는 테스트 작성**

`package.json`:

```json
{
  "name": "com.bun3.unity.rooms.steam.ngo",
  "displayName": "Bun3 Rooms Steam NGO Transport",
  "version": "0.1.0",
  "unity": "6000.3",
  "description": "Connects an NGO room session to the Steam Datagram Relay transport, admitting only room members.",
  "author": {
    "name": "Bun3",
    "url": "https://github.com/Bun3",
    "email": "bun3.dev@gmail.com"
  },
  "dependencies": {
    "com.bun3.unity.rooms.ngo": "0.1.0",
    "com.bun3.unity.steam.ngo": "0.1.0"
  }
}
```

`Runtime/Bun3.Unity.Rooms.Steam.Ngo.asmdef`:

```json
{
    "name": "Bun3.Unity.Rooms.Steam.Ngo",
    "rootNamespace": "Bun3.Unity.Rooms.Steam.Ngo",
    "references": [ "Bun3.Unity.Rooms", "Bun3.Unity.Rooms.Ngo", "Bun3.Unity.Steam.Ngo", "Unity.Netcode.Runtime" ],
    "includePlatforms": [ "Editor", "LinuxStandalone64", "macOSStandalone", "WindowsStandalone64" ],
    "autoReferenced": true,
    "defineConstraints": [ "BUN3_STEAMWORKS" ]
}
```

`Runtime/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Bun3.Unity.Rooms.Steam.Ngo.Tests")]
```

`Tests/Editor/Bun3.Unity.Rooms.Steam.Ngo.Tests.asmdef`:

```json
{
  "name": "Bun3.Unity.Rooms.Steam.Ngo.Tests",
  "rootNamespace": "Bun3.Unity.Rooms.Steam.Ngo.Tests",
  "references": ["Bun3.Unity.Rooms", "Bun3.Unity.Rooms.Steam.Ngo", "UniTask", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS", "BUN3_STEAMWORKS"]
}
```

`Tests/Editor/SteamRoomTransportTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Bun3.Unity.Rooms.Steam.Ngo.Tests
{
    public class SteamRoomTransportTests
    {
        private sealed class MembersRoom : IRoom
        {
            private readonly List<RoomMember> members = new List<RoomMember>();

            public MembersRoom(params string[] ids)
            {
                foreach (string id in ids) members.Add(new RoomMember(id, 0));
            }

            public string Id => "1";
            public RoomCode Code => default;
            public string Name => null;
            public IReadOnlyList<RoomMember> Members => members;
            public int MaxMembers => 0;
            public string LocalMemberId => members[0].Id;
            public string OwnerId => members[0].Id;
            public bool IsOwner => true;
            public bool IsJoinable => true;
            public event Action MembersChanged { add { } remove { } }
            public event Action OwnerChanged { add { } remove { } }
            public event Action DataChanged { add { } remove { } }
            public event Action Closed { add { } remove { } }
            public string GetData(string key) => null;
            public UniTask SetDataAsync(string key, string value) => UniTask.CompletedTask;
            public UniTask SetJoinableAsync(bool joinable) => UniTask.CompletedTask;
            public UniTask TransferOwnershipAsync(string memberId) => UniTask.CompletedTask;
            public UniTask LeaveAsync() => UniTask.CompletedTask;
        }

        [Test]
        public void OnlyCurrentRoomMembersAreAccepted()
        {
            var room = new MembersRoom("76561198000000001", "76561198000000002");
            Assert.That(SteamRoomTransport.IsMember(room, 76561198000000002), Is.True);
            Assert.That(SteamRoomTransport.IsMember(room, 76561198000000003), Is.False);
        }

        [Test]
        public void HostEndpointIsTheHostSteamId() =>
            Assert.That(SteamRoomTransport.ParseEndpoint("76561198000000001"), Is.EqualTo(76561198000000001ul));

        [TestCase("")]
        [TestCase(null)]
        [TestCase("0")]
        [TestCase("127.0.0.1:7777")]
        [TestCase(" 1")]
        public void MalformedEndpointsAreRejected(string endpoint) =>
            Assert.Throws<FormatException>(() => SteamRoomTransport.ParseEndpoint(endpoint));
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Run: 공통 블록, `$p='EditMode'; $a='Bun3.Unity.Rooms.Steam.Ngo.Tests'; $n='rooms-steam-ngo-red'`
Expected: `NO RESULT XML`, 로그에 `error CS0103: The name 'SteamRoomTransport' does not exist`.

- [ ] **Step 3: 구현**

`Runtime/SteamRoomTransport.cs`:

```csharp
using System;
using System.Globalization;
using Bun3.Unity.Rooms.Ngo;
using Bun3.Unity.Steam.Ngo;
using Unity.Netcode;

namespace Bun3.Unity.Rooms.Steam.Ngo
{
    /// <summary>
    /// Room transport over <see cref="SteamSocketsTransport"/>. The host endpoint is the host's SteamID and the
    /// host accepts only SteamIDs that are current room members.
    /// </summary>
    public sealed class SteamRoomTransport : INgoRoomTransport
    {
        private readonly SteamSocketsTransport transport;

        /// <summary>Wraps the manager's Steam transport component.</summary>
        public SteamRoomTransport(SteamSocketsTransport transport) =>
            this.transport = transport != null ? transport : throw new ArgumentNullException(nameof(transport));

        /// <summary>Admits only members of <paramref name="room"/> and returns the local SteamID as the endpoint.</summary>
        public string PrepareHost(NetworkManager manager, IRoom room)
        {
            transport.AcceptFilter = steamId => IsMember(room, steamId);
            manager.NetworkConfig.NetworkTransport = transport;
            return room.LocalMemberId;
        }

        /// <summary>Targets the host SteamID in <paramref name="endpoint"/>.</summary>
        /// <exception cref="FormatException"><paramref name="endpoint"/> is not a non-zero decimal SteamID.</exception>
        public void PrepareClient(NetworkManager manager, IRoom room, string endpoint)
        {
            transport.TargetSteamId = ParseEndpoint(endpoint);
            manager.NetworkConfig.NetworkTransport = transport;
        }

        internal static bool IsMember(IRoom room, ulong steamId)
        {
            var members = room.Members;
            for (int i = 0; i < members.Count; i++)
                if (ulong.TryParse(members[i].Id, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && id == steamId)
                    return true;
            return false;
        }

        internal static ulong ParseEndpoint(string endpoint) =>
            ulong.TryParse(endpoint, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && id != 0
                ? id
                : throw new FormatException("The host endpoint is not a SteamID.");
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: 공통 블록, `$p='EditMode'; $a='Bun3.Unity.Rooms.Steam.Ngo.Tests'; $n='rooms-steam-ngo'`
Expected: 모든 케이스 통과, 경고·오류 없음.

- [ ] **Step 5: README와 CHANGELOG**

`README.md`:

````markdown
# Bun3 Rooms Steam NGO Transport

Joins `com.bun3.unity.rooms.ngo` sessions to the Steam Datagram Relay transport from `com.bun3.unity.steam.ngo`.

## Installation

```json
{
  "com.bun3.unity.rooms.steam.ngo": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.rooms.steam.ngo#Bun3/jp-steam-networking"
}
```

Requires the rooms, rooms NGO, Steam and Steam NGO packages with `BUN3_STEAMWORKS` active. The runtime assembly is `Bun3.Unity.Rooms.Steam.Ngo`.

## Usage

```csharp
var sockets = manager.GetComponent<SteamSocketsTransport>();
var session = new NgoRoomSession(manager, room, new SteamRoomTransport(sockets), options);
```

- `PrepareHost` admits only SteamIDs that are current members of the room and publishes the host's SteamID as the endpoint.
- `PrepareClient` connects to the SteamID in the endpoint and throws `FormatException` for anything else.
- Both assign the Steam transport as `NetworkConfig.NetworkTransport`.
````

`CHANGELOG.md`:

```markdown
# Changelog

## [0.1.0] - 2026-10-12

- Add the Steam room transport that admits only room members.
```

- [ ] **Step 6: Commit**

```bash
cd E:/Projects/orca/workspace/bun3-kit/jp-steam-networking
git add unity/Packages/com.bun3.unity.rooms.steam.ngo
git commit -m "✨ Add Steam room transport admitting only room members" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: 전체 회귀와 경고 확인

**Files:** 없음 (검증만)

- [ ] **Step 1: 새 EditMode 테스트 전체**

Run: 공통 블록, `$p='EditMode'; $a='Bun3.Unity.Rooms.Tests;Bun3.Unity.Steam.Editor.Tests;Bun3.Unity.Steam.Ngo.Tests;Bun3.Unity.Rooms.Steam.Tests;Bun3.Unity.Rooms.Steam.Ngo.Tests'; $n='all-edit'`
Expected: `failed="0"`, 경고·오류 출력 없음.

- [ ] **Step 2: 새 PlayMode 테스트와 기존 패키지 PlayMode 테스트**

Run: 공통 블록, `$p='PlayMode'; $a='Bun3.Unity.Rooms.Ngo.Tests;Bun3.Unity.Core.Tests;Bun3.Unity.Audio.Dissonance.Netcode.PlayMode.Tests'; $n='all-play'`
Expected: `failed="0"`. 기존 Dissonance NGO 테스트가 깨지지 않았는지(같은 NGO 버전 공유) 확인한다.

- [ ] **Step 3: 기존 키트 EditMode 테스트 전체**

Run: 공통 블록에서 `-assemblyNames` 없이 실행 — 첫 줄을 `$p='EditMode'; $n='kit-edit'`로 두고 Unity 명령에서 `-assemblyNames $a`를 뺀다.
Expected: 새 실패 0. 실패가 있으면 실패한 테스트가 이번 패키지(NGO 의존성 공유, define 추가)와 관련되는지 로그로 확인해 원인과 함께 보고한다.

---

### Task 8: 푸시 요청

**Files:** 없음

- [ ] **Step 1: 사용자에게 푸시 승인 요청**

`git log --oneline main..HEAD`로 커밋 목록을 보여 주고, `Bun3/jp-steam-networking` 브랜치를 `origin`에 푸시해도 되는지 묻는다. JP 쪽 플랜(`2026-10-12-steam-networking-integration.md`)은 이 브랜치가 원격에 있어야 패키지를 받을 수 있다. 승인 후에만:

```bash
git -C E:/Projects/orca/workspace/bun3-kit/jp-steam-networking push -u origin Bun3/jp-steam-networking
```
