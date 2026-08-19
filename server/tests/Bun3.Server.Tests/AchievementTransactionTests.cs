using Bun3.Server.Achievements;
using Bun3.Server.Core;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class AchievementTransactionTests
{
    private AchievementCatalog<AchievementDefinition> _catalog = null!;
    private AchievementId _kill;
    private AchievementId _daily;
    private Transaction _transaction = null!;
    private AchievementManager<AchievementDefinition> _achievements = null!;
    private int _dirty;
    private readonly List<string> _completed = new();

    [SetUp]
    public void SetUp()
    {
        _catalog = new AchievementCatalog<AchievementDefinition>(new[]
        {
            new AchievementDefinition("kill", 10),
            new AchievementDefinition("daily", 5, repeatable: true),
        });
        _kill = _catalog.GetRequired("kill");
        _daily = _catalog.GetRequired("daily");

        _dirty = 0;
        _completed.Clear();
        _transaction = new Transaction();
        _achievements = new AchievementManager<AchievementDefinition>(
            _catalog,
            onDirty: () => _dirty++,
            transaction: _transaction)
        {
            OnCompleted = (id, def, count) => _completed.Add($"{def.Id}:{count}"),
        };
    }

    [Test]
    public void Rollback_restores_progress_and_completion_counters()
    {
        _achievements.Increase(_kill, 4);

        using (_transaction.Begin())
        {
            Assert.That(_achievements.Increase(_kill, 6), Is.EqualTo(1));
            Assert.That(_achievements.GetStatus(_kill), Is.EqualTo(AchievementStatus.Completed));
        }

        Assert.That(_achievements.GetState(_kill).Progress, Is.EqualTo(4));
        Assert.That(_achievements.GetState(_kill).CompletedCount, Is.Zero);
        Assert.That(_achievements.GetStatus(_kill), Is.EqualTo(AchievementStatus.Active));
    }

    [Test]
    public void Rollback_restores_a_claim()
    {
        _achievements.Increase(_daily, 5);
        var before = _achievements.GetState(_daily);

        using (_transaction.Begin())
        {
            Assert.That(_achievements.TryClaim(_daily), Is.True);
        }

        var after = _achievements.GetState(_daily);
        Assert.That(after.ClaimedCount, Is.EqualTo(before.ClaimedCount));
        Assert.That(after.Progress, Is.EqualTo(before.Progress));
        Assert.That(_achievements.GetClaimableCount(_daily), Is.EqualTo(1));
    }

    [Test]
    public void Rollback_restores_availability_transitions()
    {
        using (_transaction.Begin())
        {
            Assert.That(_achievements.Lock(_kill), Is.True);
        }

        Assert.That(_achievements.GetStatus(_kill), Is.EqualTo(AchievementStatus.Active));
    }

    [Test]
    public void Commit_keeps_changes_and_fires_hooks_once()
    {
        using (var scope = _transaction.Begin())
        {
            _achievements.Increase(_kill, 10);
            Assert.That(_dirty, Is.Zero, "onDirty must be buffered");
            Assert.That(_completed, Is.Empty, "OnCompleted must be buffered");
            scope.Commit();
        }

        Assert.That(_dirty, Is.EqualTo(1));
        Assert.That(_completed, Is.EqualTo(new[] { "kill:1" }));
        Assert.That(_achievements.GetState(_kill).CompletedCount, Is.EqualTo(1));
    }

    [Test]
    public void Rollback_drops_buffered_hooks()
    {
        using (_transaction.Begin())
        {
            _achievements.Increase(_kill, 10);
        }

        Assert.That(_dirty, Is.Zero);
        Assert.That(_completed, Is.Empty);
    }

    [Test]
    public void Outside_a_transaction_hooks_stay_immediate()
    {
        _achievements.Increase(_kill, 10);

        Assert.That(_dirty, Is.EqualTo(1));
        Assert.That(_completed, Is.EqualTo(new[] { "kill:1" }));
    }

    [Test]
    public void Multiple_achievements_roll_back_together()
    {
        using (_transaction.Begin())
        {
            _achievements.Increase(_kill, 3);
            _achievements.Increase(_daily, 2);
        }

        Assert.That(_achievements.GetState(_kill).Progress, Is.Zero);
        Assert.That(_achievements.GetState(_daily).Progress, Is.Zero);
    }
}
