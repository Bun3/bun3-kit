namespace Bun3.Unity.UI.Buttons
{
    /// <summary>
    /// Strategy that replays the reason when a disabled button is clicked.
    /// </summary>
    /// <remarks>
    /// Implementations must be stateless — many buttons share one
    /// <see cref="ButtonInteractableScope.DefaultHandler"/>.
    /// </remarks>
    public interface IButtonDisabledHandler
    {
        /// <summary>Replays one reason. Only non-empty reasons are delivered.</summary>
        void Handle(DisabledReason reason);
    }
}
