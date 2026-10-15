using System.Collections.Generic;
using NUnit.Framework;

namespace Bun3.Unity.Rooms.Tests
{
    public class RoomListingTests
    {
        [TestCase(0, 50)]
        [TestCase(-3, 50)]
        [TestCase(1, 1)]
        [TestCase(20, 20)]
        [TestCase(50, 50)]
        [TestCase(51, 50)]
        public void MaxResultsIsClampedToTheLimit(int requested, int expected) =>
            Assert.That(new RoomListOptions { MaxResults = requested }.Count, Is.EqualTo(expected));

        [Test]
        public void ListingReturnsOnlyPresentRequestedData()
        {
            var listing = new RoomListing("7", "Host", 2, 8, new Dictionary<string, string> { ["map"] = "Map_PoC", ["empty"] = "" });
            Assert.That(listing.Id, Is.EqualTo("7"));
            Assert.That(listing.Name, Is.EqualTo("Host"));
            Assert.That(listing.MemberCount, Is.EqualTo(2));
            Assert.That(listing.MaxMembers, Is.EqualTo(8));
            Assert.That(listing.GetData("map"), Is.EqualTo("Map_PoC"));
            Assert.That(listing.GetData("empty"), Is.Null);
            Assert.That(listing.GetData("missing"), Is.Null);
            Assert.That(listing.GetData(null), Is.Null);
            Assert.That(new RoomListing("8", null, 1, 0, null).GetData("map"), Is.Null);
        }
    }
}
