# Unity 텔레메트리 패키지 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** JP가 게임 코드로 만든 텔레메트리 중 게임 지식이 없는 부분을 bun3-kit 패키지 3개(코어·PostHog·Sentry)로 옮기고, JP를 같은 분석 브랜치에서 그 패키지로 갈아태운다.

**Architecture:** 코어 패키지 `com.bun3.unity.telemetry`가 기록 형식·허브·빌더·환경 옵션 묶음·로컬 저장소·자동 기록을 갖고, 팝업 기록은 같은 패키지 안의 선택 어셈블리다. PostHog와 Sentry는 각자 패키지이며 빌더 확장 메서드로 붙는다. JP는 이벤트 정의, 공통 속성 채우기, 환경 판정과 환경별 묶음, 시작 조립 코드만 남긴다.

**Tech Stack:** Unity 6000.5.5f1(JP), 패키지 최소 6000.3, UniTask, ZString, MackySoft SerializeReferenceExtensions, Sentry Unity SDK, Unity Test Framework(NUnit).

**Spec:** `docs/superpowers/specs/2026-11-02-unity-telemetry-design.md` (bun3-kit). 결정 원문은 스펙 §2에 있다.

**작업 위치**
- kit: `E:/Projects/orca/workspace/bun3-kit/jp-telemetry` (브랜치 `Bun3/jp-telemetry`). 패키지는 `unity/Packages/<패키지>/`.
- JP: `E:/Projects/orca/workspace/jurassic-paradise/sparkles-analytics-log-system` (브랜치 `✨/analytics-log-system`). 게임 코드는 `unity/Assets/_Game/`.

## Global Constraints

- kit 코드·XML 문서·패키지 메타데이터는 영어만 쓴다. 모든 public 멤버에 XML 문서를 단다. 코드가 못 보여 주는 계약만 주석으로 남긴다. 코드에 레거시·JP·다른 패키지 언급을 쓰지 않는다.
- 빌드 경고 0. Unity 계층 문자열은 ZString으로 만든다. 폴더 = 네임스페이스.
- 새 패키지 `package.json`: `"version": "0.1.0"`, `"unity": "6000.3"`, `author`는 기존 kit 패키지와 같다. git 의존성(UniTask, ZString, Sentry)도 `package.json`에 주소로 적는다.
- 분류 `Analytics, Ui, Popup, Scene, Interaction, EngineLog, Activity`, 단계 `Analytics=1, Debug=2, Activity=4`는 kit이 고정한다.
- 분류·저장소·자동 기록을 켜고 끄는 스위치를 두지 않는다. 저장소는 빌더에 한 줄씩 붙는다.
- 입력칸 내용은 어떤 기록에도 넣지 않는다. 경로와 글자 수만 남긴다.
- 속성을 빼는 것은 게임이 넘긴 키 목록뿐이다. JP는 `"chat_text"`, `"token"`을 넘긴다. 그 밖의 값(SteamID, 닉네임, 참가 코드, 엔진 로그 원문)은 가리지 않는다.
- kit은 로비·매치·역할·방·Steam을 모른다.
- 커밋: `writing-gitmoji-commits` 스킬. 메시지는 기존 커밋처럼 한국어. kit은 문단마다 `-m`. 마지막 문단은 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- JP: `unity/ProjectSettings/*` 변경과 테스트·에디터가 다시 저장한 에셋(셰이더·폰트·씬 등)은 커밋하지 않고 `git checkout --`로 되돌린다. `SentryCliOptions.asset`은 절대 커밋하지 않는다. PostHog 키와 Sentry DSN은 비워 둔다.
- push 금지. `git stash` 금지. 다른 프로젝트의 Unity 에디터(포트 7800)는 건드리지 않는다.

## 이 계획의 판단 (스펙이 정하지 않은 것)

형식: 판단 — 이유 — 틀렸을 때 비용.

1. 네임스페이스는 `Bun3.Unity.Telemetry`, `.Popups`, `.PostHog`, `.Sentry`다 — kit 폴더=네임스페이스 규칙 — 이름 변경 비용만 든다. `.Sentry` 네임스페이스 안에서는 SDK 타입을 `Sentry.X`로 한정하지 말고 파일 맨 위 `using Sentry;`/`using Sentry.Unity;`로 쓴다(한정 이름이 네임스페이스로 해석된다).
2. 공통 속성은 kit이 `run_id`·`build`·`environment`·`scene`을 채우고, 게임 채우기 함수(`TelemetryContextFill`)가 `distinct_id`·`language`·추가 속성 배열을 채운다 — 스펙 §4.2의 "고정" 중 게임마다 같은 코드는 kit 몫 — 언어까지 kit이 읽기로 바뀌면 JP 채우기 한 줄을 지운다.
3. 추가 속성은 `(string Key, object Value)[]`이고 값이 null인 항목은 어떤 저장소에도 쓰지 않는다 — JP의 "없으면 생략" 동작 유지 — 없음.
4. 공통 속성 채우기가 예외를 던져도 kit이 채운 값으로 기록을 남긴다(경고 한 번) — 스펙 §4.2 원문. JP는 지금 기록을 버린다 — 없음.
5. ~~PostHog는 엔진 로그를 따로 빼지 않는다.~~ 사용자가 빼기로 정했다(스펙 결정 13). PostHog 저장소는 받을 단계와 상관없이 `EngineLog` 분류를 보내지 않는다.
6. Sentry 저장소는 엔진 로그 기록도 브레드크럼으로 넣는다. 게임은 훅(`SentryRecordHook`)으로 특정 기록을 직접 처리할 수 있고, JP는 이 훅으로 `[JP.Net]` 줄을 `NetLogLine` 변환에 넘긴다 — 같은 이유 — 브레드크럼이 시끄러우면 기본 처리에서 엔진 로그를 뺀다.
7. 접두어 로그 줄의 이벤트 이름은 `log_line`(속성 `line`), 경고 이상은 `engine_log`다 — JP 형식 호환은 필요 없다(결정 6) — 이름 변경 비용만 든다.
8. 팝업 기록은 빌더 확장 `CapturePopups(stack)`로 붙이고, 코어 `Telemetry.Install`이 `ITelemetryCapture` 목록으로 함께 설치한다 — 스펙 §4.3 "설치 호출 한 번" — 없음.
9. Sentry 첨부는 `Telemetry.Hub?.Sink<LocalJsonlSink>()`를 첨부 시점에 찾는다 — JP 부트스트랩의 타입 검사 루프가 없어진다 — 없음.
10. Sentry 동의 갱신 구독은 Sentry 어셈블리의 `BeforeSceneLoad`에서 한다. `SubsystemRegistration` 초기화는 첨부 차례·래치만 되돌리고 구성 객체와 동의 값을 지우지 않는다 — SDK 초기화와 `SubsystemRegistration` 순서가 정해져 있지 않다(JP `SentryPrivacy`와 같은 이유) — 없음.
11. JP의 빌드 시점(에디터, 플레이 아님) Sentry 묶음은 `playtest` 묶음이다 — JP 지금 동작(`SentrySettings.ForBuild`) 유지 — 출시 DSN이 다르면 심볼이 플레이테스트 프로젝트로 올라간다. 그때 빌드 환경 판정을 더한다.
12. JP `TelemetryPrivacyScanTests`의 "다른 플레이어 이름·SteamID 읽기 금지" 검사는 지운다. 대신 JP 허브가 `chat_text`·`token`만 빼고 `nickname`·`join_code`는 남기는지 검사한다 — 결정 4로 이름·SteamID 원본 전송이 허용됐다 — 없음.
13. kit 패키지 테스트는 JP 에디터에서 돌린다. JP manifest가 kit 작업 공간을 `file:`로 참조하고 `testables`에 넣는다(스펙 §7-11). 이 manifest 변경은 J1에서 JP에 커밋한다 — 이전 kit 작업과 같은 방식 — 없음.
14. `Telemetry.Hub` 설정자는 public이다 — JP 테스트 22곳이 허브를 바꿔 끼운다. 별도 Scope 타입보다 짧다 — 운영 코드가 허브를 바꾸는 일이 생기면 Scope로 감싼다.
15. JP 전환은 한 작업(J1)이다 — 새 `TelemetryConfig`가 Sentry 옵션 타입을 쓰고 JP 옛 Sentry 코드가 옛 설정 필드를 읽어, 나누면 중간 상태가 컴파일되지 않는다 — 리뷰 범위가 크다. J1은 standard 이상 모델로 맡긴다.

## 테스트 실행 방법

JP 작업 공간 루트에서 Git Bash로 실행한다. JP 에디터가 열려 있어야 한다(닫혀 있으면 `unity open "<JP>/unity"`로 연다).

```bash
API=.agents/skills/jp-unity-api/scripts/unity_api.py
python $API --project unity --action eval --code-file Logs/tools/refresh.eval.cs      # 파일 변경 가져오기
python $API --project unity --action eval --code-file Logs/tools/IsCompiling.eval.cs  # compiling·updating이 false가 될 때까지 반복
python Logs/tools/mkrun.py edit "^Bun3\.Unity\.Telemetry" k1 Logs/tools/k1.json        # edit|play, 정규식, 결과 이름
python $API --project unity --action execute --command run_script --parameters-file Logs/tools/k1.json
bash Logs/tools/waitxml.sh k1                                                          # 합계와 실패 목록
```

- PlayMode 실행 전에는 `Logs/tools/reload.eval.cs`도 돌린다.
- 결과 XML의 FullName이 요청한 클래스로 시작하는지 확인한다. 다르면 이전 실행 결과다.
- 경고 검사: K1에서 `Logs/tools/CsWarnings.eval.cs`를 복사해 `Logs/tools/KitWarnings.eval.cs`를 만들고, 거르는 조건을 `s.Contains("com.bun3.unity.telemetry")`로 바꾼다. JP 코드는 기존 `CsWarnings.eval.cs`를 쓴다. 둘 다 0이어야 한다.
- 새 kit 파일의 `.meta`는 에디터가 refresh 때 만든다. 커밋 전에 refresh를 한 번 돌리고 `.meta`를 함께 커밋한다.
- 테스트가 다시 저장한 JP 에셋은 `git -C <JP> status`로 확인해 되돌린다.

## Review Focus

1. 공통 속성 채우기나 씬 이름 읽기가 예외를 던질 때(메인 스레드 밖 기록 포함) 기록은 kit이 채운 값으로 남아야 한다. → K1 `ContextFillThrows_RecordKept`, `SceneReadThrows_RecordKeptWithoutScene`.
2. 기록 보관함이 닫힌 동안의 기록, 열린 채 다시 `Open()`, 닫힌 채 `Close()`는 예외 없이 스펙대로 동작해야 한다. → K2 `HistorySinkTests` 전체.
3. 허브가 없거나 허브에 로컬 JSONL 저장소가 없을 때 Sentry 오류 보고는 첨부 없이 나가야 한다. → K6 `NoLocalSink_SendsWithoutAttachment`.
4. 묶음이 null이거나, 저장소 옵션이 없거나, 받을 단계가 비었거나, 키·DSN이 비었으면 그 저장소는 붙지 않고 예외도 없어야 한다. → K1 `AddConsole_*`, K5 `AddPostHog_*`, K6 `AddSentry_*`.
5. 팝업 기록 처리 중 예외가 나도 팝업 열기·닫기 흐름으로 새지 않아야 한다. → K4 `ClockThrows_HandlersDoNotThrow`.

---

### Task K1: 코어 패키지 — 기록 형식, 허브, 빌더, JSON, 콘솔

**Files (kit `unity/Packages/com.bun3.unity.telemetry/`):**
- Create: `package.json`, `README.md`, `CHANGELOG.md`
- Create: `Runtime/Bun3.Unity.Telemetry.asmdef` (references `UniTask`, `ZString`, `MackySoft.SerializeReferenceExtensions`), `Runtime/AssemblyInfo.cs` (`InternalsVisibleTo` 두 테스트 어셈블리)
- Create: `Runtime/TelemetryTypes.cs`, `Runtime/TelemetryHub.cs`, `Runtime/TelemetryBuilder.cs`, `Runtime/TelemetryEnvironmentOptions.cs`, `Runtime/TelemetryJson.cs`, `Runtime/ConsoleSink.cs`, `Runtime/Telemetry.cs`
- Create: `Tests/Editor/Bun3.Unity.Telemetry.Editor.Tests.asmdef` (audio의 Editor.Tests asmdef 모양, rootNamespace `Bun3.Unity.Telemetry.Tests`), `Tests/Editor/ListSink.cs`, `Tests/Editor/TelemetryHubTests.cs`, `Tests/Editor/TelemetryBuilderTests.cs`, `Tests/Editor/TelemetryJsonTests.cs`
- Modify: kit `unity/Packages/manifest.json` — `"com.cysharp.zstring": "https://github.com/Cysharp/ZString.git?path=src/ZString.Unity/Assets/Scripts/ZString"` 추가
- Modify (JP, 커밋은 J1): `unity/Packages/manifest.json` — `"com.bun3.unity.telemetry": "file:E:/Projects/orca/workspace/bun3-kit/jp-telemetry/unity/Packages/com.bun3.unity.telemetry"`와 `testables` 항목
- Create (JP 로컬, 커밋 안 함): `Logs/tools/KitWarnings.eval.cs`
- 출발점: JP `Scripts/Telemetry/TelemetryTypes.cs`, `TelemetryHub.cs`, `TelemetryJson.cs`, `ConsoleSink.cs`, `Telemetry.cs`와 `Tests/EditMode/Telemetry/TelemetryHubTests.cs`, `TelemetryJsonTests.cs`, `ListSink.cs`

**Interfaces — Produces (namespace `Bun3.Unity.Telemetry`):**
```csharp
public enum TelemetryCategory : byte { Analytics, Ui, Popup, Scene, Interaction, EngineLog, Activity }
[Flags] public enum TelemetryTier : byte { None = 0, Analytics = 1, Debug = 2, Activity = 4, All = Analytics | Debug | Activity }
public static class TelemetryCategories   // Tier(c), TierName(t), Name(c): JP와 같은 값("analytics","ui","popup","scene","interaction","engine_log","activity")
public delegate void TelemetryContextFill(ref TelemetryContext context);
public struct TelemetryContext
{
    public string DistinctId, RunId, Build, Environment, Scene, Language;
    public (string Key, object Value)[] Extra;   // null 값 항목은 쓰지 않는다
    public object Get(string key);               // Extra에서 첫 일치 값, 없으면 null
}
public sealed class TelemetryRecord { public string Name; public TelemetryCategory Category; public DateTime Utc; public long Seq;
    public TelemetryContext Context; public (string Key, object Value)[] Props; public object Prop(string key); }
public interface ITelemetrySink { bool Remote { get; } void Write(TelemetryRecord record); void Tick(double now); UniTask FlushAsync(TimeSpan timeout); }
public sealed class TelemetryHub
{
    public void Track(TelemetryCategory category, string name, params (string Key, object Value)[] props);
    public void Tick(double now);
    public UniTask FlushAsync(TimeSpan timeout);
    public T Sink<T>() where T : class, ITelemetrySink;   // 첫 일치, 없으면 null
}
public sealed class TelemetryBuilder
{
    public TelemetryBuilder(TelemetryEnvironmentOptions environment);   // null 허용: 이름 null, 옵션 없음
    public TelemetryEnvironmentOptions Environment { get; }
    public TelemetryBuilder Context(TelemetryContextFill fill);
    public TelemetryBuilder Consent(Func<bool> consent);                // 없으면 동의로 본다
    public TelemetryBuilder DropKeys(params string[] keys);
    public TelemetryBuilder RunId(string runId);                        // 없거나 비면 Guid "N"
    public TelemetryBuilder Add(ITelemetrySink sink, TelemetryTier tiers);   // tiers None이면 붙이지 않는다
    public TelemetryBuilder AddConsole();                               // 묶음의 ConsoleSinkOptions가 있고 Tiers != None일 때만
    public TelemetryHub Build();
    internal TelemetryBuilder WithClock(Func<DateTime> utcNow);         // 테스트 seam
    internal TelemetryBuilder WithScene(Func<string> sceneName);        // 테스트 seam, 기본은 활성 씬 이름
}
[Serializable] public sealed class TelemetryEnvironmentOptions
{
    public string Name;
    [SerializeReference, SubclassSelector] public List<TelemetrySinkOptions> Sinks = new();
    public T Get<T>() where T : TelemetrySinkOptions;   // 첫 일치, 없으면 null
}
[Serializable] public abstract class TelemetrySinkOptions { public TelemetryTier Tiers; }
[Serializable] public sealed class ConsoleSinkOptions : TelemetrySinkOptions { }
public sealed class ConsoleSink : ITelemetrySink   // Remote=false, EngineLog는 찍지 않는다, "[Bun3.Telemetry] " + JSON 줄
public static class TelemetryJson { WriteString(ref Utf16ValueStringBuilder, string); WriteValue(ref Utf16ValueStringBuilder, object); string RecordLine(TelemetryRecord); }
public static class Telemetry
{
    public static TelemetryHub Hub { get; set; }   // Install이 설정한다. 테스트는 바꿔 끼우고 되돌린다
    public static void Track(TelemetryCategory category, string name, params (string Key, object Value)[] props);   // 허브 없으면 무시
    public static UniTask FlushAsync(TimeSpan timeout);
    public static event Action ConsentChanged;
    public static void NotifyConsentChanged();
}
```

허브 기록 순서: `Utc`=시계, `Seq`=증가, 공통 속성은 kit 값(`RunId`, 빌드 시점에 캐시한 `Application.version`, 묶음 `Name`, 씬 이름 — 예외면 null) → 게임 채우기(예외면 경고 한 번 후 그대로 진행) → 빼는 키 거르기(키마다 경고 한 번) → 단계·동의(원격만)로 저장소에 쓰기(저장소별 예외 경고 한 번). 경고 접두어는 `[Bun3.Telemetry]`. JSON 줄은 JP 형식에서 JP 전용 필드 대신 `Extra`를 순서대로 쓴다: `{"ts","seq","tier","category","event","distinct_id","run_id","build","environment","scene","language",<Extra>,"props":{...}}`.

- [ ] **Step 1: 실패하는 테스트를 쓴다.** JP 테스트를 옮기고 아래를 더한다.

```csharp
// TelemetryHubTests
[Test] public void ContextFillThrows_RecordKept()        // fill이 던짐 → sink 1건, RunId·Environment 채워짐, LogAssert 경고 1회(두 번째 Track에는 경고 없음)
[Test] public void SceneReadThrows_RecordKeptWithoutScene()  // WithScene(() => throw) → 1건, Scene == null
[Test] public void KitFillsRunIdBuildEnvironmentScene()  // RunId("r1"), 묶음 Name "dev", WithScene(() => "Menu") → 그대로, Build == Application.version
[Test] public void DropKeysRemovedAndWarnedOnce()        // DropKeys("chat_text","token"); props에 chat_text·nickname → nickname만 남음, 경고 1회
[Test] public void RemoteSinkSkippedWithoutConsent()     // Consent(() => false) → Remote 저장소 0건, 로컬 1건
[Test] public void ConsentThrows_TreatedAsNo()
[Test] public void SinkThrows_OthersStillWrite_WarnedOnce()
[Test] public void RoutesByTier()                        // Analytics만 받는 저장소에 Ui 기록 없음
[Test] public void SinkOfType_ReturnsFirstOrNull()
[Test] public void TrackWithoutHub_IsNoop()              // Telemetry.Hub = null; Track 예외 없음
// TelemetryBuilderTests
[Test] public void AddConsole_MissingOptions_NotAttached()
[Test] public void AddConsole_NoneTiers_NotAttached()
[Test] public void AddConsole_WithTiers_AttachedWithThoseTiers()   // Debug만 → Analytics 기록은 안 받음
[Test] public void NullEnvironment_BuildsWithExplicitSinksOnly()
[Test] public void Add_NoneTiers_NotAttached()
// TelemetryJsonTests (JP 테스트 + 아래)
[Test] public void ExtraWrittenInOrder_NullValuesSkipped()  // Extra [("lobby_id","L"),("match_id",null),("is_host",true)] → ,"lobby_id":"L","is_host":true, 이고 match_id 없음
```

- [ ] **Step 2: 실행해 실패를 확인한다.** 실행: 위 테스트 방법, `edit "^Bun3\.Unity\.Telemetry"`. 기대: 컴파일 오류(타입 없음).
- [ ] **Step 3: 위 Interfaces대로 구현한다.** JP 파일을 출발점으로 네임스페이스·영어 문서·kit 규칙에 맞춘다.
- [ ] **Step 4: 다시 실행해 통과를 확인한다.** 기대: 새 테스트와 옮긴 테스트 전부 통과, `KitWarnings` 0.
- [ ] **Step 5: 커밋한다(kit).** 패키지 폴더 전체(.meta 포함)와 kit manifest. JP manifest 변경은 커밋하지 않는다.

### Task K2: 로컬 JSONL 저장소와 기록 보관

**Files (kit 코어 패키지):**
- Create: `Runtime/LocalJsonlSink.cs`, `Runtime/HistorySink.cs`
- Create: `Tests/Editor/LocalJsonlSinkTests.cs`, `Tests/Editor/HistorySinkTests.cs`
- 출발점: JP `LocalJsonlSink.cs`, `MatchHistorySink.cs`와 각 테스트

**Interfaces:**
- Consumes: K1 `ITelemetrySink`, `TelemetryRecord`, `TelemetryJson.RecordLine`, `TelemetryCategories.Tier`
- Produces:
```csharp
public sealed class LocalJsonlSink : ITelemetrySink   // JP와 같은 생성자·동작: (directory, runId, startedUtc, keepRuns, maxFileBytes, flushSeconds = 2f), FilePath, byte[] ReadTail(int maxBytes)
public sealed class HistorySink : ITelemetrySink
{
    public const int DefaultCapacity = 4000;
    public HistorySink(int capacity = DefaultCapacity);
    public bool IsOpen { get; }
    public void Open();    // 비우고 받기 시작. 열린 채 불러도 비운다
    public void Close();   // 비우고 받기 멈춤
    public IReadOnlyList<TelemetryRecord> Find(params string[] names);   // 이름 없으면 전부, Seq 순
}
```
HistorySink는 처음에 닫혀 있고, `Remote=false`, 모든 메서드는 잠금으로 보호한다. 용량을 넘으면 `Debug` 단계 기록부터 하나 버리고, 없으면 가장 오래된 기록을 버린다.

- [ ] **Step 1: 실패하는 테스트를 쓴다.** LocalJsonl은 JP 테스트를 옮긴다. HistorySink:

```csharp
[Test] public void StartsClosed_DropsRecords()           // Write 후 Find().Count == 0, IsOpen false
[Test] public void Open_ClearsAndAccepts()
[Test] public void OpenWhileOpen_Clears()                // 1건 쓰고 Open → 0건
[Test] public void Close_ClearsAndStops()                // Close 후 Write → 0건
[Test] public void CloseWhileClosed_DoesNotThrow()
[Test] public void OverCapacity_DropsDebugFirst()         // capacity 3, Debug 1 + Analytics 3 → Debug 없음, Analytics 3
[Test] public void OverCapacity_NoDebug_DropsOldest()
[Test] public void Find_FiltersByName_SortedBySeq()
```

- [ ] **Step 2: 실행해 실패를 확인한다.** 기대: 컴파일 오류.
- [ ] **Step 3: 구현한다.**
- [ ] **Step 4: 실행해 통과를 확인한다.** `edit "^Bun3\.Unity\.Telemetry"` 전부 통과, 경고 0.
- [ ] **Step 5: 커밋한다(kit).**

### Task K3: 자동 기록과 설치·수명

**Files (kit 코어 패키지):**
- Modify: `Runtime/Bun3.Unity.Telemetry.asmdef` — references에 `Unity.TextMeshPro`, `UnityEngine.UI` 추가. `package.json` dependencies에 `"com.unity.ugui": "2.0.0"`
- Create: `Runtime/ITelemetryCapture.cs`, `Runtime/EngineLogCapture.cs`, `Runtime/SceneCapture.cs`, `Runtime/UiPath.cs`, `Runtime/ClickBurst.cs`, `Runtime/UiControlTracker.cs`, `Runtime/UiInteractionCapture.cs`, `Runtime/TelemetryRunner.cs`
- Modify: `Runtime/Telemetry.cs` (Install/Uninstall), `Runtime/TelemetryBuilder.cs` (`EngineLogPrefixes`, `Capture`)
- Create: `Tests/Editor/ClickBurstTests.cs`, `Tests/Editor/EngineLogCaptureTests.cs`, `Tests/Editor/SceneCaptureTests.cs`, `Tests/Editor/UiPathTests.cs`
- Create: `Tests/Runtime/Bun3.Unity.Telemetry.Tests.asmdef` (PlayMode, audio Tests asmdef 모양, references에 `Unity.TextMeshPro`, `UnityEngine.UI`, `UniTask`), `Tests/Runtime/UiInteractionCaptureTests.cs`, `Tests/Runtime/TelemetryInstallTests.cs`
- 출발점: JP `EngineLogCapture.cs`, `DebugCaptures.cs`(SceneCapture), `UiPath.cs`, `ClickBurst.cs`, `UiControlTracker.cs`, `UiInteractionCapture.cs`, `TelemetryRunner.cs`와 테스트 `ClickBurstTests`, `EngineLogCaptureTests`, `DebugCapturesTests`(씬 부분), PlayMode `UiInteractionCaptureTests`

**Interfaces:**
- Consumes: K1 `Telemetry`, `TelemetryBuilder`, `TelemetryHub`
- Produces:
```csharp
public interface ITelemetryCapture { void Install(); void Uninstall(); }
// TelemetryBuilder
public TelemetryBuilder EngineLogPrefixes(params string[] prefixes);   // 등급과 상관없이 남길 로그 줄 접두어
public TelemetryBuilder Capture(ITelemetryCapture capture);            // Install 때 함께 설치
// Telemetry
public static void Install(TelemetryBuilder builder);   // 이미 설치돼 있으면 먼저 Uninstall
public static void Uninstall();
```
`Install`이 하는 일: `Build()` → `Hub` 설정 → 엔진 로그·씬 전환 기록 설치 → 빌더의 `Capture` 목록 설치 → `DontDestroyOnLoad` 오브젝트 `"Bun3.Telemetry"`에 `TelemetryRunner`(매 프레임 `Hub.Tick(Time.realtimeSinceStartupAsDouble)`)와 `UiInteractionCapture` 부착 → `Application.quitting`에 `FlushAsync(2초)`. `Uninstall`은 역순으로 되돌리고 `Hub = null`. `SubsystemRegistration`에서 정적 상태(허브, 설치 목록, `ConsentChanged`)를 비운다.

이벤트와 속성(JP와 같은 값, 다른 점만 굵게):
- 엔진 로그: 경고 이상 `engine_log` (`type`, `message`, 예외일 때 `stack`, 같은 문구 20번째에 `suppressed=true`). 문구별 20회·문구 표 256개 제한 유지. **가리기 없음.** 접두어 줄 **`log_line` (`line`)**, 빈도 제한 없음. 기록 중 나온 로그는 다시 기록하지 않는다.
- 씬: `scene_changed` (`from`, `to`).
- UI: `ui_click` (`kind`, `path`, `interactable`, 토글이면 `value`), `ui_click_repeat` (`path`, `count`), `ui_slider` (`path`, `value`), `ui_dropdown` (`path`, `index`), `ui_input_submit` (`path`, `length`). 0.5초마다 새 `Selectable`에 추적기를 붙이고 `Scrollbar`는 뺀다. 연타 창 1.0초.

- [ ] **Step 1: 실패하는 테스트를 쓴다.** JP 테스트를 옮기고 아래를 더한다.

```csharp
// EngineLogCaptureTests
[Test] public void PrefixLine_RecordedAsLogLine()        // EngineLogPrefixes("[X.Net]"), Log 등급 "[X.Net] a=1" → log_line, line == 원문
[Test] public void Warning_RecordedUnmasked()            // "join ABC234" 경고 → message에 "ABC234" 그대로
[Test] public void PlainLog_NotRecorded()
// SceneCaptureTests
[Test] public void Changed_RecordsFromAndTo()
// UiInteractionCaptureTests (PlayMode, JP 테스트 옮김 + 아래)
[UnityTest] public IEnumerator InputContentNeverRecorded()   // TMP_InputField에 "secret-text" 입력·제출 → ui_input_submit length 11, 모든 기록의 props·JSON 줄에 "secret-text" 없음
// TelemetryInstallTests (PlayMode)
[UnityTest] public IEnumerator Install_TicksSinksEachFrame()     // 한 프레임 뒤 저장소 Tick 호출 수 > 0
[UnityTest] public IEnumerator Uninstall_RemovesRunnerAndHub()   // "Bun3.Telemetry" 오브젝트 없음, Hub null
[UnityTest] public IEnumerator Reinstall_DoesNotDuplicateCaptures()  // 두 번 Install 후 경고 로그 1건 → engine_log 1건
[UnityTest] public IEnumerator Install_InstallsBuilderCaptures()     // 가짜 ITelemetryCapture: Install 1회, Uninstall 1회
```

- [ ] **Step 2: 실행해 실패를 확인한다.** EditMode와 PlayMode(`play "^Bun3\.Unity\.Telemetry"`, 먼저 reload). 기대: 컴파일 오류.
- [ ] **Step 3: 구현한다.**
- [ ] **Step 4: 실행해 통과를 확인한다.** EditMode·PlayMode 전부 통과, 경고 0.
- [ ] **Step 5: 커밋한다(kit).**

### Task K4: 팝업 기록 어셈블리

**Files (kit 코어 패키지):**
- Create: `Runtime/Popups/Bun3.Unity.Telemetry.Popups.asmdef` — references `Bun3.Unity.Telemetry`, `Bun3.Unity.UI`; `versionDefines` `{ "name": "com.bun3.unity.ui", "expression": "", "define": "BUN3_TELEMETRY_POPUPS" }`; `defineConstraints` `["BUN3_TELEMETRY_POPUPS"]`
- Create: `Runtime/Popups/PopupCapture.cs`, `Runtime/Popups/TelemetryBuilderPopupExtensions.cs`, `Runtime/Popups/AssemblyInfo.cs`
- Create: `Tests/Editor/Popups/Bun3.Unity.Telemetry.Popups.Editor.Tests.asmdef` (같은 versionDefine·defineConstraints + `UNITY_INCLUDE_TESTS`), `Tests/Editor/Popups/PopupCaptureTests.cs`
- 출발점: JP `DebugCaptures.cs`의 `PopupCapture`와 `DebugCapturesTests`의 팝업 부분
- `package.json`에 UI 패키지 의존성을 넣지 않는다. 없으면 이 어셈블리만 빠진다.

**Interfaces:**
- Consumes: K3 `ITelemetryCapture`, `TelemetryBuilder.Capture`; UI `Bun3.Unity.UI.Popups.PopupStack`(`event Action<Popup> Opened, Closed`), `Popup.Key.Name`
- Produces (namespace `Bun3.Unity.Telemetry.Popups`):
```csharp
public sealed class PopupCapture : ITelemetryCapture
{
    public PopupCapture(PopupStack stack);
    internal PopupCapture(PopupStack stack, Func<double> clock);   // 테스트 seam, 기본 Time.realtimeSinceStartupAsDouble
    internal void HandleOpened(Popup popup);
    internal void HandleClosed(Popup popup);
}
public static class TelemetryBuilderPopupExtensions
{
    public static TelemetryBuilder CapturePopups(this TelemetryBuilder builder, PopupStack stack);
}
```
기록: `popup_opened` (`popup`=`Key.Name`), `popup_closed` (`popup`, 열린 시각을 알면 `seconds_open`). 추적은 최대 64개, 넘으면 비운다. 닫힌 팝업은 파괴됐을 수 있어 `Key` 외 Unity 멤버를 읽지 않는다. 두 처리 함수는 모든 예외를 잡아 `Debug.LogException`으로만 남긴다.

- [ ] **Step 1: 실패하는 테스트를 쓴다.** (팝업은 `new GameObject().AddComponent<TestPopup>()`로 만든다. `TestPopup : Popup`은 테스트 파일 안에 둔다.)

```csharp
[Test] public void OpenThenClose_RecordsSecondsOpen()     // clock 10 → 12.5: popup_closed seconds_open == 2.5
[Test] public void CloseWithoutOpen_OmitsSeconds()
[Test] public void ClockThrows_HandlersDoNotThrow()       // clock이 던져도 HandleOpened/HandleClosed 예외 없음, LogAssert로 예외 로그 기대
[Test] public void InstallUninstall_SubscribesOnce()      // PopupManagerBuilder로 만든 실제 스택: Install 두 번 → 이벤트당 기록 1건, Uninstall 뒤 0건
```

- [ ] **Step 2: 실행해 실패를 확인한다.** 기대: 컴파일 오류.
- [ ] **Step 3: 구현한다.**
- [ ] **Step 4: 실행해 통과를 확인한다.** EditMode 전부 통과, 경고 0.
- [ ] **Step 5: 커밋한다(kit).** 이 커밋에서 코어 패키지 README·CHANGELOG에 K1~K4 내용을 정리한다.

### Task K5: PostHog 패키지

**Files (kit `unity/Packages/com.bun3.unity.telemetry.posthog/`):**
- Create: `package.json` (dependencies `"com.bun3.unity.telemetry": "0.1.0"`, UniTask git, `"com.unity.modules.unitywebrequest": "1.0.0"`), `README.md`, `CHANGELOG.md`
- Create: `Runtime/Bun3.Unity.Telemetry.PostHog.asmdef` (references `Bun3.Unity.Telemetry`, `UniTask`, `ZString`), `Runtime/AssemblyInfo.cs`
- Create: `Runtime/PostHogSink.cs` (`IPostHogTransport`, `PostHogOutcome`, `UnityWebRequestPostHogTransport`, `PostHogSink`), `Runtime/PostHogSinkOptions.cs`, `Runtime/TelemetryBuilderPostHogExtensions.cs`
- Create: `Tests/Editor/Bun3.Unity.Telemetry.PostHog.Editor.Tests.asmdef`, `Tests/Editor/PostHogSinkTests.cs`
- Modify (JP, 커밋은 J1): manifest에 `file:` 항목과 `testables`
- 출발점: JP `PostHogSink.cs`, `PostHogSinkTests.cs`

**Interfaces:**
- Consumes: K1 타입, `TelemetryJson`, `TelemetryBuilder.Add`, `TelemetryEnvironmentOptions.Get<T>()`
- Produces (namespace `Bun3.Unity.Telemetry.PostHog`):
```csharp
[Serializable] public sealed class PostHogSinkOptions : TelemetrySinkOptions { public string Host = "https://eu.i.posthog.com"; public string Key; }
public static class TelemetryBuilderPostHogExtensions
{
    // 옵션이 없거나 Tiers None이거나 Key가 비면 붙이지 않는다. batchSize 20, flushSeconds 10, maxQueue 500.
    public static TelemetryBuilder AddPostHog(this TelemetryBuilder builder, IPostHogTransport transport = null);
}
public sealed class PostHogSink : ITelemetrySink   // 생성자·Classify·전송 동작은 JP와 같다
```
JP와 다른 점: 속성에 JP 전용 필드 대신 `Extra`를 넣는다(null 생략). 엔진 로그(`EngineLog` 분류)는 JP처럼 보내지 않는다(스펙 결정 13). 경고 문구는 영어, 접두어 `[Bun3.Telemetry]`.

- [ ] **Step 1: 실패하는 테스트를 쓴다.** JP 테스트를 옮기고 아래를 더한다.

```csharp
[Test] public void AddPostHog_MissingOptions_NotAttached()
[Test] public void AddPostHog_EmptyKey_NotAttached()
[Test] public void AddPostHog_NoneTiers_NotAttached()
[Test] public void AddPostHog_Attached_UsesOptionTiers()   // Tiers Analytics → Ui 기록은 큐에 없음
[Test] public void Body_ContainsExtra_SkipsNull()          // Extra [("match_id","M"),("room",null)] → "match_id":"M", "room" 없음
[Test] public void EngineLog_NotQueued()           // 묶음 Tiers가 All이어도 큐에 없음
```

- [ ] **Step 2: 실행해 실패를 확인한다.** `edit "^Bun3\.Unity\.Telemetry"`. 기대: 컴파일 오류.
- [ ] **Step 3: 구현한다.**
- [ ] **Step 4: 실행해 통과를 확인한다.** 전부 통과, 경고 0(`KitWarnings`가 `com.bun3.unity.telemetry` 접두어로 PostHog 패키지도 잡는다).
- [ ] **Step 5: 커밋한다(kit).**

### Task K6: Sentry 패키지

**Files (kit `unity/Packages/com.bun3.unity.telemetry.sentry/`):**
- Create: `package.json` (dependencies `"com.bun3.unity.telemetry": "0.1.0"`, `"io.sentry.unity": "https://github.com/getsentry/unity.git"`), `README.md`, `CHANGELOG.md`
- Create: `Runtime/Bun3.Unity.Telemetry.Sentry.asmdef` (references `Bun3.Unity.Telemetry`, `UniTask`; `overrideReferences` false라 Sentry DLL은 자동 참조), `Runtime/AssemblyInfo.cs`
- Create: `Runtime/ISentryApi.cs` (`ISentryApi`, `SentryApi`), `Runtime/SentrySink.cs`, `Runtime/SentrySinkOptions.cs`, `Runtime/SentryGate.cs`, `Runtime/TelemetrySentryConfiguration.cs`, `Runtime/TelemetryBuilderSentryExtensions.cs`
- Create: `Tests/Editor/Bun3.Unity.Telemetry.Sentry.Editor.Tests.asmdef`, `Tests/Editor/SentrySinkTests.cs`, `Tests/Editor/SentryGateTests.cs`, `Tests/Editor/SentrySettingsTests.cs`
- Modify: kit `unity/Packages/manifest.json` — `"io.sentry.unity": "https://github.com/getsentry/unity.git"`
- Modify (JP, 커밋은 J1): manifest에 `file:` 항목과 `testables`
- 출발점: JP `Telemetry/Sentry/ISentryApi.cs`, `SentrySink.cs`, `SentrySettings.cs`, `JpSentryOptions.cs`(`Configure` 본문과 `SentryPrivacy`)와 테스트 `SentrySinkTests`, `SentryPrivacyAttachmentTests`, `SentrySettingsTests`. `JoinCodeMask`는 옮기지 않는다.

**Interfaces:**
- Consumes: K1 타입, `Telemetry.Hub`, `Telemetry.ConsentChanged`, K2 `LocalJsonlSink.ReadTail`
- Produces (namespace `Bun3.Unity.Telemetry.Sentry`):
```csharp
[Serializable] public sealed class SentrySinkOptions : TelemetrySinkOptions { public string Dsn; public bool Debug; }
public interface ISentryApi   // JP와 같은 4개 메서드
public delegate bool SentryRecordHook(TelemetryRecord record, ISentryApi api);   // true면 기본 처리를 건너뛴다
public sealed class SentrySink : ITelemetrySink
{
    public SentrySink(ISentryApi api, IReadOnlyList<string> tagKeys, SentryRecordHook hook);
}
public static class TelemetryBuilderSentryExtensions
{
    // 옵션이 없거나 Tiers None이거나 Dsn이 비면 붙이지 않는다.
    public static TelemetryBuilder AddSentry(this TelemetryBuilder builder, IReadOnlyList<string> tagKeys = null,
        SentryRecordHook hook = null, ISentryApi api = null);   // api null이면 SentryApi
}
public abstract class TelemetrySentryConfiguration : SentryOptionsConfiguration
{
    public sealed override void Configure(SentryUnityOptions options);
    protected abstract TelemetryEnvironmentOptions Environment(bool building);   // building: 에디터에서 플레이 중이 아님(빌드 후처리)
    protected abstract bool Consent();                                           // 메인 스레드에서 부른다
    internal static SentrySettings Settings(TelemetryEnvironmentOptions env, bool consent, bool building, string version);
}
internal struct SentrySettings { public bool Enabled, Debug; public string Dsn, Environment, Release; public int MaxBreadcrumbs; }
internal static class SentryGate   // consent·consentEverOff(volatile), 첨부 차례(60초), Clock seam, BeforeSend, BeforeBreadcrumb, Refresh
```
- `SentrySink.Write`: 처음 한 번 `run_id`·`build`·`environment` 태그, `tagKeys`의 `Context.Get(key)` 값이 바뀌면 태그(null이면 지움), `distinct_id`가 바뀌면 `SetUser`. 훅이 true면 끝. 아니면 모든 분류를 브레드크럼으로 넣는다(`Analytics`만 info). 데이터 변환은 JP `Data`와 같다.
- `Settings`: `Dsn` 있음 && (`building` || `consent`)일 때 Enabled. `Debug`는 옵션 값, `Environment`는 묶음 `Name`, `Release`는 version, `MaxBreadcrumbs` 100. 묶음이나 Sentry 옵션이 없으면 Enabled false.
- `Configure`: JP `JpSentryOptions.Configure`의 SDK 옵션 설정을 그대로 옮긴다(SendDefaultPii false, SampleRate 1, TracesSampleRate 0, AutoSessionTracking·스크린샷·뷰 계층 끔, 로그 자동 브레드크럼 끔, `SetBeforeSend(SentryGate.BeforeSend)`, `SetBeforeBreadcrumb(SentryGate.BeforeBreadcrumb)`). 실행 중이면 `this`를 게이트의 동의 읽기 원본으로 등록하고 동의를 읽는다. 예외는 `Enabled = false`와 경고로 끝낸다.
- `SentryGate`: `BeforeSend`는 동의가 없으면 null. `consentEverOff`가 아니고 차례를 얻으면 `Telemetry.Hub?.Sink<LocalJsonlSink>()?.ReadTail(1024 * 1024)`를 `telemetry.jsonl`(`application/x-ndjson`)로 붙인다. 읽기 예외는 삼킨다. 가리기는 없다. `BeforeBreadcrumb`는 동의가 없으면 null, 아니면 그대로.
- 수명(판단 10): `SubsystemRegistration`은 첨부 차례·`consentEverOff`·Clock만 되돌리고 등록된 구성이 있으면 동의를 다시 읽는다. `BeforeSceneLoad`에서 `Telemetry.ConsentChanged -= Refresh; += Refresh; Refresh();`.

- [ ] **Step 1: 실패하는 테스트를 쓴다.** JP 세 테스트를 옮긴다(가리기 단언은 지운다). 아래를 더한다.

```csharp
// SentrySinkTests
[Test] public void TagKeys_SetFromExtra_UpdatedOnChange()   // tagKeys ["lobby_id","match_id"]: match_id M1 → M2 → null 순서로 SetTag 3회
[Test] public void Hook_HandledRecord_SkipsBreadcrumb()
[Test] public void EngineLog_AddedAsBreadcrumb()
// SentryGateTests
[Test] public void NoHub_SendsWithoutAttachment()
[Test] public void NoLocalSink_SendsWithoutAttachment()     // 허브에 HistorySink만
[Test] public void ConsentOffOnce_NeverAttachesAgain()
[Test] public void AttachmentThrottled_60Seconds()          // Clock seam: 0초 첨부, 59초 없음, 60초 첨부
[Test] public void NoConsent_DropsEventAndBreadcrumb()
// SentrySettingsTests
[Test] public void Building_IgnoresConsent()
[Test] public void Runtime_RequiresConsentAndDsn()
[Test] public void MissingOptions_Disabled()
[Test] public void ValuesFromBundle()                       // Name "release", Debug true → Environment "release", Debug true, MaxBreadcrumbs 100
// TelemetryBuilderSentryExtensions (SentrySinkTests 파일에 둔다)
[Test] public void AddSentry_MissingOptions_NotAttached()
[Test] public void AddSentry_EmptyDsn_NotAttached()
[Test] public void AddSentry_NoneTiers_NotAttached()
```

- [ ] **Step 2: 실행해 실패를 확인한다.** 기대: 컴파일 오류.
- [ ] **Step 3: 구현한다.**
- [ ] **Step 4: 실행해 통과를 확인한다.** `edit "^Bun3\.Unity\.Telemetry"` 전부 통과, 경고 0.
- [ ] **Step 5: 커밋한다(kit).**

### Task J1: JP 코드를 kit 패키지로 전환

J1은 코어·PostHog·팝업·기록 보관·Sentry를 한 번에 바꾼다. 새 `TelemetryConfig`가 Sentry 옵션 타입을 쓰고 JP의 옛 Sentry 코드가 옛 설정 필드를 읽기 때문에, 둘로 나누면 중간 상태가 컴파일되지 않는다.

**Files (JP `unity/`):**
- Modify: `Packages/manifest.json`, `Packages/packages-lock.json` — K1·K5·K6의 `file:` 항목과 `testables` 3개
- Modify: `Assets/_Game/Scripts/JP.Game.asmdef`과 텔레메트리 타입을 쓰는 테스트 asmdef — `Bun3.Unity.Telemetry`, `Bun3.Unity.Telemetry.Popups`, `Bun3.Unity.Telemetry.PostHog`, `Bun3.Unity.Telemetry.Sentry` 참조
- Modify: `Scripts/Telemetry/TelemetryConfig.cs`, `TelemetryEnvironments.cs`, `TelemetryContextSource.cs`, `TelemetryBootstrap.cs`, `MatchHistory.cs`, `MatchSummary.cs`, `MatchTelemetry.cs`, `MissionPopupTracker.cs`, `Sentry/JpSentryOptions.cs`(`TelemetrySentryConfiguration` 상속, `SentryPrivacy` 삭제)
- Modify: 텔레메트리 타입을 쓰는 나머지 JP 파일 — `using Bun3.Unity.Telemetry;`만 더한다(`grep -rlE "Telemetry\.Track|TelemetryCategory|TelemetryRecord|ITelemetrySink|TelemetryTier"`로 찾는다)
- Create: `Scripts/Telemetry/JpTelemetryRunner.cs` (지금 `TelemetryRunner`의 `PollRoom`과 `MissionPopupTracker.Tick`만), `Scripts/Telemetry/Sentry/JpSentryHooks.cs`
- Modify: `Assets/_Game/Resources/TelemetryConfig.asset` — 네 환경 묶음으로 다시 저장
- Delete (.cs와 .meta): `Telemetry.cs`, `TelemetryTypes.cs`, `TelemetryHub.cs`, `TelemetryJson.cs`, `LocalJsonlSink.cs`, `ConsoleSink.cs`, `PostHogSink.cs`, `MatchHistorySink.cs`, `EngineLogCapture.cs`, `UiControlTracker.cs`, `UiInteractionCapture.cs`, `UiPath.cs`, `ClickBurst.cs`, `DebugCaptures.cs`, `TelemetryRunner.cs`, `Sentry/ISentryApi.cs`, `Sentry/SentrySink.cs`, `Sentry/SentrySettings.cs`, `Sentry/JoinCodeMask.cs`
- Delete 테스트: EditMode `ClickBurstTests`, `DebugCapturesTests`(JP 미션 팝업 판정 단언은 `MissionPopupTrackerTests`로 옮긴다), `EngineLogCaptureTests`, `LocalJsonlSinkTests`, `MatchHistorySinkTests`, `PostHogSinkTests`, `TelemetryHubTests`, `TelemetryJsonTests`, `JoinCodeMaskTests`, `JoinCodeMaskEscapedTests`, `SentryPrivacyAttachmentTests`, `SentrySettingsTests`, `SentrySinkTests`(net 단언은 `JpSentryHooksTests`로 옮긴다); PlayMode `UiInteractionCaptureTests`
- Modify 테스트: `ListSink.cs`(kit 인터페이스), `TelemetryBootstrapTests.cs`, `MatchSummaryTests.cs`, `MatchTelemetryTests.cs`, `PlayerRoomTrackerTests.cs`, 그리고 허브를 직접 만드는 테스트 21곳 — `new TelemetryHub(ctx, consent, clock).AddSink(s, t)`를 `new TelemetryBuilder(null).Context(fill).Consent(consent).Add(s, t).Build()`로 바꾼다(`grep -rn "new TelemetryHub(" Tests`)
- Create 테스트: `MatchHistoryTests.cs`, `TelemetryConfigTests.cs`, `JpSentryHooksTests.cs`, `JpSentryOptionsTests.cs`
- 확인: `Assets/Resources/Sentry/JpSentryOptions.asset`의 스크립트 GUID가 그대로다(같은 .cs를 고친다). `Editor/TelemetrySetup.cs`가 지운 필드를 쓰지 않는다.

**Interfaces:**
- Consumes: K1~K6 전부
- Produces (JP, namespace `JP`):
```csharp
public sealed class TelemetryConfig : ScriptableObject
{
    public TelemetryEnvironmentOptions Editor, Dev, Playtest, Release;   // 필드 초기값 = 아래 묶음 값
    public TelemetryEnvironmentOptions For(TelemetryEnvironment e);
}
public static class TelemetryContextSource
{
    public static void Fill(ref TelemetryContext c);   // DistinctId, Language(L10n.Language), Extra 6개: lobby_id, is_host, match_id, session_seconds, role, room (없으면 null)
    internal static string MatchId();                  // 지금 match_id, 없으면 null
}
public static class MatchHistory
{
    public static HistorySink Sink { get; internal set; }
    public static IReadOnlyList<TelemetryRecord> Find(params string[] names);   // Sink 없으면 빈 목록
    public static void Open();    // MatchTelemetry.Entered가 부른다
    public static void Close();
    internal static void OnActiveSceneChanged(string next);   // next == SceneFlow.Menu면 Close
}
public sealed class JpSentryOptions : TelemetrySentryConfiguration
{
    protected override TelemetryEnvironmentOptions Environment(bool building);   // TelemetryConfig.Load()?.For(building ? Playtest : TelemetryEnvironments.Current)
    protected override bool Consent();                                           // PersonalSettings.Data.telemetry
}
public static class JpSentryHooks
{
    public static bool Net(TelemetryRecord record, ISentryApi api);   // log_line이고 line이 "[JP.Net]"로 시작할 때만 true. NetLogLine 변환: 샘플이면 SetContext("net", last_sample), 아니면 AddBreadcrumb(..., "net", null, false). 변환 실패도 true
}
```
- 묶음 초기값(스펙 §4.4): `editor` = Console All + Sentry(Tiers None, Debug true); `dev`·`playtest` = PostHog(Host `https://eu.i.posthog.com`, Key 빈 값, All) + Sentry(Analytics|Debug, Dsn 빈 값); `release` = PostHog(Analytics) + Sentry(Analytics|Debug). 키·DSN은 비워 둔다.
- 부트스트랩(`BeforeSceneLoad`): `new TelemetryBuilder(config.For(TelemetryEnvironments.Current)).Context(TelemetryContextSource.Fill).Consent(() => PersonalSettings.Data.telemetry).DropKeys("chat_text", "token").RunId(NetworkDiagnostics.Run).EngineLogPrefixes("[JP.Net]").Add(new LocalJsonlSink(<logs>, runId, DateTime.UtcNow, keepRuns: 20, maxFileBytes: 8 * 1024 * 1024), TelemetryTier.All).Add(history, TelemetryTier.All).AddConsole().AddPostHog().AddSentry(new[] { "lobby_id", "match_id" }, JpSentryHooks.Net).CapturePopups(GamePopups.Stack)` → `Telemetry.Install(builder)` → `MatchHistory.Sink = history` → `MissionPopupTracker`가 `GamePopups.PopupOpened/PopupClosed`를 직접 구독 → `"JP.TelemetryRunner"`(`JpTelemetryRunner`, `DontDestroyOnLoad`) 생성 → `SceneManager.activeSceneChanged`로 `MatchHistory.OnActiveSceneChanged(next.name)` → `PersonalSettings.Changed`로 `Telemetry.NotifyConsentChanged()`. 정적 구독은 `SubsystemRegistration`에서 푼다.
- `MatchTelemetry.Entered`: 새 매치 ID일 때 `MatchHistory.Open()`. `MatchEndTelemetry.Write`: `Telemetry.Hub?.ContextMatchId()` 대신 `TelemetryContextSource.MatchId()`. `MatchSummary`: `r.Context.Room` 대신 `r.Context.Get("room") as string`.

- [ ] **Step 0: 기준선을 저장한다.** 코드를 바꾸기 전 JP HEAD에서 전체 EditMode(`edit "." j0-edit`)와 전체 PlayMode(`play "." j0-play`)를 돌려 `Logs/popup-stack/j0-edit.xml`, `j0-play.xml`을 남긴다. 이때 manifest에는 K 작업의 `file:` 항목이 이미 있다.
- [ ] **Step 1: 실패하는 테스트를 쓴다.**

```csharp
// TelemetryConfigTests
[Test] public void Defaults_MatchSpec()          // new TelemetryConfig 네 묶음의 Name·옵션 종류·Tiers·Debug가 위 값과 같다
[Test] public void Asset_HasFourBundles()        // Resources.Load<TelemetryConfig>("TelemetryConfig")의 네 묶음 Name이 editor/dev/playtest/release
// MatchHistoryTests
[Test] public void Entered_OpensHistory()        // MatchTelemetry.Entered("m1", ...) 후 Sink.IsOpen
[Test] public void MenuScene_ClosesHistory()     // OnActiveSceneChanged("Menu") → IsOpen false
[Test] public void OtherScene_KeepsOpen()        // OnActiveSceneChanged("Game") → IsOpen true
[Test] public void NoSink_FindReturnsEmpty()
// TelemetryBootstrapTests (갱신)
[Test] public void EditorBundle_AttachesLocalHistoryConsole()     // Sink<ConsoleSink>() != null, Sink<PostHogSink>() == null, Sink<SentrySink>() == null
[Test] public void DevBundle_EmptyKeyAndDsn_NoRemoteSinks()
[Test] public void DropsChatAndTokenOnly()       // chat_text·token 빠짐, nickname·join_code 남음
// JpSentryHooksTests
[Test] public void NetSample_SetsContext()
[Test] public void NetLine_AddsBreadcrumb()
[Test] public void OtherLogLine_NotHandled()     // "[Other] x" → false
[Test] public void NonLogLine_NotHandled()       // engine_log → false
// JpSentryOptionsTests
[Test] public void Building_UsesPlaytestBundle() // internal seam으로 Environment(true).Name == "playtest"
```

- [ ] **Step 2: 실행해 실패를 확인한다.** `edit "TelemetryConfigTests|MatchHistoryTests|TelemetryBootstrapTests|JpSentryHooksTests|JpSentryOptionsTests"`. 기대: 컴파일 오류.
- [ ] **Step 3: 구현한다.** 순서: 파일 삭제 → asmdef 참조 → `using` 추가 → 설정·공통 속성·부트스트랩·기록 보관 → Sentry → 테스트 수정. 에셋은 에디터에서 `TelemetryConfig` 기본값이 들어가도록 다시 저장한다(`EditorUtility.SetDirty` + `AssetDatabase.SaveAssets` eval).
- [ ] **Step 4: 실행해 통과를 확인한다.** 전체 EditMode(`edit "."`)에서 j0-edit 대비 새로 실패한 이름이 없다. 지운 테스트는 비교에서 뺀다. PlayMode `SteakPlayTests|ActivityNoticeNetworkTests|MatchEndNetworkTests|StatusTelemetryNetworkTests|PersonalSettingsPlayTests`를 클래스별로 통과. JP `CsWarnings` 0, `KitWarnings` 0.
- [ ] **Step 5: 커밋한다(JP).** manifest·lock의 임시 `file:` 경로 포함. 재저장된 무관한 에셋은 되돌린다.

### Task J2: 개인정보 규칙·문서 정리와 전체 기준선

**Files (JP):**
- Modify: `unity/Assets/_Game/Tests/EditMode/Telemetry/TelemetryPrivacyScanTests.cs` — 이름·SteamID 검사 두 개 삭제(판단 12). 파일에 남는 검사가 없으면 파일을 지운다.
- Modify: `docs/superpowers/specs/2026-10-28-analytics-debug-logging-design.md`, `docs/superpowers/specs/2026-11-01-in-game-activity-logging-design.md` — 개인정보 규칙을 결정 4·5로 바꾸고(채팅 본문·토큰만 제외, 입력칸은 글자 수만), 저장소·자동 기록 구현 위치가 kit 패키지로 옮겨졌다고 적는다. 기록 보관은 열기/닫기(kit 스펙 결정 12)로 고친다. 옛 문장은 지우지 말고 바뀐 날짜와 kit 스펙 경로를 단다.
- Modify: `docs/playtest-guide.md` — 텔레메트리 설정 위치(`TelemetryConfig`의 네 환경 묶음)와 개인정보 규칙
- Modify: `unity/CLAUDE.md` — 승인 예외 목록에 "텔레메트리 패키지 3개(`com.bun3.unity.telemetry`, `.posthog`, `.sentry`)는 `#Bun3/jp-telemetry` 브랜치를 지정한다(2026-11-03). 개발 중에는 kit 작업 공간 `file:` 경로를 임시로 쓰고 `main`에 넣기 전에 브랜치 주소로 바꾼다" 한 줄
- 확인: `grep -rn "JoinCodeMask\|ForbiddenKeys\|input_text\|room_code" unity/Assets/_Game docs/playtest-guide.md`가 문서의 이력 문장 말고는 없다

- [ ] **Step 1: 검사 테스트를 고치고 문서를 고친다.**
- [ ] **Step 2: JP 전체 EditMode를 돌린다.** J1 Step 0의 `j0-edit.xml`과 비교해 새로 실패하는 테스트 이름이 없다(지운 테스트는 뺀다).
- [ ] **Step 3: JP 전체 PlayMode를 돌린다.** `j0-play.xml`과 비교한다. 새로 실패한 클래스는 따로 다시 돌린다(연쇄 실패와 `JP.Lobby.Rules.v1` PlayerPrefs 오염은 기존 문제). 개별 재실행에서도 실패하면 이 작업에서 고친다.
- [ ] **Step 4: 경고 0을 확인한다.** `CsWarnings`, `KitWarnings`.
- [ ] **Step 5: 커밋한다(JP).** 테스트 변경과 문서 변경은 목적이 다르면 커밋을 나눈다.

## 계획 뒤 (사용자 확인 필요)

- kit 브랜치 `Bun3/jp-telemetry`를 원격에 올린다.
- JP manifest의 `file:` 세 줄을 `https://github.com/Bun3/bun3-kit.git?path=unity/Packages/<패키지>#Bun3/jp-telemetry`로 바꾸고 lock을 갱신해 커밋한다.
- JP 분석 브랜치의 통합 방식(로컬 main 머지 / PR / 유지)을 고른다.
