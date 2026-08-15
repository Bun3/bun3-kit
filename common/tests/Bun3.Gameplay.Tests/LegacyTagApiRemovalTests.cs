using System;
using System.Linq;
using System.Reflection;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class LegacyTagApiRemovalTests
{
    [Test]
    public void Dynamic_registry_and_ambiguous_tag_set_are_absent()
    {
        var exported = typeof(GameplayTag).Assembly.GetExportedTypes();
        Assert.That(exported.Any(t => t.FullName == "Bun3.Gameplay.Tags.TagRegistry"), Is.False);
        Assert.That(exported.Any(t => t.FullName == "Bun3.Gameplay.Tags.TagSet"), Is.False);
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.That(typeof(GameplayTag).GetField("Handle", members), Is.Null);
        Assert.That(typeof(GameplayTag).GetProperty("Handle", members), Is.Null);
        Assert.That(typeof(GameplayTag).GetConstructors(members).Any(c =>
            c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == typeof(int)), Is.False);
    }
}
