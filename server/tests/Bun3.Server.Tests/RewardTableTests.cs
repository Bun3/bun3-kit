using Bun3.Gameplay.Numerics;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class RewardTableTests
{
    private sealed class ItemState;

    /// <summary>Deterministic RNG that returns scripted values in order.</summary>
    private sealed class ScriptedRng(params long[] values) : IRandomSource
    {
        private int _cursor;
        public int Calls => _cursor;

        public long Next(long maxExclusive)
        {
            Assert.That(_cursor, Is.LessThan(values.Length), "more random draws than scripted");
            var value = values[_cursor++];
            Assert.That(value, Is.InRange(0, maxExclusive - 1), "scripted value out of range");
            return value;
        }
    }

    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold;
    private ItemId _gem;
    private ItemId _sword;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "Gold")
            .Register("gem", "Gem")
            .Register("sword", "Sword", unstackable: true)
            .Build();
        _gold = _catalog.GetRequired("gold");
        _gem = _catalog.GetRequired("gem");
        _sword = _catalog.GetRequired("sword");
    }

    [Test]
    public void Sample_rolls_guaranteed_group_without_probability_rng()
    {
        var table = new RewardTable(new[]
        {
            new RewardGroup(10000, grantAll: true,
                new RewardEntry(_gold, 1, 100, 200),     // one roll
                new RewardEntry(_gem, 1, 3, 3)),         // fixed — no roll
        });
        var rng = new ScriptedRng(50);                    // 100 + 50 = 150
        var buffer = new List<ItemDelta>();

        table.Sample(rng, buffer);

        Assert.That(buffer, Has.Count.EqualTo(2));
        Assert.That(buffer[0].Item, Is.EqualTo(_gold));
        Assert.That(buffer[0].Amount, Is.EqualTo((BigNum)150));
        Assert.That(buffer[1].Amount, Is.EqualTo((BigNum)3));
        Assert.That(rng.Calls, Is.EqualTo(1), "guaranteed group and fixed amounts draw no randomness");
    }

    [Test]
    public void Sample_weighted_group_triggers_by_permyriad_and_picks_by_weight()
    {
        var table = new RewardTable(new[]
        {
            new RewardGroup(2500, grantAll: false,
                new RewardEntry(_sword, 1, 1, 1),
                new RewardEntry(_gem, 9, 5, 5)),
        });
        var buffer = new List<ItemDelta>();

        // No trigger (roll 2500 >= 2500)
        table.Sample(new ScriptedRng(2500), buffer);
        Assert.That(buffer, Is.Empty);

        // Trigger (roll 0) + weight roll 0 → first entry (sword)
        table.Sample(new ScriptedRng(0, 0), buffer);
        Assert.That(buffer[^1].Item, Is.EqualTo(_sword));

        // Trigger (roll 2499) + weight roll 9 → second entry (gem, weight range 1-9)
        table.Sample(new ScriptedRng(2499, 9), buffer);
        Assert.That(buffer[^1].Item, Is.EqualTo(_gem));

        // Permyriad-0 group is skipped without drawing randomness
        var zero = new RewardTable(new[] { new RewardGroup(0, true, new RewardEntry(_gold, 1, 1, 1)) });
        var zeroRng = new ScriptedRng();
        zero.Sample(zeroRng, buffer);
        Assert.That(zeroRng.Calls, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_rejects_bad_data_at_boot()
    {
        Assert.That(() => new RewardEntry(ItemId.None, 1, 1, 1), Throws.ArgumentException);
        Assert.That(() => new RewardEntry(_gold, -1, 1, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new RewardEntry(_gold, 1, 5, 4), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new RewardGroup(10001, true), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new RewardGroup(100, grantAll: false, new RewardEntry(_gold, 0, 1, 1)),
            Throws.ArgumentException, "weighted draw requires positive total weight");
    }

    [Test]
    public void TryGrant_samples_and_applies_atomically()
    {
        long nextId = 0;
        var inventory = new ItemInventory<ItemState>(_catalog, () => ++nextId, _ => new ItemState());
        var table = new RewardTable(new[]
        {
            new RewardGroup(10000, grantAll: true,
                new RewardEntry(_gold, 1, 100, 100),
                new RewardEntry(_sword, 1, 1, 1)),
        });
        var created = new List<ItemInstance<ItemState>>();

        Assert.That(inventory.TryGrant(table, new ScriptedRng(), out _, created), Is.EqualTo(InventoryError.None));
        Assert.That(inventory.GetQuantity(_gold), Is.EqualTo((BigNum)100));
        Assert.That(inventory.GetQuantity(_sword), Is.EqualTo(BigNum.One));
        Assert.That(created, Has.Count.EqualTo(2));
    }
}
