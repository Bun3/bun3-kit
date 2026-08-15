using System;
using System.Linq;
using System.Runtime.InteropServices;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests
{
    [TestFixture]
    public sealed class GameplayTagTests
    {
        [Test]
        public void Representation_is_two_byte_catalog_index()
        {
            var tag = new GameplayTag(42);

            Assert.That(Marshal.SizeOf<GameplayTag>(), Is.EqualTo(sizeof(ushort)));
            Assert.That(tag.Index, Is.EqualTo((ushort)42));
            Assert.That(tag.IsValid, Is.True);
            Assert.That(GameplayTag.None.Index, Is.Zero);
            Assert.That(GameplayTag.None.IsValid, Is.False);
        }

        [Test]
        public void Equality_and_hash_use_only_the_index()
        {
            var left = new GameplayTag(19);
            var right = new GameplayTag(19);
            var other = new GameplayTag(20);

            Assert.That(left, Is.EqualTo(right));
            Assert.That(left.GetHashCode(), Is.EqualTo(19));
            Assert.That(left == right, Is.True);
            Assert.That(left != other, Is.True);
        }

        [Test]
        public void Raw_index_constructor_is_not_public()
        {
            Assert.That(
                typeof(GameplayTag).GetConstructors()
                    .Any(c => c.GetParameters().Length == 1
                        && c.GetParameters()[0].ParameterType == typeof(ushort)),
                Is.False);
        }
    }
}
