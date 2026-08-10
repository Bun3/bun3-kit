using System;
using System.Threading.Tasks;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class TagRegistryTests
{
    [Test]
    public void Register_returns_stable_handle_and_interned_name()
    {
        var registry = new TagRegistry();
        var dead = registry.GetOrRegister("State.Dead");
        var again = registry.GetOrRegister("State.Dead");

        Assert.That(dead.IsValid, Is.True);
        Assert.That(again, Is.EqualTo(dead));
        Assert.That(registry.GetName(dead), Is.EqualTo("State.Dead"));
        // 인터닝 — 같은 참조가 반환된다(무할당 계약)
        Assert.That(ReferenceEquals(registry.GetName(dead), registry.GetName(again)), Is.True);
    }

    [Test]
    public void Ancestors_are_auto_registered()
    {
        var registry = new TagRegistry();
        var ghost = registry.GetOrRegister("State.Dead.Ghost");

        Assert.That(registry.TryGet("State.Dead", out var dead), Is.True);
        Assert.That(registry.TryGet("State", out var state), Is.True);
        Assert.That(registry.GetParent(ghost), Is.EqualTo(dead));
        Assert.That(registry.GetParent(dead), Is.EqualTo(state));
        Assert.That(registry.GetParent(state), Is.EqualTo(GameplayTag.None));
    }

    [Test]
    public void IsAncestorOrSelf_walks_hierarchy()
    {
        var registry = new TagRegistry();
        var ghost = registry.GetOrRegister("State.Dead.Ghost");
        var dead = registry.GetOrRegister("State.Dead");
        var state = registry.GetOrRegister("State");
        var rooted = registry.GetOrRegister("State.Rooted");

        Assert.That(registry.IsAncestorOrSelf(dead, ghost), Is.True);
        Assert.That(registry.IsAncestorOrSelf(state, ghost), Is.True);
        Assert.That(registry.IsAncestorOrSelf(ghost, ghost), Is.True);
        Assert.That(registry.IsAncestorOrSelf(ghost, dead), Is.False);   // 방향 확인
        Assert.That(registry.IsAncestorOrSelf(rooted, ghost), Is.False);
    }

    [Test]
    public void Unregistered_lookup_fails_but_GetOrRegister_registers_dynamically()
    {
        var registry = new TagRegistry();
        Assert.That(registry.TryGet("Never.Registered", out _), Is.False);

        // 스펙 §7: 미등록 태그는 동적 등록
        var tag = registry.GetOrRegister("Wire.Received.Later");
        Assert.That(tag.IsValid, Is.True);
        Assert.That(registry.TryGet("Wire.Received.Later", out var found), Is.True);
        Assert.That(found, Is.EqualTo(tag));
    }

    [TestCase("")]
    [TestCase(".")]
    [TestCase(".Leading")]
    [TestCase("Trailing.")]
    [TestCase("Double..Dot")]
    public void Invalid_names_throw(string name)
    {
        Assert.Throws<ArgumentException>(() => new TagRegistry().GetOrRegister(name));
    }

    [Test]
    public void Names_are_case_sensitive_ordinal()
    {
        var registry = new TagRegistry();
        var a = registry.GetOrRegister("State.Dead");
        var b = registry.GetOrRegister("state.dead");
        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public async Task Concurrent_registration_is_safe_and_consistent()
    {
        var registry = new TagRegistry();
        var tasks = new Task<GameplayTag>[16];
        for (var i = 0; i < tasks.Length; i++)
        {
            var n = i;
            tasks[i] = Task.Run(() => registry.GetOrRegister($"Load.Branch{n % 4}.Leaf{n}"));
        }

        var tags = await Task.WhenAll(tasks);
        foreach (var tag in tags)
        {
            Assert.That(tag.IsValid, Is.True);
            Assert.That(registry.TryGet(registry.GetName(tag), out var found), Is.True);
            Assert.That(found, Is.EqualTo(tag));
        }
    }
}
