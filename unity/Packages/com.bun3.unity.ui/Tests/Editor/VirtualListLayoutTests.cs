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
