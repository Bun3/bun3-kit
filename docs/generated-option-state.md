# 생성 옵션 상태 사용과 검증

`Bun3.State` 0.1.0은 netstandard2.1 런타임과 netstandard2.0 생성기를 하나의 NuGet 패키지로 제공한다. C# 9 소비 프로젝트는 패키지만 참조하면 생성된 프로퍼티를 쓸 수 있다. `Bun3.Server.Items` 0.5.0은 생성 상태를 아이템 Dirty 및 기존 `Transaction`에 연결한다. 이 문서의 수치는 .NET 10 Release 테스트 실행에서 측정했으며 Unity/IL2CPP 수치가 아니다.

## 스키마와 변경

```csharp
using Bun3.State;

namespace Game
{
    [TrackedState]
    public sealed partial class EquipmentState
    {
        [Tracked] private int _level = 1;
        [Tracked] private EquipmentOptionState _option = new EquipmentOptionState();
    }

    [TrackedState]
    public sealed partial class EquipmentOptionState
    {
        [Tracked] private readonly TrackedList<RerollOptionState> _rerollOptions =
            new TrackedList<RerollOptionState>();
    }

    [TrackedState]
    public sealed partial class RerollOptionState
    {
        [Tracked] private int _level;
    }
}
```

```csharp
item.State.Option.RerollOptions[0].Level = 5;
```

생성 setter는 동일 값이면 반환하고, 실제 변경 전 소유자에게 Undo 기록을 요청한 다음 필드를 바꾼다. 연결된 아이템은 변경 후 Dirty가 된다. private 필드는 초기값 선언에만 직접 쓰며 이후 대입·증감·`ref/out` 노출은 진단 오류다. 첫 버전의 지원 범위와 진단은 [설계](superpowers/specs/2026-09-29-generated-option-state-design.md)를 따른다. 읽지 않은 readonly 목록도 루트 연결 때 검증된다.

위 `_level`에 대해 생성되는 setter와 복원 분기는 다음 형태다. `SetValue`가 동일 값 검사, 변경 전 기록, 적용, 소유 아이템 알림을 담당한다.

```csharp
set { var tracking = ((global::Bun3.State.ITrackedNode)this).Tracking; tracking.InitializeChildren(); tracking.SetValue(0, ref this.@_level, value); }
// The generated IStateValueTarget<int>.RestoreValue switch restores directly:
case 0: this.@_level = value!; break;
```

트랜잭션은 선택 사항이다. 연결되지 않은 상태와 비활성 트랜잭션의 변경은 그대로 실행되고 Undo 항목을 만들지 않는다. 활성 트랜잭션에서는 실제 변경마다 이전 값을 타입별 버퍼에 순서대로 기록한다. 예를 들어 `1 → 5 → 1`도 두 항목을 남기며 rollback은 `5`, `1` 순서로 복원한다. 목록 추가·삭제·교체와 자식 변경도 같은 전체 역순을 따른다. `Clear`는 제거한 원소마다 기록한다. 복원은 공개 setter나 게임 콜백을 호출하지 않으며 원래 객체와 목록 내용을 제자리에서 되돌린다.

활성 트랜잭션에서 자식을 제거한 뒤 외부 참조로 수정해도 그 수정은 원래 journal에 기록된다. 제거된 자식만 수정할 때는 현재 아이템에 연결되어 있지 않으므로 그 수정만으로 Dirty가 되지 않는다. 원래 루트 안으로 다시 넣을 수 있으며 다른 루트로의 연결은 트랜잭션 종료까지 거부된다. commit은 최종 분리 노드의 소속과 Undo 참조를 해제하고 rollback은 원래 위치와 값 및 Dirty 상태를 복원한다. 사용자 제공 복원 대상이 예외를 던지면 남은 역순 복원을 시도하고 참조를 해제한 뒤 오류를 전파한다. 실패한 cycle의 완료를 다시 호출해 재생하지 않으며 새 기록으로 다음 cycle을 시작해야 한다.

`ItemInstance<TState>` 하나의 수명 동안 생성 추적과 `BeginStateChange()`를 혼합할 수 없다. 생성 상태가 한 번 연결되면 현재 루트가 null 또는 opaque로 바뀌어도 `BeginStateChange()`는 거부된다. 반대로 기존 scope를 한 번 사용한 인스턴스는 생성 상태로 전환할 수 없다. 이 경우 새 인스턴스를 로드하거나 명시적 이전 절차가 필요하다. 기존 opaque 상태는 계속 `snapshotState`와 `BeginStateChange()`로 가변 편집을 복사해 보관한다.

## 저장 형식과 ACK

게임 매핑은 기존 JSON/protobuf DTO를 생성 상태로 읽고, 저장 시 다시 DTO로 만든다. 이 명시적 매핑과 DTO 직렬화 비용은 남는다. 필드 번호, 목록 순서, 미설정과 명시적 0, null과 빈 메시지의 표현 범위는 각 wire 형식에 맞춰 보존해야 한다. 알 수 없는 필드가 필요하면 로드한 DTO의 복제본을 상태 노드별 메타데이터로 따로 보관하고 저장할 때 알려진 필드만 덮어쓴다. JSON의 unknown 속성은 별도 병합 매핑이 필요하다. 이 메타데이터는 Undo 스냅샷이 아니며 제거된 노드를 영구 보관하지 않도록 수명을 관리해야 한다. 대표 payload 검증은 [wire fixture 기록](research/2026-09-30-generated-option-wire-fixtures.md)에 있다.

메모리 transaction commit은 DB ACK가 아니다. `DrainChanges`로 받은 저장 버퍼를 성공까지 보유하고, 실패하면 같은 저장분을 재시도한다. 저장 중 새 변경이 생기면 성공한 이전 저장분만 해제하고 플레이어를 다시 `MarkDirty`하여 다음 저장을 예약해야 한다. DTO/payload와 저장 버전은 세션 실행 안에서 확정하고 외부 I/O에 전달한다. 별도 옵션 ACK API는 아직 없다. Updated 아이템은 옵션을 다시 직렬화한다.

## 비용과 검증 범위

Release/net10.0에서 준비된 10,000회 변경 구간을 `GC.GetAllocatedBytesForCurrentThread()`로 측정했다. scalar, 이미 만든 자식 교체, 용량을 준비한 목록 add/remove 각각에 대해 미연결·비활성·활성 commit·활성 rollback 경로 모두 추가 할당 **0 B**였다. 측정 구간은 실제 변경만 포함하고 assertion, 새 노드 생성, DTO 변환, transaction 완료는 밖에 두었다. 한 실행에서 10개 옵션의 최초 그래프 연결은 4,104 B, 10,000개는 2,449,456 B였고, 첫 scalar Undo 버퍼 성장은 424 B였다. 두 그래프에서 준비된 scalar 하나의 Undo 기록은 각각 0 B였으므로 옵션 수에 비례하는 깊은 복사 경로는 관측되지 않았다. 초기화 수치는 실행 상태에 따라 조금 달라질 수 있으며 반복 기록의 0 B 주장에 포함하지 않는다.

생성 상태의 Undo는 바뀐 항목 수에 비례하지만, 기존 인벤토리는 첫 참여 때 인스턴스 멤버십 사전과 재생 기준 사전을 **전체 얕은 복사**한다. 전체 transaction 비용이 변경량에만 비례한다는 뜻은 아니다. 실제 DB, 외부 게임, Unity/IL2CPP 실행은 이번 검증 범위 밖이다. 패키지 소비 검증은 격리된 임시 NuGet 캐시에서 C# 9 생성·실행과 `B3STATE0004` 진단 및 Roslyn 런타임 의존성 부재를 확인한다.
