# uGUI 가변 높이 가상화 리스트 설계 (`Bun3.Unity.UI.Lists.VirtualList`)

- 날짜: 2026-10-12
- 상태: 사용자 리뷰 대기
- 범위: `unity/Packages/com.bun3.unity.ui`에 세로 가상화 리스트 추가(0.3.1 → 0.4.0)
- 목표 규모: 항목 10,000개(사용자 결정)
- 첫 소비자: jurassic-paradise 채팅 로그. 채팅 패키지 스펙은 `2026-10-12-chat-design.md`, 게임 쪽은 JP 레포 `docs/superpowers/specs/2026-10-12-chat-system-design.md`

## 1. 목적과 경계

항목마다 높이가 다른 세로 목록을 보이는 셀만 만들어 표시한다. 텍스트를 자르지 않고, 셀의 선호 높이를 그대로 칸 높이로 쓴다. 항목이 10,000개여도 처음 표시, 너비 변경, 추가·제거 때 프레임이 멈추지 않아야 한다.

| 포함 | 제외(필요해지면 추가) |
|---|---|
| 세로 방향, 가변 높이, 셀 풀링 | 가로·그리드 |
| 끝에 추가, 앞에서 제거 | 중간 삽입·삭제, 항목별 갱신 |
| 끝 따라가기, 보던 위치 유지(제거·재측정·드래그 중 포함) | 특정 항목으로 스크롤, 스크롤 애니메이션 |
| 보이는 항목만 측정, 나머지는 추정 높이 | 유휴 프레임 백그라운드 측정 |

## 2. 조사와 선택

| 후보 | 높이를 얻는 방식 | 10,000개 | 채팅 기능 | 판단 |
|---|---|---|---|---|
| UI Toolkit `ListView` DynamicHeight | 보이는 항목만 측정, 나머지는 고정 추정값, 첫 표시 항목 앵커 | 내부에 O(n) 순회가 있고 대량 측정 시 O(n²) | `stickToBottom` | uGUI 화면 사이 렌더 순서, 한글 IME, 폰트 체계 위험으로 제외(사용자 결정). 측정·앵커 방식은 참고 |
| LoopScrollRect(MIT, ★2.7k, 2026-10 갱신) | 보이는 셀을 레이아웃으로 측정, 스크롤바는 평균 추정 | 가능(상대 배치) | 역방향 모드만 있음. 앞쪽 제거 시 위치 유지 없음 | `ScrollRect`를 통째로 대체하고 `UnityEngine.UI` 네임스페이스에 타입을 추가한다. 위치 보존을 덧대야 해서 제외 |
| cyKimScroller(MIT, ★0, 2026-10 갱신) | 콜백(`GetCellViewSize`)으로 전체 크기를 받음 | 크기 계산이 게임 몫이라 TMP 측정 O(n)이 그대로 남음 | 삽입·삭제 시 위치 유지, 드래그 중 기준점 재설정 | 8천 줄, 단일 작성자라 제외. 드래그 기준점 재설정 기법은 차용 |
| UnityDynamicScrollView(MIT, ★401, 2024-11) · UIS(MIT, ★513) · EnhancedScroller(유료) | 콜백으로 전체 크기를 받음 | 위와 같음 | 없음 | 제외 |
| Optimized ScrollView Adapter($60) · SuperScrollView(유료) | 가변 크기 지원 | 지원 | 채팅 사용 사례 있음 | Asset Store EULA라 공개 레포인 bun3-kit에 넣을 수 없어 제외 |

Unity 밖(웹 TanStack Virtual·react-virtuoso·virtua, 모바일 UITableView·RecyclerView·Compose·Flutter super_sliver_list·FlashList v2, 엔진 Unreal SListView·Dear ImGui, 로블록스·Godot 커뮤니티)도 조사했다. 채팅용 구현은 대부분 "보이는 것만 측정 + 추정 + 앵커" 방식이다. 상대 배치 방식(RecyclerView, Unreal)은 전체 높이를 보이는 항목으로 추정해서, Unreal에서는 가변 높이 목록의 맨 아래까지 스크롤되지 않는 버그(UE-151120)가 보고됐다. 그래서 끝 위치가 중요한 채팅에는 맞지 않는다. 다음 세 가지를 차용한다.

- 끝 판정 여유: TanStack `scrollEndThreshold`, FlashList `autoscrollToBottomThreshold`.
- 항목별 추정값: UITableView `estimatedHeightForRowAt`, super_sliver_list `estimateExtent(index)`, TanStack `estimateSize(index)`.
- 내 메시지는 항상 끝으로: react-virtuoso message-list. 이건 게임 쪽 규칙이라 JP 스펙에 둔다.

공통 결론: 10,000개에서 비싼 것은 높이 합 계산이 아니라 **측정**이다. 한 번 측정할 때마다 셀 바인딩, 레이아웃 재계산, TMP 줄바꿈이 일어난다. 그래서 다음 세 가지를 직접 구현한다.

- UI Toolkit처럼 보이는 항목만 측정하고 나머지는 추정 높이로 둔다.
- 높이 합은 펜윅 트리로 O(log n)에 계산한다.
- 위치 보정은 하나의 앵커 규칙으로 처리하고, 드래그 중 보정은 cyKimScroller 방식으로 `ScrollRect` 기준점을 다시 잡는다.

## 3. 위치

```
unity/Packages/com.bun3.unity.ui/
  package.json                     // 0.4.0, 의존성 변경 없음
  CHANGELOG.md                     // [0.4.0] Added
  README.md, Documentation/Unity UI.md  // VirtualList 절 추가
  Runtime/AssemblyInfo.cs          // InternalsVisibleTo("Bun3.Unity.UI.Editor.Tests")
  Runtime/Lists/
    IVirtualListSource.cs
    VirtualList.cs                 // MonoBehaviour: 측정, 풀링, 앵커, 드래그 추적
    VirtualListLayout.cs           // internal, 엔진 무관: 높이 저장 + 펜윅 트리
  Tests/Editor/VirtualListLayoutTests.cs
  Tests/Runtime/VirtualListTests.cs
```

## 4. API

```csharp
namespace Bun3.Unity.UI.Lists
{
    public interface IVirtualListSource
    {
        int Count { get; }
        RectTransform CreateCell(RectTransform parent);  // 풀이 모자랄 때만 호출
        void BindCell(RectTransform cell, int index);     // 셀 내용을 index 항목으로 채움
    }

    // 선택 구현. 소스가 함께 구현하면 미측정 항목 높이를 항목별로 추정한다.
    public interface IVirtualListEstimator
    {
        float EstimateHeight(int index, float cellWidth);  // 산술만. 레이아웃·TMP 호출 금지
    }

    [RequireComponent(typeof(ScrollRect))]
    [DisallowMultipleComponent]
    public sealed class VirtualList : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectOffset Padding { get; set; }           // [SerializeField], 변경 시 Refresh
        public float Spacing { get; set; }                // [SerializeField], 변경 시 Refresh
        public float EstimatedItemHeight { get; set; }    // [SerializeField], > 0, 추정기가 없을 때 미측정 항목 높이
        public float EndThreshold { get; set; }           // [SerializeField], >= 0, 기본 1px, 끝 판정 여유
        public bool IsAtEnd { get; }

        public void SetSource(IVirtualListSource? source);  // 전부 미측정으로 시작, 맨 위로
        public void Refresh();                              // 전부 미측정으로 되돌림, 위치 보존
        public void NotifyAppended(int count);
        public void NotifyRemovedFromStart(int count);
        public void ScrollToEnd();
    }

    internal sealed class VirtualListLayout
    {
        public VirtualListLayout(float spacing);
        public float Spacing { get; set; }           // 변경 시 재구성 O(n)
        public int Count { get; }
        public float ContentHeight { get; }          // 높이 합 + 간격, 패딩 제외
        public void Clear();
        public void Append(float estimatedHeight);   // 미측정 항목 추가
        public void RemoveFromStart(int count);
        public void SetMeasured(int index, float height);   // O(log n)
        public void SetEstimated(int index, float height);  // 미측정으로 되돌림, 트리는 Rebuild 때 반영
        public void Rebuild();                       // O(n)
        public bool IsMeasured(int index);
        public float GetTop(int index);              // 콘텐츠 시작 기준 위쪽 오프셋
        public float GetHeight(int index);
        public int IndexAt(float offset);            // offset이 걸친 항목, 간격은 앞 항목에 속함
    }
}
```

## 5. 동작

### 5.1 레이아웃 계약

- `ScrollRect`는 세로 스크롤로 설정한다. 뷰포트는 `ScrollRect.viewport`, 없으면 `ScrollRect` 자신의 `RectTransform`이다.
- 리스트가 `content`의 앵커를 위쪽 가로 늘이기, 피벗을 (0.5, 1)로 강제하고 높이를 `Padding.vertical + ContentHeight`로 설정한다. `content`에 LayoutGroup이나 ContentSizeFitter를 두지 않는다.
- 셀도 같은 앵커와 피벗으로 강제한다. 좌우는 `Padding`만큼 띄우고, y 위치와 높이는 리스트가 설정한다.
- 셀 루트는 선호 높이를 내야 한다. 예: `childControlHeight`가 켜진 `VerticalLayoutGroup`이나 `LayoutElement`. 셀에 ContentSizeFitter를 두지 않는다.

### 5.2 높이 저장(`VirtualListLayout`)

- 항목 높이 배열과 측정 여부를 둔다. 미측정 항목은 추가할 때 받은 추정 높이를 쓴다.
- 펜윅 트리에 `높이 + 간격`을 저장한다.
  - `GetTop`은 접두합 O(log n)이다.
  - `IndexAt`은 트리 하강 O(log n)이다.
  - `SetMeasured`는 차이만큼 갱신 O(log n)이다.
  - 트리 값은 `double`로 둔다. 갱신이 반복될 때 누적 오차가 쌓이지 않게 하기 위해서다.
- 앞쪽 제거는 시작 위치(`head`)만 옮긴다. 오프셋은 `prefix(head + i) - prefix(head)`다. 비어 있는 앞부분이 전체의 절반을 넘으면 배열과 트리를 앞으로 당겨 다시 만든다(분할 상환 O(1)).
- 끝 추가는 배열 용량 안이면 노드당 O(log n)이다. 용량이 차면 두 배로 늘리고 다시 만든다.
- `Rebuild`와 `Spacing` 변경은 O(n) 재구성이다. 10,000개면 수십 µs다.

### 5.3 측정

- 측정 시점은 항목이 보이는 범위에 처음 들어와 셀에 바인딩될 때뿐이다. 측정에는 그 셀을 그대로 쓴다.
- 측정 순서:
  1. 셀 너비를 `뷰포트 너비 - Padding.horizontal`로 맞춘다.
  2. `BindCell`을 호출한다.
  3. `LayoutRebuilder.ForceRebuildLayoutImmediate`로 레이아웃을 다시 계산한다.
  4. `LayoutUtility.GetPreferredHeight`로 높이를 읽어 `SetMeasured`한다.
- 이미 측정한 항목은 다시 보일 때 바인딩만 하고 높이를 다시 읽지 않는다.
- `SetSource`, `Refresh`, 너비 변경은 전부 미측정으로 되돌린다. 그다음 보이는 항목만 측정한다. 그래서 측정 비용은 항목 수와 무관하게 보이는 셀 수에 비례한다.
- 추정 높이:
  - 소스가 `IVirtualListEstimator`를 구현했으면 `EstimateHeight(index, 셀 너비)`를 쓰고, 아니면 `EstimatedItemHeight`를 쓴다.
  - 전부 미측정으로 되돌릴 때는 모든 항목에 대해 추정기를 부른다(O(n)). `NotifyAppended` 때는 새 항목만 부른다.
  - 그래서 추정기는 글자 수 같은 데이터로 계산하는 산술이어야 한다. 레이아웃이나 TMP를 호출하면 O(n) 측정으로 되돌아간다.
  - 추정을 실제보다 크게 잡는 편이 화면 튐이 적다(TanStack 권고).
- 측정으로 높이가 바뀌면 보이는 범위가 달라질 수 있다. 그러면 새로 들어온 항목을 측정하는 과정을 범위가 변하지 않을 때까지 반복한다. 항목마다 한 번만 측정하므로 반복은 보이는 셀 수 안에서 끝난다.

### 5.4 가시 범위와 풀링

- `ScrollRect.onValueChanged`마다 `[scrollTop, scrollTop + 뷰포트 높이]`의 첫·끝 항목을 `IndexAt`으로 구한다. 범위를 벗어난 셀은 비활성화해 풀로 돌리고, 새로 들어온 항목만 풀에서 꺼내 바인딩한다.
- 항목 구성이 바뀌면(추가·제거·초기화) 보이는 셀을 모두 다시 바인딩한다. 데이터 인덱스가 밀리기 때문이다.
- 셀은 `content`의 자식으로 함께 수명을 다한다. 단, `SetSource`로 다른 소스 객체를 받으면 이전 소스가 만든 셀은 모양이 다를 수 있으므로 파괴한다.

### 5.5 앵커: 보던 위치 유지

높이나 항목 구성이 바뀌는 모든 작업(측정, 추가, 제거, 재측정, 너비 변경)은 같은 순서로 처리한다.

1. 작업 전에 앵커를 기록한다.
   - 끝에 있으면(`IsAtEnd`) **끝 앵커**다.
   - 아니면 **항목 앵커**다. 뷰포트에 걸친 항목 중 가장 위의 **측정된** 항목(없으면 맨 위에 걸친 항목)과, 뷰포트 위쪽에서 그 항목 위쪽까지의 거리를 기록한다. 측정된 항목을 고르는 이유가 있다. 위로 스크롤해 새로 드러난 항목은 측정되며 높이가 바뀐다. 그 항목을 앵커로 잡으면 이미 보이던 아래 항목이 그 차이만큼 밀린다.
2. 작업을 적용한다.
3. 앵커로 `scrollTop`을 복원한다.
   - 끝 앵커면 최대 스크롤 위치로 간다.
   - 항목 앵커면 `GetTop(앵커 항목) + 거리`로 간다. 앞쪽 제거로 앵커 항목이 지워졌으면 0번 항목, 거리 0으로 간다.
   - 결과는 `[0, 최대 스크롤]` 범위로 자른다.

이 규칙으로 다음 경우가 한 번에 처리된다.
- 위로 스크롤하다 위쪽 항목이 측정돼 높이가 추정과 달라져도 화면이 튀지 않는다.
- 끝을 보고 있으면 새 메시지를 따라 내려간다.
- 위로 올려 읽는 중에 새 메시지가 오거나 앞쪽이 밀려나도 보던 메시지가 그대로 있다.

사용자 스크롤(`onValueChanged`)로 시작된 보정은 측정으로 높이가 바뀐 경우에만 위치를 복원한다. 측정이 없는 프레임에도 범위로 잘라 복원하면, 가장자리 너머로 당기는 고무줄 효과가 매 프레임 끊긴다.

`IsAtEnd`는 `scrollTop >= max(0, 콘텐츠 높이 - 뷰포트 높이) - EndThreshold`다. 맨 아래에서 `EndThreshold` 이내로 올라가 있으면 끝으로 본다. 콘텐츠가 뷰포트보다 짧으면 항상 참이다. `ScrollToEnd`는 `StopMovement` 후 끝 앵커로 복원한다.

### 5.6 드래그 중 위치 보정

- `ScrollRect`는 드래그 시작 시점의 콘텐츠 위치에 손가락 이동량을 더해 위치를 계산한다. 그래서 드래그 중에 리스트가 위치를 옮기면 다음 드래그 이벤트에서 화면이 튄다.
- 리스트는 같은 GameObject에서 드래그 이벤트를 함께 받아 마지막 `PointerEventData`를 보관한다.
- 앵커 복원으로 위치를 옮긴 뒤에는 두 가지를 한다.
  - 드래그 중이면 `scrollRect.OnBeginDrag(보관한 이벤트)`로 기준점을 지금 위치로 다시 잡는다.
  - 항상 `scrollRect.Rebuild(CanvasUpdate.PostLayout)`로 이전 위치를 맞춘다. 순간이동이 손을 뗀 뒤의 관성 속도에 섞이지 않게 하기 위해서다.
- 복원 결과를 범위로 자르므로, 가장자리 너머로 당기는 중에 보정이 일어나면 고무줄 늘어남이 한 번 풀린다. 위치가 튀지는 않는다.

### 5.7 너비 변경과 비활성 상태

- `LateUpdate`에서 셀 너비를 이전 값과 비교한다. 달라졌으면 `Refresh`한다. 해상도·CanvasScaler 변화도 같은 경로로 처리한다.
- `isActiveAndEnabled`가 아닐 때 받은 `SetSource`/`Refresh`/`Notify*`/`ScrollToEnd`는 처리하지 않고 표시만 해 둔다.
  - 활성화된 뒤 첫 `LateUpdate`에서 `Refresh`하고, 끝 이동 요청이 있었으면 끝으로 간다. 비활성 레이아웃 컴포넌트는 측정에 참여하지 않기 때문이다.
  - `Refresh`가 예약된 동안 들어온 `Notify*`는 개수 검사 없이 흡수한다. 예약된 `Refresh`가 `source.Count`로 다시 시작하기 때문이다.
- 활성 상태에서 개수가 맞지 않으면(`source.Count != layout.Count + count`, 또는 제거 수가 현재 수보다 많음) `InvalidOperationException`을 던진다.

### 5.8 알려진 한계

- 스크롤바 크기와 위치는 추정 높이가 섞여 있어 근사값이다. 스크롤하면서 항목이 측정되면 조금씩 바뀐다(UI Toolkit, Qt, Android RecyclerView도 같다). 추정기를 구현하거나 `EstimatedItemHeight`를 실제 평균에 가깝게 둘수록 덜 흔들린다. 정확도가 문제 되면 유휴 프레임에 미측정 항목을 조금씩 측정하는 단계를 추가한다.

## 6. README 사용 예

```csharp
public sealed class ChatLogView : MonoBehaviour, IVirtualListSource
{
    [SerializeField] VirtualList _list;
    [SerializeField] ChatLine _linePrefab;
    readonly List<string> _lines = new();

    public int Count => _lines.Count;
    public RectTransform CreateCell(RectTransform parent) => (RectTransform)Instantiate(_linePrefab, parent).transform;
    public void BindCell(RectTransform cell, int index) => cell.GetComponent<ChatLine>().Bind(_lines[index]);

    void Start() => _list.SetSource(this);

    public void Append(string line)
    {
        _lines.Add(line);
        _list.NotifyAppended(1);  // 끝을 보고 있었으면 따라 내려간다
    }
}
```

## 7. 테스트

- **EditMode `VirtualListLayoutTests`**:
  - 미측정 항목은 추가할 때 받은 추정 높이다. `SetMeasured` 후 뒤 항목들의 `GetTop`이 차이만큼 바뀐다. `SetEstimated` + `Rebuild` 후 미측정으로 돌아간다.
  - `IndexAt` 경계: 항목 경계에 정확히 걸침, 간격 안(앞 항목), 끝을 넘음, 빈 목록.
  - `RemoveFromStart`: 여러 번 제거, 앞당김 재구성 경계 전후, 제거 뒤 `Append`.
  - `Spacing` 변경 후 값이 맞다.
  - 무작위 대조: 고정 시드로 추가·제거·측정·조회 10,000회를 단순 배열 구현과 비교해 오차 0.01px 이내.
  - 성능 스모크: 10,000개에서 (앞 1개 제거 + 끝 1개 추가 + 측정 1회 + `IndexAt` 1회) × 10,000회가 50ms 미만. 넉넉한 상한으로 O(n) 퇴행만 잡는다.
- **PlayMode `VirtualListTests`**: Canvas, `ScrollRect`, `RectMask2D` 뷰포트를 코드로 만든다. 가짜 소스의 셀은 인덱스별 `LayoutElement.preferredHeight`를 가지고, 추정 높이와 일부러 다르게 둔다. 너비 의존 케이스에서는 높이를 너비의 함수로 둔다. `CreateCell`/`BindCell` 호출 수를 센다.
  1. `SetSource`(10,000개): `BindCell` 호출 수와 만든 셀 수가 보이는 항목 수 + 1 이하다. 전체 측정이 없다.
  2. 보이는 셀의 위치와 높이가 각 항목의 선호 높이와 일치한다.
  3. 끝에서 시작해 맨 위까지 단계적으로 스크롤: 매 단계에서 뷰포트 맨 위 항목의 화면 위치가 보정 전후로 같다. `CreateCell` 수가 늘지 않는다.
  4. 끝에서 `NotifyAppended`: 끝을 유지한다. 위로 스크롤한 상태에서는 보던 항목이 그대로다.
  5. 채팅 시뮬레이션: 10,000개, 끝에서 (앞 1개 제거 + 끝 1개 추가) × 1,000회 → 끝 유지, 셀 수 불변. 중간에서 같은 작업 → 보던 항목 유지.
  6. 너비 변경: 보이는 항목만 다시 측정하고(`BindCell` ≤ 보이는 항목 수 + 1) 항목 앵커를 유지한다.
  7. 드래그 중 보정: `OnBeginDrag` 후 앞쪽 제거, 같은 포인터 위치로 `OnDrag` → 콘텐츠가 보정된 위치에서 움직이지 않는다.
  8. 비활성 중 알림(추가·제거 여러 번): 예외가 없고, 활성화 후 첫 `LateUpdate`가 지나면 상태가 올바르다.
  9. 활성 상태 개수 불일치: `InvalidOperationException`.
  10. 추정기: 소스가 `IVirtualListEstimator`를 구현하면 미측정 항목의 높이 합이 추정값 합과 같다. `SetSource` 때 추정기는 항목 수만큼, `BindCell`은 보이는 수만큼만 불린다.
  11. `EndThreshold`: 맨 아래에서 상한보다 덜 올라가 있으면 새 항목을 따라가고, 더 올라가 있으면 보던 위치를 유지한다.

## 8. 완료 기준

- bun3-kit Unity 프로젝트에서 경고 없이 컴파일되고, EditMode·PlayMode 테스트가 통과하며, `.meta`가 커밋돼 있다.
- 모든 public 멤버에 영어 XML 문서. README·Documentation에 레이아웃 계약(5.1), 추정기 계약(산술만), 추정 높이와 스크롤바 근사(5.8), 사용 예를 적는다. CHANGELOG에 0.4.0 항목을 추가한다.
- JP가 `#Bun3/jp-chat` 브랜치로 참조해 채팅 패널 테스트를 통과한다.
