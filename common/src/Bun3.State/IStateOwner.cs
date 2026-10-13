namespace Bun3.State
{
    /// <summary>Coordinates tracked changes with an item's transaction and notification lifecycle.</summary>
    public interface IStateOwner
    {
        /// <summary>Prepares a real change and returns its active journal, if any.</summary>
        /// <returns>The active journal, or <see langword="null"/> outside a transaction.</returns>
        StateJournal? BeforeChange();

        /// <summary>Notifies the owner after an attached node has changed.</summary>
        void OnChanged();
    }
}
