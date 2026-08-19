using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class GrowthTests
{
    // required exp for level n -> n+1
    private static long Table(int level) => level * 100;

    [Test]
    public void Settles_multiple_levels_and_preserves_remainder()
    {
        var level = 1;
        long exp = 350;   // consumes 100 for 1->2, 200 for 2->3, remainder 50

        Assert.That(Growth.SettleExp(ref level, ref exp, maxLevel: 10, Table), Is.EqualTo(2));
        Assert.That(level, Is.EqualTo(3));
        Assert.That(exp, Is.EqualTo(50), "remainder exp preserved");

        // no change when insufficient
        Assert.That(Growth.SettleExp(ref level, ref exp, maxLevel: 10, Table), Is.EqualTo(0));
        Assert.That((level, exp), Is.EqualTo((3, 50L)));
    }

    [Test]
    public void Stops_at_max_level_and_keeps_overflow_exp()
    {
        var level = 1;
        long exp = 1_000_000;

        Assert.That(Growth.SettleExp(ref level, ref exp, maxLevel: 3, Table), Is.EqualTo(2));
        Assert.That(level, Is.EqualTo(3), "stops at max level");
        Assert.That(exp, Is.EqualTo(1_000_000 - 100 - 200), "overflow exp at max level is preserved — discarding is caller policy");

        // already at max level — not even a table lookup
        Assert.That(Growth.SettleExp(ref level, ref exp, maxLevel: 3, _ => throw new Exception()), Is.EqualTo(0));
    }

    [Test]
    public void Rejects_invalid_table_data()
    {
        var level = 1;
        long exp = 100;
        Assert.That(() => Growth.SettleExp(ref level, ref exp, 10, _ => 0),
            Throws.TypeOf<ArgumentOutOfRangeException>(), "required exp <= 0 is a data error");
    }
}
