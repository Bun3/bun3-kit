using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class RegenTests
{
    [Test]
    public void Accrues_whole_periods_and_preserves_remainder_without_drift()
    {
        long refresh = 10;

        // t=35: 25 경과 / 주기 10 → 2개, 기준은 소비한 주기만큼만 전진(30)
        Assert.That(Regen.SettlePeriodic(0, 100, 10, 35, ref refresh), Is.EqualTo(2));
        Assert.That(refresh, Is.EqualTo(30));

        // t=44: 14 경과 → 1개, 기준 40 — 연속 호출 합산이 (44-10)/10 = 3과 일치(드리프트 없음)
        Assert.That(Regen.SettlePeriodic(2, 100, 10, 44, ref refresh), Is.EqualTo(1));
        Assert.That(refresh, Is.EqualTo(40));

        // t=49: 주기 미만 — 0개, 기준 유지
        Assert.That(Regen.SettlePeriodic(3, 100, 10, 49, ref refresh), Is.EqualTo(0));
        Assert.That(refresh, Is.EqualTo(40));
    }

    [Test]
    public void Clamps_at_max_and_resets_bank_when_full()
    {
        long refresh = 0;
        Regen.SettlePeriodic(0, 5, 10, 100, ref refresh);   // 초기화

        // 1000 경과 → 90개분이지만 상한 5 — 가득 도달 시 기준을 현재로(적립 제거)
        Assert.That(Regen.SettlePeriodic(0, 5, 10, 1100, ref refresh), Is.EqualTo(5));
        Assert.That(refresh, Is.EqualTo(1100));

        // 이미 가득 — 0개, 기준 현재로 재설정(가득 상태 적립 방지)
        Assert.That(Regen.SettlePeriodic(5, 5, 10, 1250, ref refresh), Is.EqualTo(0));
        Assert.That(refresh, Is.EqualTo(1250));

        // 소모 후 재개 — 충전으로 다시 가득에 도달하면 기준을 현재로 재설정
        Assert.That(Regen.SettlePeriodic(4, 5, 10, 1265, ref refresh), Is.EqualTo(1));
        Assert.That(refresh, Is.EqualTo(1265), "가득 도달 재설정");
    }

    [Test]
    public void Guards_uninitialized_and_backwards_clock()
    {
        long refresh = 0;
        Assert.That(Regen.SettlePeriodic(0, 100, 10, 500, ref refresh), Is.EqualTo(0), "미초기화는 가득 지급 대신 초기화");
        Assert.That(refresh, Is.EqualTo(500));

        refresh = 900;
        Assert.That(Regen.SettlePeriodic(0, 100, 10, 800, ref refresh), Is.EqualTo(0), "시계 역행 보호");
        Assert.That(refresh, Is.EqualTo(800));

        Assert.That(() => Regen.SettlePeriodic(0, 100, 0, 800, ref refresh),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
