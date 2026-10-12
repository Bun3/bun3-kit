#nullable enable
using Bun3.Gameplay.Abilities;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AbilitySetTests
{
    [Test]
    public void Grants_are_enumerable_in_canonical_order_and_version_tracks_changes()
    {
        var set = new AbilitySet();
        var v0 = set.Version;

        set.Grant(2, GrantSource.Archetype(), level: 1);
        set.Grant(1, GrantSource.Archetype(), level: 3);
        Assert.That(set.Version, Is.EqualTo(v0 + 2));
        Assert.That(set.GetGrant(0).AbilityId, Is.EqualTo(1));
        Assert.That(set.GetGrant(0).Level, Is.EqualTo(3));
        Assert.That(set.GetGrant(1).AbilityId, Is.EqualTo(2));
        Assert.That(set.GetGrant(1).Source, Is.EqualTo(GrantSource.Archetype()));

        var beforeNoop = set.Version;
        set.Revoke(GrantSource.Effect(999));
        Assert.That(set.Version, Is.EqualTo(beforeNoop), "a revoke that removes nothing is not a change");

        set.RestoreSnapshot(set.CreateSnapshot());
        Assert.That(set.Version, Is.GreaterThan(beforeNoop));
    }

    [Test]
    public void Grant_then_revoke_by_the_same_source_removes_the_ability()
    {
        var set = new AbilitySet();
        set.Grant(abilityId: 7, GrantSource.Archetype(), level: 1);

        Assert.That(set.IsGranted(7), Is.True);
        Assert.That(set.Revoke(GrantSource.Archetype()), Is.EqualTo(1));
        Assert.That(set.IsGranted(7), Is.False);
    }

    [Test]
    public void Revoking_one_of_two_sources_keeps_the_ability()
    {
        var set = new AbilitySet();
        set.Grant(7, GrantSource.Archetype(), level: 1);
        set.Grant(7, GrantSource.Effect(instanceId: 42), level: 1);

        set.Revoke(GrantSource.Effect(instanceId: 42));

        Assert.That(set.IsGranted(7), Is.True);
        Assert.That(set.GrantCount, Is.EqualTo(1));
    }

    [Test]
    public void Level_is_the_maximum_across_granting_sources()
    {
        var set = new AbilitySet();
        set.Grant(7, GrantSource.Archetype(), level: 1);
        set.Grant(7, GrantSource.Item(itemId: 9), level: 5);
        set.Grant(7, GrantSource.Code(), level: 3);

        Assert.That(set.GetLevel(7), Is.EqualTo(5));

        set.Revoke(GrantSource.Item(itemId: 9));

        Assert.That(set.GetLevel(7), Is.EqualTo(3));
    }

    [Test]
    public void Regranting_the_same_level_is_not_a_change_and_keeps_archetype_grants_shared()
    {
        var warmUp = new AbilitySet(AbilitySet.ForArchetype(new[] { 7 }, new[] { 2 }));
        warmUp.Grant(7, GrantSource.Archetype(), level: 2);
        var set = new AbilitySet(AbilitySet.ForArchetype(new[] { 7 }, new[] { 2 }));
        var version = set.Version;

        var before = System.GC.GetAllocatedBytesForCurrentThread();
        set.Grant(7, GrantSource.Archetype(), level: 2);
        var allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(set.Version, Is.EqualTo(version));
        Assert.That(allocated, Is.Zero, "a no-op regrant must not copy the archetype's shared grants");
        Assert.That(set.GetLevel(7), Is.EqualTo(2));
    }

    [Test]
    public void Regranting_the_same_source_updates_the_level_without_duplicating()
    {
        var set = new AbilitySet();
        set.Grant(7, GrantSource.Archetype(), level: 1);
        set.Grant(7, GrantSource.Archetype(), level: 4);

        Assert.That(set.GrantCount, Is.EqualTo(1));
        Assert.That(set.GetLevel(7), Is.EqualTo(4));
    }

    [Test]
    public void Ungranted_ability_reports_level_zero()
    {
        var set = new AbilitySet();

        Assert.That(set.IsGranted(7), Is.False);
        Assert.That(set.GetLevel(7), Is.Zero);
    }

    [Test]
    public void Grants_are_kept_in_ascending_ability_then_source_order()
    {
        var set = new AbilitySet();
        set.Grant(9, GrantSource.Code(key: 2), level: 1);
        set.Grant(3, GrantSource.Item(itemId: 5), level: 1);
        set.Grant(9, GrantSource.Archetype(), level: 1);

        var snapshot = set.CreateSnapshot();
        var restored = new AbilitySet();
        restored.RestoreSnapshot(snapshot);

        Assert.That(restored.GrantCount, Is.EqualTo(3));
        Assert.That(restored.CreateSnapshot().DebugOrder(), Is.EqualTo(snapshot.DebugOrder()));
        Assert.That(snapshot.DebugOrder(), Is.EqualTo("3/Item:5 9/Archetype:0 9/Code:2"));
    }

    [Test]
    public void Restore_replaces_all_previous_grants()
    {
        var set = new AbilitySet();
        set.Grant(1, GrantSource.Archetype(), level: 1);
        var empty = new AbilitySet().CreateSnapshot();

        set.RestoreSnapshot(empty);

        Assert.That(set.GrantCount, Is.Zero);
        Assert.That(set.IsGranted(1), Is.False);
    }
}
