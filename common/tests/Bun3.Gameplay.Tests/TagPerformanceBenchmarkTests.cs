#nullable enable
using System;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TagPerformanceBenchmarkTests
{
    [TestCase(0)]
    [TestCase(1)]
    public void Reserved_add_remove_cycles_report_operation_results_and_final_state(
        int containerKindValue)
    {
        var containerKind = (TagContainerKind)containerKindValue;
        var fixture = TagRuntimeFixture.Create(
            catalogSize: 5_000,
            exactKinds: 8,
            depth: 16,
            containerKind,
            startEmpty: true);

        var newChecksum = fixture.RunReservedAddRemoveCycles(16);
        var legacyChecksum = fixture.RunLegacyAddRemoveCycles(16);

        Assert.That(newChecksum, Is.EqualTo(1_488));
        Assert.That(legacyChecksum, Is.EqualTo(1_488));
    }

    [TestCase(8, 1_600)]
    [TestCase(32, 4_038)]
    [TestCase(64, 7_244)]
    public void Read_write_mixed_reports_literal_semantic_checksum(int exactKinds, int expectedChecksum)
    {
        var fixture = TagRuntimeFixture.Create(
            catalogSize: 5_000,
            exactKinds,
            depth: 16,
            containerKind: TagContainerKind.TagCountContainer);
        fixture.WarmUpMutation();

        var newChecksum = fixture.RunNewReadWriteMixed(1_000);
        var legacyChecksum = fixture.RunLegacyReadWriteMixed(1_000);

        Assert.That(newChecksum, Is.EqualTo(expectedChecksum));
        Assert.That(legacyChecksum, Is.EqualTo(expectedChecksum));
    }

    [Test]
    public void Read_result_log_includes_semantic_checksum()
    {
        var result = new TagPerformanceResult(
            "DotNet", 5_000, 8, 16,
            TagContainerKind.TagContainer, TagQueryKind.ParentHit,
            1, 2, 3, 4, 5, 6, 100_000, 0);

        Assert.That(
            result.ToLogLine(),
            Does.Contain("legacy_p99_ticks=6 semantic_checksum=100000 alloc_count=0"));
    }

    [Test]
    public void Mutation_result_log_includes_semantic_checksum()
    {
        var result = new TagMutationPerformanceResult(
            "DotNet", 5_000, 8, 16,
            TagContainerKind.TagCountContainer, TagMutationKind.AddRemove,
            1, 2, 3, 4, 5, 6, 93_000, 0);

        Assert.That(
            result.ToLogLine(),
            Does.Contain("legacy_p99_ticks=6 semantic_checksum=93000 alloc_count=0"));
    }

    [Test]
    public void DotNet_read_and_mutation_matrices_report_release_metrics()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("BUN3_RUN_TAG_BENCHMARKS"),
                "1",
                StringComparison.Ordinal))
            Assert.Ignore("Set BUN3_RUN_TAG_BENCHMARKS=1 for the release performance gate.");

        var readRows = TagPerformanceFixture.MeasureMatrix("DotNet");
        var mutationRows = TagPerformanceFixture.MeasureMutationMatrix("DotNet");
        Assert.That(readRows, Has.Length.EqualTo(144));
        Assert.That(mutationRows, Has.Length.EqualTo(96));
        foreach (var row in readRows)
            TestContext.Out.WriteLine(row.ToLogLine());
        foreach (var row in mutationRows)
            TestContext.Out.WriteLine(row.ToLogLine());
    }
}
