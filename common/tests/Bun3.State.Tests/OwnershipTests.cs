using NUnit.Framework;
using System;

namespace Bun3.State.Tests
{
    public sealed class OwnershipTests
    {
        [Test]
        public void Duplicate_positions_and_cycles_fail_before_preparing_owner()
        {
            var child = new TestState();
            var root = new TestState { Child = child };
            var owner = new TestStateOwner(root);
            owner.Begin();
            Assert.Throws<InvalidOperationException>(() => root.Other = child);
            Assert.Throws<InvalidOperationException>(() => root.Children.Add(child));
            Assert.Throws<InvalidOperationException>(() => root.Other = root);
            Assert.Throws<InvalidOperationException>(() => child.Child = root);
            Assert.Throws<InvalidOperationException>(() => new TestStateOwner(child));
            Assert.That(root.Child, Is.SameAs(child));
            Assert.That(root.Other, Is.Null);
            Assert.That(root.Children, Is.Empty);
            Assert.That(owner.PrepareCalls, Is.Zero);
            Assert.That(owner.ChangedCalls, Is.Zero);
        }

        [Test]
        public void Invalid_preinitialized_graph_does_not_bind_earlier_siblings()
        {
            var first = new TestState();
            var shared = new TestState();
            var invalid = new TestState();
            invalid.InitializeChildren(shared, shared);
            var root = new TestState();
            root.InitializeChildren(first, invalid);
            Assert.Throws<InvalidOperationException>(() => new TestStateOwner(root));
            var owner = new TestStateOwner(first);
            first.Level = 2;
            Assert.That(owner.ChangedCalls, Is.EqualTo(1));
            Assert.DoesNotThrow(() => new TestStateOwner(shared));
        }

        [Test]
        public void Root_validation_is_side_effect_free_and_defensive_replacement_rejects_shared_graph()
        {
            var root = new TestState();
            var owner = new TestStateOwner(root);
            var candidate = new TestState();
            owner.Binding.ValidateRoot(candidate);
            Assert.DoesNotThrow(() => new TestStateOwner(candidate));
            Assert.Throws<InvalidOperationException>(() => owner.Binding.ReplaceRoot(candidate, null));
            Assert.That(owner.Binding.Root, Is.SameAs(root));
            Assert.That(owner.PrepareCalls, Is.Zero);
            Assert.That(owner.ChangedCalls, Is.Zero);
        }

        [Test]
        public void Unbound_ownership_and_cycles_are_enforced_and_scalar_writes_do_not_visit_graph()
        {
            var child = new TestState();
            var root = new TestState { Child = child };
            Assert.Throws<InvalidOperationException>(() => root.Other = child);
            Assert.Throws<InvalidOperationException>(() => child.Child = root);
            var owner = new TestStateOwner(root);
            var visits = root.Visits + child.Visits;
            owner.Begin();
            child.Level = 2;
            root.Level = 3;
            owner.Rollback();
            Assert.That(root.Visits + child.Visits, Is.EqualTo(visits));
        }

        [Test]
        public void Candidate_graph_cannot_embed_the_current_root_as_an_initialized_child()
        {
            var root = new TestState();
            var owner = new TestStateOwner(root);
            var candidate = new TestState();
            candidate.InitializeChildren(root, null);
            Assert.Throws<InvalidOperationException>(() => owner.Binding.ValidateRoot(candidate));
            Assert.Throws<InvalidOperationException>(() => owner.Binding.ReplaceRoot(candidate, null));
            Assert.That(owner.Binding.Root, Is.SameAs(root));
            Assert.That(owner.PrepareCalls, Is.Zero);
            Assert.That(owner.ChangedCalls, Is.Zero);
        }

        [Test]
        public void Rejected_child_graph_leaves_valid_siblings_free_and_owner_unchanged()
        {
            var root = new TestState();
            var owner = new TestStateOwner(root);
            var valid = new TestState();
            var invalid = new TestState();
            invalid.InitializeChildren(valid, valid);
            owner.Begin();
            Assert.Throws<InvalidOperationException>(() => root.Child = invalid);
            Assert.Throws<InvalidOperationException>(() => root.Children.Add(invalid));
            Assert.That(root.Child, Is.Null);
            Assert.That(root.Children, Is.Empty);
            Assert.That(owner.PrepareCalls, Is.Zero);
            Assert.That(owner.ChangedCalls, Is.Zero);
            Assert.DoesNotThrow(() => new TestStateOwner(valid));
        }

        [Test]
        public void Unbound_removed_child_can_move_to_another_parent()
        {
            var child = new TestState();
            var first = new TestState { Child = child };
            var second = new TestState();
            first.Child = null;
            second.Children.Add(child);
            Assert.Throws<InvalidOperationException>(() => first.Child = child);
            Assert.That(second.Children[0], Is.SameAs(child));
        }

        [Test]
        public void Replacing_an_invalid_initialized_child_does_not_detach_another_tree()
        {
            var child = new TestState();
            var owned = new TestState { Child = child };
            var owner = new TestStateOwner(owned);
            var invalid = new TestState();
            invalid.InitializeChildren(child, null);
            Assert.Throws<InvalidOperationException>(() => invalid.Child = null);
            child.Level = 2;
            Assert.That(owned.Child, Is.SameAs(child));
            Assert.That(owner.ChangedCalls, Is.EqualTo(1));
        }

        [Test]
        public void Public_initialization_establishes_unbound_initialized_field_ownership()
        {
            var child = new TestState();
            var root = new TestState();
            root.InitializeChildren(child, null);

            ((ITrackedNode)root).Tracking.InitializeChildren();

            Assert.Throws<InvalidOperationException>(() => root.Other = child);
            Assert.Throws<InvalidOperationException>(() => root.Children.Add(child));
            Assert.Throws<InvalidOperationException>(() => new TestStateOwner(child));
            Assert.That(root.Child, Is.SameAs(child));
            Assert.That(root.Other, Is.Null);
            child.Level = 3;
            Assert.That(root.Child!.Level, Is.EqualTo(3));
            var owner = new TestStateOwner(root);
            Assert.That(owner.PrepareCalls, Is.Zero);
            Assert.That(owner.ChangedCalls, Is.Zero);
        }

        [Test]
        public void Public_initialization_owns_readonly_lists_without_a_binding()
        {
            var child = new TestState();
            var list = new TrackedList<TestState?> { child };
            var root = new InitializedListState(list);
            var alias = new InitializedListState(list);
            var tracking = ((ITrackedNode)root).Tracking;
            Assert.That(root.Visits, Is.Zero, "Metadata access must not initialize the graph.");

            tracking.InitializeChildren();

            Assert.That(root.Values, Is.SameAs(list));
            Assert.That(root.Values[0], Is.SameAs(child));
            Assert.Throws<InvalidOperationException>(() => ((ITrackedNode)alias).Tracking.InitializeChildren());
            var owner = new ReusingOwner(root);
            Assert.Throws<InvalidOperationException>(() => owner.Binding.ValidateRoot(alias));
        }

        [Test]
        public void Public_initialization_marks_all_nodes_and_repeated_calls_do_not_visit()
        {
            var child = new TestState();
            var root = new TestState();
            root.InitializeChildren(child, null);
            root.Tracking.InitializeChildren();
            var visits = root.Visits + child.Visits;

            for (var i = 0; i < 10; i++)
            {
                root.Tracking.InitializeChildren();
                child.Tracking.InitializeChildren();
                child.Level = i;
            }

            Assert.That(root.Visits + child.Visits, Is.EqualTo(visits));
            Assert.That(child.Level, Is.EqualTo(9));
            Assert.Throws<InvalidOperationException>(() => child.Child = root);
        }

        [Test]
        public void Attachment_marks_initialized_descendants_without_owner_callbacks_on_initialization()
        {
            var child = new TestState();
            var root = new TestState();
            root.InitializeChildren(child, null);
            var owner = new TestStateOwner(root);
            var visits = root.Visits + child.Visits;

            root.Tracking.InitializeChildren();
            child.Tracking.InitializeChildren();
            ((ITrackedNode)root.Children).Tracking.InitializeChildren();

            Assert.That(root.Visits + child.Visits, Is.EqualTo(visits));
            Assert.That(owner.PrepareCalls, Is.Zero);
            Assert.That(owner.ChangedCalls, Is.Zero);
        }

        [Test]
        public void Failed_public_initialization_does_not_link_earlier_siblings_or_their_children()
        {
            var grandchild = new TestState();
            var first = new TestState();
            first.InitializeChildren(grandchild, null);
            var shared = new TestState();
            var invalid = new TestState();
            invalid.InitializeChildren(shared, shared);
            var root = new TestState();
            root.InitializeChildren(first, invalid);

            Assert.Throws<InvalidOperationException>(() => root.Tracking.InitializeChildren());

            var independent = new TestState { Child = grandchild, Other = shared };
            Assert.That(independent.Child, Is.SameAs(grandchild));
            Assert.That(independent.Other, Is.SameAs(shared));
            first.InitializeChildren(null, null);
            Assert.DoesNotThrow(() => new TestStateOwner(first));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Public_initialization_rejects_cycles_and_failed_initialization_can_be_retried(bool ancestorCycle)
        {
            var root = new TestState();
            var child = ancestorCycle ? new TestState() : root;
            root.InitializeChildren(child, null);
            if (ancestorCycle) child.InitializeChildren(root, null);

            Assert.Throws<InvalidOperationException>(() => root.Tracking.InitializeChildren());

            child.InitializeChildren(null, null);
            root.Tracking.InitializeChildren();
            Assert.Throws<InvalidOperationException>(() => root.Other = root);
            if (ancestorCycle)
                Assert.Throws<InvalidOperationException>(() => root.Other = child);
        }

        [Test]
        public void Root_validation_does_not_skip_subsequent_unbound_initialization()
        {
            var owner = new TestStateOwner(new TestState());
            var child = new TestState();
            var candidate = new TestState();
            candidate.InitializeChildren(child, null);
            owner.Binding.ValidateRoot(candidate);
            candidate.Tracking.InitializeChildren();
            Assert.Throws<InvalidOperationException>(() => candidate.Other = child);
            Assert.That(owner.PrepareCalls, Is.Zero);
            Assert.That(owner.ChangedCalls, Is.Zero);
        }

        [Test]
        public void Initializing_a_detached_subtree_keeps_its_journal_and_does_not_reconnect_it()
        {
            var child = new TestState();
            var root = new TestState();
            root.InitializeChildren(child, null);
            var owner = new TestStateOwner(root);
            owner.Begin();
            root.Child = null;
            var visits = child.Visits;
            child.Tracking.InitializeChildren();
            child.Level = 7;
            Assert.That(owner.ChangedCalls, Is.EqualTo(1));
            Assert.That(child.Visits, Is.EqualTo(visits));
            owner.Rollback();
            Assert.That(root.Child, Is.SameAs(child));
            Assert.That(child.Level, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Warmed_initialization_and_scalar_writes_do_not_allocate_or_visit(bool active)
        {
            var child = new TestState();
            var root = new TestState();
            root.InitializeChildren(child, null);
            var owner = new ReusingOwner(root) { Active = active };
            for (var i = 0; i < 100; i++) InitializeAndWrite(root, child, owner, i);
            var visits = root.Visits + child.Visits;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) InitializeAndWrite(root, child, owner, i);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(root.Visits + child.Visits, Is.EqualTo(visits));
        }

        private static void InitializeAndWrite(TestState root, TestState child, ReusingOwner owner, int value)
        {
            root.Tracking.InitializeChildren();
            child.Tracking.InitializeChildren();
            child.Level = value;
            owner.Journal.Commit();
        }

        private sealed class InitializedListState : ITrackedState
        {
            private readonly TrackedList<TestState?> _values;
            private StateNode? _tracking;

            internal InitializedListState(TrackedList<TestState?> values) { _values = values; }
            internal TrackedList<TestState?> Values => _values;
            internal int Visits { get; private set; }

            StateNode ITrackedNode.Tracking => _tracking ??= new StateNode(this);

            void ITrackedNode.VisitChildren(IStateChildVisitor visitor)
            {
                Visits++;
                visitor.Visit(_values);
            }
        }
    }
}
