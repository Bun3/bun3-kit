using Bun3.Server.Players;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerExecutionAllocationTests
{
    [Test]
    public void Admission_scope_has_no_per_request_allocation()
    {
        var gate = new PlayerAdmissionGate();
        for (var i = 0; i < 1000; i++) gate.Enter().Dispose();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) gate.Enter().Dispose();
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }
    [Test]
    public void Uncontended_single_owner_has_no_per_iteration_allocation()
    {
        var table = new PlayerExecutionTable();
        table.RetainKey("a");
        for (var i = 0; i < 1000; i++) table.AcquireAsync("a", default).GetAwaiter().GetResult().Dispose();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) table.AcquireAsync("a", default).GetAwaiter().GetResult().Dispose();
        var allocatedBytesAfterWarmup = GC.GetAllocatedBytesForCurrentThread() - before;
        table.ReleaseKey("a");
        Assert.That(allocatedBytesAfterWarmup, Is.Zero);
    }
}
