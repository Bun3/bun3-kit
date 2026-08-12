#nullable enable
using System;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TagPerformanceBenchmarkTests
{
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
