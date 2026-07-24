namespace Bun3.UI.Buttons
{
    public interface IButtonDisabledHandler
    {
        void OnDisabled(ButtonInteractableScope.DisabledReason reason);
        void Handle();
    }
}
