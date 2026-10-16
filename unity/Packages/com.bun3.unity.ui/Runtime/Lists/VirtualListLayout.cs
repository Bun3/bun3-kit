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
