using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Bun3.State.Tests
{
    public sealed class TrackedListTests
    {
        [Test]
        public void Scalar_operations_restore_values_and_order()
        {
            var root = new TestState();
            root.Numbers.Add(1);
            root.Numbers.Add(null);
            root.Numbers.Add(2);
            var owner = new TestStateOwner(root);
            owner.Begin();
            root.Numbers.Insert(1, 5);
            root.Numbers[0] = 9;
            root.Numbers.Remove(null);
            root.Numbers.RemoveAt(1);
            root.Numbers.Clear();
            root.Numbers.Add(7);
            var changed = owner.ChangedCalls;
            owner.Rollback();
            Assert.That(root.Numbers, Is.EqualTo(new int?[] { 1, null, 2 }));
            Assert.That(owner.ChangedCalls, Is.EqualTo(changed));
        }

        [Test]
        public void Invalid_indices_and_noops_do_not_prepare_owner()
        {
            var root = new TestState();
            var child = new TestState();
            root.Children.Add(child);
            var owner = new TestStateOwner(root);
            owner.Begin();
            Assert.Throws<ArgumentOutOfRangeException>(() => root.Children.Insert(2, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => root.Children.RemoveAt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => root.Children[1] = null);
            root.Children[0] = child;
            Assert.That(root.Children.Remove(new TestState()), Is.False);
            root.Numbers.Clear();
            root.Child = null;
            Assert.That(owner.PrepareCalls, Is.Zero);
            Assert.That(owner.ChangedCalls, Is.Zero);
        }

        [Test]
        public void Node_equality_uses_identity_for_replace_search_and_remove()
        {
            var a = new TestState { Level = 1 };
            var b = new TestState { Level = 2 };
            var root = new TestState();
            root.Children.Add(a);
            root.Children.Add(b);
            var owner = new TestStateOwner(root);
            Assert.That(root.Children.IndexOf(b), Is.EqualTo(1));
            root.Children.Remove(b);
            Assert.That(root.Children[0], Is.SameAs(a));
            root.Children[0] = b;
            Assert.That(root.Children[0], Is.SameAs(b));
            Assert.That(owner.ChangedCalls, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => root.Children.Add(b));
        }

        [Test]
        public void Null_nodes_are_values_and_enumerator_detects_modification()
        {
            var list = new TrackedList<TestState?>(4) { null, null };
            Assert.That(list.IndexOf(null), Is.Zero);
            var enumerator = list.GetEnumerator();
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.Null);
            list.Remove(null);
            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
            var copy = new TestState?[1];
            ((ICollection<TestState?>)list).CopyTo(copy, 0);
            Assert.That(copy[0], Is.Null);
            Assert.That(((ICollection<TestState?>)list).IsReadOnly, Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Warmed_child_and_list_changes_do_not_allocate_per_write(bool active)
        {
            var root = new TestState();
            var child = new TestState();
            var owner = new ReusingOwner(root) { Active = active };
            for (var i = 0; i < 100; i++) MutateAndComplete(root, child, owner);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) MutateAndComplete(root, child, owner);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void Nullable_node_elements_restore_nulls_and_distinct_references()
        {
            var root = new TestState();
            var child = new TestState();
            root.Children.Add(null);
            root.Children.Add(child);
            root.Children.Add(null);
            var owner = new TestStateOwner(root);
            owner.Begin();
            root.Children[1] = null;
            root.Children[0] = child;
            root.Children.Remove(null);
            root.Children.Clear();
            owner.Rollback();
            Assert.That(root.Children.Count, Is.EqualTo(3));
            Assert.That(root.Children[0], Is.Null);
            Assert.That(root.Children[1], Is.SameAs(child));
            Assert.That(root.Children[2], Is.Null);
        }

        private static void MutateAndComplete(TestState root, TestState child, ReusingOwner owner)
        {
            root.Child = child;
            child.Level = 2;
            root.Child = null;
            root.Children.Add(child);
            root.Children.RemoveAt(0);
            root.Numbers.Add(1);
            root.Numbers[0] = 2;
            root.Numbers.RemoveAt(0);
            owner.Journal.Commit();
        }
    }
}
