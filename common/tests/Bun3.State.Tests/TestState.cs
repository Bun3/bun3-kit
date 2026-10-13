namespace Bun3.State.Tests
{
    internal enum TestStatus
    {
        Pending,
        Active,
        Complete
    }

    internal sealed class TestState : ITrackedState, IStateValueTarget<int>, IStateValueTarget<int?>,
        IStateValueTarget<TestStatus>, IStateValueTarget<string?>, IStateValueTarget<TestState?>
    {
        private int _level = 1;
        private int? _optional;
        private TestStatus _status;
        private string? _name;
        private TestState? _child;
        private TestState? _other;

        public TrackedList<TestState?> Children { get; } = new TrackedList<TestState?>();
        public TrackedList<int?> Numbers { get; } = new TrackedList<int?>();
        public int Visits { get; private set; }

        public TestState? Child
        {
            get => _child;
            set => Tracking.SetChild(4, ref _child, value);
        }

        public TestState? Other
        {
            get => _other;
            set => Tracking.SetChild(5, ref _other, value);
        }

        internal void InitializeChildren(TestState? child, TestState? other)
        {
            _child = child;
            _other = other;
        }

        internal TestState()
        {
            Tracking = new StateNode(this);
        }

        public StateNode Tracking { get; }

        public int Level
        {
            get => _level;
            set => Tracking.SetValue(0, ref _level, value);
        }

        public int? Optional
        {
            get => _optional;
            set => Tracking.SetValue(1, ref _optional, value);
        }

        public TestStatus Status
        {
            get => _status;
            set => Tracking.SetValue(2, ref _status, value);
        }

        public string? Name
        {
            get => _name;
            set => Tracking.SetValue(3, ref _name, value);
        }

        public void VisitChildren(IStateChildVisitor visitor)
        {
            Visits++;
            if (_child != null) visitor.Visit(_child);
            if (_other != null) visitor.Visit(_other);
            visitor.Visit(Children);
            visitor.Visit(Numbers);
        }

        void IStateValueTarget<TestState?>.RestoreValue(int slot, TestState? value)
        {
            if (slot == 4) _child = value;
            else if (slot == 5) _other = value;
            else throw new System.ArgumentOutOfRangeException(nameof(slot));
        }

        public override bool Equals(object? obj) => obj is TestState;
        public override int GetHashCode() => 0;

        void IStateValueTarget<int>.RestoreValue(int slot, int value)
        {
            if (slot != 0) throw new System.ArgumentOutOfRangeException(nameof(slot));
            _level = value;
        }

        void IStateValueTarget<int?>.RestoreValue(int slot, int? value)
        {
            if (slot != 1) throw new System.ArgumentOutOfRangeException(nameof(slot));
            _optional = value;
        }

        void IStateValueTarget<TestStatus>.RestoreValue(int slot, TestStatus value)
        {
            if (slot != 2) throw new System.ArgumentOutOfRangeException(nameof(slot));
            _status = value;
        }

        void IStateValueTarget<string?>.RestoreValue(int slot, string? value)
        {
            if (slot != 3) throw new System.ArgumentOutOfRangeException(nameof(slot));
            _name = value;
        }
    }
}
