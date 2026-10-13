# Generated Option State Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 생성된 상태 프로퍼티와 중첩 옵션 목록을 직접 수정하면 Dirty와 변경 기록이 자동 처리되고, 기존 저장 형식을 유지하면서 정확하게 롤백되는 첫 구현을 만든다.

**Architecture:** 공용 `Bun3.State`가 소유권·타입별 Undo 버퍼·추적 목록을 제공하고 생성기는 partial/interface 방식으로 필드 접근과 직접 복원을 연결한다. Items는 이 기능을 기존 Transaction 참여자 안에 통합한다. JSON/protobuf 매핑과 저장 경계는 서버 테스트의 대표 fixture로 검증한다.

**Tech Stack:** C# 9, netstandard2.1 runtime, netstandard2.0 Roslyn generator/analyzer, net10.0 NUnit tests, 기존 Google.Protobuf/Grpc.Tools/Newtonsoft.Json, 테스트 전용 protobuf-net.

**Spec:** [승인된 설계](../specs/2026-09-29-generated-option-state-design.md). 승인 후 Task 1–6의 구현·패키지 소비·측정·검증과 전체 브랜치 리뷰를 완료했다. 최종 지적 5건을 한 번에 수정하고 범위 재리뷰에서 모두 해결됐음을 확인했다. 실제 사용과 측정은 [사용 가이드](../../generated-option-state.md)에 기록한다.

- [x] 최종 전체 브랜치 리뷰 및 재리뷰 완료 — 미해결 지적 없음. 아래 단계별 체크박스는 작성 당시 계획이며 실행 증거는 SDD 작업 보고서에 있다.

최종 수정 후 검증은 State Release 66/66, Generator Release 93/93, 격리된 NuGet 소비 1/1, 관련 서버 Release 회귀 149/149, 전체 솔루션 Release 빌드 경고·오류 0이다. 서버 전체 Debug·Release 476/476은 최종 수정 전 검증이며, 수정 후에는 영향 범위 149개를 실행했다. 로컬 패키지는 최종 DLL과 README로 다시 만들었다. 커밋·배포·외부 게임 이전은 하지 않았다.

## Global Constraints

- 런타임은 netstandard2.1/C# 9, 생성기는 기존 저장소의 analyzer처럼 컴파일러 호스트용 netstandard2.0/C# 9로 구성한다. 생성 결과는 C# 9에서 컴파일되어야 한다.
- 공용 런타임은 서버 패키지, DB, protobuf를 참조하지 않는다.
- 생성기는 상태 partial class에 추적 interface와 내부 메타데이터를 추가한다. 새로운 기반 클래스 상속을 강제하지 않는다.
- 새 런타임 패키지는 최초 구현 시 `0.1.0`, Items는 새 공개 기능에 맞춰 `0.5.0`으로 올린다. 다른 패키지 소스까지 변경하면 해당 버전도 올린다.
- 첫 버전은 **모든 실변경을 순서대로 기록**한다. 최초 필드 변경만 보관하는 중복 제거는 적용하지 않는다.
- 스칼라 이전 값을 object로 boxing하거나, 매 변경마다 Action/클로저·이벤트 객체·문자열을 생성하지 않는다.
- **무복사 보장은 생성 상태의 롤백에 적용된다.** 인벤토리 멤버십과 재생 기준 사전의 기존 얕은 복사는 유지한다.
- `DrainChanges`는 DB ACK가 아니며, 저장 작업은 전달받은 버퍼를 성공까지 보유한다.
- 기존 미커밋 수정은 보존한다. 기존 worktree에서 작업하며 커밋·배포·외부 게임 수정·DB 접속은 하지 않는다.
- 제품 코드와 주석·XML·README는 영어, 설명 문서는 `docs/`에 한국어로 작성한다. 모든 public 멤버에 영어 XML 문서를 작성하고 Release 빌드 경고를 없앤다.
- 파일 배치는 폴더=namespace, partial 파일은 `Type.Role.cs`, 단독 제네릭 파일은 `Type{T}.cs`를 따른다.

## Review Focus

1. readonly 목록의 getter를 처음 읽기 전 로딩/연결하는 경우에도 자식 소유권이 검증돼야 한다. Task 2/3의 초기 그래프 검증 테스트가 담당한다.
2. `Equals`를 재정의한 서로 다른 옵션 객체는 서로 다른 노드다. Task 2의 참조 동일성 테스트가 담당한다.
3. 트랜잭션에서 생성 후 삭제된 아이템과 그 옵션의 외부 참조가 살아 있어도 완료 뒤 인벤토리나 Undo가 붙잡히면 안 된다. Task 4/6의 수명 테스트가 담당한다.
4. 알 수 없는 자식 필드가 원소 재정렬 후 다른 원소에 붙으면 안 된다. Task 5의 노드별 메타데이터 왕복 테스트가 담당한다.
5. 프로젝트 참조로 성공한 생성기가 NuGet 소비에서는 누락되거나 Roslyn을 런타임에 끌어오면 안 된다. Task 6의 외부 소비 테스트가 담당한다.

## 실행 범위와 파일 구조

현재 브랜치는 `Bun3/server-outgame-items-achievements`이며 이전 알림·스냅샷 회귀 수정이 미커밋 상태다. 실행 시작 시 HEAD와 전체 status, tracked diff, 기존 untracked 파일 목록을 로컬 작업 기록에 보관한다. 리뷰는 실행 전 상태 대비 이번 변경을 구별해야 한다. 미커밋 변경이 빠지는 HEAD 간 diff만 리뷰 입력으로 쓰지 않는다.

`CLAUDE.md`가 지정한 SDD를 실행 방식으로 사용한다. 아래 6개 작업은 순서대로 구현하고 각 작업마다 검토한다. 동시에 여러 구현자가 공유 파일을 수정하지 않는다. 이 계획 작성 단계에서는 구현자나 리뷰어를 실행하지 않는다.

| 묶음 | 생성·변경 경로 | 역할 |
|---|---|---|
| runtime | `common/src/Bun3.State/` | attribute, 노드/소유자 계약, binding, journal, 목록 |
| runtime tests | `common/tests/Bun3.State.Tests/` | 손으로 작성한 작은 소비 상태를 통한 runtime 검증 |
| generator | `common/src/Bun3.State.Generator/` | 코드 생성, 스키마·직접 쓰기 진단 |
| generator tests | `common/tests/Bun3.State.Generator.Tests/` | 생성 결과 컴파일·실행, analyzer 진단 |
| Items | `server/src/Bun3.Server.Items/ItemInstance.StateTracking.cs`, `ItemInventory.StateTracking.cs`와 기존 수명 파일 | 기존 인스턴스·Transaction 연결 |
| server tests | `server/tests/Bun3.Server.Tests/GeneratedOption*.cs`, `Protos/tracked_options.proto` | 인벤토리·업적 통합, 저장 형식과 실패 검증 |
| packaging/docs | runtime csproj, `common/tests/Bun3.State.Package.Tests/`, `docs/generated-option-state.md` | analyzer 포함 패키지 소비, 비용과 사용법 |

## 공통 구현 계약

아래는 작업 간 이름과 호출 계약이다. 낮은 수준의 조정 interface는 생성 코드와 프레임워크용으로 XML에 표시한다. 게임이 보통 호출하는 것은 생성된 프로퍼티와 `TrackedList<T>`뿐이다.

```csharp
public interface ITrackedNode
{
    StateNode Tracking { get; }
    void VisitChildren(IStateChildVisitor visitor);
}
public interface ITrackedState : ITrackedNode { }
public interface IStateChildVisitor { void Visit(ITrackedNode child); }
public interface IStateValueTarget<T> { void RestoreValue(int slot, T value); }
public interface IStateOwner
{
    StateJournal? BeforeChange();
    void OnChanged();
}
```

- `ITrackedState`와 `IStateValueTarget<T>`는 생성 class에서 명시적으로 구현한다. `TrackedList<T>`도 `ITrackedNode`를 명시적으로 구현한다.
- `BeforeChange()`는 실제 값 변경 전에 호출한다. 서버에서는 기존 인스턴스 baseline 캡처와 참여자 등록 후 활성 journal을 반환하고, 비활성이면 null이다. 이 호출만으로 Dirty/외부 알림을 발생시키지 않는다.
- `OnChanged()`는 변경 완료 후 현재 아이템에 연결된 노드에서만 호출한다. 분리됐지만 기록 소속을 유지한 노드는 BeforeChange/Undo 기록만 사용한다.
- `StateBinding`은 아이템 하나의 안정적인 소유권 식별자다. State 참조 교체로 binding을 새로 만들지 않는다.
- `StateJournal`은 한 인벤토리의 모든 생성 상태 변경 순서를 묶는다. scalar/root/child/list 기록과 소유권 기록이 같은 역순 실행에 참여한다. 다른 플레이어와 공유하지 않는다.
- `ITrackedNode.VisitChildren`은 생성 필드와 목록 원소를 직접 방문한다. `IEnumerable`/iterator를 매 변경마다 만들지 않는다. 초기 그래프 연결 비용과 준비된 변경 경로 비용을 구별한다.
- 생성된 `RestoreValue`는 해당 private 필드만 대입한다. 소유권 복원은 journal의 연결 기록이 담당하고, 복원 중 setter/owner callback을 호출하지 않는다.

### Task 1: 스칼라 변경 기록과 공용 상태 수명

**Files:**
- Create: `common/src/Bun3.State/Bun3.State.csproj`, `TrackedStateAttribute.cs`, `TrackedAttribute.cs`, `ITrackedNode.cs`, `ITrackedState.cs`, `IStateChildVisitor.cs`, `IStateValueTarget{T}.cs`, `IStateOwner.cs`, `StateNode.cs`, `StateBinding.cs`, `StateJournal.cs`, `StateJournal.Values.cs`.
- Create/Test: `common/tests/Bun3.State.Tests/Bun3.State.Tests.csproj`, `ScalarTrackingTests.cs`, `TestState.cs`, `TestStateOwner.cs`.
- Modify: `Bun3.sln`.

**Interfaces:**
- Consumes: 공통 구현 계약. 생성기 없이 명시적 interface를 구현한 `TestState`를 테스트에 사용한다.
- Produces: `StateNode(ITrackedNode target)`, `bool SetValue<T>(int slot, ref T storage, T value)`.
- Produces: `StateBinding(IStateOwner owner)`, `void AttachRoot(ITrackedState root)` 및 읽기 전용 `ITrackedState? Root`.
- Produces: `StateJournal`의 `void RecordValue<T>(IStateValueTarget<T> target, int slot, T oldValue)`, `void Commit()`, `void Rollback()`. journal 생성은 owner의 첫 활성 변경에 한정한다.
- Produces: 테스트 owner의 `Begin()`, `Commit()`, `Rollback()`, `PrepareCalls`, `ChangedCalls`, `JournalCreations`. 이들은 제품 API가 아니다.

- [ ] **Step 1: 최소 프로젝트와 실패 테스트를 함께 작성한다.** runtime `0.1.0`, test net10.0; NUnit/Test SDK 버전은 기존 common 테스트 프로젝트를 따른다. `ScalarTrackingTests.Repeated_writes_restore_original_value_without_notifying`에서 `1 → 5 → 9` rollback 결과 `1`을 검증한다. 별도 commit 결과 `9`, `1 → 5 → 1`의 정확한 rollback, 같은 값 대입 시 owner 호출 0도 검증한다.

```csharp
owner.Begin();
state.Level = 5;
state.Level = 9;
owner.Rollback();
Assert.That(state.Level, Is.EqualTo(1));
Assert.That(owner.ChangedCalls, Is.EqualTo(2)); // Replay must not notify.
```

- [ ] **Step 2: RED를 기록한다.** `dotnet test common/tests/Bun3.State.Tests/Bun3.State.Tests.csproj --filter FullyQualifiedName~ScalarTrackingTests` 실행. 미구현 타입/메서드 또는 명시한 assertion 때문에 실패해야 한다. restore/환경 실패는 RED로 취급하지 않는다.
- [ ] **Step 3: 스칼라 runtime을 구현한다.** `EqualityComparer<T>.Default`로 no-op을 먼저 판정하고 BeforeChange → typed record → 필드 대입 → OnChanged 순서로 처리한다. enum/nullable에도 동일하게 적용한다. 타입별 버퍼에 target/slot/oldValue를 저장하고, 전체 순서는 버퍼 참조와 항목 인덱스를 가진 struct 목록으로 기록한다. 매 항목에 class/Action을 만들지 않는다.
- [ ] **Step 4: 수명 테스트를 추가한다.** 연결 전 변경은 owner 호출 없음; transaction 없음/비활성/빈 transaction은 `JournalCreations == 0`; active/commit/rollback을 반복해 이전 기록이 재사용되지 않음; nullable `null → 0 → 4 → rollback` 결과 null; enum과 string 복원; 서로 다른 owner 2개의 완료가 상대 상태에 영향 없음. journal 완료 뒤 동일 scope의 중복 완료 호출은 부작용 없이 종료하도록 한다.
- [ ] **Step 5: GREEN과 리뷰 입력을 기록한다.** Step 2 명령을 다시 실행하고 runtime `dotnet build common/src/Bun3.State/Bun3.State.csproj -c Release -warnaserror`를 실행한다. 테스트 결과·변경 파일·고정 interface를 작업 보고서에 남긴다. 커밋하지 않는다.

### Task 2: 중첩 소유권과 추적 목록

**Files:**
- Create: `common/src/Bun3.State/StateNode.Children.cs`, `StateBinding.Ownership.cs`, `StateJournal.Ownership.cs`, `TrackedList{T}.cs`.
- Modify: `StateNode.cs`, `StateBinding.cs`, `StateJournal.cs`, `ITrackedNode.cs`의 XML.
- Create/Test: `common/tests/Bun3.State.Tests/OwnershipTests.cs`, `TrackedListTests.cs`, `DetachedNodeTests.cs`; extend `TestState.cs`.

**Interfaces:**
- Consumes: Task 1의 owner, typed journal, root binding.
- Produces: `StateNode`의 `bool SetChild<T>(int slot, ref T storage, T value)`; 생성기가 허용한 nullable/nonnull 상태 참조에 사용하는 메서드이며 구조 검증 후 기록한다.
- Produces: `StateBinding`의 `void ReplaceRoot(ITrackedState? root, StateJournal? journal)`, `void DetachRoot(StateJournal? journal)`; 이 조정 메서드는 owner callback을 호출하지 않고 연결 변경만 검증·기록·적용한다. `DetachRoot(journal)`는 null root 교체와 동일한 수명이다.
- Produces: `TrackedList<T> : IList<T>, IReadOnlyList<T>, ITrackedNode`, 기본/`int capacity` 생성자, struct Enumerator. element 종류는 T마다 한 번 분류하고 scalar 경로에 노드 검사 boxing을 만들지 않는다.
- Produces: journal 완료 시 분리 노드의 기록 소속을 해제하는 정리 경로. 연결은 검증 단계와 적용 단계를 분리한다.

- [ ] **Step 1: 실패 테스트를 작성한다.** `DetachedNodeTests.Removal_edit_reinsert_and_clear_restore_original_nodes`는 두 옵션 A(Level=1), B(Level=2)로 시작해 A 제거 → A.Level=7 → B 교체 → A 재삽입 → Clear → rollback한다. 결과는 같은 A/B 참조의 `[A, B]`, level `[1, 2]`이고 replay의 ChangedCalls 증가는 0이다. 새 C 추가 → C 수정 → rollback 후 목록에 C가 없고 C는 추가 당시 값으로 복원돼야 한다.
- [ ] **Step 2: RED를 기록한다.** `dotnet test common/tests/Bun3.State.Tests/Bun3.State.Tests.csproj --filter "FullyQualifiedName~OwnershipTests|FullyQualifiedName~TrackedListTests|FullyQualifiedName~DetachedNodeTests"`.
- [ ] **Step 3: 부모/소유권과 역연산을 구현한다.** 이미 연결된 노드의 중복 사용과 순환은 실제 참조로 검사한다. 제거/교체된 하위 트리는 active journal의 원래 binding을 유지하고 같은 binding에만 재연결한다. Clear는 마지막 인덱스부터 RemoveAt을 기록한다. Insert/Remove/Replace는 typed 값과 원래 위치를 기록하고 내부 복원 연산으로 역전한다. 리스트 원소의 Equals 재정의가 동일성 판단을 바꾸지 않게 상태 노드는 참조로, scalar는 값으로 검색/비교한다.
- [ ] **Step 4: 그래프 실패 원자성과 수명 테스트를 추가한다.** 같은 노드를 2개 필드/목록 위치/아이템에 연결, self/ancestor cycle, 연결 전 초기화된 그래프의 공유 자식, invalid index를 각각 거부하고 기존 구조·Dirty·PrepareCalls가 그대로인지 확인한다. 같은 참조 교체/없는 값 Remove/빈 Clear는 no-op이다. null 자식/nullable 원소는 값으로 지원하며 소유권 대상이 아니다. 버전이 다른 서로 다른 옵션이 Equals상 같더라도 교체되고 정확한 참조가 제거돼야 한다.
- [ ] **Step 5: 분리 후 종료를 검증한다.** 제거 후 수정 rollback, 같은 루트 재삽입, 다른 루트 재연결 거부, commit 뒤 제거 노드 수정은 이전 owner 호출 0, commit 뒤 다른 루트에 정상 연결을 검증한다. `AttachRoot` 중간에 공유 자식을 발견해도 앞서 방문한 형제에게 binding이 남지 않아야 한다. 정상 scalar 변경은 전체 옵션 트리를 다시 방문하지 않아야 한다.
- [ ] **Step 6: GREEN과 리뷰를 수행한다.** 전체 `Bun3.State.Tests` 실행; Task 1 회귀도 함께 통과해야 한다. 생성 상태와 목록을 통한 결과를 검증하며 private 보관 필드명에 테스트를 결합하지 않는다.

### Task 3: 실제 생성기와 변경 우회 진단

**Files:**
- Create: `common/src/Bun3.State.Generator/Bun3.State.Generator.csproj`, `TrackedStateGenerator.cs`, `TrackedStateModel.cs`, `TrackedStateEmitter.cs`, `TrackedStateAnalyzer.cs`, `StateDiagnostics.cs`, `AnalyzerReleases.Shipped.md`, `AnalyzerReleases.Unshipped.md`.
- Create/Test: `common/tests/Bun3.State.Generator.Tests/Bun3.State.Generator.Tests.csproj`, `GeneratorCompilation.cs`, `GeneratedStateTests.cs`, `StateDiagnosticTests.cs`.
- Modify: `Bun3.sln`.

**Interfaces:**
- Consumes: Tasks 1–2의 runtime interface와 StateNode setter. Roslyn `Microsoft.CodeAnalysis.CSharp [4.8.0]`는 기존 analyzer와 같은 버전으로 고정하고 generator 측 `PrivateAssets="all"`을 지정한다.
- Produces: `TrackedStateGenerator : IIncrementalGenerator`, `TrackedStateAnalyzer : DiagnosticAnalyzer`.
- Produces: `[TrackedState] sealed partial class`에서 명시적 `ITrackedState`, 필요한 `IStateValueTarget<T>`별 RestoreValue, private tracking metadata, 프로퍼티, VisitChildren. framework callback은 public 게임 메서드로 추가하지 않는다.

| 진단 | Error 조건 |
|---|---|
| B3STATE0001 | partial/sealed/namespace 조건 불충족, nested/generic/struct/추적 상속 등 지원하지 않는 상태 선언 |
| B3STATE0002 | 지원하지 않는 필드 타입, static tracked 필드, readonly scalar, readonly가 아닌 tracked list, private가 아닌 tracked 필드 |
| B3STATE0003 | 생성 프로퍼티/메타데이터 이름 충돌 또는 유효하지 않은 생성 이름 |
| B3STATE0004 | 필드 초기값 선언 밖에서 직접 대입·복합 대입·증감·deconstruction 대입·ref/out 노출 |
| B3STATE0005 | 스키마가 직접 선언한 가변 필드/가변 auto-property가 추적 대상에서 누락 |

- [ ] **Step 1: 생성 결과를 사용하는 실패 테스트를 작성한다.** `GeneratedStateTests.Generated_nested_properties_restore_values_and_identity`는 스펙의 EquipmentState/EquipmentOptionState/RerollOptionState를 C# 9 compilation에 넣고 생성 후 assembly를 emit하여 생성 프로퍼티로 변경/rollback한다. `Level` 기본값 1, nested option identity, readonly 목록 getter를 먼저 읽지 않은 상태에서 AttachRoot한 뒤 자식 수정의 Dirty 전파를 검증한다. 다른 기반 class의 메서드가 그대로 호출되어야 한다.
- [ ] **Step 2: RED를 기록한다.** `dotnet test common/tests/Bun3.State.Generator.Tests/Bun3.State.Generator.Tests.csproj`. 참조 없는 생성기나 생성 프로퍼티 미존재로 실패해야 한다.
- [ ] **Step 3: 생성기를 구현한다.** 필드는 ordinal 이름 순서로 고정된 slot을 부여하고 fully-qualified 타입 이름을 출력한다. 선행 `_` 제거 + 첫 문자 invariant 대문자로 프로퍼티명을 만든다. getter/metadata 초기화에서 자식 초기값도 추적 노드에 연결한다. scalar RestoreValue는 raw 필드 대입, child RestoreValue도 raw 대입이며 연결 기록은 Task 2가 맡는다. public 프로퍼티에 영어 XML 문서를 생성한다. 잘못된 타입은 진단으로 끝내고 연쇄적인 깨진 생성 코드를 내지 않는다.
- [ ] **Step 4: 진단 테스트를 구현한다.** 표의 각 ID를 정확히 검증한다. alias attribute, 여러 partial 파일, 두 namespace의 동명 class, nullable 설정, `_` 단독 이름, `_x`/`x` 충돌, 기존 Level/추적 메타데이터 멤버 충돌을 포함한다. 사용자 `.g.cs` 파일명을 붙이는 것만으로 직접 쓰기 진단이 우회되지 않게 생성 provenance를 구별한다. 생성기 자신의 복원 코드는 진단에서 제외한다.
- [ ] **Step 5: 지원 타입 표를 테스트한다.** primitive/enum/string/decimal/DateTime/DateTimeOffset/TimeSpan/Guid/nullable, nullable child, scalar 및 child 목록이 컴파일된다. array/List/Dictionary/protobuf형 class/custom struct는 B3STATE0002다. readonly로 감싼 가변 참조의 누락과 auto-property도 진단하고, 상속받은 비추적 상태까지 보장한다고 설명하지 않는다.
- [ ] **Step 6: GREEN과 리뷰를 수행한다.** generator 테스트와 `dotnet build common/src/Bun3.State.Generator/Bun3.State.Generator.csproj -c Release -warnaserror` 실행. 생성 문자열 전체 golden 비교 대신 컴파일·실행과 진단을 판정 기준으로 사용한다.

### Task 4: Items 수명과 Transaction 통합

**Files:**
- Create: `server/src/Bun3.Server.Items/ItemInstance.StateTracking.cs`, `ItemInventory.StateTracking.cs`.
- Modify: `ItemInstance.cs`, `ItemInventory.Transaction.cs`, `ItemInventory.Tracking.cs`, `ItemInventory.Operations.cs`, `ItemInventory.Transfer.cs`, `ItemInventory.cs`, `Bun3.Server.Items.csproj`.
- Modify: `server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`.
- Create/Test: `server/tests/Bun3.Server.Tests/GeneratedOptionState.cs`, `GeneratedOptionInventoryTests.cs`, `GeneratedOptionTransactionTests.cs`.

**Interfaces:**
- Consumes: Task 3의 생성 상태, Task 1–2 runtime; 기존 `ItemInventory<TState>` constructor, `TryLoadInstance`, `State`, `BeginStateChange`, `DrainChanges`, `Transaction.Begin()`과 참여자 완료 단계.
- Produces: `ItemInstance<TState>` partial가 명시적 `IStateOwner`, `IStateValueTarget<TState>`를 구현한다. 기존 public constructor/method signature는 유지한다.
- Produces: `ItemInventory<TState>`의 `internal StateJournal? PrepareStateChange(ItemInstance<TState> instance)`; active이면 기존 Capture 후 lazy journal 반환, inactive이면 null.
- Produces: `ItemInstance<TState>`의 `internal void InitializeStateTracking()`, `internal void DetachStateTracking(StateJournal? journal)`, `internal void FinalizeStateTracking()`. StateBinding은 필요한 tracked 상태에서만 생성한다.

- [ ] **Step 1: 실제 생성된 상태로 실패 통합 테스트를 작성한다.** 서버 테스트 프로젝트에 runtime과 generator의 analyzer ProjectReference를 추가한다. `GeneratedOptionInventoryTests.Nested_edit_marks_loaded_item_without_snapshot_delegate`는 namespace 수준의 `[TrackedState]` 스키마를 사용한다. load 뒤 HasChanges false, 자식 Level 변경 뒤 Updated 1개, snapshot delegate 호출 0, 트랜잭션 없는 동일 사용을 검증한다.
- [ ] **Step 2: RED를 기록한다.** `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --filter "FullyQualifiedName~GeneratedOptionInventoryTests|FullyQualifiedName~GeneratedOptionTransactionTests"`.
- [ ] **Step 3: load/create/change 경로를 연결한다.** Items에 공용 runtime 참조와 버전 `0.5.0`을 적용한다. `ItemInstance`를 partial로 나누고 역할 주석/XML을 보완한다. load는 컨테이너 삽입 전 소유권 검증을 끝내고 Dirty/Undo 없이 연결한다. 생성 상태의 `BeginStateChange()`는 transaction 유무와 관계없이 편집 시작 전에 InvalidOperationException을 던진다. opaque TState는 그대로 둔다.
- [ ] **Step 4: root 교체/delete/transfer 경로를 연결한다.** 생성 상태 교체에서는 새 그래프 검증 → Capture → 이전 `_state` typed 기록 → binding 교체 기록/적용 → 필드 대입 → MarkChanged 순서를 따른다. 생성 상태의 동일 참조 교체는 no-op이다. opaque 상태에는 기존 대입 의미를 유지한다. 제거된 아이템은 `_owner == null`이어도 active journal과 기존 트랜잭션 소속을 유지하고, 종료 시 해제한다. 정상 transfer에서는 같은 인스턴스/binding이 새 owner를 사용하고, 활성 transaction의 이동 거부는 유지한다.
- [ ] **Step 5: commit/rollback 순서를 구현한다.** OnCommit은 journal commit·최종 분리 노드 해제 후 기존 알림을 freeze한다. OnRollback은 generated journal 역순 복원 → 기존 컨테이너/인스턴스 baseline 복원 → 새로 생성된 phantom의 binding 분리와 최종 소유권 정합화 → 보관 해제 순서다. root 교체의 raw `_state` 복원도 같은 journal에 들어간다. 기록된 옵션 및 소유권 복원이 끝나기 전에 외부 콜백을 실행하지 않는다.
- [ ] **Step 6: 복합 회귀 테스트를 추가한다.** 재료 수량 10 → 7, 장비 Level 1 → 5, 옵션 Level 2 → 9, 업적 진행 0 → 1 이후 미커밋 dispose 결과 모두 원래 값/참조다. 시작 전 Dirty/저장 대기 변경은 남는다. 원래 root A 수정 → root B 교체 → 분리된 A 재수정 → B 수정 → rollback에서 A/B 내용과 A 참조를 검증한다. 신규 아이템 생성·옵션 변경·삭제 후 commit/rollback 각각에서 외부 참조의 추가 수정이 인벤토리를 오염시키지 않아야 한다.
- [ ] **Step 7: 완료와 callback 회귀를 검증한다.** 2개 인벤토리+업적이 같은 Transaction을 사용할 때 commit callback이 다른 참가자의 최종 상태를 관찰해야 한다. 첫 callback이 던져도 뒤 참가자 정리, rollback 뒤 재사용, commit callback에서 새 옵션 변경을 각각 검증한다. 다른 플레이어의 활성 Transaction과 겹쳐도 기록이 섞이지 않는다. invalid load/root 공유는 입력 state와 기존 인벤토리를 변경하지 않아야 한다.
- [ ] **Step 8: GREEN과 기존 회귀를 수행한다.** Step 2 필터에 `ItemStateRollbackTests|ItemTransferTests|OutgameNotificationTests|TransactionTests|AchievementTransactionTests`를 포함해 실행한다. 순수 수량/opaque 상태 경로의 기존 테스트가 그대로 통과해야 한다. 비용 주장은 전체 사전 보관이 남는다는 조건을 유지한다.

### Task 5: 저장 형식 왕복과 저장 실패

**Files:**
- Create/Test: `server/tests/Bun3.Server.Tests/Protos/tracked_options.proto`, `GeneratedOptionWire.cs`, `GeneratedOptionMapping.cs`, `GeneratedOptionPersistenceTests.cs`, `GeneratedOptionSaveTests.cs`.
- Modify: `server/tests/Bun3.Server.Tests/GeneratedOptionState.cs`, `Bun3.Server.Tests.csproj`.
- Create: `docs/research/2026-09-30-generated-option-wire-fixtures.md`.

**Interfaces:**
- Consumes: Task 4의 상태/인벤토리, 기존 Google.Protobuf 3.29.3, Grpc.Tools 2.68.1, Newtonsoft.Json 13.0.2; protobuf-net은 테스트 프로젝트에만 추가한다. 고정 버전은 기존 게임의 2.4.6 계열과 fixture API 호환성을 확인하고 명시적 PackageReference로 기록한다. 런타임 패키지에는 전파하지 않는다.
- Produces: 테스트 전용 `GeneratedOptionMapping.LoadJson(string json)`, `LoadBlob(byte[] data)` → `EquipmentOptionState`; `SaveJson(EquipmentOptionState state)` → string, `SaveBlob(EquipmentOptionState state)` → byte[]. 매핑 객체는 외부 노드별 원본 메타데이터를 약한 키로 소유한다.
- Produces: 테스트 전용 `PendingOptionSave`는 `void Capture()`, `Task SaveAsync()`와 캡처된 payload를 보관한다. 동일 저장분을 ACK 전까지 다시 Drain하지 않으며 진행 중 저장은 한 개다. 이는 범용 제품 저장 큐의 추가가 아니다.

- [ ] **Step 1: 독립 fixture와 실패 테스트를 작성한다.** `GeneratedOptionPersistenceTests.Reordering_preserves_each_nodes_unknown_data`를 JSON/protobuf에 각각 작성한다. 새 proto는 Google.Protobuf용 JSON/메시지 모델을 생성하고, protobuf-net DTO는 `[ProtoContract]`/`[ProtoMember]`/`IExtensible`을 사용한다. 아래 고정 번호를 사용하고 fixture 문서에 원본 대응을 기록한다. 원본 게임을 참조하거나 타입을 통째로 복사하지 않는다. 고정 wire payload는 읽을 수 있는 hex 상수와 스키마 설명으로 테스트에 둔다.

| fixture | 고정된 대표 필드 번호 | 근거 |
|---|---|---|
| Google.Protobuf 옵션 | root `rerollOptions=2`, `productOption=15`; entry `id=1`, `poolId=2`, `level=3` | idlez `Server/Commons/Types/Players/PlayerItem.cs` |
| protobuf-net 옵션 | root `entries=1`, `version=2`, `mainOptionLevel=3`, `petOption=5`; entry `id=1`, `level=2` | growdaemon `network.proto:682`, growninja `Commons/network.cs:1399`의 root 공통 구조 |

Google.Protobuf의 일반 proto3 scalar는 미설정/0을 구별하지 않는 fixture로 두고, protobuf-net의 nullable backing/ShouldSerialize 조합에서 미설정/명시적 0을 검증한다. 임의 optional 키워드를 추가해 기존 포맷의 의미를 바꾸지 않는다. root/entry에 예약되지 않은 field 99를 unknown 데이터로 추가하고, 각 entry에는 서로 다른 값을 사용한다.

- [ ] **Step 2: RED를 기록한다.** `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --filter "FullyQualifiedName~GeneratedOptionPersistenceTests|FullyQualifiedName~GeneratedOptionSaveTests"`.
- [ ] **Step 3: 명시적 매핑을 구현한다.** 로딩은 연결 전 생성 프로퍼티로 상태를 채운다. 원본 DTO/JSON 메타데이터는 node identity와 연결하고 game state에 raw DTO를 넣지 않는다. 저장은 해당 메타데이터의 복제본에서 알려진 필드를 현재 상태로 갱신한다. JSON은 기존 `JObject.FromObject`/`ToObject<T>` 경로의 이름·값 표현을 fixture로 고정하고 protobuf `JsonFormatter`로 대체하지 않는다. unknown property는 원본 JSON 객체 병합으로, protobuf unknown field는 해당 라이브러리의 보존 모델로 유지한다. 알려진 자식 목록은 현재 순서로 재구성하되 각 자식의 unknown 데이터는 같은 상태 객체를 따른다.
- [ ] **Step 4: 포맷 assertion을 고정한다.** 미설정과 명시적 0, nullable child null과 빈 child, 목록 `[A, B] → [B, A]`, root/child unknown 필드, 새 노드 추가·기존 노드 제거, 값 변경 후 round-trip을 검증한다. 실제 포맷이 구별하지 못하는 null/empty를 억지로 요구하지 않고 그 한계를 fixture 표에 쓴다. 동일 생성기로 만든 round-trip만 통과시키지 않도록 기존 형식의 고정 payload를 독립 입력으로 사용한다.
- [ ] **Step 5: 저장 실패/중간 변경 테스트를 구현한다.** `GeneratedOptionSaveTests.Retry_acknowledges_only_captured_payload`에서 TaskCompletionSource로 가짜 I/O를 제어한다. v1 payload 캡처 → 전송 실패 → 같은 v1 재시도 → 성공 동안, 상태를 v2로 바꾸면 v2의 Updated가 다음 Drain에 남아야 한다. 첫 ACK 뒤 pending v1만 해제하고 두 번째 저장으로 v2를 전송한다. 상태 접근은 한 실행 흐름에서 직렬화하고, 백그라운드 I/O는 준비된 payload만 읽는다.

Player 쪽은 별도 세션 harness 테스트로 실제 저장 중 `MarkDirty`가 살아남는지 검증한다. 상태를 다른 스레드에서 직접 수정하거나 테스트를 위해 internal `TrySaveAsync`를 public으로 바꾸지 않는다. 재시도 시점에는 Player가 새 dirtyVersion을 캡처할 수 있으므로 **오래된 pending payload만 저장하고 새 버전까지 저장했다고 처리하면 안 된다.** fixture의 저장 hook은 pending ACK 후 `inventory.HasChanges`가 남으면 `MarkDirty()`를 다시 호출하고, 다음 저장까지 IsDirty가 유지되는 것을 검증한다. 이 hook의 조정이 필요한 현재 계약도 사용 설명에 기록한다. 임의 Task.Delay 타이밍에 의존하지 않는다.

- [ ] **Step 6: 저장과 Undo의 분리를 검증한다.** 옵션 transaction rollback은 새 저장 변경을 남기지 않지만 이전 pending 저장분은 보존한다. memory commit은 pending 저장분을 지우지 않는다. 캡처한 payload는 후속 상태 수정으로 바뀌지 않는다. 실제 DB 접속 없이 테스트하며 DB 통합 완료라고 보고하지 않는다.
- [ ] **Step 7: GREEN과 리뷰를 수행한다.** Step 2 필터 실행. fixture 문서에 실제 사용한 serializer 버전, 호환이 확인된 의미, 확인하지 않은 실제 게임 DLL/DB 환경을 기록한다. 기존 게임별 매핑을 자동 제공한다고 설명하지 않는다.

### Task 6: 할당·수명·패키지 소비와 최종 검증

**Files:**
- Create/Test: `common/tests/Bun3.State.Tests/StateAllocationTests.cs`, `StateRetentionTests.cs`.
- Create/Test: `common/tests/Bun3.State.Package.Tests/Bun3.State.Package.Tests.csproj`, `StatePackageTests.cs`, `Fixtures/Consumer/Consumer.csproj`, `Fixtures/Consumer/EquipmentState.cs`, `Fixtures/Consumer/Program.cs`.
- Modify: `common/src/Bun3.State/Bun3.State.csproj`, `Bun3.sln`.
- Create: `common/src/Bun3.State/README.md`, `docs/generated-option-state.md`.
- Modify: `docs/outgame-rollback.md`, `docs/research/2026-09-29-game-state-generation-and-persistence.md`의 구현 현황 링크.

**Interfaces:**
- Consumes: Tasks 1–5의 완료된 runtime/generator/Items/fixture와 기존 analyzer packaging 패턴.
- Produces: `Bun3.State.0.1.0.nupkg`에 netstandard2.1 runtime과 `analyzers/dotnet/cs/Bun3.State.Generator.dll`. Roslyn/runtime DB/protobuf 의존성을 추가하지 않는다.
- Produces: runtime 패키지만 명시적으로 참조한 C# 9 소비 프로젝트에서 자동 생성·실행 성공과 진단 동작. 패키지 테스트의 출력/소비 프로젝트는 임시 디렉터리에 복사해 사용한다.

- [ ] **Step 1: 실패 성능/수명/소비 테스트를 작성한다.** `StateAllocationTests.Warmed_changes_allocate_no_per_write_objects`에서 warm-up 뒤 `GC.GetAllocatedBytesForCurrentThread()`로 scalar 변경, 이미 만들어진 child 교체, capacity가 준비된 목록 변경을 각각 10,000회 측정한다. transaction 없음/비활성/활성(commit/rollback 별도)을 구분하고 측정 구간에는 assertion/새 노드/DTO 변환을 넣지 않는다. 준비된 runtime scalar/list 경로는 추가 할당 0을 기대한다. root 그래프 초기 연결 및 버퍼 성장 비용은 별도 수치로 기록한다.
- [ ] **Step 2: RED를 기록한다.** `dotnet test common/tests/Bun3.State.Tests/Bun3.State.Tests.csproj -c Release --filter "FullyQualifiedName~StateAllocationTests|FullyQualifiedName~StateRetentionTests"`; `dotnet test common/tests/Bun3.State.Package.Tests/Bun3.State.Package.Tests.csproj -c Release`. 아직 포장되지 않은 analyzer 때문에 소비 테스트가 실패해야 한다.
- [ ] **Step 3: 보관 수명과 변경량 측정을 완성한다.** helper 메서드 밖에서 WeakReference를 확인해 JIT 지역 수명 영향을 피한다. commit 뒤 제거 노드, rollback 뒤 생성 노드, 종료된 owner가 live journal/기존 외부 분리 노드 때문에 남지 않아야 한다. 10개/10,000개 옵션에서 같은 scalar 1개 수정 시 준비된 Undo 경로가 동일한 기록량/할당을 보이고 깊은 복사 함수를 호출하지 않는지 검증한다. 기존 inventory dictionary 복사 비용을 이 수치와 섞지 않는다.
- [ ] **Step 4: analyzer 자산과 소비 검증을 구현한다.** 기존 Gameplay csproj의 pack target을 참고해 build-only generator output을 pack한다. NuGet 소비 프로젝트는 ProjectReference 없이 로컬 패키지 `0.1.0`만 참조하고 `LangVersion=9.0`으로 생성된 `Level`을 실행한다. 잘못된 private 필드 직접 쓰기를 넣은 별도 소비 빌드는 B3STATE0004로 실패해야 한다. `.deps.json`/nupkg metadata에 Roslyn 런타임 의존성이 없는지 확인한다. 임시 NuGet 캐시를 써 같은 버전의 낡은 로컬 패키지가 검증에 섞이지 않게 한다.
- [ ] **Step 5: 생성기/런타임 성능 문제를 고치고 필요한 테스트만 재실행한다.** 예상 0 allocation이 깨지면 측정 결과와 원인을 남기고 할당 경로를 고친다. 수치를 완화하거나 측정 구간에서 실제 변경을 빼서 통과시키지 않는다. 측정은 net10.0/실행 구성 기준이며 Unity 수치를 추정하지 않는다.
- [ ] **Step 6: 사용 설명과 제한을 작성한다.** 스키마, 실제 생성 setter, optional transaction, 제거한 노드의 후속 수정 예, 역순 복원, 기존 opaque 상태와의 차이, 명시적 매핑 비용, unknown 데이터 보관, 남은 inventory 사전 복사, 저장 ACK 계약, 실행한 성능 수치를 포함한다. `docs/outgame-rollback.md`는 두 상태 모드가 모두 현재 계약임을 설명하도록 갱신한다.
- [ ] **Step 7: 최종 검증을 실행하고 기록한다.** 아래 명령을 순서대로 실행한다. 이미 같은 코드에서 통과한 테스트는 새 실패/변경 없이 반복하지 않는다.

```powershell
dotnet test common/tests/Bun3.State.Tests/Bun3.State.Tests.csproj -c Release
dotnet test common/tests/Bun3.State.Generator.Tests/Bun3.State.Generator.Tests.csproj -c Release
dotnet test common/tests/Bun3.State.Package.Tests/Bun3.State.Package.Tests.csproj -c Release
dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj
dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release
dotnet build Bun3.sln -c Release -warnaserror
git diff --check
```

- [ ] **Step 8: 전체 변경을 검토하고 결과를 보고한다.** 실행 시작 전 상태와 이번 변경을 구별한 리뷰 자료를 만든다. 새 기능과 기존 알림/opaque 상태 회귀, 리뷰에서 보류한 항목을 확인한다. 제품 기능·테스트 결과·측정 한계·남은 사전 복사를 설명하고, 커밋/배포나 세 게임 이전을 완료했다고 말하지 않는다.

## 스펙 대응표와 인계

| 스펙 | 구현 작업 |
|---|---|
| 1–2 범위·패키지·선택적 사용 | Tasks 1, 4, 6 |
| 3 스키마·생성·우회 진단 | Task 3 |
| 4 소유권·중첩·목록·분리 수명 | Tasks 2, 4 |
| 5 타입별 기록·역순 복원·할당 | Tasks 1, 2, 6 |
| 6 인벤토리/Transaction 연결·opaque 호환 | Task 4 |
| 7 저장 형식·presence·unknown·저장 ACK | Task 5 |
| 8 검증과 9 완료 조건 | 모든 작업의 GREEN + Task 6 |

실행자는 작업마다 RED/GREEN 명령과 결과, 변경 파일, interface 변경 필요 여부를 보고한다. 잘못된 계획이나 스펙 모순을 발견하면 근거와 수정 범위를 기록한 뒤 고친다. 스펙의 공개 동작/범위를 바꾸는 것은 단순한 내부 구현 선택으로 처리하지 않는다.

이 계획은 작성 후 스펙 대응·타입/메서드명·검증 범위를 자체 검토한다. 사용자 검토 후 저장소 지침에 따른 SDD로 실행한다. 이번 인계는 제품 구현 완료 보고가 아니다.
