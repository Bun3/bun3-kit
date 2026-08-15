# GameplayTag 리뷰 후속 보완 설계

- 상태: 승인됨
- 작성일: 2026-08-13
- 적용 패키지: `Bun3.Gameplay`, Unity 전용 에디터 어댑터
- 기반 명세: [`2026-08-12-gameplay-tag-catalog-design.md`](2026-08-12-gameplay-tag-catalog-design.md)

## 1. 목적과 범위

GameplayTag 구현 리뷰에서 확인된 네 가지 문제를 보완한다.

1. Unity 에디터에서 저장하지 않은 편집 내용이 세션 교체, 창 닫기 또는 domain reload 때
   경고 없이 유실되는 문제
2. 태그 조회 allocation smoke test가 assertion과 JIT의 영향으로 24바이트 오탐을 내는 문제
3. `TagCatalog.Create`가 최종 카탈로그 하나를 만들면서 fingerprint를 두 번 계산하는 문제
4. 외부 저장·네트워크 어댑터가 컨테이너의 exact 상태를 무할당으로 추출할 interface가 없는 문제

기존 태그 의미, JSON schema, 결정적 index 및 fingerprint 값, 조회와 mutation 의미는 바꾸지 않는다.

## 2. Unity 에디터 dirty lifecycle

### 2.1 책임

`GameplayTagCatalogWindow`가 사용자 상호작용과 Unity lifecycle 정책을 소유한다.
`GameplayTagCatalogWindowController`는 세션과 파일 작업을 계속 담당하지만 Unity 팝업 타입이나
메시지를 알지 않는다. dirty 확인은 Window의 단일 helper를 통해 수행하여 세션을 교체하거나
버리는 모든 경로에 같은 정책을 적용한다.

### 2.2 사용자 동작

- `New`, `Open`, `Reload`가 dirty 세션을 교체하려 하면 `Save`, `Discard`, `Cancel`을 제공한다.
- `Save`는 현재 세션을 기존 파일에 저장한 뒤 요청한 작업을 계속한다.
- `Discard`는 저장하지 않고 요청한 작업을 계속한다.
- `Cancel`은 현재 세션과 선택 상태를 그대로 유지하고 요청한 작업을 중단한다.
- 파일 선택 창에서 취소한 경우에는 dirty 팝업을 띄우지 않는다.

창 닫기와 Unity 종료에는 Unity 2022.3의 unsaved-change lifecycle을 사용한다. Window의
`hasUnsavedChanges`를 컨트롤러의 dirty 상태와 동기화하고 `SaveChanges`와 `DiscardChanges`에서
각각 현재 파일 저장과 명시적 폐기를 수행한다. Unity가 제공하는 닫기 팝업의 취소 동작을
그대로 사용한다.

### 2.3 Domain reload

Unity의 assembly reload는 사용자가 취소할 수 없으므로 `AssemblyReloadEvents.beforeAssemblyReload`
시점에 dirty 세션이 있으면 `Save`와 `Discard`만 제공한다. `Save`는 reload가 시작되기 전에
동기적으로 현재 JSON 파일을 저장한다. `Discard`는 편집 내용을 버리고 reload를 계속한다.

임시 초안이나 자동 복구 파일은 만들지 않는다. 저장하지 않고 `Discard`를 선택한 내용은 의도적으로
복구할 수 없다. 저장을 선택한 내용은 원본 JSON에 남지만, reload 후 Window가 해당 파일을 자동으로
다시 열어야 한다는 요구는 이번 범위에 포함하지 않는다.

### 2.4 오류 처리

팝업에서 선택한 저장이 실패하면 validation 창으로 오류를 표시하고, 취소 가능한 `New`, `Open`,
`Reload` 작업은 진행하지 않는다. 취소할 수 없는 assembly reload에서는 오류를 표시한 뒤 reload를
계속하며 원본 dirty 상태를 저장된 것으로 표시하지 않는다.

## 3. Allocation smoke test 격리

태그 조회 루프와 `GC.GetAllocatedBytesForCurrentThread` 호출을 `NoInlining` test helper 안에 둔다.
helper는 조회 적중 횟수를 `out` 값으로 반환하고 측정된 allocation byte 수를 반환한다. assertion은
helper 호출이 끝난 뒤 allocation 0을 먼저 확인하고 적중 횟수를 확인한다.

이 구조는 NUnit assertion의 boxing이나 JIT 코드 이동이 측정 구간에 섞이지 않게 한다. production
코드는 바꾸지 않으며, 기존 400,000회 조회와 0바이트 계약은 유지한다.

## 4. Fingerprint 단일 계산

카탈로그 구축 implementation을 두 단계로 분리한다.

1. `Build`는 canonical lookup, display name, parent 및 subtree-end 배열을 포함한 내부 build data를
   반환한다.
2. `Create`가 redirect를 최종 index로 변환하고 canonical name 배열과 fingerprint를 한 번 만든 뒤
   최종 `TagCatalog`를 생성한다.

중간 `TagCatalog` 인스턴스는 만들지 않는다. 최종 생성자는 이미 계산된 fingerprint를 받으므로
redirect 없는 임시 fingerprint를 계산할 경로가 없다. 공개 interface와 fingerprint byte 값은
변하지 않는다.

## 5. 컨테이너 exact 상태 추출

### 5.1 `TagContainer`

다음 공개 interface를 추가한다.

```csharp
public int CopyExactTags(Span<GameplayTag> destination)
```

- catalog index 오름차순으로 exact 태그를 복사한다.
- 반환값은 복사한 태그 수이며 항상 `ExactKindCount`와 같다.
- destination 길이가 `ExactKindCount`보다 작으면 쓰기를 시작하기 전에 `ArgumentException`을 던진다.
- 빈 컨테이너와 빈 destination 조합은 0을 반환한다.
- heap allocation을 수행하지 않는다.

### 5.2 `TagCountContainer`

exact count 하나를 표현하는 다음 공개 readonly value type을 추가한다.

```csharp
public readonly struct TagCountEntry
{
    public GameplayTag Tag { get; }
    public int Count { get; }
}
```

`TagCountEntry`의 생성자는 internal이며 외부 호출자는 `CopyExactEntries`를 통해서만 유효한 entry를
받는다. 반환된 entry는 값 동등성, hash code, `==`와 `!=`를 제공한다. 반환된 `Count`는 항상 양수인
exact count이며 aggregate-only 조상 entry는 외부에 노출하지 않는다. 모든 value type과 마찬가지로
`default(TagCountEntry)`는 `GameplayTag.None`과 count 0을 가지며 유효한 복사 결과로 반환되지 않는다.

다음 공개 interface를 추가한다.

```csharp
public int CopyExactEntries(Span<TagCountEntry> destination)
```

- catalog index 오름차순으로 exact count가 0보다 큰 entry만 복사한다.
- 반환값은 복사한 entry 수이며 항상 `ExactKindCount`와 같다.
- destination 길이가 `ExactKindCount`보다 작으면 쓰기를 시작하기 전에 `ArgumentException`을 던진다.
- 빈 컨테이너와 빈 destination 조합은 0을 반환한다.
- heap allocation을 수행하지 않는다.

내부 배열과 mutable view, 일반 `IEnumerable<T>`는 공개하지 않는다. 호출자가 stack 또는 재사용 배열을
선택하게 하여 저장·네트워크 어댑터 seam을 제공하면서 컨테이너 implementation은 감춘다.

## 6. 호환성과 버전

변경은 기존 public interface에 대한 additive change다. 기존 호출자의 동작과 wire index는 변하지
않는다. 공개 interface가 추가되고 패키지 내용이 변경되므로 NuGet과 UPM 버전을 함께 `0.6.0`으로
올린다. Unity 최소 버전은 계속 `2022.3`, 공용 런타임 target은 계속 `netstandard2.1`과 C# 9다.

모든 새 public 멤버는 한국어 XML 문서를 가진다. 조회·복사 hot path는 LINQ, iterator, boxing과
heap allocation을 사용하지 않는다.

## 7. 테스트와 완료 조건

- Window의 dirty 결정 helper가 `Save`, `Discard`, `Cancel` 각각에 맞게 작업을 진행하거나 중단한다.
- 저장 실패 시 세션 교체가 중단되고 dirty 상태가 유지된다.
- Window의 unsaved-change 상태가 controller dirty 상태와 동기화된다.
- domain reload handler가 dirty 세션에서 저장 또는 폐기를 수행하고 clean 세션에서는 아무 작업도
  하지 않는다.
- allocation smoke test가 Release에서 0바이트로 통과하며 assertion 순서에 의존하지 않는다.
- 기존 fixture에서 최종 index와 fingerprint가 변경되지 않는다.
- 대형 카탈로그 생성 allocation 계약이 canonical 직렬화와 hash의 단일 실행 예산 안에서 통과하고,
  기존 이중 fingerprint 구현에서는 실패한다.
- 두 복사 interface가 빈 상태, 정렬 순서, 정확한 값, destination 부족 시 무변경 예외를 검증한다.
- 두 복사 interface의 반복 호출이 Release에서 0바이트를 유지한다.
- 전체 .NET 테스트가 경고와 실패 없이 통과한다.
- 다른 Unity 인스턴스가 프로젝트를 점유하지 않는 환경에서 Unity EditMode 테스트가 통과한다.

## 8. 비목표

- dirty 편집본 임시 저장 또는 domain reload 후 자동 복구
- reload 후 마지막 카탈로그 자동 재열기
- 컨테이너 내부 배열, mutable span 또는 iterator 공개
- aggregate-only `TagCountContainer` entry의 외부 노출
- JSON schema, tag index 규칙 또는 fingerprint 형식 변경
