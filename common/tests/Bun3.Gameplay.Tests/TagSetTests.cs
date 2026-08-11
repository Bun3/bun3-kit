using System;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class TagSetTests
{
    private TagRegistry _registry = null!;
    private TagSet _set = null!;
    private GameplayTag _dead;
    private GameplayTag _ghost;
    private GameplayTag _state;
    private GameplayTag _hasted;

    [SetUp]
    public void SetUp()
    {
        _registry = new TagRegistry();
        _set = new TagSet(_registry);
        _ghost = _registry.GetOrRegister("State.Dead.Ghost");
        _dead = _registry.GetOrRegister("State.Dead");
        _state = _registry.GetOrRegister("State");
        _hasted = _registry.GetOrRegister("Buff.Hasted");
    }

    [Test]
    public void Add_and_exact_queries()
    {
        _set.Add(_ghost);
        Assert.That(_set.HasExact(_ghost), Is.True);
        Assert.That(_set.HasExact(_dead), Is.False);   // 정확 일치 — 계층 아님
        Assert.That(_set.ExactCount(_ghost), Is.EqualTo(1));
        Assert.That(_set.KindCount, Is.EqualTo(1));
    }

    [Test]
    public void Hierarchical_has_matches_ancestors()
    {
        _set.Add(_ghost);
        Assert.That(_set.Has(_dead), Is.True);     // Ghost 보유 → "State.Dead" 매칭
        Assert.That(_set.Has(_state), Is.True);
        Assert.That(_set.Has(_ghost), Is.True);
        Assert.That(_set.Has(_hasted), Is.False);
    }

    [Test]
    public void Counted_semantics_two_sources_one_removal()
    {
        // 스펙 §7: 같은 태그를 부여하는 소스 2개 — 하나 꺼져도 태그 유지
        _set.Add(_hasted);
        _set.Add(_hasted);
        Assert.That(_set.ExactCount(_hasted), Is.EqualTo(2));

        Assert.That(_set.Remove(_hasted), Is.True);
        Assert.That(_set.HasExact(_hasted), Is.True);
        Assert.That(_set.ExactCount(_hasted), Is.EqualTo(1));

        Assert.That(_set.Remove(_hasted), Is.True);
        Assert.That(_set.HasExact(_hasted), Is.False);
        Assert.That(_set.KindCount, Is.EqualTo(0));
    }

    [Test]
    public void Hierarchical_count_sums_descendants()
    {
        _set.Add(_ghost, 2);
        _set.Add(_dead);
        Assert.That(_set.Count(_dead), Is.EqualTo(3));    // Dead(1) + Ghost(2)
        Assert.That(_set.Count(_state), Is.EqualTo(3));
        Assert.That(_set.Count(_ghost), Is.EqualTo(2));
        Assert.That(_set.ExactCount(_dead), Is.EqualTo(1));
    }

    [Test]
    public void Remove_missing_returns_false()
    {
        Assert.That(_set.Remove(_hasted), Is.False);
        _set.Add(_hasted);
        Assert.That(_set.Remove(_hasted, 5), Is.True);    // 초과 제거 = 0으로
        Assert.That(_set.HasExact(_hasted), Is.False);
    }

    [Test]
    public void Invalid_tag_and_nonpositive_count_throw()
    {
        Assert.Throws<ArgumentException>(() => _set.Add(GameplayTag.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => _set.Add(_hasted, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _set.Remove(_hasted, -1));
    }

    [Test]
    public void Add_fails_fast_before_exact_count_wraps_negative()
    {
        _set.Add(_hasted, int.MaxValue);
        Assert.That(() => _set.Add(_hasted), Throws.TypeOf<OverflowException>());
        Assert.That(_set.ExactCount(_hasted), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void Hierarchical_count_fails_fast_before_sum_wraps_negative()
    {
        _set.Add(_dead, int.MaxValue);
        _set.Add(_ghost);
        Assert.That(() => _set.Count(_state), Throws.TypeOf<OverflowException>());
    }
}
