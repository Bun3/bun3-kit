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
        public IEnumerator ScrollingUpInsideEndThresholdKeepsTheReadingPosition()
        {
            list.EndThreshold = 50;
            Fill(source, 200);
            list.SetSource(source);
            list.ScrollToEnd();
            yield return null;
            for (int step = 0; step < 60; step++)
            {
                ScrollTop -= step == 0 ? 30 : 15;
                var anchor = TopVisible();
                int id = anchor.Id;
                float before = FromTop(anchor);
                yield return null;
                var after = Find(id);
                Assert.That(after, Is.Not.Null);
                Assert.That(FromTop(after!), Is.EqualTo(before).Within(0.5f));
                if (step == 0) Assert.That(MaxScroll - ScrollTop, Is.EqualTo(30).Within(0.5f));
            }
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
    }
}
