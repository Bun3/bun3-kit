#nullable enable

namespace Bun3.Gameplay.Tags
{
    internal static class TagSearch
    {
        internal static int LowerBound(ushort[] indices, int count, ushort target, out int comparisons)
        {
            var low = 0;
            var high = count;
            comparisons = 0;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                comparisons++;
                if (indices[middle] < target)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }
    }
}
