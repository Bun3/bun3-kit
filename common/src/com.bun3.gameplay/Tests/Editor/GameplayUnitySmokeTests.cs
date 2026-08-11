using Bun3.Gameplay.Numerics;
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
    }
}
