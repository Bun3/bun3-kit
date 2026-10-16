using NUnit.Framework;

namespace Bun3.Unity.Rooms.Tests
{
    public class RoomOwnerElectionTests
    {
        private static RoomMember M(string id, long joinedAt) => new RoomMember(id, joinedAt);

        [Test]
        public void EarliestJoinWins() =>
            Assert.That(RoomOwnerElection.Oldest(new[] { M("a", 5), M("b", 1), M("c", 3) }, null), Is.EqualTo("b"));

        [Test]
        public void TiesBreakByOrdinalId() =>
            Assert.That(RoomOwnerElection.Oldest(new[] { M("b", 1), M("a", 1), M("A", 1) }, null), Is.EqualTo("A"));

        [Test]
        public void ExcludedMemberIsSkipped() =>
            Assert.That(RoomOwnerElection.Oldest(new[] { M("b", 1), M("c", 3) }, "b"), Is.EqualTo("c"));

        [Test]
        public void NoCandidateReturnsNull()
        {
            Assert.That(RoomOwnerElection.Oldest(new RoomMember[0], null), Is.Null);
            Assert.That(RoomOwnerElection.Oldest(new[] { M("a", 1) }, "a"), Is.Null);
        }
    }
}
