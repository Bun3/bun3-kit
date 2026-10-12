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
