namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// Implemented by popups that receive initial data passed to <see cref="PopupStack.PushAsync{TArg}"/>.
    /// </summary>
    /// <remarks>
    /// Called right after factory loading, before stack insertion and the open transition
    /// (<c>PlayShowAsync</c>) — <see cref="Popup.Stack"/> is still null at call time.
    /// The stack bridges async loading and initial-data delivery, so there is no need to create
    /// the instance synchronously just to call an init method.
    /// </remarks>
    public interface IPopupArg<in TArg>
    {
        /// <summary>Receives the initial data carried by Push/Enqueue.</summary>
        void OnPopupArg(TArg arg);
    }
}
