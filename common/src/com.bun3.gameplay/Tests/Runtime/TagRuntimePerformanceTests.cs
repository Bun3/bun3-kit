#nullable enable
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace Bun3.Gameplay.Tests
{
    [TestFixture]
    public sealed class TagRuntimePerformanceTests
    {
        [Test]
        [Timeout(7_200_000)]
        public void Read_and_mutation_matrices_report_release_metrics()
        {
#if ENABLE_IL2CPP
            const string backend = "IL2CPP";
#else
            const string backend = "Mono";
#endif
            var readRows = TagPerformanceFixture.MeasureMatrix(backend);
            var mutationRows = TagPerformanceFixture.MeasureMutationMatrix(backend);
            Assert.That(readRows, Has.Length.EqualTo(144));
            Assert.That(mutationRows, Has.Length.EqualTo(96));
            foreach (var row in readRows)
                TestContext.Out.WriteLine(row.ToLogLine());
            foreach (var row in mutationRows)
                TestContext.Out.WriteLine(row.ToLogLine());
        }

        [Test]
        public void One_hundred_thousand_hierarchical_queries_allocate_zero()
        {
            var fixture = TagRuntimeFixture.Create(
                catalogSize: 50_000,
                exactKinds: 64,
                depth: 16,
                containerKind: TagContainerKind.TagCountContainer);
            fixture.WarmUp();

            var checksum = 0;
            Assert.That(
                () => { checksum = fixture.RunNewQueries(100_000, TagQueryKind.ParentHit); },
                UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.That(checksum, Is.EqualTo(100_000));
        }

        [Test]
        public void Reserved_mutation_cycles_allocate_zero()
        {
            var fixture = TagRuntimeFixture.Create(
                catalogSize: 50_000,
                exactKinds: 64,
                depth: 16,
                containerKind: TagContainerKind.TagCountContainer,
                startEmpty: true);
            fixture.WarmUpMutation();
            var checksum = 0;
            Assert.That(
                () => { checksum = fixture.RunReservedAddRemoveCycles(1_000); },
                UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.That(checksum, Is.EqualTo(93_000));
        }
    }
}
