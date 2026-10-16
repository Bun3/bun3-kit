# 텍스트 채팅 패키지 구현 플랜 (`com.bun3.chat` + `com.bun3.unity.chat.ngo`)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 전송 계층과 무관한 채팅 코어(본문 정리·도배 제한·정책 기반 릴레이·로그·호스트 기록)와 그 코어를 NGO named message로 나르는 어댑터를 만든다.

**Architecture:** 코어는 netstandard2.1 `Bun3.Chat`(엔진 참조 없음)로 두고 `dotnet test`와 Unity EditMode가 같은 NUnit 소스를 돌린다. 수신자·송신자 판정은 게임이 구현하는 `IChatPolicy`에 맡긴다. 어댑터 `NgoChatScope`는 서버에서 `ChatRelay.Submit`을 호출해 허용된 수신자에게만 보내고, 새로 접속한 클라이언트에게는 기록을 4KB 묶음으로 재전송한다.

**Tech Stack:** C# 9 / netstandard2.1, .NET 10 SDK + NUnit 4.1(dotnet), Unity 6000.3.14f1 Test Framework, Netcode for GameObjects 2.13.1, UnityTransport.

**Spec:** `docs/superpowers/specs/2026-10-12-chat-design.md`

## Global Constraints

- 레포 규칙은 `CLAUDE.md`가 원본이다. 코드·README·패키지 메타데이터는 영어만 쓴다(테스트 데이터의 한글도 `\uAC00` 같은 이스케이프로 쓴다). 레거시·타 패키지 언급 금지.
- netstandard2.1 + C# 9 블록 네임스페이스, 모든 public 멤버에 영어 XML 문서, 빌드 경고 0. 코어 csproj는 `GenerateDocumentationFile`을 켜서 문서 누락을 경고(CS1591)로 잡는다.
- 널 허용 주석을 쓰는 파일은 첫 줄에 `#nullable enable`을 둔다(Unity 컴파일에서 CS8632 경고 방지).
- 공유 테스트 소스는 NUnit 4(dotnet)와 Unity NUnit 3 양쪽에서 컴파일돼야 한다. `Assert.That`·`Assert.Throws`·`Is.*`만 쓴다(`Assert.AreEqual` 같은 고전 API는 NUnit 4에 없다).
- 패키지 버전: `com.bun3.chat` 0.1.0, `com.bun3.unity.chat.ngo` 0.1.0. 같은 버전 재퍼블리시 금지.
- 기본값(스펙 §3): `MaxLength` 100(텍스트 요소), `Burst` 5, `BurstWindowMs` 5000, `HistoryCapacity` 0, `HistoryReplayCount` 0. `ChatMessage.MaxIdentityBytes` = 448.
- 어댑터: 메시지 이름 `Bun3.Chat.Submit`/`Bun3.Chat.Message`/`Bun3.Chat.History`, 전송 방식 `NetworkDelivery.ReliableFragmentedSequenced`, 기록 묶음 4096바이트 이하, 생성자 검사 `MaxLength × 4 + 512 ≤ 4096`, 문자열은 `ushort` 바이트 수 + UTF-8.
- Unity 테스트는 에디터가 이 프로젝트를 열고 있지 않을 때 batchmode로 돌린다. 열려 있으면 `unity-cli` 스킬의 Pipeline API로 같은 필터를 실행한다. 결과 XML과 로그는 `unity/Logs/`에 남긴다.
- `.meta`는 Unity가 생성한다(수기 작성 금지). Unity 실행 뒤 새 `.meta`를 함께 커밋한다.
- 커밋은 사용자가 이 실행에서 커밋을 승인했을 때만 한다. 메시지와 트레일러는 `writing-gitmoji-commits` 스킬과 `CLAUDE.md`의 트레일러 규칙을 따른다(서브에이전트는 `git commit -m "<제목>" -m "<트레일러>"`).

## Review Focus

- **호스트 시계가 뒤로 감**(NTP 보정): 모든 송신자가 그 차이만큼 도배 제한에 걸리면 안 된다. 기대: 가장 오래된 기록보다 `nowMs`가 작으면 창이 지난 것으로 보고 허용. → Task 3 테스트 `ClockGoingBackwardsDoesNotLockSenderOut`.
- **정책이 빈 플레이어 ID나 지나치게 긴 이름을 돌려줌**: 어댑터의 4KB 크기 계산이 깨지면 안 된다. 기대: `UnknownSender`로 거부. → Task 4 테스트 `SenderWithoutUsableIdentityIsUnknown`.
- **악의적 클라이언트의 깨진 `Submit` 페이로드**(길이 접두사가 상한 초과 또는 실제 길이보다 큼): 호스트가 예외 없이 버린다. → Task 6 테스트 `MalformedSubmitIsDroppedSilently`.
- **연결 전·`Dispose` 뒤 `Send`**: NGO 오류 로그 없이 `false`. → Task 6 테스트 `SendBeforeConnectingReturnsFalse`, `DisposeStopsEvents`.
- **`SeedHistory`에 자기 `History`나 더 큰 로그를 넘김**: 무한 증가·순서 꼬임이 없어야 한다. 기대: 자기 자신은 무시, 큰 로그는 최신 `Capacity`개만. → Task 4 테스트 `SeedHistoryCopiesInOrderAndKeepsNewest`.

---

### Task 1: 코어 패키지 골격 + `ChatMessage` + `ChatText`

**Files:**
- Create: `common/src/com.bun3.chat/package.json`
- Create: `common/src/com.bun3.chat/Bun3.Chat.asmdef`
- Create: `common/src/com.bun3.chat/Bun3.Chat.csproj`
- Create: `common/src/com.bun3.chat/Runtime/ChatMessage.cs`
- Create: `common/src/com.bun3.chat/Runtime/ChatText.cs`
- Create: `common/src/com.bun3.chat/Tests/Editor/Bun3.Chat.Tests.asmdef`
- Create: `common/src/com.bun3.chat/Tests/Editor/ChatTextTests.cs`
- Create: `common/tests/Bun3.Chat.Tests/Bun3.Chat.Tests.csproj`
- Modify: `Bun3.sln` (두 프로젝트 추가)
- Modify: `unity/Packages/manifest.json` (`com.bun3.chat` file 의존성 + `testables`)

**Interfaces:**
- Produces: `Bun3.Chat.ChatMessage(ulong senderId, string senderPlayerId, string senderName, string text, byte channel, long timestampMs)`, 속성 `SenderId`, `SenderPlayerId`, `SenderName`, `Text`, `Channel`, `TimestampMs`, 상수 `ChatMessage.MaxIdentityBytes = 448`.
- Produces: `static bool ChatText.TryNormalize(string? input, int maxLength, out string text)`.

- [ ] **Step 1: 패키지 메타데이터 작성**

`common/src/com.bun3.chat/package.json`:

```json
{
  "name": "com.bun3.chat",
  "displayName": "Bun3 Chat",
  "version": "0.1.0",
  "unity": "2022.3",
  "description": "Transport-agnostic text chat core: text normalization, rate limiting, policy-driven relay, and message logs. netstandard2.1; no UnityEngine dependency.",
  "dependencies": {},
  "author": {
    "name": "Bun3",
    "url": "https://github.com/Bun3",
    "email": "bun3.dev@gmail.com"
  }
}
```

`common/src/com.bun3.chat/Bun3.Chat.asmdef`:

```json
{
    "name": "Bun3.Chat",
    "rootNamespace": "Bun3.Chat",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

`common/src/com.bun3.chat/Bun3.Chat.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <RootNamespace>Bun3.Chat</RootNamespace>
    <AssemblyName>Bun3.Chat</AssemblyName>
    <PackageId>Bun3.Chat</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-kit</RepositoryUrl>
  </PropertyGroup>

  <ItemGroup>
    <Compile Remove="Tests/**/*.cs" />
  </ItemGroup>

</Project>
```

`common/src/com.bun3.chat/Tests/Editor/Bun3.Chat.Tests.asmdef`:

```json
{
    "name": "Bun3.Chat.Tests",
    "rootNamespace": "Bun3.Chat.Tests",
    "references": [
        "Bun3.Chat",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`common/tests/Bun3.Chat.Tests/Bun3.Chat.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <RootNamespace>Bun3.Chat.Tests</RootNamespace>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="NUnit" Version="4.1.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\com.bun3.chat\Bun3.Chat.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Compile Include="..\..\src\com.bun3.chat\Tests\Editor\**\*.cs" Link="Shared\%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>

</Project>
```

솔루션에 추가:

```bash
dotnet sln Bun3.sln add common/src/com.bun3.chat/Bun3.Chat.csproj common/tests/Bun3.Chat.Tests/Bun3.Chat.Tests.csproj
```

`unity/Packages/manifest.json`: `dependencies`의 `"com.bun3.common"` 바로 위에 `"com.bun3.chat": "file:../../common/src/com.bun3.chat",`를 넣고, `testables` 배열 맨 앞에 `"com.bun3.chat",`를 넣는다.

- [ ] **Step 2: 실패하는 테스트 작성**

`common/src/com.bun3.chat/Tests/Editor/ChatTextTests.cs`:

```csharp
#nullable enable
using System;
using NUnit.Framework;

namespace Bun3.Chat.Tests
{
    public sealed class ChatTextTests
    {
        [TestCase("a\nb", "a b")]
        [TestCase("a\tb\r\n", "a b")]
        [TestCase("  hi  ", "hi")]
        public void ControlCharactersBecomeSpacesAndEdgesAreTrimmed(string input, string expected)
        {
            Assert.That(ChatText.TryNormalize(input, 100, out var text), Is.True);
            Assert.That(text, Is.EqualTo(expected));
        }

        [Test]
        public void FormatCharactersAreRemoved()
        {
            Assert.That(ChatText.TryNormalize("a\u200Bb\u202Ec", 100, out var text), Is.True);
            Assert.That(text, Is.EqualTo("abc"));
        }

        [Test]
        public void TextIsComposedToNfc()
        {
            Assert.That(ChatText.TryNormalize("\u1100\u1161", 100, out var hangul), Is.True);
            Assert.That(hangul, Is.EqualTo("\uAC00"));
            Assert.That(ChatText.TryNormalize("e\u0301", 100, out var accented), Is.True);
            Assert.That(accented, Is.EqualTo("\u00E9"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\n\t")]
        [TestCase("\u200B")]
        [TestCase("\u0301\u0301")]
        public void InvisibleTextIsRejected(string? input)
        {
            Assert.That(ChatText.TryNormalize(input, 100, out var text), Is.False);
            Assert.That(text, Is.Empty);
        }

        [Test]
        public void LengthCountsTextElements()
        {
            Assert.That(ChatText.TryNormalize("\uAC00\uB098\uB2E4", 3, out _), Is.True);
            Assert.That(ChatText.TryNormalize("\uAC00\uB098\uB2E4\uB77C", 3, out _), Is.False);
            Assert.That(ChatText.TryNormalize("\U0001F600\U0001F600", 2, out _), Is.True);
            Assert.That(ChatText.TryNormalize("\U0001F600\U0001F600\U0001F600", 2, out _), Is.False);
        }

        [TestCase("a\uD800b")]
        [TestCase("\uDC00")]
        [TestCase("a\uD800")]
        public void LoneSurrogatesAreRejected(string input)
        {
            Assert.That(ChatText.TryNormalize(input, 100, out _), Is.False);
        }

        [Test]
        public void CombiningMarkFloodIsRejectedByUtf8Budget()
        {
            // One text element each: 2 + 299 * 2 = 600 bytes > 400, and 2 + 149 * 2 = 300 bytes <= 400.
            Assert.That(ChatText.TryNormalize("a" + new string('\u0301', 300), 100, out _), Is.False);
            Assert.That(ChatText.TryNormalize("a" + new string('\u0301', 150), 100, out _), Is.True);
        }

        [Test]
        public void NonPositiveMaxLengthThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ChatText.TryNormalize("a", 0, out _));
        }
    }
}
```

- [ ] **Step 3: 테스트가 실패하는지 확인**

Run: `dotnet test common/tests/Bun3.Chat.Tests`
Expected: 빌드 실패 — `ChatText` 형식을 찾을 수 없음(CS0103/CS0246).

- [ ] **Step 4: `ChatMessage`와 `ChatText` 구현**

`common/src/com.bun3.chat/Runtime/ChatMessage.cs`:

```csharp
#nullable enable
using System;

namespace Bun3.Chat
{
    /// <summary>
    /// One relayed chat message. The relay host stamps every field except the text, which it normalizes.
    /// </summary>
    public readonly struct ChatMessage
    {
        /// <summary>Upper bound on the UTF-8 bytes of <see cref="SenderPlayerId"/> plus <see cref="SenderName"/>.</summary>
        public const int MaxIdentityBytes = 448;

        /// <summary>Creates a message. Only the relay host should create messages that peers display.</summary>
        public ChatMessage(ulong senderId, string senderPlayerId, string senderName, string text, byte channel, long timestampMs)
        {
            SenderId = senderId;
            SenderPlayerId = senderPlayerId ?? throw new ArgumentNullException(nameof(senderPlayerId));
            SenderName = senderName ?? throw new ArgumentNullException(nameof(senderName));
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Channel = channel;
            TimestampMs = timestampMs;
        }

        /// <summary>Transport connection id of the sender. Routing only: transports reassign it on reconnect.</summary>
        public ulong SenderId { get; }

        /// <summary>Stable player identity supplied by the policy. Use it for "mine", blocking and reporting.</summary>
        public string SenderPlayerId { get; }

        /// <summary>Display name supplied by the policy, never by the sending client.</summary>
        public string SenderName { get; }

        /// <summary>Normalized message text.</summary>
        public string Text { get; }

        /// <summary>Channel chosen by the policy. Its meaning is defined by the game.</summary>
        public byte Channel { get; }

        /// <summary>Unix time in milliseconds, stamped by the relay host.</summary>
        public long TimestampMs { get; }
    }
}
```

`common/src/com.bun3.chat/Runtime/ChatText.cs`:

```csharp
#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace Bun3.Chat
{
    /// <summary>Normalizes user-typed chat text into a single displayable line.</summary>
    public static class ChatText
    {
        /// <summary>
        /// Replaces control characters with spaces, removes format characters, trims, and composes to NFC.
        /// Fails on lone surrogates, on text with nothing visible, on more than <paramref name="maxLength"/>
        /// text elements, and on more than <paramref name="maxLength"/> × 4 UTF-8 bytes.
        /// </summary>
        /// <param name="input">Raw text; null counts as empty.</param>
        /// <param name="maxLength">Maximum number of text elements. Must be positive.</param>
        /// <param name="text">The normalized text, or an empty string on failure.</param>
        /// <returns>True when <paramref name="text"/> is valid.</returns>
        public static bool TryNormalize(string? input, int maxLength, out string text)
        {
            if (maxLength < 1) throw new ArgumentOutOfRangeException(nameof(maxLength));
            text = string.Empty;
            if (string.IsNullOrEmpty(input)) return false;

            var builder = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= input.Length || !char.IsLowSurrogate(input[i + 1])) return false;
                    builder.Append(c).Append(input[++i]);
                    continue;
                }
                if (char.IsLowSurrogate(c)) return false;
                var category = char.GetUnicodeCategory(c);
                if (category == UnicodeCategory.Control) builder.Append(' ');
                else if (category != UnicodeCategory.Format) builder.Append(c);
            }

            string normalized;
            try { normalized = builder.ToString().Trim().Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { return false; }
            if (!HasVisibleCharacter(normalized)) return false;
            if (new StringInfo(normalized).LengthInTextElements > maxLength) return false;
            if (Encoding.UTF8.GetByteCount(normalized) > (long)maxLength * 4) return false;
            text = normalized;
            return true;
        }

        static bool HasVisibleCharacter(string value)
        {
            foreach (char c in value)
                if (!char.IsWhiteSpace(c) && char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) return true;
            return false;
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test common/tests/Bun3.Chat.Tests`
Expected: PASS, 경고 0. `dotnet build common/src/com.bun3.chat/Bun3.Chat.csproj -warnaserror`도 성공해야 한다.

- [ ] **Step 6: 커밋**

```bash
git add common/src/com.bun3.chat common/tests/Bun3.Chat.Tests Bun3.sln unity/Packages/manifest.json
git commit -m "✨ 채팅 코어 패키지와 본문 정리 추가" -m "<CLAUDE.md 트레일러>"
```

---

### Task 2: `ChatLog`

**Files:**
- Create: `common/src/com.bun3.chat/Runtime/ChatLog.cs`
- Test: `common/src/com.bun3.chat/Tests/Editor/ChatLogTests.cs`

**Interfaces:**
- Consumes: `ChatMessage` (Task 1).
- Produces: `ChatLog(int capacity)`, `int Capacity`, `int Count`, `ChatMessage this[int index]`(0 = 가장 오래됨), `int Unread`, `long TotalAdded`, `event Action? Changed`, `void Add(in ChatMessage)`, `void MarkRead()`, `void Clear()`.

- [ ] **Step 1: 실패하는 테스트 작성**

`common/src/com.bun3.chat/Tests/Editor/ChatLogTests.cs`:

```csharp
#nullable enable
using System;
using NUnit.Framework;

namespace Bun3.Chat.Tests
{
    public sealed class ChatLogTests
    {
        static ChatMessage Message(string text) => new ChatMessage(1, "p1", "Alice", text, 0, 0);

        static string[] Texts(ChatLog log)
        {
            var texts = new string[log.Count];
            for (int i = 0; i < texts.Length; i++) texts[i] = log[i].Text;
            return texts;
        }

        [Test]
        public void KeepsInsertionOrder()
        {
            var log = new ChatLog(3);
            log.Add(Message("a"));
            log.Add(Message("b"));
            Assert.That(log.Capacity, Is.EqualTo(3));
            Assert.That(Texts(log), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void OverwritesOldestWhenFull()
        {
            var log = new ChatLog(3);
            foreach (var text in new[] { "a", "b", "c", "d", "e" }) log.Add(Message(text));
            Assert.That(log.Count, Is.EqualTo(3));
            Assert.That(Texts(log), Is.EqualTo(new[] { "c", "d", "e" }));
        }

        [Test]
        public void IndexOutsideCountThrows()
        {
            var log = new ChatLog(3);
            log.Add(Message("a"));
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = log[1]; });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = log[-1]; });
        }

        [Test]
        public void UnreadIsCappedAtCapacityAndResetByMarkRead()
        {
            var log = new ChatLog(2);
            for (int i = 0; i < 5; i++) log.Add(Message("x"));
            Assert.That(log.Unread, Is.EqualTo(2));
            log.MarkRead();
            Assert.That(log.Unread, Is.EqualTo(0));
        }

        [Test]
        public void ChangedFiresOnAddOnEffectiveMarkReadAndOnClear()
        {
            var log = new ChatLog(4);
            int changes = 0;
            log.Changed += () => changes++;
            log.Add(Message("a"));
            Assert.That(changes, Is.EqualTo(1));
            log.MarkRead();
            Assert.That(changes, Is.EqualTo(2));
            log.MarkRead();
            Assert.That(changes, Is.EqualTo(2));
            log.Clear();
            Assert.That(changes, Is.EqualTo(3));
        }

        [Test]
        public void ClearEmptiesButKeepsTotalAdded()
        {
            var log = new ChatLog(3);
            for (int i = 0; i < 5; i++) log.Add(Message("x"));
            Assert.That(log.TotalAdded, Is.EqualTo(5));
            log.Clear();
            Assert.That(log.Count, Is.EqualTo(0));
            Assert.That(log.Unread, Is.EqualTo(0));
            Assert.That(log.TotalAdded, Is.EqualTo(5));
            log.Add(Message("new"));
            Assert.That(Texts(log), Is.EqualTo(new[] { "new" }));
            Assert.That(log.TotalAdded, Is.EqualTo(6));
        }

        [Test]
        public void NonPositiveCapacityThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatLog(0));
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Chat.Tests --filter ChatLogTests`
Expected: 빌드 실패 — `ChatLog`를 찾을 수 없음.

- [ ] **Step 3: 구현**

`common/src/com.bun3.chat/Runtime/ChatLog.cs`:

```csharp
#nullable enable
using System;

namespace Bun3.Chat
{
    /// <summary>
    /// Fixed-capacity ring of recent messages; the oldest message is overwritten when full.
    /// Not thread-safe: consume it from the main thread.
    /// </summary>
    public sealed class ChatLog
    {
        readonly ChatMessage[] items;
        int start;

        /// <summary>Creates a log that holds at most <paramref name="capacity"/> messages.</summary>
        public ChatLog(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            items = new ChatMessage[capacity];
        }

        /// <summary>Maximum number of messages kept.</summary>
        public int Capacity => items.Length;

        /// <summary>Number of messages currently kept.</summary>
        public int Count { get; private set; }

        /// <summary>Messages added since the last <see cref="MarkRead"/>, capped at <see cref="Capacity"/>.</summary>
        public int Unread { get; private set; }

        /// <summary>
        /// Total number of <see cref="Add"/> calls since construction; <see cref="Clear"/> does not reset it.
        /// Compare it with <see cref="Count"/> to tell how many messages were appended and how many fell off the front.
        /// </summary>
        public long TotalAdded { get; private set; }

        /// <summary>Raised after <see cref="Add"/>, after a <see cref="MarkRead"/> that changed <see cref="Unread"/>, and after <see cref="Clear"/>.</summary>
        public event Action? Changed;

        /// <summary>The message at <paramref name="index"/>; 0 is the oldest.</summary>
        public ChatMessage this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                return items[(start + index) % items.Length];
            }
        }

        /// <summary>Appends a message, overwriting the oldest when full.</summary>
        public void Add(in ChatMessage message)
        {
            if (Count < items.Length)
            {
                items[(start + Count) % items.Length] = message;
                Count++;
            }
            else
            {
                items[start] = message;
                start = (start + 1) % items.Length;
            }
            if (Unread < items.Length) Unread++;
            TotalAdded++;
            Changed?.Invoke();
        }

        /// <summary>Resets <see cref="Unread"/> to zero.</summary>
        public void MarkRead()
        {
            if (Unread == 0) return;
            Unread = 0;
            Changed?.Invoke();
        }

        /// <summary>Removes every message. <see cref="TotalAdded"/> is kept.</summary>
        public void Clear()
        {
            Array.Clear(items, 0, items.Length);
            start = 0;
            Count = 0;
            Unread = 0;
            Changed?.Invoke();
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test common/tests/Bun3.Chat.Tests`
Expected: PASS(ChatText + ChatLog), 경고 0.

- [ ] **Step 5: 커밋**

```bash
git add common/src/com.bun3.chat
git commit -m "✨ 채팅 로그 링 버퍼 추가" -m "<CLAUDE.md 트레일러>"
```

---

### Task 3: `ChatRateLimiter`

**Files:**
- Create: `common/src/com.bun3.chat/Runtime/ChatRateLimiter.cs`
- Test: `common/src/com.bun3.chat/Tests/Editor/ChatRateLimiterTests.cs`

**Interfaces:**
- Produces: `ChatRateLimiter(int burst, long windowMs)`, `bool TryAcquire(ulong senderId, long nowMs)`, `void Forget(ulong senderId)`, `void Clear()`.

- [ ] **Step 1: 실패하는 테스트 작성**

`common/src/com.bun3.chat/Tests/Editor/ChatRateLimiterTests.cs`:

```csharp
#nullable enable
using System;
using NUnit.Framework;

namespace Bun3.Chat.Tests
{
    public sealed class ChatRateLimiterTests
    {
        [Test]
        public void AllowsBurstThenRejects()
        {
            var limiter = new ChatRateLimiter(3, 1000);
            Assert.That(limiter.TryAcquire(1, 0), Is.True);
            Assert.That(limiter.TryAcquire(1, 1), Is.True);
            Assert.That(limiter.TryAcquire(1, 2), Is.True);
            Assert.That(limiter.TryAcquire(1, 3), Is.False);
        }

        [Test]
        public void RecoversOnceTheOldestLeavesTheWindow()
        {
            var limiter = new ChatRateLimiter(3, 1000);
            for (long t = 0; t < 3; t++) limiter.TryAcquire(1, t);
            Assert.That(limiter.TryAcquire(1, 999), Is.False);
            Assert.That(limiter.TryAcquire(1, 1000), Is.True);
        }

        [Test]
        public void RejectedAttemptsDoNotConsumeBudget()
        {
            var limiter = new ChatRateLimiter(3, 1000);
            for (long t = 0; t < 3; t++) limiter.TryAcquire(1, t);
            for (int i = 0; i < 10; i++) Assert.That(limiter.TryAcquire(1, 500), Is.False);
            Assert.That(limiter.TryAcquire(1, 1000), Is.True);
            Assert.That(limiter.TryAcquire(1, 1001), Is.True);
            Assert.That(limiter.TryAcquire(1, 1002), Is.True);
            Assert.That(limiter.TryAcquire(1, 1003), Is.False);
        }

        [Test]
        public void SendersAreIndependent()
        {
            var limiter = new ChatRateLimiter(1, 1000);
            Assert.That(limiter.TryAcquire(1, 0), Is.True);
            Assert.That(limiter.TryAcquire(2, 0), Is.True);
            Assert.That(limiter.TryAcquire(1, 1), Is.False);
        }

        [Test]
        public void ForgetAndClearResetSenders()
        {
            var limiter = new ChatRateLimiter(1, 1000);
            limiter.TryAcquire(1, 0);
            limiter.TryAcquire(2, 0);
            limiter.Forget(1);
            Assert.That(limiter.TryAcquire(1, 1), Is.True);
            Assert.That(limiter.TryAcquire(2, 1), Is.False);
            limiter.Clear();
            Assert.That(limiter.TryAcquire(2, 2), Is.True);
        }

        [Test]
        public void ClockGoingBackwardsDoesNotLockSenderOut()
        {
            var limiter = new ChatRateLimiter(2, 1000);
            limiter.TryAcquire(1, 10_000);
            limiter.TryAcquire(1, 10_001);
            Assert.That(limiter.TryAcquire(1, 5_000), Is.True);
        }

        [Test]
        public void InvalidArgumentsThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRateLimiter(0, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRateLimiter(1, 0));
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Chat.Tests --filter ChatRateLimiterTests`
Expected: 빌드 실패 — `ChatRateLimiter`를 찾을 수 없음.

- [ ] **Step 3: 구현**

`common/src/com.bun3.chat/Runtime/ChatRateLimiter.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Chat
{
    /// <summary>
    /// Allows each sender at most <c>burst</c> messages within any <c>windowMs</c> span.
    /// A per-sender ring is allocated on the sender's first message and reused afterwards.
    /// </summary>
    public sealed class ChatRateLimiter
    {
        sealed class Ring
        {
            public readonly long[] Times;
            public int Next;
            public int Count;
            public Ring(int size) => Times = new long[size];
        }

        readonly int burst;
        readonly long windowMs;
        readonly Dictionary<ulong, Ring> rings = new Dictionary<ulong, Ring>();

        /// <summary>Creates a limiter. Both arguments must be positive.</summary>
        public ChatRateLimiter(int burst, long windowMs)
        {
            if (burst < 1) throw new ArgumentOutOfRangeException(nameof(burst));
            if (windowMs < 1) throw new ArgumentOutOfRangeException(nameof(windowMs));
            this.burst = burst;
            this.windowMs = windowMs;
        }

        /// <summary>
        /// Records a message at <paramref name="nowMs"/> and returns true, or returns false without recording
        /// when the sender already sent <c>burst</c> messages in the window. A clock that moved backwards
        /// counts as the window having passed.
        /// </summary>
        public bool TryAcquire(ulong senderId, long nowMs)
        {
            if (!rings.TryGetValue(senderId, out var ring))
            {
                ring = new Ring(burst);
                rings.Add(senderId, ring);
            }
            if (ring.Count == burst)
            {
                long elapsed = nowMs - ring.Times[ring.Next];
                if (elapsed >= 0 && elapsed < windowMs) return false;
            }
            ring.Times[ring.Next] = nowMs;
            ring.Next = (ring.Next + 1) % burst;
            if (ring.Count < burst) ring.Count++;
            return true;
        }

        /// <summary>Drops the sender's history, for example on disconnect.</summary>
        public void Forget(ulong senderId) => rings.Remove(senderId);

        /// <summary>Drops every sender's history.</summary>
        public void Clear() => rings.Clear();
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test common/tests/Bun3.Chat.Tests`
Expected: PASS, 경고 0.

- [ ] **Step 5: 커밋**

```bash
git add common/src/com.bun3.chat
git commit -m "✨ 송신자별 채팅 도배 제한 추가" -m "<CLAUDE.md 트레일러>"
```

---

### Task 4: `IChatPolicy` + `ChatRelay`(파이프라인·수신자·기록·재전송)

**Files:**
- Create: `common/src/com.bun3.chat/Runtime/IChatPolicy.cs`
- Create: `common/src/com.bun3.chat/Runtime/ChatRelay.cs` (`ChatRejectReason`, `ChatRelayOptions`, `ChatRelay`)
- Test: `common/src/com.bun3.chat/Tests/Editor/FakeChatPolicy.cs`
- Test: `common/src/com.bun3.chat/Tests/Editor/ChatRelayTests.cs`

**Interfaces:**
- Consumes: `ChatMessage`, `ChatText`, `ChatLog`, `ChatRateLimiter` (Task 1–3).
- Produces: `IChatPolicy { bool TryGetSender(ulong senderId, out string playerId, out string name); bool TryGetChannel(ulong senderId, out byte channel); bool CanReceive(ulong recipientId, ulong senderId, byte channel); }`
- Produces: `enum ChatRejectReason : byte { None, InvalidText, UnknownSender, NotAllowed, RateLimited }`
- Produces: `ChatRelayOptions { int MaxLength = 100; int Burst = 5; long BurstWindowMs = 5000; int HistoryCapacity; int HistoryReplayCount; }`
- Produces: `ChatRelay(IChatPolicy policy, ChatRelayOptions? options = null)`, `int MaxLength`, `int HistoryReplayCount`, `ChatLog? History`, `ChatRejectReason Submit(ulong senderId, string? text, long nowMs, out ChatMessage message)`, `void CollectRecipients(in ChatMessage message, IReadOnlyList<ulong> peers, List<ulong> result)`, `void CollectReplay(ulong recipientId, List<ChatMessage> result)`, `void SeedHistory(ChatLog source)`, `void Forget(ulong senderId)`, `void Clear()`.

- [ ] **Step 1: 가짜 정책과 실패하는 테스트 작성**

`common/src/com.bun3.chat/Tests/Editor/FakeChatPolicy.cs`:

```csharp
#nullable enable
using System.Collections.Generic;

namespace Bun3.Chat.Tests
{
    internal sealed class FakeChatPolicy : IChatPolicy
    {
        public readonly Dictionary<ulong, (string PlayerId, string Name)> Senders = new Dictionary<ulong, (string, string)>();
        public readonly Dictionary<ulong, byte> Channels = new Dictionary<ulong, byte>();
        public readonly HashSet<(ulong Recipient, byte Channel)> Blocked = new HashSet<(ulong, byte)>();

        public bool TryGetSender(ulong senderId, out string playerId, out string name)
        {
            if (Senders.TryGetValue(senderId, out var sender))
            {
                playerId = sender.PlayerId;
                name = sender.Name;
                return true;
            }
            playerId = name = string.Empty;
            return false;
        }

        public bool TryGetChannel(ulong senderId, out byte channel) => Channels.TryGetValue(senderId, out channel);

        public bool CanReceive(ulong recipientId, ulong senderId, byte channel) => !Blocked.Contains((recipientId, channel));
    }
}
```

`common/src/com.bun3.chat/Tests/Editor/ChatRelayTests.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Bun3.Chat.Tests
{
    public sealed class ChatRelayTests
    {
        FakeChatPolicy policy = null!;

        [SetUp]
        public void SetUp()
        {
            policy = new FakeChatPolicy();
            policy.Senders[1] = ("p1", "Alice");
            policy.Channels[1] = 7;
        }

        static ChatRelayOptions History(int capacity, int replay) => new ChatRelayOptions { HistoryCapacity = capacity, HistoryReplayCount = replay };

        static string[] Texts(IReadOnlyList<ChatMessage> messages)
        {
            var texts = new string[messages.Count];
            for (int i = 0; i < texts.Length; i++) texts[i] = messages[i].Text;
            return texts;
        }

        static string[] Texts(ChatLog log)
        {
            var texts = new string[log.Count];
            for (int i = 0; i < texts.Length; i++) texts[i] = log[i].Text;
            return texts;
        }

        [Test]
        public void InvalidOptionsThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRelay(policy, new ChatRelayOptions { MaxLength = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRelay(policy, new ChatRelayOptions { Burst = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRelay(policy, new ChatRelayOptions { BurstWindowMs = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRelay(policy, History(-1, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRelay(policy, History(5, -1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRelay(policy, History(5, 6)));
            Assert.Throws<ArgumentNullException>(() => new ChatRelay(null!));
        }

        [Test]
        public void SuccessStampsPolicyIdentityChannelAndTime()
        {
            var relay = new ChatRelay(policy);
            Assert.That(relay.Submit(1, "  hi\n", 1234, out var message), Is.EqualTo(ChatRejectReason.None));
            Assert.That(message.SenderId, Is.EqualTo(1UL));
            Assert.That(message.SenderPlayerId, Is.EqualTo("p1"));
            Assert.That(message.SenderName, Is.EqualTo("Alice"));
            Assert.That(message.Text, Is.EqualTo("hi"));
            Assert.That(message.Channel, Is.EqualTo(7));
            Assert.That(message.TimestampMs, Is.EqualTo(1234));
        }

        [Test]
        public void PipelineReportsTheFirstFailingStage()
        {
            var relay = new ChatRelay(policy, new ChatRelayOptions { Burst = 1 });
            Assert.That(relay.Submit(9, "", 0, out _), Is.EqualTo(ChatRejectReason.UnknownSender));
            Assert.That(relay.Submit(1, " ", 0, out _), Is.EqualTo(ChatRejectReason.InvalidText));
            policy.Senders[2] = ("p2", "Bob");
            Assert.That(relay.Submit(2, "hi", 0, out _), Is.EqualTo(ChatRejectReason.NotAllowed));
            Assert.That(relay.Submit(1, "one", 0, out _), Is.EqualTo(ChatRejectReason.None));
            Assert.That(relay.Submit(1, "two", 1, out var rejected), Is.EqualTo(ChatRejectReason.RateLimited));
            Assert.That(rejected.Text, Is.Null);
        }

        [Test]
        public void RejectedMessagesDoNotConsumeRateBudget()
        {
            var relay = new ChatRelay(policy, new ChatRelayOptions { Burst = 2 });
            for (int i = 0; i < 5; i++) relay.Submit(1, "", 0, out _);
            policy.Channels.Remove(1);
            for (int i = 0; i < 5; i++) relay.Submit(1, "hi", 0, out _);
            policy.Channels[1] = 7;
            Assert.That(relay.Submit(1, "a", 0, out _), Is.EqualTo(ChatRejectReason.None));
            Assert.That(relay.Submit(1, "b", 0, out _), Is.EqualTo(ChatRejectReason.None));
        }

        [Test]
        public void SenderWithoutUsableIdentityIsUnknown()
        {
            var relay = new ChatRelay(policy);
            policy.Senders[1] = ("", "Alice");
            Assert.That(relay.Submit(1, "hi", 0, out _), Is.EqualTo(ChatRejectReason.UnknownSender));
            policy.Senders[1] = ("p1", new string('n', ChatMessage.MaxIdentityBytes - 1));
            Assert.That(relay.Submit(1, "hi", 0, out _), Is.EqualTo(ChatRejectReason.UnknownSender));
            policy.Senders[1] = ("p1", new string('n', ChatMessage.MaxIdentityBytes - 2));
            Assert.That(relay.Submit(1, "hi", 0, out _), Is.EqualTo(ChatRejectReason.None));
        }

        [Test]
        public void CollectRecipientsFiltersByPolicyAndReusesTheList()
        {
            var relay = new ChatRelay(policy);
            relay.Submit(1, "hi", 0, out var message);
            policy.Blocked.Add((3, 7));
            var result = new List<ulong> { 99 };
            relay.CollectRecipients(message, new ulong[] { 1, 2, 3 }, result);
            Assert.That(result, Is.EqualTo(new ulong[] { 1, 2 }));
        }

        [Test]
        public void HistoryIsOffByDefaultAndStoresOnlyAcceptedMessages()
        {
            Assert.That(new ChatRelay(policy).History, Is.Null);
            var relay = new ChatRelay(policy, History(2, 0));
            relay.Submit(1, "a", 0, out _);
            relay.Submit(1, " ", 0, out _);
            relay.Submit(1, "b", 0, out _);
            relay.Submit(1, "c", 0, out _);
            Assert.That(Texts(relay.History!), Is.EqualTo(new[] { "b", "c" }));
        }

        [Test]
        public void CollectReplayReturnsNewestReceivableOldestFirst()
        {
            var relay = new ChatRelay(policy, History(10, 2));
            relay.Submit(1, "a", 0, out _);
            relay.Submit(1, "b", 0, out _);
            policy.Channels[1] = 8;
            relay.Submit(1, "secret", 0, out _);
            policy.Channels[1] = 7;
            relay.Submit(1, "c", 0, out _);
            policy.Blocked.Add((5, 8));
            var result = new List<ChatMessage> { new ChatMessage(0, "x", "x", "junk", 0, 0) };
            relay.CollectReplay(5, result);
            Assert.That(Texts(result), Is.EqualTo(new[] { "b", "c" }));
        }

        [Test]
        public void CollectReplayIsEmptyWhenReplayIsOff()
        {
            var relay = new ChatRelay(policy, History(10, 0));
            relay.Submit(1, "a", 0, out _);
            var result = new List<ChatMessage>();
            relay.CollectReplay(5, result);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void SeedHistoryCopiesInOrderAndKeepsNewest()
        {
            var source = new ChatLog(10);
            foreach (var text in new[] { "a", "b", "c", "d" }) source.Add(new ChatMessage(1, "p1", "Alice", text, 7, 0));
            var relay = new ChatRelay(policy, History(3, 3));
            relay.SeedHistory(source);
            Assert.That(Texts(relay.History!), Is.EqualTo(new[] { "b", "c", "d" }));
            relay.SeedHistory(relay.History!);
            Assert.That(Texts(relay.History!), Is.EqualTo(new[] { "b", "c", "d" }));
            var off = new ChatRelay(policy);
            Assert.DoesNotThrow(() => off.SeedHistory(source));
            Assert.That(off.History, Is.Null);
        }

        [Test]
        public void ClearEmptiesHistoryAndResetsRateLimit()
        {
            var relay = new ChatRelay(policy, new ChatRelayOptions { Burst = 1, HistoryCapacity = 5 });
            relay.Submit(1, "a", 0, out _);
            Assert.That(relay.Submit(1, "b", 1, out _), Is.EqualTo(ChatRejectReason.RateLimited));
            relay.Clear();
            Assert.That(relay.History!.Count, Is.EqualTo(0));
            Assert.That(relay.Submit(1, "c", 2, out _), Is.EqualTo(ChatRejectReason.None));
        }
    }
}
```

`SenderWithoutUsableIdentityIsUnknown`의 경계: `"p1"`(2바이트) + 이름 447바이트 = 449 > 448이면 거부, 446바이트면 448로 통과.

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Chat.Tests --filter ChatRelayTests`
Expected: 빌드 실패 — `IChatPolicy`/`ChatRelay`를 찾을 수 없음.

- [ ] **Step 3: 구현**

`common/src/com.bun3.chat/Runtime/IChatPolicy.cs`:

```csharp
#nullable enable
namespace Bun3.Chat
{
    /// <summary>
    /// Game rules the relay consults. Implementations run on the relay host.
    /// </summary>
    public interface IChatPolicy
    {
        /// <summary>
        /// Resolves the stable player id and display name for a connection. Return false for unknown senders.
        /// The id must be non-empty, and id plus name must fit <see cref="ChatMessage.MaxIdentityBytes"/> UTF-8 bytes.
        /// </summary>
        bool TryGetSender(ulong senderId, out string playerId, out string name);

        /// <summary>Returns false when the sender may not chat right now; otherwise the channel to stamp.</summary>
        bool TryGetChannel(ulong senderId, out byte channel);

        /// <summary>
        /// Decides delivery. Replayed history is judged with current state, so decide by
        /// <paramref name="channel"/>, not by the sender's current state; the sender id may be stale.
        /// </summary>
        bool CanReceive(ulong recipientId, ulong senderId, byte channel);
    }
}
```

`common/src/com.bun3.chat/Runtime/ChatRelay.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Bun3.Chat
{
    /// <summary>Why <see cref="ChatRelay.Submit"/> rejected a message.</summary>
    public enum ChatRejectReason : byte
    {
        /// <summary>Accepted.</summary>
        None,
        /// <summary>The text failed <see cref="ChatText.TryNormalize"/>.</summary>
        InvalidText,
        /// <summary>The policy has no usable identity for the sender.</summary>
        UnknownSender,
        /// <summary>The policy does not allow the sender to chat right now.</summary>
        NotAllowed,
        /// <summary>The sender exceeded the burst limit.</summary>
        RateLimited,
    }

    /// <summary>Relay tuning, validated when the relay is constructed.</summary>
    public sealed class ChatRelayOptions
    {
        /// <summary>Maximum text elements per message. At least 1.</summary>
        public int MaxLength { get; set; } = 100;

        /// <summary>Messages a sender may send within <see cref="BurstWindowMs"/>. At least 1.</summary>
        public int Burst { get; set; } = 5;

        /// <summary>Burst window in milliseconds. At least 1.</summary>
        public long BurstWindowMs { get; set; } = 5000;

        /// <summary>Messages the host keeps in memory; 0 keeps none.</summary>
        public int HistoryCapacity { get; set; }

        /// <summary>History messages replayed to a newly connected peer; 0 replays none. At most <see cref="HistoryCapacity"/>.</summary>
        public int HistoryReplayCount { get; set; }
    }

    /// <summary>
    /// Host-side chat pipeline: identifies the sender, normalizes the text, asks the policy for a channel,
    /// applies the burst limit, and optionally records accepted messages for replay.
    /// </summary>
    public sealed class ChatRelay
    {
        readonly IChatPolicy policy;
        readonly ChatRateLimiter limiter;

        /// <summary>Creates a relay. Invalid <paramref name="options"/> throw <see cref="ArgumentOutOfRangeException"/>.</summary>
        public ChatRelay(IChatPolicy policy, ChatRelayOptions? options = null)
        {
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
            options ??= new ChatRelayOptions();
            if (options.MaxLength < 1) throw new ArgumentOutOfRangeException(nameof(options), "MaxLength must be at least 1.");
            if (options.Burst < 1) throw new ArgumentOutOfRangeException(nameof(options), "Burst must be at least 1.");
            if (options.BurstWindowMs < 1) throw new ArgumentOutOfRangeException(nameof(options), "BurstWindowMs must be at least 1.");
            if (options.HistoryCapacity < 0) throw new ArgumentOutOfRangeException(nameof(options), "HistoryCapacity must not be negative.");
            if (options.HistoryReplayCount < 0 || options.HistoryReplayCount > options.HistoryCapacity)
                throw new ArgumentOutOfRangeException(nameof(options), "HistoryReplayCount must be between 0 and HistoryCapacity.");
            MaxLength = options.MaxLength;
            HistoryReplayCount = options.HistoryReplayCount;
            limiter = new ChatRateLimiter(options.Burst, options.BurstWindowMs);
            History = options.HistoryCapacity > 0 ? new ChatLog(options.HistoryCapacity) : null;
        }

        /// <summary>Maximum text elements per message.</summary>
        public int MaxLength { get; }

        /// <summary>History messages replayed to a newly connected peer.</summary>
        public int HistoryReplayCount { get; }

        /// <summary>Accepted messages kept by the host, or null when history is off. Shared across channels.</summary>
        public ChatLog? History { get; }

        /// <summary>Validates and stamps a message. On success the message is also added to <see cref="History"/>.</summary>
        public ChatRejectReason Submit(ulong senderId, string? text, long nowMs, out ChatMessage message)
        {
            message = default;
            if (!policy.TryGetSender(senderId, out var playerId, out var name) || string.IsNullOrEmpty(playerId) || name == null ||
                Encoding.UTF8.GetByteCount(playerId) + Encoding.UTF8.GetByteCount(name) > ChatMessage.MaxIdentityBytes)
                return ChatRejectReason.UnknownSender;
            if (!ChatText.TryNormalize(text, MaxLength, out var normalized)) return ChatRejectReason.InvalidText;
            if (!policy.TryGetChannel(senderId, out var channel)) return ChatRejectReason.NotAllowed;
            if (!limiter.TryAcquire(senderId, nowMs)) return ChatRejectReason.RateLimited;
            message = new ChatMessage(senderId, playerId, name, normalized, channel, nowMs);
            History?.Add(message);
            return ChatRejectReason.None;
        }

        /// <summary>Clears <paramref name="result"/> and fills it with the peers the policy lets receive <paramref name="message"/>.</summary>
        public void CollectRecipients(in ChatMessage message, IReadOnlyList<ulong> peers, List<ulong> result)
        {
            result.Clear();
            for (int i = 0; i < peers.Count; i++)
                if (policy.CanReceive(peers[i], message.SenderId, message.Channel)) result.Add(peers[i]);
        }

        /// <summary>
        /// Clears <paramref name="result"/> and fills it with up to <see cref="HistoryReplayCount"/> of the newest
        /// history messages <paramref name="recipientId"/> may receive, oldest first.
        /// </summary>
        public void CollectReplay(ulong recipientId, List<ChatMessage> result)
        {
            result.Clear();
            if (History == null || HistoryReplayCount == 0) return;
            for (int i = History.Count - 1; i >= 0 && result.Count < HistoryReplayCount; i--)
            {
                var message = History[i];
                if (policy.CanReceive(recipientId, message.SenderId, message.Channel)) result.Add(message);
            }
            result.Reverse();
        }

        /// <summary>
        /// Appends <paramref name="source"/> to <see cref="History"/> oldest first, keeping the newest on overflow.
        /// Used when the server role moves to a peer that has its own log. Does nothing when history is off.
        /// </summary>
        public void SeedHistory(ChatLog source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (History == null || ReferenceEquals(source, History)) return;
            for (int i = Math.Max(0, source.Count - History.Capacity); i < source.Count; i++) History.Add(source[i]);
        }

        /// <summary>Drops the sender's rate-limit state, for example on disconnect.</summary>
        public void Forget(ulong senderId) => limiter.Forget(senderId);

        /// <summary>Clears rate-limit state and history, for example when the server stops.</summary>
        public void Clear()
        {
            limiter.Clear();
            History?.Clear();
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test common/tests/Bun3.Chat.Tests`
Expected: PASS(전체), 경고 0.

- [ ] **Step 5: 커밋**

```bash
git add common/src/com.bun3.chat
git commit -m "✨ 정책 기반 채팅 릴레이와 호스트 기록 추가" -m "<CLAUDE.md 트레일러>"
```

---

### Task 5: 코어 README + Unity EditMode 검증

**Files:**
- Create: `common/src/com.bun3.chat/README.md`
- Create(Unity 생성): `common/src/com.bun3.chat/**/*.meta`

**Interfaces:**
- Consumes: Task 1–4의 공개 API 전부.

- [ ] **Step 1: README 작성**

`common/src/com.bun3.chat/README.md`:

````markdown
# Bun3 Chat

Transport-agnostic text chat core. netstandard2.1, no UnityEngine dependency.

## What it does

- `ChatText.TryNormalize` turns typed text into one line: control characters become spaces, format characters are removed, the text is trimmed and composed to NFC. It rejects invisible text, more than `maxLength` text elements, and more than `maxLength × 4` UTF-8 bytes.
- `ChatRelay` runs on the host: sender identity → text → channel → burst limit. Rejected messages do not spend the burst budget. Names and player ids come only from the policy, never from the client.
- `ChatLog` is a fixed-capacity ring with an unread counter. `TotalAdded` survives `Clear`, so a view can diff appended and evicted counts.
- Optional host history: `HistoryCapacity` keeps accepted messages in memory; `HistoryReplayCount` replays the newest receivable ones to new peers. `0` turns each off.

## Policy contract

Implement `IChatPolicy` with your game's rules:

- `TryGetSender` returns a stable, non-empty player id and a display name; together at most `ChatMessage.MaxIdentityBytes` (448) UTF-8 bytes.
- `TryGetChannel` returns false when the sender may not chat now.
- `CanReceive` decides delivery. Replay uses it with the recipient's current state, so decide by `channel` (state at send time), not by the sender's current state. The sender id in history can be stale after reconnects; use `SenderPlayerId` for "mine", blocking and reporting.

## Example

```csharp
var relay = new ChatRelay(new MyPolicy(), new ChatRelayOptions { HistoryCapacity = 1000, HistoryReplayCount = 100 });
var log = new ChatLog(1000);
// A transport adapter calls relay.Submit on the host and delivers the message to CollectRecipients.
```

Persistence, read cursors, whispers and blocking are out of scope. Transport adapters (for example `com.bun3.unity.chat.ngo`) live in separate packages.
````

- [ ] **Step 2: Unity EditMode에서 같은 테스트 실행**

Run:

```bash
"E:/Unitys/6000.3.14f1/Editor/Unity.exe" -batchmode -projectPath unity -runTests -testPlatform EditMode -testFilter "Bun3.Chat.Tests" -testResults "$(pwd -W)/unity/Logs/chat-core-editmode.xml" -logFile "$(pwd -W)/unity/Logs/chat-core-editmode.log"
```

Expected: 종료 코드 0, 결과 XML의 `failed="0"`이고 `total`이 dotnet 테스트 수와 같다. 로그에서 `grep -n "warning CS" unity/Logs/chat-core-editmode.log | grep -i "Bun3.Chat"` 결과가 비어 있다. 첫 실행은 임포트 때문에 오래 걸린다. `run_in_background`로 돌리고 결과 XML을 확인한다.

- [ ] **Step 3: 생성된 `.meta` 확인**

Run: `git status --short common/src/com.bun3.chat`
Expected: 모든 파일·폴더 옆에 `.meta`가 생겼다. `Bun3.Chat.csproj.meta`도 생긴다(기존 `com.bun3.common`과 같다).

- [ ] **Step 4: 커밋**

```bash
git add common/src/com.bun3.chat
git commit -m "📝 채팅 코어 README와 Unity 메타 추가" -m "<CLAUDE.md 트레일러>"
```

---

### Task 6: NGO 어댑터 패키지 `com.bun3.unity.chat.ngo`

**Files:**
- Create: `unity/Packages/com.bun3.unity.chat.ngo/package.json`
- Create: `unity/Packages/com.bun3.unity.chat.ngo/README.md`
- Create: `unity/Packages/com.bun3.unity.chat.ngo/CHANGELOG.md`
- Create: `unity/Packages/com.bun3.unity.chat.ngo/Runtime/Bun3.Unity.Chat.Ngo.asmdef`
- Create: `unity/Packages/com.bun3.unity.chat.ngo/Runtime/NgoChatScope.cs`
- Create: `unity/Packages/com.bun3.unity.chat.ngo/Tests/PlayMode/Bun3.Unity.Chat.Ngo.PlayMode.Tests.asmdef`
- Test: `unity/Packages/com.bun3.unity.chat.ngo/Tests/PlayMode/NgoChatScopeTests.cs`

**Interfaces:**
- Consumes: `ChatRelay`, `ChatMessage`, `ChatText`, `ChatRejectReason`, `ChatLog`, `IChatPolicy` (Task 1–4).
- Produces: `NgoChatScope(NetworkManager manager, ChatRelay relay) : IDisposable`, `event Action<ChatMessage>? Received`, `event Action<ChatMessage>? HistoryReceived`, `bool Send(string text)`, `void Dispose()`.

- [ ] **Step 1: 패키지 메타데이터와 asmdef**

`unity/Packages/com.bun3.unity.chat.ngo/package.json`:

```json
{
  "name": "com.bun3.unity.chat.ngo",
  "version": "0.1.0",
  "displayName": "Bun3 Chat NGO Adapter",
  "description": "Relays Bun3 chat over Netcode for GameObjects named messages, with host history replay.",
  "unity": "6000.3",
  "dependencies": {
    "com.bun3.chat": "0.1.0",
    "com.unity.netcode.gameobjects": "2.13.1"
  },
  "author": {
    "name": "Bun3",
    "url": "https://github.com/Bun3",
    "email": "bun3.dev@gmail.com"
  }
}
```

`unity/Packages/com.bun3.unity.chat.ngo/Runtime/Bun3.Unity.Chat.Ngo.asmdef`:

```json
{
    "name": "Bun3.Unity.Chat.Ngo",
    "rootNamespace": "Bun3.Unity.Chat.Ngo",
    "references": [
        "Bun3.Chat",
        "Unity.Netcode.Runtime",
        "Unity.Collections"
    ],
    "autoReferenced": true
}
```

`unity/Packages/com.bun3.unity.chat.ngo/Tests/PlayMode/Bun3.Unity.Chat.Ngo.PlayMode.Tests.asmdef`:

```json
{
    "name": "Bun3.Unity.Chat.Ngo.PlayMode.Tests",
    "rootNamespace": "Bun3.Unity.Chat.Ngo.Tests",
    "references": [
        "Bun3.Unity.Chat.Ngo",
        "Bun3.Chat",
        "Unity.Netcode.Runtime",
        "Unity.Collections",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: 실패하는 PlayMode 테스트 작성**

`unity/Packages/com.bun3.unity.chat.ngo/Tests/PlayMode/NgoChatScopeTests.cs`:

```csharp
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Bun3.Chat;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Chat.Ngo.Tests
{
    public sealed class NgoChatScopeTests
    {
        sealed class Policy : IChatPolicy
        {
            public bool AllowSend = true;
            public byte Channel = 1;
            public readonly HashSet<ulong> Deaf = new HashSet<ulong>();
            public readonly HashSet<byte> HiddenChannels = new HashSet<byte>();

            public bool TryGetSender(ulong senderId, out string playerId, out string name)
            {
                playerId = "player-" + senderId;
                name = "Name" + senderId;
                return true;
            }

            public bool TryGetChannel(ulong senderId, out byte channel)
            {
                channel = Channel;
                return AllowSend;
            }

            public bool CanReceive(ulong recipientId, ulong senderId, byte channel) =>
                !Deaf.Contains(recipientId) && !HiddenChannels.Contains(channel);
        }

        sealed class Peer
        {
            public NetworkManager Manager = null!;
            public ChatRelay Relay = null!;
            public NgoChatScope Scope = null!;
            public readonly List<ChatMessage> Live = new List<ChatMessage>();
            public readonly List<string> Events = new List<string>();
        }

        readonly List<Peer> peers = new List<Peer>();
        bool priorRunInBackground;

        [SetUp]
        public void SetUp()
        {
            priorRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var peer in peers)
            {
                peer.Scope.Dispose();
                if (peer.Manager != null && peer.Manager.IsListening) peer.Manager.Shutdown();
            }
            yield return null;
            yield return null;
            foreach (var peer in peers)
                if (peer.Manager != null) Object.Destroy(peer.Manager.gameObject);
            peers.Clear();
            yield return null;
            Application.runInBackground = priorRunInBackground;
        }

        static ushort FreePort()
        {
            using var socket = new UdpClient(0);
            return (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
        }

        Peer Create(string name, ushort port, Policy policy, ChatRelayOptions? options = null)
        {
            var root = new GameObject(name);
            var transport = root.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
            var manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
            var peer = new Peer { Manager = manager, Relay = new ChatRelay(policy, options) };
            peer.Scope = new NgoChatScope(manager, peer.Relay);
            peer.Scope.Received += message =>
            {
                peer.Live.Add(message);
                peer.Events.Add("live:" + message.Text);
            };
            peer.Scope.HistoryReceived += message => peer.Events.Add("history:" + message.Text);
            peers.Add(peer);
            return peer;
        }

        static IEnumerator Until(Func<bool> condition, float seconds = 10)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) Assert.Fail("Timed out waiting for the network.");
                yield return null;
            }
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        IEnumerator HostAndClient(Policy policy, ChatRelayOptions? options, Action<Peer, Peer> ready)
        {
            ushort port = FreePort();
            var host = Create("Host", port, policy, options);
            Assert.That(host.Manager.StartHost(), Is.True);
            var client = Create("Client", port, new Policy(), options);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Manager.IsConnectedClient);
            ready(host, client);
        }

        [UnityTest]
        public IEnumerator ClientSendReachesBothWithHostPolicyIdentity()
        {
            Peer host = null!, client = null!;
            yield return HostAndClient(new Policy(), null, (h, c) => { host = h; client = c; });
            Assert.That(client.Scope.Send("  hello\n"), Is.True);
            yield return Until(() => host.Live.Count == 1 && client.Live.Count == 1);
            var message = client.Live[0];
            ulong id = client.Manager.LocalClientId;
            Assert.That(message.Text, Is.EqualTo("hello"));
            Assert.That(message.SenderId, Is.EqualTo(id));
            Assert.That(message.SenderPlayerId, Is.EqualTo("player-" + id));
            Assert.That(message.SenderName, Is.EqualTo("Name" + id));
        }

        [UnityTest]
        public IEnumerator HostSendUsesTheLocalPath()
        {
            Peer host = null!, client = null!;
            yield return HostAndClient(new Policy(), null, (h, c) => { host = h; client = c; });
            Assert.That(host.Scope.Send("from host"), Is.True);
            yield return Until(() => host.Live.Count == 1 && client.Live.Count == 1);
            Assert.That(client.Live[0].SenderId, Is.EqualTo(NetworkManager.ServerClientId));
        }

        [UnityTest]
        public IEnumerator PolicyCanExcludeARecipient()
        {
            var policy = new Policy();
            Peer host = null!, client = null!;
            yield return HostAndClient(policy, null, (h, c) => { host = h; client = c; });
            policy.Deaf.Add(client.Manager.LocalClientId);
            client.Scope.Send("muted");
            yield return Until(() => host.Live.Count == 1);
            yield return Frames(10);
            Assert.That(client.Live, Is.Empty);
        }

        [UnityTest]
        public IEnumerator DeniedChannelReachesNobody()
        {
            var policy = new Policy { AllowSend = false };
            Peer host = null!, client = null!;
            yield return HostAndClient(policy, null, (h, c) => { host = h; client = c; });
            Assert.That(client.Scope.Send("denied"), Is.True);
            yield return Frames(20);
            Assert.That(host.Live, Is.Empty);
            Assert.That(client.Live, Is.Empty);
        }

        [UnityTest]
        public IEnumerator RestartRegistersHandlersAgain()
        {
            Peer host = null!, client = null!;
            yield return HostAndClient(new Policy(), null, (h, c) => { host = h; client = c; });
            client.Manager.Shutdown();
            host.Manager.Shutdown();
            yield return Until(() => !host.Manager.IsListening && !client.Manager.IsListening &&
                !host.Manager.ShutdownInProgress && !client.Manager.ShutdownInProgress);
            Assert.That(host.Manager.StartHost(), Is.True);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Manager.IsConnectedClient);
            client.Scope.Send("again");
            yield return Until(() => host.Live.Count == 1 && client.Live.Count == 1);
        }

        [UnityTest]
        public IEnumerator DisposeStopsEvents()
        {
            Peer host = null!, client = null!;
            yield return HostAndClient(new Policy(), null, (h, c) => { host = h; client = c; });
            client.Scope.Dispose();
            Assert.That(client.Scope.Send("after dispose"), Is.False);
            host.Scope.Send("unheard");
            yield return Until(() => host.Live.Count == 1);
            yield return Frames(10);
            Assert.That(client.Live, Is.Empty);
        }

        [UnityTest]
        public IEnumerator SendBeforeConnectingReturnsFalse()
        {
            var peer = Create("Idle", FreePort(), new Policy());
            Assert.That(peer.Scope.Send("not listening"), Is.False);
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        [UnityTest]
        public IEnumerator NewClientGetsRecentHistoryBeforeLiveMessages()
        {
            var options = new ChatRelayOptions { HistoryCapacity = 10, HistoryReplayCount = 2 };
            ushort port = FreePort();
            var host = Create("Host", port, new Policy(), options);
            Assert.That(host.Manager.StartHost(), Is.True);
            foreach (var text in new[] { "one", "two", "three" }) host.Scope.Send(text);
            yield return Until(() => host.Live.Count == 3);
            var client = Create("Client", port, new Policy(), options);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Events.Count == 2);
            host.Scope.Send("four");
            yield return Until(() => client.Events.Count == 3);
            Assert.That(client.Events, Is.EqualTo(new[] { "history:two", "history:three", "live:four" }));
        }

        [UnityTest]
        public IEnumerator ReplayCountZeroSendsOnlyLaterMessages()
        {
            var options = new ChatRelayOptions { HistoryCapacity = 10, HistoryReplayCount = 0 };
            ushort port = FreePort();
            var host = Create("Host", port, new Policy(), options);
            Assert.That(host.Manager.StartHost(), Is.True);
            host.Scope.Send("before");
            yield return Until(() => host.Live.Count == 1);
            var client = Create("Client", port, new Policy(), options);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Manager.IsConnectedClient);
            host.Scope.Send("after");
            yield return Until(() => client.Events.Count == 1);
            Assert.That(client.Events, Is.EqualTo(new[] { "live:after" }));
        }

        [UnityTest]
        public IEnumerator HiddenChannelIsNotReplayed()
        {
            var policy = new Policy();
            var options = new ChatRelayOptions { HistoryCapacity = 10, HistoryReplayCount = 10 };
            ushort port = FreePort();
            var host = Create("Host", port, policy, options);
            Assert.That(host.Manager.StartHost(), Is.True);
            policy.Channel = 2;
            host.Scope.Send("secret");
            policy.Channel = 1;
            host.Scope.Send("public");
            yield return Until(() => host.Live.Count == 2);
            policy.HiddenChannels.Add(2);
            var client = Create("Client", port, new Policy(), options);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Manager.IsConnectedClient);
            host.Scope.Send("live");
            yield return Until(() => client.Events.Contains("live:live"));
            Assert.That(client.Events, Is.EqualTo(new[] { "history:public", "live:live" }));
        }

        [UnityTest]
        public IEnumerator LargeHistoryArrivesInOrderAcrossChunks()
        {
            var options = new ChatRelayOptions { HistoryCapacity = 60, HistoryReplayCount = 60 };
            ushort port = FreePort();
            var host = Create("Host", port, new Policy(), options);
            Assert.That(host.Manager.StartHost(), Is.True);
            var seed = new ChatLog(60);
            var expected = new string[60];
            for (int i = 0; i < 60; i++)
            {
                expected[i] = "history:" + i.ToString("D3") + new string('x', 97);
                seed.Add(new ChatMessage(0, "player-0", "Name0", i.ToString("D3") + new string('x', 97), 1, i));
            }
            host.Relay.SeedHistory(seed);   // 60 x ~136 bytes: at least two 4 KB chunks
            var client = Create("Client", port, new Policy(), options);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Events.Count == 60);
            Assert.That(client.Events, Is.EqualTo(expected));
        }

        [UnityTest]
        public IEnumerator RestartedServerStartsWithEmptyHistory()
        {
            var options = new ChatRelayOptions { HistoryCapacity = 10, HistoryReplayCount = 10 };
            ushort port = FreePort();
            var host = Create("Host", port, new Policy(), options);
            Assert.That(host.Manager.StartHost(), Is.True);
            host.Scope.Send("old");
            yield return Until(() => host.Live.Count == 1);
            host.Manager.Shutdown();
            yield return Until(() => !host.Manager.IsListening && !host.Manager.ShutdownInProgress);
            Assert.That(host.Relay.History!.Count, Is.EqualTo(0));
            Assert.That(host.Manager.StartHost(), Is.True);
            var client = Create("Client", port, new Policy(), options);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Manager.IsConnectedClient);
            host.Scope.Send("new");
            yield return Until(() => client.Events.Count == 1);
            Assert.That(client.Events, Is.EqualTo(new[] { "live:new" }));
        }

        [UnityTest]
        public IEnumerator MalformedSubmitIsDroppedSilently()
        {
            Peer host = null!, client = null!;
            yield return HostAndClient(new Policy(), null, (h, c) => { host = h; client = c; });
            using (var oversized = new FastBufferWriter(4, Allocator.Temp))
            {
                oversized.WriteValueSafe((ushort)60000);
                client.Manager.CustomMessagingManager.SendNamedMessage("Bun3.Chat.Submit", NetworkManager.ServerClientId, oversized, NetworkDelivery.ReliableFragmentedSequenced);
            }
            using (var truncated = new FastBufferWriter(8, Allocator.Temp))
            {
                truncated.WriteValueSafe((ushort)50);
                truncated.WriteBytesSafe(Encoding.UTF8.GetBytes("abc"));
                client.Manager.CustomMessagingManager.SendNamedMessage("Bun3.Chat.Submit", NetworkManager.ServerClientId, truncated, NetworkDelivery.ReliableFragmentedSequenced);
            }
            client.Scope.Send("valid");
            yield return Until(() => host.Live.Count == 1);
            Assert.That(host.Live[0].Text, Is.EqualTo("valid"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OversizedMaxLengthIsRejected()
        {
            var root = new GameObject("Manager");
            try
            {
                var manager = root.AddComponent<NetworkManager>();
                Assert.Throws<ArgumentOutOfRangeException>(() => new NgoChatScope(manager, new ChatRelay(new Policy(), new ChatRelayOptions { MaxLength = 897 })));
                using var fits = new NgoChatScope(manager, new ChatRelay(new Policy(), new ChatRelayOptions { MaxLength = 896 }));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
```

- [ ] **Step 3: 실패 확인**

Run:

```bash
"E:/Unitys/6000.3.14f1/Editor/Unity.exe" -batchmode -projectPath unity -runTests -testPlatform PlayMode -testFilter "Bun3.Unity.Chat.Ngo" -testResults "$(pwd -W)/unity/Logs/chat-ngo-playmode.xml" -logFile "$(pwd -W)/unity/Logs/chat-ngo-playmode.log"
```

Expected: 컴파일 실패 — 로그에 `error CS0246: The type or namespace name 'NgoChatScope' could not be found`.

- [ ] **Step 4: `NgoChatScope` 구현**

`unity/Packages/com.bun3.unity.chat.ngo/Runtime/NgoChatScope.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Bun3.Chat;
using Unity.Collections;
using Unity.Netcode;

namespace Bun3.Unity.Chat.Ngo
{
    /// <summary>
    /// Carries <see cref="ChatRelay"/> traffic over Netcode for GameObjects named messages.
    /// The server validates and fans out submissions; peers display only what the server sends back.
    /// Newly connected clients receive the relay's replay before any later live message.
    /// </summary>
    public sealed class NgoChatScope : IDisposable
    {
        const string SubmitName = "Bun3.Chat.Submit";
        const string MessageName = "Bun3.Chat.Message";
        const string HistoryName = "Bun3.Chat.History";
        const int ChunkBytes = 4096;
        const int ReservedBytes = 512;
        const int FixedBytes = sizeof(ulong) + sizeof(byte) + sizeof(long) + 3 * sizeof(ushort);
        const NetworkDelivery Delivery = NetworkDelivery.ReliableFragmentedSequenced;

        readonly NetworkManager manager;
        readonly ChatRelay relay;
        readonly List<ulong> recipients = new List<ulong>();
        readonly List<ChatMessage> buffer = new List<ChatMessage>();
        CustomMessagingManager? messaging;
        bool submitRegistered;
        bool disposed;

        /// <summary>Binds <paramref name="relay"/> to the current and every future session of <paramref name="manager"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">One message would not fit a 4 KB history chunk (MaxLength above 896).</exception>
        public NgoChatScope(NetworkManager manager, ChatRelay relay)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            this.manager = manager;
            this.relay = relay ?? throw new ArgumentNullException(nameof(relay));
            if ((long)relay.MaxLength * 4 + ReservedBytes > ChunkBytes)
                throw new ArgumentOutOfRangeException(nameof(relay), "MaxLength must be at most 896 so one message fits a history chunk.");
            manager.OnServerStarted += Register;
            manager.OnClientStarted += Register;
            manager.OnServerStopped += OnServerStopped;
            manager.OnClientStopped += OnClientStopped;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            if (manager.IsListening) Register();
        }

        /// <summary>Raised for each live message the server relays to this peer.</summary>
        public event Action<ChatMessage>? Received;

        /// <summary>Raised for each replayed history message, oldest first, after this peer connects.</summary>
        public event Action<ChatMessage>? HistoryReceived;

        /// <summary>
        /// Sends normalized text to the server. Returns false when not connected, disposed, or the text is invalid.
        /// Nothing is shown locally until the server relays the message back.
        /// </summary>
        public bool Send(string text)
        {
            if (disposed || messaging == null || !(manager.IsServer || manager.IsConnectedClient)) return false;
            if (!ChatText.TryNormalize(text, relay.MaxLength, out var normalized)) return false;
            var bytes = Encoding.UTF8.GetBytes(normalized);
            using var writer = new FastBufferWriter(sizeof(ushort) + bytes.Length, Allocator.Temp);
            writer.WriteValueSafe((ushort)bytes.Length);
            writer.WriteBytesSafe(bytes);
            messaging.SendNamedMessage(SubmitName, NetworkManager.ServerClientId, writer, Delivery);
            return true;
        }

        /// <summary>Unsubscribes from the manager and unregisters every handler.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Unregister();
            manager.OnServerStarted -= Register;
            manager.OnClientStarted -= Register;
            manager.OnServerStopped -= OnServerStopped;
            manager.OnClientStopped -= OnClientStopped;
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        void Register()
        {
            var current = manager.CustomMessagingManager;
            if (disposed || current == null) return;
            if (!ReferenceEquals(messaging, current))
            {
                Unregister();
                messaging = current;
                current.RegisterNamedMessageHandler(MessageName, OnMessage);
                current.RegisterNamedMessageHandler(HistoryName, OnHistory);
            }
            if (manager.IsServer && !submitRegistered)
            {
                current.RegisterNamedMessageHandler(SubmitName, OnSubmit);
                submitRegistered = true;
            }
        }

        void Unregister()
        {
            if (messaging == null) return;
            messaging.UnregisterNamedMessageHandler(MessageName);
            messaging.UnregisterNamedMessageHandler(HistoryName);
            if (submitRegistered) messaging.UnregisterNamedMessageHandler(SubmitName);
            messaging = null;
            submitRegistered = false;
        }

        void OnServerStopped(bool _)
        {
            relay.Clear();
            Unregister();
        }

        void OnClientStopped(bool _) => Unregister();

        void OnClientDisconnected(ulong clientId)
        {
            if (manager.IsServer) relay.Forget(clientId);
        }

        void OnClientConnected(ulong clientId)
        {
            if (!manager.IsServer || messaging == null || clientId == manager.LocalClientId || relay.HistoryReplayCount == 0) return;
            relay.CollectReplay(clientId, buffer);
            // ponytail: the whole replay is queued at once; spread chunks across ticks if join-time spikes matter.
            for (int start = 0; start < buffer.Count;)
            {
                int end = start, size = sizeof(ushort);
                while (end < buffer.Count && (end == start || size + SizeOf(buffer[end]) <= ChunkBytes)) size += SizeOf(buffer[end++]);
                using var writer = new FastBufferWriter(size, Allocator.Temp);
                writer.WriteValueSafe((ushort)(end - start));
                for (int i = start; i < end; i++) Write(writer, buffer[i]);
                messaging.SendNamedMessage(HistoryName, clientId, writer, Delivery);
                start = end;
            }
            buffer.Clear();
        }

        void OnSubmit(ulong senderClientId, FastBufferReader reader)
        {
            if (!manager.IsServer || messaging == null) return;
            string text;
            try
            {
                if (!TryReadUtf8(reader, relay.MaxLength * 4, out text)) return;
            }
            catch (OverflowException) { return; }
            if (relay.Submit(senderClientId, text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), out var message) != ChatRejectReason.None) return;
            relay.CollectRecipients(message, manager.ConnectedClientsIds, recipients);
            if (recipients.Count == 0) return;
            using var writer = new FastBufferWriter(SizeOf(message), Allocator.Temp);
            Write(writer, message);
            messaging.SendNamedMessage(MessageName, recipients, writer, Delivery);
        }

        void OnMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (senderClientId != NetworkManager.ServerClientId) return;
            ChatMessage message;
            try
            {
                if (!TryRead(reader, out message)) return;
            }
            catch (OverflowException) { return; }
            Received?.Invoke(message);
        }

        void OnHistory(ulong senderClientId, FastBufferReader reader)
        {
            if (senderClientId != NetworkManager.ServerClientId) return;
            buffer.Clear();
            try
            {
                reader.ReadValueSafe(out ushort count);
                for (int i = 0; i < count; i++)
                {
                    if (!TryRead(reader, out var message)) break;
                    buffer.Add(message);
                }
            }
            catch (OverflowException) { /* Keep the messages read before the malformed tail. */ }
            for (int i = 0; i < buffer.Count; i++) HistoryReceived?.Invoke(buffer[i]);
            buffer.Clear();
        }

        static int SizeOf(in ChatMessage message) => FixedBytes +
            Encoding.UTF8.GetByteCount(message.SenderPlayerId) + Encoding.UTF8.GetByteCount(message.SenderName) +
            Encoding.UTF8.GetByteCount(message.Text);

        static void Write(FastBufferWriter writer, in ChatMessage message)
        {
            writer.WriteValueSafe(message.SenderId);
            writer.WriteValueSafe(message.Channel);
            writer.WriteValueSafe(message.TimestampMs);
            WriteUtf8(writer, message.SenderPlayerId);
            WriteUtf8(writer, message.SenderName);
            WriteUtf8(writer, message.Text);
        }

        static bool TryRead(FastBufferReader reader, out ChatMessage message)
        {
            message = default;
            reader.ReadValueSafe(out ulong senderId);
            reader.ReadValueSafe(out byte channel);
            reader.ReadValueSafe(out long timestampMs);
            if (!TryReadUtf8(reader, ChatMessage.MaxIdentityBytes, out var playerId) ||
                !TryReadUtf8(reader, ChatMessage.MaxIdentityBytes, out var name) ||
                !TryReadUtf8(reader, ChunkBytes, out var text)) return false;
            message = new ChatMessage(senderId, playerId, name, text, channel, timestampMs);
            return true;
        }

        static void WriteUtf8(FastBufferWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.WriteValueSafe((ushort)bytes.Length);
            writer.WriteBytesSafe(bytes);
        }

        static bool TryReadUtf8(FastBufferReader reader, int maxBytes, out string value)
        {
            value = string.Empty;
            reader.ReadValueSafe(out ushort length);
            if (length > maxBytes) return false;
            var bytes = new byte[length];
            reader.ReadBytesSafe(ref bytes, length);
            value = Encoding.UTF8.GetString(bytes);
            return true;
        }
    }
}
```

`FastBufferWriter`/`FastBufferReader`는 내부 상태를 포인터(`Handle`)로 들고 있어 값으로 넘겨도 같은 버퍼를 읽고 쓴다(NGO 2.13.1 소스 확인).

- [ ] **Step 5: README와 CHANGELOG**

`unity/Packages/com.bun3.unity.chat.ngo/README.md`:

````markdown
# Bun3 Chat NGO Adapter

Relays [`com.bun3.chat`](../../../common/src/com.bun3.chat) over Netcode for GameObjects named messages.

- `Bun3.Chat.Submit` (client → server), `Bun3.Chat.Message` (server → recipients), `Bun3.Chat.History` (server → one new client) all use `ReliableFragmentedSequenced`, so a replay always arrives before later live messages.
- The server runs `ChatRelay.Submit`, sends to `CollectRecipients`, and replays `CollectReplay` in chunks of at most 4 KB when a client connects. Rejections are dropped silently.
- `Send` never displays optimistically: only messages the server relays back raise `Received`.
- Handlers are registered per session and released on stop; stopping the server clears the relay. `MaxLength` must be at most 896.

```csharp
var relay = new ChatRelay(new MyPolicy(), new ChatRelayOptions { HistoryCapacity = 1000, HistoryReplayCount = 100 });
var scope = new NgoChatScope(networkManager, relay);
var log = new ChatLog(1000);
scope.Received += m => log.Add(m);
scope.HistoryReceived += m => log.Add(m);   // skip this when the peer already has a log (reconnect)
networkManager.OnServerStarted += () => relay.SeedHistory(log);   // a new host inherits its own log
scope.Send(input.text);
```

Dispose the scope when the owner goes away.
````

`unity/Packages/com.bun3.unity.chat.ngo/CHANGELOG.md`:

```markdown
# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-10-12

### Added

- `NgoChatScope`: NGO named-message transport for `ChatRelay`, with host history replay on connect.
```

- [ ] **Step 6: 통과 확인**

Step 3의 PlayMode 명령을 다시 실행한다. 이어서 코어 회귀 확인으로 EditMode 필터 `Bun3.Chat.Tests`도 다시 실행한다.
Expected: 둘 다 `failed="0"`. `grep -n "warning CS" unity/Logs/chat-ngo-playmode.log | grep -i "Chat"` 결과가 비어 있다.

- [ ] **Step 7: `.meta` 확인 후 커밋**

```bash
git status --short unity/Packages/com.bun3.unity.chat.ngo
git add unity/Packages/com.bun3.unity.chat.ngo
git commit -m "✨ NGO 채팅 어댑터와 기록 재전송 추가" -m "<CLAUDE.md 트레일러>"
```

---

## 완료 확인 (스펙 §8)

- [ ] `dotnet build Bun3.sln` 경고 0, `dotnet test common/tests/Bun3.Chat.Tests` 통과.
- [ ] bun3-kit Unity 프로젝트 EditMode(`Bun3.Chat.Tests`)·PlayMode(`Bun3.Unity.Chat.Ngo`) 통과, 새 `.meta` 커밋.
- [ ] public 멤버 XML 문서 누락 없음(코어는 CS1591로 확인, 어댑터는 파일 검토).
- [ ] JP 통합은 JP 플랜(`jurassic-paradise/docs/superpowers/plans/2026-10-12-chat-system.md`)이 이 브랜치를 참조해 확인한다.
