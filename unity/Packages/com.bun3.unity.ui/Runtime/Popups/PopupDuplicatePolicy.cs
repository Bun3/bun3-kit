namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// How <see cref="PopupStack.Push"/> behaves when a popup with the same key is already open or loading.
    /// </summary>
    public enum PopupDuplicatePolicy
    {
        /// <summary>Ignore the request.</summary>
        Ignore = 0,

        /// <summary>Append to the sequential queue; shown once all existing popups close.</summary>
        Queue = 1,

        /// <summary>Close the existing open instance and open a new one.</summary>
        Replace = 2,

        /// <summary>
        /// Reuse the existing open instance: raise it to the top of its layer, and re-deliver the
        /// arg via <see cref="IPopupArg{TArg}"/> for arg pushes.
        /// (Does nothing when only a loading instance exists.)
        /// </summary>
        Focus = 3,
    }
}
