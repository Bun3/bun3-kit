namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// Popup lifecycle phase. Transitioned by <see cref="PopupStack"/>.
    /// </summary>
    public enum PopupPhase
    {
        /// <summary>Not in a stack.</summary>
        None = 0,

        /// <summary>Inserted into the stack; awaiting the open transition (<c>PlayShowAsync</c>).</summary>
        Opening = 1,

        /// <summary>Open complete. Eligible for back-key routing.</summary>
        Open = 2,

        /// <summary>Awaiting the close transition (<c>PlayHideAsync</c>).</summary>
        Closing = 3,
    }
}
