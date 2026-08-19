using Bun3.Server.Core;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class TransactionTests
{
    private sealed class Participant : ITransactionParticipant
    {
        private readonly List<string> _trace;
        private readonly string _name;

        public Participant(List<string> trace, string name)
        {
            _trace = trace;
            _name = name;
        }

        public void OnCommit() => _trace.Add($"commit:{_name}");

        public void OnRollback() => _trace.Add($"rollback:{_name}");
    }

    [Test]
    public void Commit_notifies_participants_in_enlist_order()
    {
        var trace = new List<string>();
        var transaction = new Transaction();
        var a = new Participant(trace, "a");
        var b = new Participant(trace, "b");

        using (var scope = transaction.Begin())
        {
            transaction.Enlist(a);
            transaction.Enlist(b);
            scope.Commit();
        }

        Assert.That(trace, Is.EqualTo(new[] { "commit:a", "commit:b" }));
        Assert.That(transaction.IsActive, Is.False);
    }

    [Test]
    public void Dispose_without_commit_rolls_back()
    {
        var trace = new List<string>();
        var transaction = new Transaction();

        using (transaction.Begin())
        {
            transaction.Enlist(new Participant(trace, "a"));
        }

        Assert.That(trace, Is.EqualTo(new[] { "rollback:a" }));
        Assert.That(transaction.IsActive, Is.False);
    }

    [Test]
    public void Enlist_is_idempotent_per_transaction()
    {
        var trace = new List<string>();
        var transaction = new Transaction();
        var a = new Participant(trace, "a");

        using (var scope = transaction.Begin())
        {
            transaction.Enlist(a);
            transaction.Enlist(a);
            scope.Commit();
        }

        Assert.That(trace, Is.EqualTo(new[] { "commit:a" }));
    }

    [Test]
    public void Nested_begin_throws()
    {
        var transaction = new Transaction();

        using (transaction.Begin())
        {
            Assert.That(() => transaction.Begin(), Throws.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    public void Enlist_without_active_transaction_throws()
    {
        var transaction = new Transaction();

        Assert.That(
            () => transaction.Enlist(new Participant(new List<string>(), "a")),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Transaction_is_inactive_while_notifying_so_hooks_may_mutate()
    {
        var transaction = new Transaction();
        var observed = true;
        var participant = new DelegateParticipant(() => observed = transaction.IsActive, () => { });

        using (var scope = transaction.Begin())
        {
            transaction.Enlist(participant);
            scope.Commit();
        }

        Assert.That(observed, Is.False);
    }

    [Test]
    public void Empty_transaction_commits_cleanly()
    {
        var transaction = new Transaction();

        using (var scope = transaction.Begin())
        {
            scope.Commit();
        }

        Assert.That(transaction.IsActive, Is.False);
    }

    [Test]
    public void Reusing_a_stale_scope_after_commit_is_a_no_op()
    {
        var trace = new List<string>();
        var transaction = new Transaction();
        var scope = transaction.Begin();
        transaction.Enlist(new Participant(trace, "a"));
        scope.Commit();

        scope.Dispose();   // already committed — must not roll back

        Assert.That(trace, Is.EqualTo(new[] { "commit:a" }));
    }

    private sealed class DelegateParticipant : ITransactionParticipant
    {
        private readonly Action _commit;
        private readonly Action _rollback;

        public DelegateParticipant(Action commit, Action rollback)
        {
            _commit = commit;
            _rollback = rollback;
        }

        public void OnCommit() => _commit();

        public void OnRollback() => _rollback();
    }
}
