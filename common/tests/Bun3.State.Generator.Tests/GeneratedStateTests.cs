using System;
using NUnit.Framework;

namespace Bun3.State.Generator.Tests
{
    public class GeneratedStateTests
    {
        internal const string Schema = @"
using Bun3.State;
namespace Fixtures {
public class ExistingBase { public int ExistingMethod() => 42; }
[TrackedState] public sealed partial class EquipmentState : ExistingBase {
 [Tracked] private int _level = 1;
 [Tracked] private EquipmentOptionState _option = new EquipmentOptionState();
}
[TrackedState] public sealed partial class EquipmentOptionState {
 [Tracked] private readonly TrackedList<RerollOptionState> _rerollOptions = new TrackedList<RerollOptionState> { new RerollOptionState() };
}
[TrackedState] public sealed partial class RerollOptionState {
 [Tracked] private int _id;
 [Tracked] private int _level;
}
}";

        [Test]
        public void Generated_nested_properties_restore_values_and_identity()
        {
            var assembly = GeneratorCompilation.Emit(Schema);
            dynamic root = Activator.CreateInstance(assembly.GetType("Fixtures.EquipmentState")!)!;
            var owner = new Owner();
            new StateBinding(owner).AttachRoot((ITrackedState)root);
            dynamic option = root.Option;
            dynamic child = option.RerollOptions[0];
            Assert.That((int)root.Level, Is.EqualTo(1));
            Assert.That((int)root.ExistingMethod(), Is.EqualTo(42));
            root.Level = 5;
            child.Level = 9;
            Assert.That(owner.Changes, Is.EqualTo(2));
            root.Option = (dynamic)Activator.CreateInstance(assembly.GetType("Fixtures.EquipmentOptionState")!)!;
            owner.Journal.Rollback();
            Assert.That((int)root.Level, Is.EqualTo(1));
            Assert.That((object)root.Option, Is.SameAs((object)option));
            Assert.That((object)root.Option.RerollOptions[0], Is.SameAs((object)child));
            Assert.That((int)child.Level, Is.Zero);
            Assert.That(owner.Changes, Is.EqualTo(3));
        }

        internal sealed class Owner : IStateOwner
        {
            internal StateJournal Journal { get; } = new StateJournal();
            internal int Changes { get; private set; }
            public StateJournal BeforeChange() => Journal;
            public void OnChanged() => Changes++;
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void Child_equality_does_not_control_initial_parent_dirty_or_rollback(bool throws, bool inherited)
        {
            var assembly = GeneratorCompilation.Emit(EqualitySchema(throws, inherited, false));
            dynamic root = Activator.CreateInstance(assembly.GetType("Fixtures.Root")!)!;
            dynamic second = Activator.CreateInstance(assembly.GetType("Fixtures.Root")!)!;
            dynamic child = root.A;
            Assert.Throws<InvalidOperationException>(() => second.A = child);
            var owner = new Owner();
            new StateBinding(owner).AttachRoot((ITrackedState)root);
            child.Value = 9;
            Assert.That(owner.Changes, Is.EqualTo(1));
            owner.Journal.Rollback();
            Assert.That((int)child.Value, Is.EqualTo(1));
            Assert.That((object)root.A, Is.SameAs((object)child));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void Child_equality_does_not_hide_duplicate_initial_references(bool throws, bool attach)
        {
            var assembly = GeneratorCompilation.Emit(EqualitySchema(throws, false, true));
            dynamic root = Activator.CreateInstance(assembly.GetType("Fixtures.Root")!)!;
            var owner = new Owner();
            var binding = new StateBinding(owner);
            var failure = attach
                ? Assert.Throws<InvalidOperationException>(() => binding.AttachRoot((ITrackedState)root))
                : Assert.Throws<InvalidOperationException>(() => { var ignored = root.A; });
            Assert.That(failure!.Message, Does.Contain("shared nodes"));
            Assert.That(binding.Root, Is.Null);
            dynamic shared = assembly.GetType("Fixtures.Initial")!.GetField("Shared")!.GetValue(null)!;
            binding.AttachRoot((ITrackedState)shared);
            shared.Value = 9;
            Assert.That(owner.Changes, Is.EqualTo(1));
            owner.Journal.Rollback();
            Assert.That((int)shared.Value, Is.EqualTo(1));
        }

        private static string EqualitySchema(bool throws, bool inherited, bool duplicate)
        {
            var type = inherited ? "EqualityBase" : "Child";
            var operators = "public static bool operator ==(" + type + "? a, " + type + "? b) => " +
                (throws ? "throw new System.InvalidOperationException(\"equality invoked\")" : "true") + ";" +
                "public static bool operator !=(" + type + "? a, " + type + "? b) => " +
                (throws ? "throw new System.InvalidOperationException(\"equality invoked\")" : "false") + ";" +
                "public override bool Equals(object? other) => ReferenceEquals(this, other); public override int GetHashCode() => 0;";
            return "using Bun3.State; namespace Fixtures { " +
                (inherited ? "public class EqualityBase { " + operators + " }" : "") +
                "[TrackedState] public sealed partial class Child " + (inherited ? ": EqualityBase" : "") +
                " { [Tracked] private int _value = 1; " + (inherited ? "" : operators) + " }" +
                "public static class Initial { public static readonly Child Shared = new Child(); }" +
                "[TrackedState] public sealed partial class Root { [Tracked] private Child _a = " +
                (duplicate ? "Initial.Shared; [Tracked] private Child _b = Initial.Shared;" : "new Child();") + " } }";
        }

        [TestCase("value", "Value")]
        [TestCase("slot", "Slot")]
        [TestCase("tracking", "Tracking")]
        public void Backing_fields_matching_generated_locals_mutate_notify_and_restore(string field, string property)
        {
            var source = "using Bun3.State; namespace Fixtures { [TrackedState] public sealed partial class Root { [Tracked] private int " + field + " = 1; } }";
            var assembly = GeneratorCompilation.Emit(source);
            var type = assembly.GetType("Fixtures.Root")!;
            var root = (ITrackedState)Activator.CreateInstance(type)!;
            var accessor = type.GetProperty(property)!;
            var owner = new Owner();
            new StateBinding(owner).AttachRoot(root);
            Assert.That(accessor.GetValue(root), Is.EqualTo(1));
            accessor.SetValue(root, 5);
            Assert.That(accessor.GetValue(root), Is.EqualTo(5));
            Assert.That(owner.Changes, Is.EqualTo(1));
            owner.Journal.Rollback();
            Assert.That(accessor.GetValue(root), Is.EqualTo(1));
            Assert.That(owner.Changes, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Child_and_list_fields_named_visitor_preserve_ownership_and_rollback(bool list)
        {
            var field = list
                ? "[Tracked] private readonly TrackedList<Child> visitor = new TrackedList<Child> { new Child() };"
                : "[Tracked] private Child visitor = new Child();";
            var source = "using Bun3.State; namespace Fixtures { [TrackedState] public sealed partial class Root { " + field + " } [TrackedState] public sealed partial class Child { [Tracked] private int _amount = 1; } }";
            var assembly = GeneratorCompilation.Emit(source);
            dynamic root = Activator.CreateInstance(assembly.GetType("Fixtures.Root")!)!;
            var owner = new Owner();
            new StateBinding(owner).AttachRoot((ITrackedState)root);
            dynamic original = list ? root.Visitor[0] : root.Visitor;
            original.Amount = 5;
            Assert.That(owner.Changes, Is.EqualTo(1));
            dynamic replacement = Activator.CreateInstance(assembly.GetType("Fixtures.Child")!)!;
            if (list) root.Visitor.Add(replacement);
            else root.Visitor = replacement;
            Assert.That(owner.Changes, Is.EqualTo(2));
            owner.Journal.Rollback();
            Assert.That((object)(list ? root.Visitor[0] : root.Visitor), Is.SameAs((object)original));
            Assert.That((int)original.Amount, Is.EqualTo(1));
            if (list) Assert.That((int)root.Visitor.Count, Is.EqualTo(1));
            Assert.That(owner.Changes, Is.EqualTo(2));
        }

        [Test]
        public void Initial_child_and_readonly_list_cannot_be_reused_before_root_attachment()
        {
            var assembly = GeneratorCompilation.Emit(Schema);
            dynamic first = Activator.CreateInstance(assembly.GetType("Fixtures.EquipmentState")!)!;
            dynamic second = Activator.CreateInstance(assembly.GetType("Fixtures.EquipmentState")!)!;
            dynamic original = second.Option;
            Assert.Throws<InvalidOperationException>(() => second.Option = first.Option);
            Assert.That((object)second.Option, Is.SameAs((object)original));
            dynamic list = first.Option.RerollOptions;
            Assert.Throws<InvalidOperationException>(() => second.Option.RerollOptions.Add(list[0]));
            Assert.That((int)second.Option.RerollOptions.Count, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Duplicate_initial_graph_is_rejected_atomically_by_getter_and_attachment(bool attach)
        {
            const string source = @"
using Bun3.State;
namespace Fixtures {
[TrackedState] public sealed partial class Child { [Tracked] private int _value; }
public static class Initial { public static Child Shared = new Child(); }
[TrackedState] public sealed partial class Root {
 [Tracked] private Child _a = Initial.Shared;
 [Tracked] private Child _b = Initial.Shared;
}
}";
            var assembly = GeneratorCompilation.Emit(source);
            dynamic root = Activator.CreateInstance(assembly.GetType("Fixtures.Root")!)!;
            dynamic child = assembly.GetType("Fixtures.Initial")!.GetField("Shared")!.GetValue(null)!;
            var owner = new Owner();
            var binding = new StateBinding(owner);
            if (attach) Assert.Throws<InvalidOperationException>(() => binding.AttachRoot((ITrackedState)root));
            else Assert.Throws<InvalidOperationException>(() => { var ignored = root.A; });
            Assert.That(binding.Root, Is.Null);
            Assert.That(owner.Changes, Is.Zero);
            // Failure must leave the first visited child available for independent attachment.
            binding.AttachRoot((ITrackedState)child);
            child.Value = 7;
            Assert.That(owner.Changes, Is.EqualTo(1));
            owner.Journal.Rollback();
            Assert.That((int)child.Value, Is.Zero);
        }

        [Test]
        public void Initial_cycle_is_rejected_without_claiming_earlier_sibling()
        {
            const string source = @"
using Bun3.State;
namespace Fixtures {
public static class Initial { public static Child Shared = new Child(); public static Root? Current; }
[TrackedState] public sealed partial class Child { [Tracked] private int _value; }
[TrackedState] public sealed partial class Root {
 [Tracked] private Child _a = Initial.Shared;
 [Tracked] private Child? _spare;
 [Tracked] private Root? _z = Initial.Current;
}
}";
            var assembly = GeneratorCompilation.Emit(source);
            dynamic root = Activator.CreateInstance(assembly.GetType("Fixtures.Root")!)!;
            // Reflection constructs an invalid initial graph, not a supported mutation path.
            root.GetType().GetField("_z", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(root, root);
            var owner = new Owner();
            var binding = new StateBinding(owner);
            Assert.Throws<InvalidOperationException>(() => binding.AttachRoot((ITrackedState)root));
            Assert.Throws<InvalidOperationException>(() => { var ignored = root.A; });
            var child = (ITrackedState)assembly.GetType("Fixtures.Initial")!.GetField("Shared")!.GetValue(null)!;
            binding.AttachRoot(child);
            Assert.That(owner.Changes, Is.Zero);
        }

        [Test]
        public void Nullable_reference_and_value_slots_restore_without_public_callbacks()
        {
            const string source = @"
using Bun3.State;
namespace Fixtures {
[TrackedState] public sealed partial class Root {
 [Tracked] private string _a = ""start"";
 [Tracked] private string? _b;
 [Tracked] private int? _number;
 [Tracked] private System.DayOfWeek _day;
 [Tracked] private Root? _child;
}
}";
            var assembly = GeneratorCompilation.Emit(source);
            dynamic root = Activator.CreateInstance(assembly.GetType("Fixtures.Root")!)!;
            var owner = new Owner();
            new StateBinding(owner).AttachRoot((ITrackedState)root);
            root.A = "next";
            root.B = "nullable";
            root.Number = 0;
            root.Day = DayOfWeek.Friday;
            root.Child = (dynamic)Activator.CreateInstance(assembly.GetType("Fixtures.Root")!)!;
            owner.Journal.Rollback();
            Assert.That((string)root.A, Is.EqualTo("start"));
            Assert.That((string?)root.B, Is.Null);
            Assert.That((int?)root.Number, Is.Null);
            Assert.That((DayOfWeek)root.Day, Is.EqualTo(DayOfWeek.Sunday));
            Assert.That((object?)root.Child, Is.Null);
            Assert.That(owner.Changes, Is.EqualTo(5));
        }

        [Test]
        public void Readonly_initial_list_is_owned_before_attachment()
        {
            const string source = @"
using Bun3.State;
namespace Fixtures {
public static class Initial { public static readonly TrackedList<int> Shared = new TrackedList<int>(); }
[TrackedState] public sealed partial class Root {
 [Tracked] private readonly TrackedList<int> _values = Initial.Shared;
}
}";
            var assembly = GeneratorCompilation.Emit(source);
            dynamic first = Activator.CreateInstance(assembly.GetType("Fixtures.Root")!)!;
            dynamic second = Activator.CreateInstance(assembly.GetType("Fixtures.Root")!)!;
            var list = (TrackedList<int>)first.Values;
            Assert.Throws<InvalidOperationException>(() => { var ignored = second.Values; });
            var owner = new Owner();
            new StateBinding(owner).AttachRoot((ITrackedState)first);
            list.Add(3);
            Assert.That(owner.Changes, Is.EqualTo(1));
            owner.Journal.Rollback();
            Assert.That(list.Count, Is.Zero);
        }

        [Test]
        public void Slots_follow_ordinal_field_names_across_partial_files_and_callbacks_are_explicit()
        {
            const string source = @"
using Bun3.State;
namespace Fixtures {
[TrackedState] public sealed partial class Root { [Tracked] private int _z; }
public sealed partial class Root { [Tracked] private int _a; }
}";
            var assembly = GeneratorCompilation.Emit(source);
            dynamic root = Activator.CreateInstance(assembly.GetType("Fixtures.Root")!)!;
            var target = (IStateValueTarget<int>)root;
            target.RestoreValue(0, 7);
            target.RestoreValue(1, 9);
            Assert.That((int)root.A, Is.EqualTo(7));
            Assert.That((int)root.Z, Is.EqualTo(9));
            Type type = root.GetType();
            Assert.That(type.GetMethod("RestoreValue"), Is.Null);
            Assert.That(type.GetMethod("VisitChildren"), Is.Null);
            Assert.That(type.GetProperty("Tracking"), Is.Null);
        }
    }
}
