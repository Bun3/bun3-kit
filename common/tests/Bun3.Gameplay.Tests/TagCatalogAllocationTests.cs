#nullable enable
using System;
using System.IO;
using System.Text;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests
{
    [TestFixture]
    public sealed class TagCatalogAllocationTests
    {
        [Test]
        public void Catalog_creation_stays_within_single_fingerprint_allocation_budget()
        {
            var utf8 = Encoding.UTF8.GetBytes(TagCatalogTestData.BuildFlatCatalog(4_096));
            _ = Load(utf8);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var before = GC.GetAllocatedBytesForCurrentThread();
            var catalog = Load(utf8);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            GC.KeepAlive(catalog);
            Assert.That(allocated, Is.LessThanOrEqualTo(8_200_000L));
        }

        private static TagCatalog Load(byte[] utf8)
        {
            using var stream = new MemoryStream(utf8, writable: false);
            return TagCatalog.Load(stream);
        }
    }
}
