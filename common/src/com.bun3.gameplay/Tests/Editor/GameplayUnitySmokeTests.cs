#nullable enable
using System;
using System.IO;
using System.Text;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Unity.Tests
{
    [TestFixture]
    public sealed class GameplayUnitySmokeTests
    {
        [Test]
        public void BigNum_contract_compiles_and_runs_in_unity()
        {
            Assert.That((BigNum)long.MaxValue + (BigNum)long.MaxValue,
                Is.EqualTo(BigNum.FromParts(1_844_674_407_370_955_161L, 1)));
            Assert.That(
                BigNum.FromParts(1, 19) + (BigNum)(-1),
                Is.EqualTo(BigNum.FromParts(999_999_999_999_999_999L, 1)));
            Assert.That(BigNum.MaxValue > BigNum.MinValue, Is.True);
            Assert.That(BigNum.FromParts(12_345, 6).GetHashCode(), Is.EqualTo(930_490_798));

            var scientific = new BigNumFormat(
                3,
                new[] { "", "K", "M", "B", "T" },
                overflowStyle: BigNumOverflowStyle.Scientific);
            Assert.That(
                BigNum.MaxValue.ToDisplayString(scientific).Length,
                Is.LessThanOrEqualTo(256));
        }

        [Test]
        public void Tag_catalog_round_trips_public_wire_indices_in_unity()
        {
            const string json =
                "{\"schemaVersion\":1,\"tags\":[" +
                "{\"name\":\"State.Alive\"},{\"name\":\"State.Dead\"}]}";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var catalog = TagCatalog.Load(stream);

            Assert.That(catalog.TryGetByIndex(catalog.GetRequired("State.Dead").Index, out var wire), Is.True);
            Assert.That(wire, Is.EqualTo(catalog.GetRequired("state.dead")));
            Assert.That(catalog.TryGetByIndex(checked((ushort)(catalog.Count + 1)), out _), Is.False);

            var tags = catalog.CreateContainer(1);
            tags.Add(catalog.GetRequired("State.Dead"));
            Span<GameplayTag> copiedTags = stackalloc GameplayTag[1];
            Assert.That(tags.CopyExactTags(copiedTags), Is.EqualTo(1));
            Assert.That(copiedTags[0], Is.EqualTo(catalog.GetRequired("State.Dead")));

            var counts = catalog.CreateCountContainer(1);
            counts.Add(catalog.GetRequired("State.Dead"), 3);
            Span<TagCountEntry> copiedCounts = stackalloc TagCountEntry[1];
            Assert.That(counts.CopyExactEntries(copiedCounts), Is.EqualTo(1));
            Assert.That(copiedCounts[0].Tag, Is.EqualTo(catalog.GetRequired("State.Dead")));
            Assert.That(copiedCounts[0].Count, Is.EqualTo(3));
        }
    }
}
