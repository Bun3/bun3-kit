# uGUI 가변 높이 가상화 리스트 구현 플랜 (`Bun3.Unity.UI.Lists.VirtualList`)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `com.bun3.unity.ui`에 항목 10,000개에서도 멈추지 않는 세로 가변 높이 가상화 리스트를 추가한다(0.3.1 → 0.4.0).

**Architecture:** 엔진 무관 `VirtualListLayout`이 항목 높이를 펜윅 트리(`double`)로 들고 오프셋·역조회를 O(log n)에 낸다. `VirtualList`(MonoBehaviour)는 보이는 항목만 셀에 바인딩해 처음 보일 때 한 번 측정하고, 나머지는 추정 높이로 둔다. 높이나 구성이 바뀌는 모든 작업은 "앵커 기록 → 적용 → 복원" 한 규칙으로 화면 위치를 유지하고, 드래그 중이면 `ScrollRect` 기준점을 다시 잡는다.

**Tech Stack:** Unity 6000.3.14f1, uGUI(`ScrollRect`, `LayoutRebuilder`, `LayoutUtility`), Unity Test Framework(EditMode + PlayMode), C# 9.

**Spec:** `docs/superpowers/specs/2026-10-12-unity-virtual-list-design.md`

## Global Constraints

- 레포 규칙은 `CLAUDE.md`가 원본이다. 코드·README·Documentation·CHANGELOG는 영어만 쓴다. 레거시·타 패키지·조사 출처 언급은 `docs/`에만 둔다.
- 모든 public 멤버에 영어 XML 문서, 컴파일 경고 0. 널 허용 주석을 쓰는 파일은 첫 줄에 `#nullable enable`.
- 네임스페이스 `Bun3.Unity.UI.Lists`, 폴더 `Runtime/Lists/`. `VirtualListLayout`은 `internal`이고 `Runtime/AssemblyInfo.cs`의 `InternalsVisibleTo("Bun3.Unity.UI.Editor.Tests")`로 테스트에 연다.
- 범용 헬퍼는 기존 것을 쓴다: 오브젝트 파괴는 `Bun3.Unity.Core.Utils.Util.SafeDestroy`.
- 목표 규모 10,000개. 측정은 보이는 셀에만 한다. 추정기는 산술만 한다(레이아웃·TMP 호출 금지).
- 기본값: `EstimatedItemHeight` 40(> 0), `EndThreshold` 1px(≥ 0), `Spacing` 0, `Padding` 0.
- Unity 테스트는 에디터가 이 프로젝트를 열고 있지 않을 때 batchmode로 돌린다. 열려 있으면 `unity-cli` 스킬의 Pipeline API로 같은 필터를 실행한다. 결과는 `unity/Logs/`에 남긴다.
- `.meta`는 Unity가 생성한다. 커밋은 사용자가 이 실행에서 커밋을 승인했을 때만 하고, 메시지·트레일러는 `writing-gitmoji-commits` 스킬과 `CLAUDE.md`를 따른다.

## Review Focus

- **추정기가 NaN·0·음수를 돌려줌**: 높이 합이 망가지거나 무한 루프가 나면 안 된다. 기대: 그 항목은 `EstimatedItemHeight`로 대체. → Task 2 테스트 `InvalidEstimatesFallBackToEstimatedItemHeight`.
- **뷰포트 크기가 0**(레이아웃이 아직 안 잡힌 패널): 예외·무한 측정이 없어야 한다. 기대: 셀 최대 1개, 예외 없음. → Task 2 테스트 `ZeroSizedViewportDoesNotThrow`.
- **가장자리 너머로 당기는 고무줄(Elastic)**: 측정이 없는 스크롤 프레임마다 위치를 범위로 잘라 끊으면 안 된다. 기대: 측정 없는 `onValueChanged`는 위치를 건드리지 않음. → Task 3 테스트 `ScrollWithoutMeasurementLeavesElasticOverscrollAlone`.
- **다른 소스로 교체**: 이전 소스가 만든 셀(모양이 다를 수 있음)을 새 소스에 바인딩하면 안 된다. 기대: 이전 셀 파괴. → Task 2 테스트 `SwitchingSourceDestroysPreviousCells`.
- **알림 없이 소스가 바뀐 뒤 `Refresh`**: `source.Count`로 다시 시작해 셀이 올바른 항목을 보여야 한다. → Task 2 테스트 `RefreshRecoversFromSilentSourceChanges`.

---

### Task 1: `VirtualListLayout` (높이 저장 + 펜윅 트리)

**Files:**
- Create: `unity/Packages/com.bun3.unity.ui/Runtime/AssemblyInfo.cs`
- Create: `unity/Packages/com.bun3.unity.ui/Runtime/Lists/VirtualListLayout.cs`
- Test: `unity/Packages/com.bun3.unity.ui/Tests/Editor/VirtualListLayoutTests.cs`

**Interfaces:**
- Produces(internal): `VirtualListLayout(float spacing)`, `float Spacing { get; set; }`(설정 시 O(n) 재구성), `int Count`, `float ContentHeight`(높이 합 + 간격, 패딩 제외), `void Clear()`, `void Append(float estimatedHeight)`, `void RemoveFromStart(int count)`, `void SetMeasured(int index, float height)`, `void SetEstimated(int index, float height)`, `void Rebuild()`, `bool IsMeasured(int index)`, `float GetTop(int index)`(0..Count), `float GetHeight(int index)`, `int IndexAt(float offset)`(빈 목록 -1, 간격은 앞 항목 소속, 경계는 다음 항목).

- [ ] **Step 1: 실패하는 테스트 작성**

`unity/Packages/com.bun3.unity.ui/Runtime/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Bun3.Unity.UI.Editor.Tests")]
```

`unity/Packages/com.bun3.unity.ui/Tests/Editor/VirtualListLayoutTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Bun3.Unity.UI.Lists;
using NUnit.Framework;

namespace Bun3.Unity.UI.Editor.Tests
{
    public sealed class VirtualListLayoutTests
    {
        sealed class NaiveLayout
        {
            public readonly List<float> Heights = new List<float>();
            public readonly List<bool> Measured = new List<bool>();
            public float Spacing;

            public double Top(int index)
            {
                double top = 0;
                for (int i = 0; i < index; i++) top += Heights[i] + Spacing;
                return top;
            }

            public double ContentHeight => Heights.Count == 0 ? 0 : Top(Heights.Count) - Spacing;

            public int IndexAt(float offset)
            {
                if (Heights.Count == 0) return -1;
                double top = 0;
                for (int i = 0; i < Heights.Count; i++)
                {
                    top += Heights[i] + Spacing;
                    if (offset < top) return i;
                }
                return Heights.Count - 1;
            }
        }

        static VirtualListLayout Build(float spacing, params float[] heights)
        {
            var layout = new VirtualListLayout(spacing);
            foreach (var height in heights) layout.Append(height);
            return layout;
        }

        [Test]
        public void UnmeasuredItemsUseTheirEstimates()
        {
            var layout = Build(2, 10, 20, 30);
            Assert.That(layout.Count, Is.EqualTo(3));
            Assert.That(layout.IsMeasured(1), Is.False);
            Assert.That(layout.GetHeight(1), Is.EqualTo(20));
            Assert.That(layout.GetTop(2), Is.EqualTo(34));
            Assert.That(layout.ContentHeight, Is.EqualTo(64));
        }

        [Test]
        public void MeasuringShiftsLaterItemsByTheDifference()
        {
            var layout = Build(2, 10, 20, 30);
            layout.SetMeasured(0, 15);
            Assert.That(layout.IsMeasured(0), Is.True);
            Assert.That(layout.GetTop(1), Is.EqualTo(17));
            Assert.That(layout.GetTop(2), Is.EqualTo(39));
            Assert.That(layout.ContentHeight, Is.EqualTo(69));
        }

        [Test]
        public void SetEstimatedThenRebuildReturnsToUnmeasured()
        {
            var layout = Build(0, 10, 20);
            layout.SetMeasured(0, 50);
            layout.SetEstimated(0, 12);
            layout.Rebuild();
            Assert.That(layout.IsMeasured(0), Is.False);
            Assert.That(layout.GetTop(1), Is.EqualTo(12));
        }

        [Test]
        public void IndexAtBoundaries()
        {
            Assert.That(new VirtualListLayout(0).IndexAt(5), Is.EqualTo(-1));
            var layout = Build(4, 10, 20, 30); // tops 0, 14, 38
            Assert.That(layout.IndexAt(-5), Is.EqualTo(0));
            Assert.That(layout.IndexAt(0), Is.EqualTo(0));
            Assert.That(layout.IndexAt(12), Is.EqualTo(0));
            Assert.That(layout.IndexAt(14), Is.EqualTo(1));
            Assert.That(layout.IndexAt(37.9f), Is.EqualTo(1));
            Assert.That(layout.IndexAt(38), Is.EqualTo(2));
            Assert.That(layout.IndexAt(1000), Is.EqualTo(2));
        }

        [Test]
        public void RemoveFromStartAcrossCompaction()
        {
            var layout = new VirtualListLayout(1);
            for (int i = 0; i < 100; i++) layout.Append(i + 1);
            layout.RemoveFromStart(10);
            Assert.That(layout.Count, Is.EqualTo(90));
            Assert.That(layout.GetHeight(0), Is.EqualTo(11));
            Assert.That(layout.GetTop(1), Is.EqualTo(12));
            layout.RemoveFromStart(45); // the dead prefix now outweighs the live items: compacts
            Assert.That(layout.Count, Is.EqualTo(45));
            Assert.That(layout.GetHeight(0), Is.EqualTo(56));
            Assert.That(layout.GetTop(1), Is.EqualTo(57));
            layout.Append(500);
            Assert.That(layout.GetHeight(45), Is.EqualTo(500));
            Assert.That(layout.GetTop(45), Is.EqualTo(3555)); // sum of (56..100) + 45 gaps
            layout.RemoveFromStart(46);
            Assert.That(layout.Count, Is.EqualTo(0));
            Assert.That(layout.ContentHeight, Is.EqualTo(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => layout.RemoveFromStart(1));
        }

        [Test]
        public void SpacingChangeRebuildsOffsets()
        {
            var layout = Build(0, 10, 20, 30);
            layout.SetMeasured(1, 25);
            layout.Spacing = 5;
            Assert.That(layout.GetTop(2), Is.EqualTo(45));
            Assert.That(layout.ContentHeight, Is.EqualTo(75));
            Assert.That(layout.IsMeasured(1), Is.True);
        }

        [Test]
        public void MatchesNaiveLayoutUnderRandomOperations()
        {
            var random = new Random(12345);
            var layout = new VirtualListLayout(3);
            var naive = new NaiveLayout { Spacing = 3 };
            for (int op = 0; op < 10000; op++)
            {
                int kind = random.Next(20);
                if (kind < 8)
                {
                    float height = 1 + (float)random.NextDouble() * 99;
                    layout.Append(height);
                    naive.Heights.Add(height);
                    naive.Measured.Add(false);
                }
                else if (kind < 11)
                {
                    int count = random.Next(Math.Min(3, naive.Heights.Count) + 1);
                    layout.RemoveFromStart(count);
                    naive.Heights.RemoveRange(0, count);
                    naive.Measured.RemoveRange(0, count);
                }
                else if (kind < 15)
                {
                    if (naive.Heights.Count == 0) continue;
                    int index = random.Next(naive.Heights.Count);
                    float height = 1 + (float)random.NextDouble() * 99;
                    layout.SetMeasured(index, height);
                    naive.Heights[index] = height;
                    naive.Measured[index] = true;
                }
                else
                {
                    Assert.That(layout.Count, Is.EqualTo(naive.Heights.Count));
                    Assert.That(layout.ContentHeight, Is.EqualTo(naive.ContentHeight).Within(0.01));
                    if (naive.Heights.Count == 0) continue;
                    int index = random.Next(naive.Heights.Count + 1);
                    Assert.That(layout.GetTop(index), Is.EqualTo(naive.Top(index)).Within(0.01));
                    if (index < naive.Heights.Count) Assert.That(layout.IsMeasured(index), Is.EqualTo(naive.Measured[index]));
                    float offset = (float)(random.NextDouble() * (naive.ContentHeight + 50) - 10);
                    Assert.That(layout.IndexAt(offset), Is.EqualTo(naive.IndexAt(offset)));
                }
            }
        }

        [Test]
        public void TenThousandItemsStayFast()
        {
            var layout = new VirtualListLayout(2);
            for (int i = 0; i < 10000; i++) layout.Append(40);
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++)
            {
                layout.RemoveFromStart(1);
                layout.Append(40);
                layout.SetMeasured(layout.Count - 1, 37);
                layout.IndexAt(i * 13 % 400000);
            }
            watch.Stop();
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(50), "Generous bound that only catches O(n) regressions.");
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Run:

```bash
"E:/Unitys/6000.3.14f1/Editor/Unity.exe" -batchmode -projectPath unity -runTests -testPlatform EditMode -testFilter "Bun3.Unity.UI.Editor.Tests.VirtualListLayoutTests" -testResults "$(pwd -W)/unity/Logs/vlist-editmode.xml" -logFile "$(pwd -W)/unity/Logs/vlist-editmode.log"
```

Expected: 컴파일 실패 — `VirtualListLayout`을 찾을 수 없음.

- [ ] **Step 3: 구현**

`unity/Packages/com.bun3.unity.ui/Runtime/Lists/VirtualListLayout.cs`:

```csharp
using System;

namespace Bun3.Unity.UI.Lists
{
    /// <summary>
    /// Item heights for <see cref="VirtualList"/> with O(log n) offsets: a Fenwick tree of (height + spacing)
    /// in doubles, so repeated updates do not accumulate float error. Removing from the start only moves
    /// <c>head</c>; storage is compacted once the dead prefix outweighs the live items.
    /// </summary>
    internal sealed class VirtualListLayout
    {
        float[] heights = new float[16];
        bool[] measured = new bool[16];
        double[] tree = new double[17];
        int head;
        int tail;
        int topStep = 16;
        float spacing;

        public VirtualListLayout(float spacing) => this.spacing = spacing;

        public float Spacing
        {
            get => spacing;
            set
            {
                spacing = value;
                Rebuild();
            }
        }

        public int Count => tail - head;

        public float ContentHeight => Count == 0 ? 0 : (float)(Prefix(tail) - Prefix(head) - spacing);

        public void Clear()
        {
            head = tail = 0;
            Array.Clear(tree, 0, tree.Length);
        }

        public void Append(float estimatedHeight)
        {
            if (tail == heights.Length) Reorganize(head * 2 >= heights.Length ? heights.Length : heights.Length * 2);
            heights[tail] = estimatedHeight;
            measured[tail] = false;
            Add(tail, estimatedHeight + spacing);
            tail++;
        }

        public void RemoveFromStart(int count)
        {
            if ((uint)count > (uint)Count) throw new ArgumentOutOfRangeException(nameof(count));
            head += count;
            if (head == tail) Clear();
            else if (head * 2 > tail) Reorganize(heights.Length);
        }

        public void SetMeasured(int index, float height)
        {
            int slot = Slot(index);
            Add(slot, height - heights[slot]);
            heights[slot] = height;
            measured[slot] = true;
        }

        public void SetEstimated(int index, float height)
        {
            int slot = Slot(index);
            heights[slot] = height;
            measured[slot] = false;
        }

        public void Rebuild() => Reorganize(heights.Length);

        public bool IsMeasured(int index) => measured[Slot(index)];

        public float GetHeight(int index) => heights[Slot(index)];

        public float GetTop(int index)
        {
            if ((uint)index > (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return (float)(Prefix(head + index) - Prefix(head));
        }

        public int IndexAt(float offset)
        {
            if (Count == 0) return -1;
            if (offset <= 0) return 0;
            double remaining = Prefix(head) + offset;
            int position = 0;
            for (int step = topStep; step > 0; step >>= 1)
            {
                int next = position + step;
                if (next <= heights.Length && tree[next] <= remaining)
                {
                    position = next;
                    remaining -= tree[next];
                }
            }
            return Math.Min(Math.Max(position - head, 0), Count - 1);
        }

        int Slot(int index)
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return head + index;
        }

        double Prefix(int slots)
        {
            double sum = 0;
            for (int i = slots; i > 0; i -= i & -i) sum += tree[i];
            return sum;
        }

        void Add(int slot, double delta)
        {
            for (int i = slot + 1; i <= heights.Length; i += i & -i) tree[i] += delta;
        }

        void Reorganize(int capacity)
        {
            int count = Count;
            if (capacity != heights.Length)
            {
                var newHeights = new float[capacity];
                var newMeasured = new bool[capacity];
                Array.Copy(heights, head, newHeights, 0, count);
                Array.Copy(measured, head, newMeasured, 0, count);
                heights = newHeights;
                measured = newMeasured;
                tree = new double[capacity + 1];
                topStep = 1;
                while (topStep * 2 <= capacity) topStep *= 2;
            }
            else if (head > 0)
            {
                Array.Copy(heights, head, heights, 0, count);
                Array.Copy(measured, head, measured, 0, count);
            }
            head = 0;
            tail = count;
            Array.Clear(tree, 0, tree.Length);
            for (int slot = 0; slot < tail; slot++) tree[slot + 1] += heights[slot] + spacing;
            for (int i = 1; i <= capacity; i++)
            {
                int parent = i + (i & -i);
                if (parent <= capacity) tree[parent] += tree[i];
            }
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Step 2 명령을 다시 실행한다.
Expected: `failed="0"`. `grep -n "warning CS" unity/Logs/vlist-editmode.log | grep -i "Lists"` 결과가 비어 있다.

- [ ] **Step 5: 커밋**

```bash
git add unity/Packages/com.bun3.unity.ui
git commit -m "✨ 가상화 리스트 높이 저장소(펜윅 트리) 추가" -m "<CLAUDE.md 트레일러>"
```

---

### Task 2: `VirtualList` 본체 — 소스·측정·풀링·앵커·알림

**Files:**
- Create: `unity/Packages/com.bun3.unity.ui/Runtime/Lists/IVirtualListSource.cs` (`IVirtualListSource`, `IVirtualListEstimator`)
- Create: `unity/Packages/com.bun3.unity.ui/Runtime/Lists/VirtualList.cs`
- Test: `unity/Packages/com.bun3.unity.ui/Tests/Runtime/VirtualListTests.cs`

**Interfaces:**
- Consumes: `VirtualListLayout` (Task 1).
- Produces: `IVirtualListSource { int Count { get; } RectTransform CreateCell(RectTransform parent); void BindCell(RectTransform cell, int index); }`
- Produces: `IVirtualListEstimator { float EstimateHeight(int index, float cellWidth); }`
- Produces: `VirtualList : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler` — `RectOffset Padding`, `float Spacing`, `float EstimatedItemHeight`, `float EndThreshold`, `bool IsAtEnd`, `void SetSource(IVirtualListSource? source)`, `void Refresh()`, `void NotifyAppended(int count)`, `void NotifyRemovedFromStart(int count)`, `void ScrollToEnd()`.

이 Task가 `VirtualList.cs` 전체를 만든다(드래그 보정·너비 변경·비활성 지연 코드 포함). 해당 동작의 테스트는 Task 3이 추가한다.

- [ ] **Step 1: 실패하는 PlayMode 테스트 작성**

`unity/Packages/com.bun3.unity.ui/Tests/Runtime/VirtualListTests.cs`:

```csharp
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using Bun3.Unity.UI.Lists;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Bun3.Unity.UI.Tests
{
    public sealed class VirtualListTestMarker : MonoBehaviour
    {
        public int Id;
        public int Index;
    }

    public sealed class VirtualListTests
    {
        const float ViewportHeight = 300;

        sealed class Item
        {
            public int Id;
            public float Height;
        }

        class Source : IVirtualListSource
        {
            public readonly List<Item> Items = new List<Item>();
            public readonly HashSet<int> BoundIds = new HashSet<int>();
            public int Created;
            public int Bound;
            public bool WidthDependent;

            public int Count => Items.Count;

            public RectTransform CreateCell(RectTransform parent)
            {
                Created++;
                var go = new GameObject("Cell", typeof(RectTransform), typeof(LayoutElement), typeof(VirtualListTestMarker));
                var rect = (RectTransform)go.transform;
                rect.SetParent(parent, false);
                return rect;
            }

            public void BindCell(RectTransform cell, int index)
            {
                Bound++;
                var item = Items[index];
                BoundIds.Add(item.Id);
                var marker = cell.GetComponent<VirtualListTestMarker>();
                marker.Id = item.Id;
                marker.Index = index;
                cell.GetComponent<LayoutElement>().preferredHeight = WidthDependent ? item.Height * 384f / cell.rect.width : item.Height;
            }
        }

        sealed class EstimatingSource : Source, IVirtualListEstimator
        {
            public int Estimates;

            public float EstimateHeight(int index, float cellWidth)
            {
                Estimates++;
                return Items[index].Height + 15;
            }
        }

        sealed class BadEstimates : Source, IVirtualListEstimator
        {
            public float EstimateHeight(int index, float cellWidth) => index % 3 == 0 ? float.NaN : index % 3 == 1 ? 0 : -5;
        }

        GameObject canvas = null!;
        GameObject? events;
        ScrollRect scroll = null!;
        RectTransform viewport = null!;
        RectTransform content = null!;
        VirtualList list = null!;
        Source source = null!;
        int nextId;

        [SetUp]
        public void SetUp()
        {
            canvas = new GameObject("Canvas", typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scrollObject = new GameObject("Scroll", typeof(RectTransform));
            var scrollRect = (RectTransform)scrollObject.transform;
            scrollRect.SetParent(canvas.transform, false);
            scrollRect.sizeDelta = new Vector2(400, ViewportHeight);
            viewport = Child("Viewport", scrollRect);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.sizeDelta = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Child("Content", viewport);
            scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            list = scrollObject.AddComponent<VirtualList>();
            list.EstimatedItemHeight = 80;
            list.Spacing = 4;
            list.Padding = new RectOffset(8, 8, 6, 6);
            source = new Source();
            nextId = 0;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(canvas);
            if (events != null) Object.Destroy(events);
        }

        static RectTransform Child(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        // 20..60: always below the 80 estimate, so measuring only ever reveals more items.
        static float HeightOf(int id) => 20 + id * 37 % 41;

        void Fill(Source target, int count)
        {
            for (int i = 0; i < count; i++)
            {
                target.Items.Add(new Item { Id = nextId, Height = HeightOf(nextId) });
                nextId++;
            }
        }

        float ScrollTop
        {
            get => content.anchoredPosition.y;
            set => content.anchoredPosition = new Vector2(0, value);
        }

        float MaxScroll => Mathf.Max(0, content.rect.height - viewport.rect.height);

        List<VirtualListTestMarker> Visible()
        {
            var result = new List<VirtualListTestMarker>(content.GetComponentsInChildren<VirtualListTestMarker>(false));
            result.Sort((a, b) => a.Index.CompareTo(b.Index));
            return result;
        }

        // Distance from the viewport top down to the cell top.
        float FromTop(VirtualListTestMarker marker) => -((RectTransform)marker.transform).anchoredPosition.y - ScrollTop;

        VirtualListTestMarker TopVisible()
        {
            foreach (var marker in Visible())
                if (FromTop(marker) + ((RectTransform)marker.transform).rect.height > 0) return marker;
            throw new AssertionException("No visible cell.");
        }

        VirtualListTestMarker? Find(int id)
        {
            foreach (var marker in Visible())
                if (marker.Id == id) return marker;
            return null;
        }

        void ReplaceOldest(int times)
        {
            for (int i = 0; i < times; i++)
            {
                source.Items.RemoveAt(0);
                list.NotifyRemovedFromStart(1);
                Fill(source, 1);
                list.NotifyAppended(1);
            }
        }

        [UnityTest]
        public IEnumerator SetSourceBindsOnlyVisibleItems()
        {
            Fill(source, 10000);
            list.SetSource(source);
            yield return null;
            int visible = Visible().Count;
            Assert.That(visible, Is.GreaterThan(0));
            Assert.That(source.Bound, Is.LessThanOrEqualTo(visible + 1));
            Assert.That(source.Created, Is.LessThanOrEqualTo(visible + 1));
            Assert.That(ScrollTop, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator CellsMatchPreferredHeightsAndStack()
        {
            Fill(source, 50);
            list.SetSource(source);
            yield return null;
            var cells = Visible();
            Assert.That(FromTop(cells[0]), Is.EqualTo(6).Within(0.01f));
            for (int i = 0; i < cells.Count; i++)
            {
                var rect = (RectTransform)cells[i].transform;
                Assert.That(rect.rect.height, Is.EqualTo(HeightOf(cells[i].Id)).Within(0.01f));
                Assert.That(rect.rect.width, Is.EqualTo(400 - 16).Within(0.01f));
                if (i > 0) Assert.That(FromTop(cells[i]), Is.EqualTo(FromTop(cells[i - 1]) + HeightOf(cells[i - 1].Id) + 4).Within(0.01f));
            }
        }

        [UnityTest]
        public IEnumerator ScrollingUpKeepsTheReadingPositionWhileMeasuring()
        {
            Fill(source, 200);
            list.SetSource(source);
            list.ScrollToEnd();
            yield return null;
            for (int step = 0; step < 60; step++)
            {
                ScrollTop -= 15;
                var anchor = TopVisible();
                int id = anchor.Id;
                float before = FromTop(anchor);
                yield return null;
                var after = Find(id);
                Assert.That(after, Is.Not.Null);
                Assert.That(FromTop(after!), Is.EqualTo(before).Within(0.5f));
            }
            Assert.That(source.Created, Is.LessThanOrEqualTo(Mathf.CeilToInt(ViewportHeight / 20) + 3));
        }

        [UnityTest]
        public IEnumerator AppendFollowsTheEndOnlyWhenAtTheEnd()
        {
            Fill(source, 100);
            list.SetSource(source);
            list.ScrollToEnd();
            yield return null;
            Fill(source, 1);
            list.NotifyAppended(1);
            Assert.That(list.IsAtEnd, Is.True);
            Assert.That(ScrollTop, Is.EqualTo(MaxScroll).Within(0.5f));
            var cells = Visible();
            Assert.That(cells[cells.Count - 1].Index, Is.EqualTo(source.Count - 1));

            ScrollTop -= 200;
            yield return null;
            var watched = TopVisible();
            int id = watched.Id;
            float before = FromTop(watched);
            Fill(source, 3);
            list.NotifyAppended(3);
            Assert.That(list.IsAtEnd, Is.False);
            Assert.That(FromTop(Find(id)!), Is.EqualTo(before).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator ChatSimulationKeepsEndOrReadingPosition()
        {
            Fill(source, 10000);
            list.SetSource(source);
            list.ScrollToEnd();
            yield return null;
            ReplaceOldest(1000);
            Assert.That(list.IsAtEnd, Is.True);
            Assert.That(content.childCount, Is.LessThanOrEqualTo(Mathf.CeilToInt(ViewportHeight / 20) + 3));

            ScrollTop = MaxScroll / 2;
            yield return null;
            var watched = TopVisible();
            int id = watched.Id;
            float before = FromTop(watched);
            ReplaceOldest(100);
            Assert.That(FromTop(Find(id)!), Is.EqualTo(before).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator CountMismatchWhileActiveThrows()
        {
            Fill(source, 10);
            list.SetSource(source);
            yield return null;
            Fill(source, 1);
            Assert.Throws<InvalidOperationException>(() => list.NotifyAppended(2));
            Assert.Throws<InvalidOperationException>(() => list.NotifyRemovedFromStart(20));
        }

        [UnityTest]
        public IEnumerator EstimatorProvidesPerItemHeights()
        {
            var estimating = new EstimatingSource();
            Fill(estimating, 1000);
            list.SetSource(estimating);
            yield return null;
            Assert.That(estimating.Estimates, Is.EqualTo(1000));
            Assert.That(estimating.Bound, Is.LessThanOrEqualTo(Visible().Count + 1));
            float expected = 12 + 4 * 999;
            foreach (var item in estimating.Items)
                expected += estimating.BoundIds.Contains(item.Id) ? item.Height : item.Height + 15;
            Assert.That(content.rect.height, Is.EqualTo(expected).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator EndThresholdDecidesWhetherToFollow()
        {
            list.EndThreshold = 50;
            Fill(source, 200);
            list.SetSource(source);
            list.ScrollToEnd();
            yield return null;
            ScrollTop -= 30;
            yield return null;
            Fill(source, 1);
            list.NotifyAppended(1);
            Assert.That(ScrollTop, Is.EqualTo(MaxScroll).Within(0.5f));

            ScrollTop -= 80;
            yield return null;
            var watched = TopVisible();
            int id = watched.Id;
            float before = FromTop(watched);
            Fill(source, 1);
            list.NotifyAppended(1);
            Assert.That(list.IsAtEnd, Is.False);
            Assert.That(FromTop(Find(id)!), Is.EqualTo(before).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator InvalidEstimatesFallBackToEstimatedItemHeight()
        {
            var bad = new BadEstimates();
            Fill(bad, 30);
            list.SetSource(bad);
            yield return null;
            float expected = 12 + 4 * 29;
            foreach (var item in bad.Items) expected += bad.BoundIds.Contains(item.Id) ? item.Height : 80;
            Assert.That(content.rect.height, Is.EqualTo(expected).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator ZeroSizedViewportDoesNotThrow()
        {
            ((RectTransform)scroll.transform).sizeDelta = Vector2.zero;
            Fill(source, 100);
            Assert.DoesNotThrow(() => list.SetSource(source));
            yield return null;
            Assert.That(Visible().Count, Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator SwitchingSourceDestroysPreviousCells()
        {
            Fill(source, 50);
            list.SetSource(source);
            yield return null;
            var replacement = new Source();
            nextId = 100000;
            Fill(replacement, 50);
            list.SetSource(replacement);
            yield return null;
            foreach (var marker in content.GetComponentsInChildren<VirtualListTestMarker>(true))
                Assert.That(marker.Id, Is.GreaterThanOrEqualTo(100000));
        }

        [UnityTest]
        public IEnumerator RefreshRecoversFromSilentSourceChanges()
        {
            Fill(source, 20);
            list.SetSource(source);
            yield return null;
            source.Items.RemoveRange(0, 15);
            list.Refresh();
            yield return null;
            foreach (var cell in Visible()) Assert.That(cell.Id, Is.EqualTo(source.Items[cell.Index].Id));
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Run:

```bash
"E:/Unitys/6000.3.14f1/Editor/Unity.exe" -batchmode -projectPath unity -runTests -testPlatform PlayMode -testFilter "Bun3.Unity.UI.Tests.VirtualListTests" -testResults "$(pwd -W)/unity/Logs/vlist-playmode.xml" -logFile "$(pwd -W)/unity/Logs/vlist-playmode.log"
```

Expected: 컴파일 실패 — `VirtualList`/`IVirtualListSource`를 찾을 수 없음.

- [ ] **Step 3: 인터페이스 작성**

`unity/Packages/com.bun3.unity.ui/Runtime/Lists/IVirtualListSource.cs`:

```csharp
using UnityEngine;

namespace Bun3.Unity.UI.Lists
{
    /// <summary>Supplies items and cells to a <see cref="VirtualList"/>.</summary>
    public interface IVirtualListSource
    {
        /// <summary>Current item count. Must change only together with a <see cref="VirtualList"/> notification or refresh.</summary>
        int Count { get; }

        /// <summary>Creates a new cell under <paramref name="parent"/>. Called only when the pool is empty.</summary>
        RectTransform CreateCell(RectTransform parent);

        /// <summary>Fills <paramref name="cell"/> with item <paramref name="index"/>. The cell already has its final width.</summary>
        void BindCell(RectTransform cell, int index);
    }

    /// <summary>
    /// Optional companion of <see cref="IVirtualListSource"/> that estimates unmeasured item heights per item.
    /// It is called for every item on a full refresh, so it must be plain arithmetic: no layout or text-mesh calls.
    /// Overestimating keeps the view steadier than underestimating.
    /// </summary>
    public interface IVirtualListEstimator
    {
        /// <summary>Estimated height of item <paramref name="index"/> for a cell <paramref name="cellWidth"/> wide.</summary>
        float EstimateHeight(int index, float cellWidth);
    }
}
```

- [ ] **Step 4: `VirtualList` 구현**

`unity/Packages/com.bun3.unity.ui/Runtime/Lists/VirtualList.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Unity.Core.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bun3.Unity.UI.Lists
{
    /// <summary>
    /// Vertical virtualized list with variable item heights for a <see cref="ScrollRect"/> on the same GameObject.
    /// Only visible items get cells. An item is measured from its cell's preferred height the first time it
    /// becomes visible; unmeasured items use estimates. Appends, removals from the start, measurement and width
    /// changes keep the reading position, and new items are followed only while <see cref="IsAtEnd"/>.
    /// </summary>
    /// <remarks>
    /// Layout contract: the list forces the content and every cell to top-stretch anchors with pivot (0.5, 1) and
    /// sets their heights. Do not put a LayoutGroup or ContentSizeFitter on the content, or a ContentSizeFitter on
    /// cells; each cell root must report a preferred height (for example a VerticalLayoutGroup that controls child
    /// height, or a LayoutElement). Calls made while the list is inactive are applied on its first LateUpdate after
    /// activation. The scrollbar is approximate while estimates remain.
    /// </remarks>
    [RequireComponent(typeof(ScrollRect))]
    [DisallowMultipleComponent]
    public sealed class VirtualList : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        enum AnchorKind : byte { Top, End, Item }

        readonly struct Anchor
        {
            public static readonly Anchor Top = new Anchor(AnchorKind.Top, 0, 0);
            public static readonly Anchor End = new Anchor(AnchorKind.End, 0, 0);
            public readonly AnchorKind Kind;
            public readonly int Index;
            public readonly float Offset;

            public Anchor(AnchorKind kind, int index, float offset)
            {
                Kind = kind;
                Index = index;
                Offset = offset;
            }

            public Anchor AfterRemoving(int count) =>
                Kind != AnchorKind.Item ? this : Index >= count ? new Anchor(Kind, Index - count, Offset) : new Anchor(Kind, 0, 0);
        }

        const int MaxSettlePasses = 64;

        [SerializeField] RectOffset padding = new RectOffset();
        [SerializeField, Min(0)] float spacing;
        [SerializeField, Min(1)] float estimatedItemHeight = 40;
        [SerializeField, Min(0)] float endThreshold = 1;

        readonly VirtualListLayout layout = new VirtualListLayout(0);
        readonly List<RectTransform> cells = new List<RectTransform>();
        readonly Stack<RectTransform> pool = new Stack<RectTransform>();
        ScrollRect? scroll;
        IVirtualListSource? source;
        IVirtualListEstimator? estimator;
        PointerEventData? drag;
        int first;
        float cellWidth = -1;
        bool refreshPending;
        bool topPending;
        bool endPending;
        bool settling;

        /// <summary>Space around the items inside the content. Assigning refreshes the list.</summary>
        public RectOffset Padding
        {
            get => padding;
            set
            {
                padding = value ?? throw new ArgumentNullException(nameof(value));
                Refresh();
            }
        }

        /// <summary>Vertical gap between items. Assigning refreshes the list.</summary>
        public float Spacing
        {
            get => spacing;
            set
            {
                spacing = Mathf.Max(0, value);
                Refresh();
            }
        }

        /// <summary>Height of unmeasured items when the source is not an <see cref="IVirtualListEstimator"/>. Must be positive.</summary>
        public float EstimatedItemHeight
        {
            get => estimatedItemHeight;
            set => estimatedItemHeight = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        /// <summary>How far above the bottom, in pixels, still counts as the end. Must not be negative.</summary>
        public float EndThreshold
        {
            get => endThreshold;
            set => endThreshold = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        /// <summary>True within <see cref="EndThreshold"/> of the bottom, or when the content is shorter than the viewport.</summary>
        public bool IsAtEnd => ScrollTop >= MaxScroll - endThreshold;

        ScrollRect Scroll => scroll != null ? scroll : scroll = GetComponent<ScrollRect>();
        RectTransform Viewport => Scroll.viewport != null ? Scroll.viewport : (RectTransform)transform;
        RectTransform Content => Scroll.content != null ? Scroll.content : throw new InvalidOperationException("VirtualList requires ScrollRect.content.");
        float ViewportHeight => Viewport.rect.height;
        float CurrentWidth => Mathf.Max(0, Viewport.rect.width - padding.horizontal);
        float TotalHeight => padding.vertical + layout.ContentHeight;
        float MaxScroll => Mathf.Max(0, TotalHeight - ViewportHeight);
        int SourceCount => source?.Count ?? 0;

        float ScrollTop
        {
            get => Content.anchoredPosition.y;
            set => Content.anchoredPosition = new Vector2(Content.anchoredPosition.x, value);
        }

        /// <summary>Shows <paramref name="value"/> from the top, with every item unmeasured. Cells from a different source are destroyed.</summary>
        public void SetSource(IVirtualListSource? value)
        {
            if (!ReferenceEquals(value, source)) DestroyCells();
            source = value;
            estimator = value as IVirtualListEstimator;
            topPending = true;
            Refresh();
        }

        /// <summary>Marks every item unmeasured and rebuilds from <see cref="IVirtualListSource.Count"/>, keeping the reading position.</summary>
        public void Refresh()
        {
            if (!isActiveAndEnabled)
            {
                refreshPending = true;
                return;
            }
            var anchor = topPending ? Anchor.Top : Capture();
            refreshPending = topPending = false;
            Rebuild(anchor);
            if (!endPending) return;
            endPending = false;
            Scroll.StopMovement();
            Settle(Anchor.End);
        }

        /// <summary>Reports <paramref name="count"/> items added at the end of the source.</summary>
        /// <exception cref="InvalidOperationException">The source count does not match while the list is active.</exception>
        public void NotifyAppended(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0 || Absorb()) return;
            if (SourceCount != layout.Count + count)
                throw new InvalidOperationException("The source count does not match the appended count.");
            var anchor = Capture();
            for (int i = 0; i < count; i++) layout.Append(Estimate(layout.Count));
            Settle(anchor);
        }

        /// <summary>Reports <paramref name="count"/> items removed from the start of the source.</summary>
        /// <exception cref="InvalidOperationException">The source count does not match while the list is active.</exception>
        public void NotifyRemovedFromStart(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0 || Absorb()) return;
            if (count > layout.Count || SourceCount != layout.Count - count)
                throw new InvalidOperationException("The source count does not match the removed count.");
            var anchor = Capture();
            ReleaseCells();
            layout.RemoveFromStart(count);
            Settle(anchor.AfterRemoving(count));
        }

        /// <summary>Stops inertia and jumps to the bottom.</summary>
        public void ScrollToEnd()
        {
            if (Absorb())
            {
                endPending = true;
                return;
            }
            Scroll.StopMovement();
            Settle(Anchor.End);
        }

        /// <inheritdoc />
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) drag = eventData;
        }

        /// <inheritdoc />
        public void OnDrag(PointerEventData eventData)
        {
            if (drag != null) drag = eventData;
        }

        /// <inheritdoc />
        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) drag = null;
        }

        void Awake()
        {
            Scroll.horizontal = false;
            Scroll.vertical = true;
            Scroll.onValueChanged.AddListener(OnScrolled);
        }

        void OnDestroy()
        {
            if (scroll != null) scroll.onValueChanged.RemoveListener(OnScrolled);
        }

        void OnDisable() => drag = null;

        void LateUpdate()
        {
            if (refreshPending) Refresh();
            else if (source != null && Mathf.Abs(CurrentWidth - cellWidth) > 0.5f) Refresh();
        }

        void OnScrolled(Vector2 _)
        {
            if (settling || refreshPending || source == null || !isActiveAndEnabled) return;
            Settle(Capture(), restore: false);
        }

        // Deferred calls are applied by the next Refresh, which restarts from source.Count.
        bool Absorb()
        {
            if (refreshPending) return true;
            if (isActiveAndEnabled) return false;
            refreshPending = true;
            return true;
        }

        void Rebuild(Anchor anchor)
        {
            ReleaseCells();
            var content = Content;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, content.sizeDelta.y);
            cellWidth = CurrentWidth;
            layout.Clear();
            layout.Spacing = spacing;
            int count = SourceCount;
            for (int i = 0; i < count; i++) layout.Append(Estimate(i));
            Settle(anchor);
        }

        float Estimate(int index)
        {
            if (estimator == null) return estimatedItemHeight;
            float height = estimator.EstimateHeight(index, cellWidth);
            return height > 0 && !float.IsInfinity(height) ? height : estimatedItemHeight;
        }

        Anchor Capture()
        {
            if (layout.Count == 0 || IsAtEnd) return Anchor.End;
            float top = ScrollTop - padding.top;
            int a = layout.IndexAt(top);
            int b = layout.IndexAt(top + ViewportHeight);
            int index = a;
            for (int i = a; i <= b; i++)
            {
                if (!layout.IsMeasured(i)) continue;
                index = i;
                break;
            }
            return new Anchor(AnchorKind.Item, index, top - layout.GetTop(index));
        }

        float Restore(in Anchor anchor)
        {
            float target = anchor.Kind switch
            {
                AnchorKind.Top => 0,
                AnchorKind.End => MaxScroll,
                _ => anchor.Index >= layout.Count ? MaxScroll : padding.top + layout.GetTop(anchor.Index) + anchor.Offset,
            };
            return Mathf.Clamp(target, 0, MaxScroll);
        }

        // A scroll-driven settle restores only after a measurement changed heights, so elastic overscroll survives.
        void Settle(Anchor anchor, bool restore = true)
        {
            if (settling) return;
            settling = true;
            try
            {
                for (int pass = 0; pass < MaxSettlePasses; pass++)
                {
                    Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, TotalHeight);
                    if (restore) ScrollTop = Restore(anchor);
                    if (!SyncCells()) break;
                    restore = true;
                }
                PlaceCells();
                if (!restore) return;
                if (drag != null) Scroll.OnBeginDrag(drag);
                Scroll.Rebuild(CanvasUpdate.PostLayout);
            }
            finally
            {
                settling = false;
            }
        }

        // Returns true when a newly visible item was measured at a height different from its estimate.
        bool SyncCells()
        {
            if (layout.Count == 0)
            {
                ReleaseCells();
                return false;
            }
            float top = ScrollTop - padding.top;
            int a = layout.IndexAt(top);
            int b = layout.IndexAt(top + ViewportHeight);
            while (cells.Count > 0 && first < a)
            {
                Release(cells[0]);
                cells.RemoveAt(0);
                first++;
            }
            while (cells.Count > 0 && first + cells.Count - 1 > b)
            {
                Release(cells[cells.Count - 1]);
                cells.RemoveAt(cells.Count - 1);
            }
            if (cells.Count == 0) first = a;
            bool changed = false;
            while (first > a)
            {
                first--;
                cells.Insert(0, Show(first, ref changed));
            }
            while (first + cells.Count - 1 < b) cells.Add(Show(first + cells.Count, ref changed));
            return changed;
        }

        RectTransform Show(int index, ref bool changed)
        {
            var cell = pool.Count > 0 ? pool.Pop() : CreateCell();
            cell.gameObject.SetActive(true);
            cell.anchorMin = new Vector2(0, 1);
            cell.anchorMax = new Vector2(1, 1);
            cell.pivot = new Vector2(0.5f, 1);
            cell.sizeDelta = new Vector2(-padding.horizontal, cell.sizeDelta.y);
            cell.anchoredPosition = new Vector2((padding.left - padding.right) * 0.5f, cell.anchoredPosition.y);
            source!.BindCell(cell, index);
            if (!layout.IsMeasured(index))
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(cell);
                float height = LayoutUtility.GetPreferredHeight(cell);
                if (Mathf.Abs(height - layout.GetHeight(index)) > 0.01f) changed = true;
                layout.SetMeasured(index, height);
            }
            return cell;
        }

        RectTransform CreateCell()
        {
            var cell = source!.CreateCell(Content);
            if (cell == null) throw new InvalidOperationException("IVirtualListSource.CreateCell returned null.");
            cell.SetParent(Content, false);
            return cell;
        }

        void PlaceCells()
        {
            for (int k = 0; k < cells.Count; k++)
            {
                int index = first + k;
                var cell = cells[k];
                cell.anchoredPosition = new Vector2(cell.anchoredPosition.x, -(padding.top + layout.GetTop(index)));
                cell.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, layout.GetHeight(index));
            }
        }

        void Release(RectTransform cell)
        {
            cell.gameObject.SetActive(false);
            pool.Push(cell);
        }

        void ReleaseCells()
        {
            for (int i = 0; i < cells.Count; i++) Release(cells[i]);
            cells.Clear();
            first = 0;
        }

        void DestroyCells()
        {
            ReleaseCells();
            while (pool.Count > 0)
            {
                var cell = pool.Pop();
                if (cell != null) cell.gameObject.SafeDestroy();
            }
        }
    }
}
```

`Util.SafeDestroy`의 확장 메서드 시그니처가 `SafeDestroy(this Object target)`이므로 `cell.gameObject.SafeDestroy()`로 부른다(`unity/Packages/com.bun3.unity.core/Runtime/Utils/Util.Object.cs:13`).

- [ ] **Step 5: 통과 확인**

Step 2 명령을 다시 실행한다.
Expected: `failed="0"`. `grep -n "warning CS" unity/Logs/vlist-playmode.log | grep -i "Lists"` 결과가 비어 있다.

- [ ] **Step 6: 커밋**

```bash
git add unity/Packages/com.bun3.unity.ui
git commit -m "✨ uGUI 가변 높이 가상화 리스트 추가" -m "<CLAUDE.md 트레일러>"
```

---

### Task 3: 드래그 보정·너비 변경·비활성 지연·고무줄 보존 테스트

**Files:**
- Test: `unity/Packages/com.bun3.unity.ui/Tests/Runtime/VirtualListTests.cs` (테스트 4개 추가)

**Interfaces:**
- Consumes: Task 2의 `VirtualList` 공개 API와 테스트 픽스처(`Fill`, `Visible`, `TopVisible`, `Find`, `FromTop`, `ScrollTop`, `MaxScroll`, `events`).

이 동작들의 코드는 Task 2에 이미 있다(`OnBeginDrag`/`Settle`의 기준점 재설정, `LateUpdate` 너비 비교, `Absorb`, `restore: false`). 여기서는 회귀를 막는 테스트만 추가한다. 하나라도 실패하면 Task 2 코드를 고친다.

- [ ] **Step 1: 테스트 추가**

`VirtualListTests` 클래스 끝(`RefreshRecoversFromSilentSourceChanges` 뒤)에 추가:

```csharp
        [UnityTest]
        public IEnumerator WidthChangeRemeasuresOnlyVisibleItemsAndKeepsTheAnchor()
        {
            source.WidthDependent = true;
            list.EstimatedItemHeight = 120;
            Fill(source, 500);
            list.SetSource(source);
            ScrollTop = 2000;
            yield return null;
            var watched = TopVisible();
            int id = watched.Id;
            float before = FromTop(watched);
            source.Bound = 0;
            ((RectTransform)scroll.transform).sizeDelta = new Vector2(300, ViewportHeight);
            yield return null;
            Assert.That(source.Bound, Is.LessThanOrEqualTo(Visible().Count + 1));
            var after = Find(id);
            Assert.That(after, Is.Not.Null);
            Assert.That(FromTop(after!), Is.EqualTo(before).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator DragIsRebasedWhenTheListMovesContent()
        {
            events = new GameObject("EventSystem", typeof(EventSystem));
            Fill(source, 300);
            list.SetSource(source);
            list.ScrollToEnd();
            yield return null;
            ScrollTop -= 500;
            yield return null;
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = new Vector2(200, 150),
            };
            ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.beginDragHandler);
            var watched = TopVisible();
            int id = watched.Id;
            float before = FromTop(watched);
            source.Items.RemoveRange(0, 5);
            list.NotifyRemovedFromStart(5);
            float corrected = ScrollTop;
            ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.dragHandler);
            Assert.That(ScrollTop, Is.EqualTo(corrected).Within(0.01f));
            Assert.That(FromTop(Find(id)!), Is.EqualTo(before).Within(0.5f));
            ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.endDragHandler);
        }

        [UnityTest]
        public IEnumerator NotificationsWhileInactiveAreAppliedOnActivation()
        {
            Fill(source, 50);
            list.SetSource(source);
            yield return null;
            scroll.gameObject.SetActive(false);
            Fill(source, 20);
            list.NotifyAppended(20);
            source.Items.RemoveRange(0, 10);
            list.NotifyRemovedFromStart(10);
            Fill(source, 1);
            list.NotifyAppended(5); // deliberately wrong: absorbed while a refresh is pending
            list.ScrollToEnd();
            scroll.gameObject.SetActive(true);
            yield return null;
            Assert.That(list.IsAtEnd, Is.True);
            var cells = Visible();
            Assert.That(cells.Count, Is.GreaterThan(0));
            foreach (var cell in cells) Assert.That(cell.Id, Is.EqualTo(source.Items[cell.Index].Id));
            Assert.That(cells[cells.Count - 1].Index, Is.EqualTo(source.Count - 1));
        }

        [UnityTest]
        public IEnumerator ScrollWithoutMeasurementLeavesElasticOverscrollAlone()
        {
            scroll.movementType = ScrollRect.MovementType.Elastic;
            Fill(source, 100);
            list.SetSource(source);
            yield return null;
            ScrollTop = -40;
            scroll.onValueChanged.Invoke(scroll.normalizedPosition);
            Assert.That(ScrollTop, Is.EqualTo(-40).Within(0.01f));
        }
```

- [ ] **Step 2: 실행**

Run: Task 2 Step 2의 PlayMode 명령.
Expected: 15개 전부 `passed`.

- [ ] **Step 3: 커밋**

```bash
git add unity/Packages/com.bun3.unity.ui/Tests/Runtime/VirtualListTests.cs
git commit -m "✅ 가상화 리스트 드래그·너비·비활성 회귀 테스트 추가" -m "<CLAUDE.md 트레일러>"
```

---

### Task 4: 문서·버전 0.4.0·메타

**Files:**
- Modify: `unity/Packages/com.bun3.unity.ui/package.json` (`"version": "0.4.0"`)
- Modify: `unity/Packages/com.bun3.unity.ui/CHANGELOG.md`
- Modify: `unity/Packages/com.bun3.unity.ui/README.md`
- Modify: `unity/Packages/com.bun3.unity.ui/Documentation/Unity UI.md`
- Create(Unity 생성): 새 파일들의 `.meta`

**Interfaces:**
- Consumes: Task 1–3의 공개 API.

- [ ] **Step 1: 버전과 CHANGELOG**

`package.json`의 `"version": "0.3.1"`을 `"version": "0.4.0"`으로 바꾼다.

`CHANGELOG.md`의 소개 단락 바로 뒤, 가장 최근 항목 위에 추가:

```markdown
## [0.4.0] - 2026-10-12

### Added

- `VirtualList` (`Bun3.Unity.UI.Lists`): vertical virtualized list for `ScrollRect` with variable item heights. Only visible items are bound and measured; the rest use per-item estimates (`IVirtualListEstimator`) or `EstimatedItemHeight`. Appends, removals from the start, measurement and width changes keep the reading position; new items are followed only within `EndThreshold` of the end. Offsets use a Fenwick tree, so 10,000 items stay responsive.
```

- [ ] **Step 2: README**

`README.md`의 `## Features` 목록에 추가:

```markdown
- **`VirtualList`** — vertical virtualized `ScrollRect` list with variable item heights. Binds and measures only visible cells, keeps the reading position when items are measured, appended, or removed from the start, and follows new items only at the end.
```

`## Quick Start` 절 끝에 추가:

````markdown
### VirtualList

```csharp
using Bun3.Unity.UI.Lists;

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
        _list.NotifyAppended(1); // follows only if the view was at the end
    }
}
```

Put `VirtualList` on the `ScrollRect` GameObject. No LayoutGroup or ContentSizeFitter on the content; each cell root reports a preferred height.
````

- [ ] **Step 3: Documentation**

`Documentation/Unity UI.md`의 `# Technical details` 바로 위에 추가:

```markdown
## VirtualList

`VirtualList` shows a vertical list whose items have different heights, creating cells only for visible items. Text is never clipped: each cell's preferred height becomes its row height.

### Layout contract

- Put `VirtualList` on the `ScrollRect` GameObject. The viewport is `ScrollRect.viewport`, or the `ScrollRect` itself.
- The list forces the content and every cell to top-stretch anchors with pivot (0.5, 1) and sets their heights. Do not add a LayoutGroup or ContentSizeFitter to the content, or a ContentSizeFitter to cells.
- Each cell root must report a preferred height, for example a `VerticalLayoutGroup` with *Control Child Size → Height*, or a `LayoutElement`.

### Measurement and estimates

- An item is measured once, the first time it becomes visible: the list sets the cell width, calls `BindCell`, rebuilds the cell layout, and reads its preferred height.
- `SetSource`, `Refresh` and width changes mark every item unmeasured; only visible items are measured again.
- Unmeasured items use `IVirtualListEstimator.EstimateHeight` when the source implements it, otherwise `EstimatedItemHeight`. The estimator runs for every item on a refresh, so keep it to arithmetic on your data. Overestimating keeps the view steadier.

### Reading position

Every change records an anchor first: the end when `IsAtEnd`, otherwise the topmost measured visible item and its offset. After the change the anchor is restored, so measuring items above, appending, or removing from the start never moves what you are reading. While the user drags, the list rebases the `ScrollRect` drag origin after moving the content.

### Limits

- Vertical only; append at the end and remove from the start only.
- The scrollbar size and position are approximate while estimates remain, and drift slightly as items are measured.
- Calls made while the list is inactive are applied on the first `LateUpdate` after activation.
```

같은 문서의 `## Package contents` 표(또는 목록)에 `Runtime/Lists/` — `VirtualList`, `VirtualListLayout`(internal), `IVirtualListSource`, `IVirtualListEstimator`를 추가하고, `## Document revision history`에 `2026-10-12 — Added VirtualList.` 한 줄을 추가한다(기존 표 형식을 따른다).

- [ ] **Step 4: 전체 UI 테스트와 메타**

Run:

```bash
"E:/Unitys/6000.3.14f1/Editor/Unity.exe" -batchmode -projectPath unity -runTests -testPlatform EditMode -testFilter "Bun3.Unity.UI" -testResults "$(pwd -W)/unity/Logs/ui-editmode.xml" -logFile "$(pwd -W)/unity/Logs/ui-editmode.log"
"E:/Unitys/6000.3.14f1/Editor/Unity.exe" -batchmode -projectPath unity -runTests -testPlatform PlayMode -testFilter "Bun3.Unity.UI" -testResults "$(pwd -W)/unity/Logs/ui-playmode.xml" -logFile "$(pwd -W)/unity/Logs/ui-playmode.log"
git status --short unity/Packages/com.bun3.unity.ui
```

Expected: 기존 UI 테스트를 포함해 `failed="0"`. 새 파일 옆에 `.meta`가 모두 있다(`Runtime/AssemblyInfo.cs.meta`, `Runtime/Lists.meta`와 그 안의 세 파일, 테스트 두 파일).

- [ ] **Step 5: 커밋**

```bash
git add unity/Packages/com.bun3.unity.ui
git commit -m "📝 VirtualList 문서와 0.4.0 버전 추가" -m "<CLAUDE.md 트레일러>"
```

---

## 완료 확인 (스펙 §8)

- [ ] bun3-kit Unity 프로젝트에서 경고 없이 컴파일, EditMode·PlayMode 통과, `.meta` 커밋.
- [ ] public 멤버 영어 XML 문서. README·Documentation에 레이아웃 계약, 추정기 계약(산술만), 추정 높이와 스크롤바 근사, 사용 예. CHANGELOG 0.4.0.
- [ ] JP 통합은 JP 플랜(`jurassic-paradise/docs/superpowers/plans/2026-10-12-chat-system.md`)의 채팅 패널 테스트로 확인한다.
