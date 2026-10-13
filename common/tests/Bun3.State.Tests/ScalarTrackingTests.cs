using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Bun3.State.Tests
{
    public sealed class ScalarTrackingTests
    {
        [Test]
        public void Repeated_writes_restore_original_value_without_notifying()
        {
            var state = new TestState();
            var owner = new TestStateOwner(state);

            owner.Begin();
            state.Level = 5;
            state.Level = 9;
            owner.Rollback();

            Assert.That(state.Level, Is.EqualTo(1));
            Assert.That(owner.ChangedCalls, Is.EqualTo(2));
            Assert.That(owner.PrepareCalls, Is.EqualTo(2));
            Assert.That(owner.JournalCreations, Is.EqualTo(1));
        }

        [Test]
        public void Commit_keeps_the_last_value_and_old_records_do_not_replay()
        {
            var state = new TestState();
            var owner = new TestStateOwner(state);

            owner.Begin();
            state.Level = 5;
            state.Level = 9;
            owner.Commit();
            owner.Begin();
            state.Level = 12;
            owner.Rollback();

            Assert.That(state.Level, Is.EqualTo(9));
            Assert.That(owner.JournalCreations, Is.EqualTo(2));
        }

        [Test]
        public void Returning_to_start_value_is_still_recorded_in_order()
        {
            var state = new TestState();
            var owner = new TestStateOwner(state);

            owner.Begin();
            state.Level = 5;
            state.Level = 1;
            owner.Rollback();

            Assert.That(state.Level, Is.EqualTo(1));
            Assert.That(owner.ChangedCalls, Is.EqualTo(2));
        }

        [Test]
        public void Returning_to_start_value_restores_both_prior_values_in_reverse_order()
        {
            var target = new RecordingTarget();
            var journal = new StateJournal();
            journal.RecordValue((IStateValueTarget<int>)target, 0, 1);
            journal.RecordValue((IStateValueTarget<int>)target, 0, 5);

            journal.Rollback();

            Assert.That(target.Restores, Is.EqualTo(new[] { "int:5", "int:1" }));
        }

        [Test]
        public void Assigning_same_value_skips_owner_and_journal()
        {
            var state = new TestState();
            var owner = new TestStateOwner(state);

            owner.Begin();
            state.Level = 1;
            state.Optional = null;
            state.Status = TestStatus.Pending;
            state.Name = null;
            owner.Commit();

            Assert.That(owner.PrepareCalls, Is.Zero);
            Assert.That(owner.ChangedCalls, Is.Zero);
            Assert.That(owner.JournalCreations, Is.Zero);
        }

        [Test]
        public void Unattached_change_and_empty_transaction_do_not_create_a_journal()
        {
            var state = new TestState();
            state.Level = 5;
            var owner = new TestStateOwner(state);

            owner.Begin();
            owner.Commit();
            state.Level = 9;

            Assert.That(state.Level, Is.EqualTo(9));
            Assert.That(owner.JournalCreations, Is.Zero);
            Assert.That(owner.PrepareCalls, Is.EqualTo(1));
            Assert.That(owner.ChangedCalls, Is.EqualTo(1));
        }

        [Test]
        public void Nullable_enum_and_string_changes_restore_in_reverse_order()
        {
            var state = new TestState();
            var owner = new TestStateOwner(state);

            owner.Begin();
            state.Optional = 0;
            state.Status = TestStatus.Active;
            state.Name = "first";
            state.Optional = 4;
            state.Status = TestStatus.Complete;
            state.Name = "last";
            owner.Rollback();

            Assert.That(state.Optional, Is.Null);
            Assert.That(state.Status, Is.EqualTo(TestStatus.Pending));
            Assert.That(state.Name, Is.Null);
            Assert.That(owner.ChangedCalls, Is.EqualTo(6));
            Assert.That(owner.JournalCreations, Is.EqualTo(1));
        }

        [Test]
        public void Alternating_transactions_do_not_replay_previous_changes()
        {
            var state = new TestState();
            var owner = new TestStateOwner(state);

            owner.Begin();
            state.Level = 5;
            owner.Rollback();
            owner.Begin();
            state.Level = 9;
            owner.Commit();
            owner.Begin();
            state.Level = 12;
            owner.Rollback();

            Assert.That(state.Level, Is.EqualTo(9));
            Assert.That(owner.ChangedCalls, Is.EqualTo(3));
            Assert.That(owner.JournalCreations, Is.EqualTo(3));
        }

        [Test]
        public void Completing_one_owner_does_not_affect_another()
        {
            var first = new TestState();
            var firstOwner = new TestStateOwner(first);
            var second = new TestState();
            var secondOwner = new TestStateOwner(second);

            firstOwner.Begin();
            secondOwner.Begin();
            first.Level = 5;
            second.Level = 9;
            firstOwner.Commit();
            secondOwner.Rollback();

            Assert.That(first.Level, Is.EqualTo(5));
            Assert.That(second.Level, Is.EqualTo(1));
            Assert.That(firstOwner.JournalCreations, Is.EqualTo(1));
            Assert.That(secondOwner.JournalCreations, Is.EqualTo(1));
        }

        [Test]
        public void Repeated_completion_of_a_journal_has_no_side_effect()
        {
            var state = new TestState();
            var journal = new StateJournal();
            journal.RecordValue((IStateValueTarget<int>)state, 0, 1);
            state.Level = 5;

            journal.Rollback();
            journal.Rollback();
            journal.Commit();

            Assert.That(state.Level, Is.EqualTo(1));
        }

        [Test]
        public void Journal_restores_interleaved_types_in_global_reverse_order()
        {
            var target = new RecordingTarget();
            var journal = new StateJournal();
            journal.RecordValue((IStateValueTarget<int>)target, 0, 1);
            journal.RecordValue((IStateValueTarget<string>)target, 1, "first");
            journal.RecordValue((IStateValueTarget<int>)target, 0, 5);

            journal.Rollback();

            Assert.That(target.Restores, Is.EqualTo(new[] { "int:5", "string:first", "int:1" }));
        }

        [Test]
        public void Failed_restore_attempts_remaining_entries_and_fresh_cycle_reuses_journal()
        {
            var target = new ThrowingTarget(2);
            var journal = new StateJournal();
            journal.RecordValue((IStateValueTarget<int>)target, 0, 1);
            journal.RecordValue((IStateValueTarget<int>)target, 0, 2);
            journal.RecordValue((IStateValueTarget<int>)target, 0, 3);

            var failure = Assert.Throws<InvalidOperationException>(() => journal.Rollback());
            Assert.That(failure!.Message, Is.EqualTo("restore 2 failed"));
            Assert.That(target.Restores, Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(Assert.Throws<InvalidOperationException>(() => journal.Rollback())!.Message,
                Does.Contain("previous state completion failed"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => journal.Commit())!.Message,
                Does.Contain("previous state completion failed"));
            Assert.That(target.Restores, Is.EqualTo(new[] { 3, 2, 1 }));

            journal.RecordValue((IStateValueTarget<int>)target, 0, 4);
            journal.Rollback();

            Assert.That(target.Restores, Is.EqualTo(new[] { 3, 2, 1, 4 }));
        }

        [Test]
        public void Multiple_restore_failures_are_reported_together_after_all_attempts()
        {
            var target = new ThrowingTarget(1, 3);
            var journal = new StateJournal();
            journal.RecordValue((IStateValueTarget<int>)target, 0, 1);
            journal.RecordValue((IStateValueTarget<int>)target, 0, 2);
            journal.RecordValue((IStateValueTarget<int>)target, 0, 3);

            var failure = Assert.Throws<AggregateException>(() => journal.Rollback());

            Assert.That(target.Restores, Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(failure!.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions[0].Message, Is.EqualTo("restore 3 failed"));
            Assert.That(failure.InnerExceptions[1].Message, Is.EqualTo("restore 1 failed"));
        }

        private sealed class RecordingTarget : IStateValueTarget<int>, IStateValueTarget<string>
        {
            internal List<string> Restores { get; } = new List<string>();

            void IStateValueTarget<int>.RestoreValue(int slot, int value)
            {
                Restores.Add($"int:{value}");
            }

            void IStateValueTarget<string>.RestoreValue(int slot, string value)
            {
                Restores.Add($"string:{value}");
            }
        }

        private sealed class ThrowingTarget : IStateValueTarget<int>
        {
            private readonly HashSet<int> _throwOn;

            internal ThrowingTarget(params int[] throwOn)
            {
                _throwOn = new HashSet<int>(throwOn);
            }

            internal List<int> Restores { get; } = new List<int>();

            public void RestoreValue(int slot, int value)
            {
                Restores.Add(value);
                if (_throwOn.Contains(value))
                {
                    throw new InvalidOperationException($"restore {value} failed");
                }
            }
        }
    }
}
