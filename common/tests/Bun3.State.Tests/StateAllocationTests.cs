using NUnit.Framework;
using System;

namespace Bun3.State.Tests
{
    public sealed class StateAllocationTests
    {
        private const int Iterations = 10_000;

        [TestCase("unattached")]
        [TestCase("inactive")]
        [TestCase("commit")]
        [TestCase("rollback")]
        public void Warmed_changes_allocate_no_per_write_objects(string mode)
        {
            var root = new TestState();
            var child = new TestState();
            var owner = mode == "unattached" ? null : new ReusingOwner(root) { Active = mode != "inactive" };

            var scalar = Measure(() => ChangeScalar(root), owner, mode);
            var replacement = Measure(() => ChangeChild(root, child), owner, mode);
            var list = Measure(() => ChangeList(root), owner, mode);

            TestContext.Out.WriteLine($"{mode}: scalar={scalar} B; child={replacement} B; list={list} B for {Iterations} iterations");
            Assert.That(scalar, Is.Zero, "scalar writes");
            Assert.That(replacement, Is.Zero, "child replacements");
            Assert.That(list, Is.Zero, "list add/remove pairs");
            GC.KeepAlive(owner);
        }

        [TestCase(10)]
        [TestCase(10_000)]
        public void One_scalar_undo_has_constant_prepared_allocation_across_graph_sizes(int optionCount)
        {
            var root = new TestState();
            for (var i = 0; i < optionCount; i++) root.Children.Add(new TestState());
            var beforeAttach = GC.GetAllocatedBytesForCurrentThread();
            var owner = new ReusingOwner(root);
            var attachBytes = GC.GetAllocatedBytesForCurrentThread() - beforeAttach;
            root.Level = 2;
            owner.Journal.Rollback();
            var before = GC.GetAllocatedBytesForCurrentThread();
            root.Level = 3;
            var writeBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            owner.Journal.Rollback();
            TestContext.Out.WriteLine($"options={optionCount}: initial graph attachment={attachBytes} B; prepared one-scalar undo={writeBytes} B");
            Assert.That(writeBytes, Is.Zero);
            Assert.That(root.Level, Is.EqualTo(1));
        }

        [Test]
        public void Initial_journal_and_buffer_growth_are_reported_separately()
        {
            var root = new TestState();
            var owner = new ReusingOwner(root);
            var before = GC.GetAllocatedBytesForCurrentThread();
            root.Level = 2;
            var firstWriteBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            owner.Journal.Rollback();
            before = GC.GetAllocatedBytesForCurrentThread();
            root.Level = 3;
            var preparedWriteBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            owner.Journal.Rollback();

            TestContext.Out.WriteLine($"first scalar undo/buffer growth={firstWriteBytes} B; prepared scalar undo={preparedWriteBytes} B");
            Assert.That(firstWriteBytes, Is.GreaterThan(0));
            Assert.That(preparedWriteBytes, Is.Zero);
        }

        private static long Measure(Action change, ReusingOwner? owner, string mode)
        {
            for (var i = 0; i < Iterations; i++) change();
            Complete(owner, mode);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++) change();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Complete(owner, mode);
            return allocated;
        }

        private static void Complete(ReusingOwner? owner, string mode)
        {
            if (mode == "commit") owner!.Journal.Commit();
            else if (mode == "rollback") owner!.Journal.Rollback();
        }

        private static void ChangeScalar(TestState root) => root.Level = root.Level == 1 ? 2 : 1;

        private static void ChangeChild(TestState root, TestState child) => root.Child = root.Child == null ? child : null;

        private static void ChangeList(TestState root)
        {
            root.Numbers.Add(1);
            root.Numbers.RemoveAt(0);
        }
    }
}
