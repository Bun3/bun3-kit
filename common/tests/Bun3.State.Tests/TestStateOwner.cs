namespace Bun3.State.Tests
{
    internal sealed class TestStateOwner : IStateOwner
    {
        private StateJournal? _journal;
        private bool _active;

        internal TestStateOwner(TestState state)
        {
            Binding = new StateBinding(this);
            Binding.AttachRoot(state);
        }

        public StateBinding Binding { get; }
        public int PrepareCalls { get; private set; }
        public int ChangedCalls { get; private set; }
        public int JournalCreations { get; private set; }

        public void Begin()
        {
            _active = true;
        }

        public void Commit()
        {
            _journal?.Commit();
            _journal = null;
            _active = false;
        }

        public void Rollback()
        {
            _journal?.Rollback();
            _journal = null;
            _active = false;
        }

        public StateJournal? BeforeChange()
        {
            PrepareCalls++;
            if (!_active) return null;
            if (_journal == null)
            {
                _journal = new StateJournal();
                JournalCreations++;
            }

            return _journal;
        }

        public void OnChanged()
        {
            ChangedCalls++;
        }
    }
}
