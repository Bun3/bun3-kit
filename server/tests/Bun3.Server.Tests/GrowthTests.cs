using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class GrowthTests
{
    // idlez RequiredExps 상당 — 레벨 n → n+1 필요 경험치
    private static long Table(int level) => level * 100;

    [Test]
    public void Settles_multiple_levels_and_preserves_remainder()
    {
        var level = 1;
        long exp = 350;   // 1→2: 100, 2→3: 200 소진, 잔여 50

        Assert.That(Growth.SettleExp(ref level, ref exp, maxLevel: 10, Table), Is.EqualTo(2));
        Assert.That(level, Is.EqualTo(3));
        Assert.That(exp, Is.EqualTo(50), "잔여 경험치 보존");

        // 부족하면 무변화
        Assert.That(Growth.SettleExp(ref level, ref exp, maxLevel: 10, Table), Is.EqualTo(0));
        Assert.That((level, exp), Is.EqualTo((3, 50L)));
    }

    [Test]
    public void Stops_at_max_level_and_keeps_overflow_exp()
    {
        var level = 1;
        long exp = 1_000_000;

        Assert.That(Growth.SettleExp(ref level, ref exp, maxLevel: 3, Table), Is.EqualTo(2));
        Assert.That(level, Is.EqualTo(3), "만렙 정지");
        Assert.That(exp, Is.EqualTo(1_000_000 - 100 - 200), "만렙 잔여는 보존 — 버릴지는 호출측 정책");

        // 이미 만렙 — 테이블 조회조차 없음
        Assert.That(Growth.SettleExp(ref level, ref exp, maxLevel: 3, _ => throw new Exception()), Is.EqualTo(0));
    }

    [Test]
    public void Rejects_invalid_table_data()
    {
        var level = 1;
        long exp = 100;
        Assert.That(() => Growth.SettleExp(ref level, ref exp, 10, _ => 0),
            Throws.TypeOf<ArgumentOutOfRangeException>(), "필요치 0 이하는 데이터 오류");
    }
}
