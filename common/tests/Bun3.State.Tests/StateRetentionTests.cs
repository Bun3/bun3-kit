using NUnit.Framework;
using System;
using System.Runtime.CompilerServices;

namespace Bun3.State.Tests
{
    public sealed class StateRetentionTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void Completed_journal_does_not_retain_removed_or_rolled_back_nodes(bool rollback)
        {
            var (reference, owner) = MakeDiscardedNode(rollback);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(reference.IsAlive, Is.False);
            GC.KeepAlive(owner);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Completed_journal_releases_removed_or_added_nodes_and_their_owner(bool rollback)
        {
            var (ownerReference, nodeReference, externalNode, journal) = CreateCompletedCycle(rollback);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(ownerReference.IsAlive, Is.False, "ended owner");
            Assert.That(nodeReference.IsAlive, Is.True, "external node remains available");
            Assert.That(externalNode.Level, Is.EqualTo(2));
            GC.KeepAlive(journal);
            GC.KeepAlive(externalNode);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (WeakReference, WeakReference, TestState, StateJournal) CreateCompletedCycle(bool rollback)
        {
            var root = new TestState();
            var node = new TestState { Level = 2 };
            var owner = new ReusingOwner(root);
            if (rollback)
            {
                root.Child = node;
                owner.Journal.Rollback();
            }
            else
            {
                root.Child = node;
                owner.Journal.Commit();
                root.Child = null;
                owner.Journal.Commit();
            }

            return (new WeakReference(owner), new WeakReference(node), node, owner.Journal);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (WeakReference, ReusingOwner) MakeDiscardedNode(bool rollback)
        {
            var root = new TestState();
            var owner = new ReusingOwner(root);
            var node = new TestState();
            if (rollback)
            {
                root.Child = node;
                owner.Journal.Rollback();
            }
            else
            {
                root.Child = node;
                owner.Journal.Commit();
                root.Child = null;
                owner.Journal.Commit();
            }

            return (new WeakReference(node), owner);
        }
    }
}
