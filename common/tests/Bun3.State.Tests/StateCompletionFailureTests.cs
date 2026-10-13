using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace Bun3.State.Tests
{
    public sealed class StateCompletionFailureTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void Ownership_failure_attempts_other_releases_and_preserves_restore_errors(bool rollback)
        {
            var journal = new StateJournal();
            var first = DetachFailingNode(journal, "first metadata");
            var second = DetachFailingNode(journal, "second metadata");
            var normal = new TestState();
            var binding = new StateBinding(new Owner(journal));
            binding.ReplaceRoot(normal, journal);
            binding.DetachRoot(journal);
            var target = new RestoreTarget { Failure = new InvalidOperationException("value restore") };
            journal.RecordValue(target, 0, 17);

            var error = Assert.Catch(() => Complete(journal, rollback));
            var errors = error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : new[] { error };
            Assert.That(errors, Does.Contain(first.Failure));
            Assert.That(errors, Does.Contain(second.Failure));
            if (rollback)
            {
                Assert.That(errors, Does.Contain(target.Failure));
                Assert.That(target.Restores, Is.EqualTo(new[] { 17 }));
            }
            else Assert.That(target.Restores, Is.Empty);

            var nextOwner = new Owner(new StateJournal());
            new StateBinding(nextOwner).AttachRoot(normal);
            normal.Level = 5;
            Assert.That(nextOwner.Changes, Is.EqualTo(1));
            nextOwner.Journal.Rollback();
            Assert.That(normal.Level, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Ownership_failure_rejects_repeated_completion_and_fresh_cycle_has_no_stale_inverse(bool rollback)
        {
            var journal = new StateJournal();
            var broken = DetachFailingNode(journal, "metadata");
            var target = new RestoreTarget();
            journal.RecordValue(target, 0, 17);
            Assert.Catch(() => Complete(journal, rollback));
            Assert.Throws<InvalidOperationException>(() => journal.Commit());
            Assert.Throws<InvalidOperationException>(() => journal.Rollback());

            journal.RecordValue(target, 0, 29);
            Assert.DoesNotThrow(() => journal.Rollback());
            Assert.That(target.Restores, Is.EqualTo(rollback ? new[] { 17, 29 } : new[] { 29 }));
            Assert.That(broken.Fail, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Ownership_failure_clears_value_list_and_ownership_references(bool rollback)
        {
            var (references, journal) = CreateFailedCycle(rollback);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            foreach (var reference in references)
                Assert.That(reference.IsAlive, Is.False, "Completed framework buffers must release every retained target, value, node and owner.");
            GC.KeepAlive(journal);
        }

        [Test]
        public void Single_commit_cleanup_error_preserves_original_exception()
        {
            var journal = new StateJournal();
            var broken = DetachFailingNode(journal, "metadata");
            Assert.That(Assert.Throws<InvalidOperationException>(() => journal.Commit()), Is.SameAs(broken.Failure));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (WeakReference[], StateJournal) CreateFailedCycle(bool rollback)
        {
            var journal = new StateJournal();
            var broken = DetachFailingNode(journal, "metadata");
            var owner = new Owner(journal);
            var root = new TestState();
            var binding = new StateBinding(owner);
            binding.AttachRoot(root);
            var child = new TestState();
            root.Children.Add(child);
            root.Children.RemoveAt(0);
            var target = new ReferenceTarget();
            var value = new object();
            journal.RecordValue(target, 0, value);
            Assert.Catch(() => Complete(journal, rollback));
            return (new[] { new WeakReference(broken), new WeakReference(owner), new WeakReference(root),
                new WeakReference(root.Children), new WeakReference(child), new WeakReference(target), new WeakReference(value) }, journal);
        }

        private static FailingState DetachFailingNode(StateJournal journal, string message)
        {
            var node = new FailingState(message);
            var binding = new StateBinding(new Owner(journal));
            binding.AttachRoot(node);
            binding.DetachRoot(journal);
            node.Fail = true;
            return node;
        }

        private static void Complete(StateJournal journal, bool rollback)
        {
            if (rollback) journal.Rollback();
            else journal.Commit();
        }

        private sealed class FailingState : ITrackedState
        {
            private readonly StateNode _tracking;
            internal FailingState(string message)
            {
                _tracking = new StateNode(this);
                Failure = new InvalidOperationException(message);
            }
            internal bool Fail { get; set; }
            internal InvalidOperationException Failure { get; }
            public StateNode Tracking => Fail ? throw Failure : _tracking;
            public void VisitChildren(IStateChildVisitor visitor) { }
        }

        private sealed class Owner : IStateOwner
        {
            internal Owner(StateJournal journal) { Journal = journal; }
            internal StateJournal Journal { get; }
            internal int Changes { get; private set; }
            public StateJournal BeforeChange() => Journal;
            public void OnChanged() => Changes++;
        }

        private sealed class RestoreTarget : IStateValueTarget<int>
        {
            internal Exception? Failure { get; set; }
            internal List<int> Restores { get; } = new List<int>();
            public void RestoreValue(int slot, int value)
            {
                Restores.Add(value);
                if (Failure != null) throw Failure;
            }
        }

        private sealed class ReferenceTarget : IStateValueTarget<object>
        {
            public void RestoreValue(int slot, object value) { }
        }
    }
}
