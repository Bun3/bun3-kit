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
