using NUnit.Framework;
using System;
using System.Runtime.CompilerServices;

namespace Bun3.State.Tests
{
    public sealed class DetachedNodeTests
    {
        [Test]
        public void Removal_edit_reinsert_and_clear_restore_original_nodes()
        {
            var a = new TestState { Level = 1 };
            var b = new TestState { Level = 2 };
            var root = new TestState();
            root.Children.Add(a);
            root.Children.Add(b);
            var owner = new TestStateOwner(root);
            owner.Begin();
            root.Children.RemoveAt(0);
            var changed = owner.ChangedCalls;
            a.Level = 7;
            Assert.That(owner.ChangedCalls, Is.EqualTo(changed));
            root.Children[0] = new TestState { Level = 3 };
            root.Children.Insert(0, a);
            root.Children.Clear();
            changed = owner.ChangedCalls;
            owner.Rollback();
            Assert.That(root.Children.Count, Is.EqualTo(2));
            Assert.That(root.Children[0], Is.SameAs(a));
            Assert.That(root.Children[1], Is.SameAs(b));
            Assert.That(a.Level, Is.EqualTo(1));
            Assert.That(b.Level, Is.EqualTo(2));
            Assert.That(owner.ChangedCalls, Is.EqualTo(changed));
        }

        [Test]
        public void Newly_added_node_is_restored_and_released_on_rollback()
        {
            var root = new TestState();
            var c = new TestState { Level = 3 };
            var owner = new TestStateOwner(root);
            owner.Begin();
            root.Children.Add(c);
            c.Level = 9;
            owner.Rollback();
            Assert.That(root.Children, Is.Empty);
            Assert.That(c.Level, Is.EqualTo(3));
            var prepares = owner.PrepareCalls;
            c.Level = 4;
            Assert.That(owner.PrepareCalls, Is.EqualTo(prepares));
            Assert.DoesNotThrow(() => new TestStateOwner(c));
        }

        [Test]
        public void Detached_subtree_keeps_journal_until_commit_then_accepts_another_owner()
        {
            var child = new TestState { Child = new TestState() };
            var root = new TestState { Child = child };
            var owner = new TestStateOwner(root);
            var other = new TestState();
            var otherOwner = new TestStateOwner(other);
            owner.Begin();
            root.Child = null;
            child.Child!.Level = 7;
            Assert.Throws<InvalidOperationException>(() => other.Child = child);
            Assert.That(otherOwner.PrepareCalls, Is.Zero);
            owner.Commit();
            var prepares = owner.PrepareCalls;
            child.Child.Level = 8;
            Assert.That(owner.PrepareCalls, Is.EqualTo(prepares));
            other.Child = child;
            child.Child.Level = 9;
            Assert.That(otherOwner.ChangedCalls, Is.EqualTo(2));
        }

        [Test]
        public void Root_replacement_and_reinsertion_restore_identity_and_detached_edits()
        {
            var root = new TestState();
            var replacement = new TestState { Level = 2 };
            var owner = new TestStateOwner(root);
            var journal = new StateJournal();
            owner.Binding.ReplaceRoot(replacement, journal);
            owner.Binding.ReplaceRoot(root, journal);
            owner.Binding.DetachRoot(journal);
            journal.Rollback();
            Assert.That(owner.Binding.Root, Is.SameAs(root));
            Assert.That(owner.PrepareCalls, Is.Zero);
            Assert.That(owner.ChangedCalls, Is.Zero);
            Assert.DoesNotThrow(() => new TestStateOwner(replacement));
        }

        [Test]
        public void Removed_subtree_can_be_edited_and_receive_new_children_before_rollback()
        {
            var a = new TestState { Child = new TestState { Level = 2 } };
            var root = new TestState { Child = a };
            var owner = new TestStateOwner(root);
            var b = new TestState { Level = 3 };
            owner.Begin();
            root.Child = null;
            a.Child!.Level = 7;
            a.Children.Add(b);
            b.Level = 8;
            Assert.That(owner.ChangedCalls, Is.EqualTo(1));
            owner.Rollback();
            Assert.That(root.Child, Is.SameAs(a));
            Assert.That(a.Child.Level, Is.EqualTo(2));
            Assert.That(a.Children, Is.Empty);
            Assert.That(b.Level, Is.EqualTo(3));
            Assert.DoesNotThrow(() => new TestStateOwner(b));
        }

        [Test]
        public void Detached_root_edits_share_the_active_journal_without_notifications()
        {
            var root = new TestState { Child = new TestState() };
            var owner = new TestStateOwner(root);
            owner.Begin();
            var journal = owner.BeforeChange();
            owner.Binding.DetachRoot(journal);
            root.Level = 7;
            root.Child!.Level = 8;
            Assert.That(owner.ChangedCalls, Is.Zero);
            owner.Rollback();
            Assert.That(owner.Binding.Root, Is.SameAs(root));
            Assert.That(root.Level, Is.EqualTo(1));
            Assert.That(root.Child.Level, Is.EqualTo(1));
        }

        [Test]
        public void Failed_restore_still_releases_detached_nodes_and_record_kinds_start_new_cycles()
        {
            var root = new TestState();
            var owner = new ReusingOwner(root);
            var replacement = new TestState();
            owner.Binding.ReplaceRoot(replacement, owner.Journal);
            owner.Journal.RecordValue(new ThrowingTarget(), 0, 1);
            Assert.Throws<InvalidOperationException>(() => owner.Journal.Rollback());
            Assert.That(owner.Binding.Root, Is.SameAs(root));
            Assert.DoesNotThrow(() => new TestStateOwner(replacement));
            Assert.Throws<InvalidOperationException>(() => owner.Journal.Commit());

            owner.Binding.DetachRoot(owner.Journal);
            owner.Journal.Rollback();
            Assert.That(owner.Binding.Root, Is.SameAs(root));

            owner.Journal.RecordValue(new ThrowingTarget(), 0, 1);
            Assert.Throws<InvalidOperationException>(() => owner.Journal.Rollback());
            root.Numbers.Add(5);
            owner.Journal.Rollback();
            Assert.That(root.Numbers, Is.Empty);

            root.Child = new TestState();
            owner.Journal.Rollback();
            Assert.That(root.Child, Is.Null);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Completed_journal_and_validators_do_not_retain_detached_nodes(bool rollback)
        {
            var root = new TestState();
            var owner = new ReusingOwner(root);
            var retained = AddEditAndRemove(root);
            if (rollback) owner.Journal.Rollback();
            else owner.Journal.Commit();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(retained.IsAlive, Is.False);
            GC.KeepAlive(owner);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference AddEditAndRemove(TestState root)
        {
            var child = new TestState { Child = new TestState() };
            root.Child = child;
            child.Child!.Level = 4;
            root.Child = null;
            root.Children.Add(child);
            root.Children.RemoveAt(0);
            return new WeakReference(child);
        }

        private sealed class ThrowingTarget : IStateValueTarget<int>
        {
            public void RestoreValue(int slot, int value) => throw new InvalidOperationException("Expected restore failure.");
        }
    }

    internal sealed class ReusingOwner : IStateOwner
    {
        internal ReusingOwner(ITrackedState root)
        {
            Binding = new StateBinding(this);
            Binding.AttachRoot(root);
        }

        internal StateJournal Journal { get; } = new StateJournal();
        internal StateBinding Binding { get; }
        internal bool Active { get; set; } = true;
        public StateJournal? BeforeChange() => Active ? Journal : null;
        public void OnChanged() { }
    }
}
