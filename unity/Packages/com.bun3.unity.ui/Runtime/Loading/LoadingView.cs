namespace Bun3.Unity.UI.Loading
{
    /// <summary>
    /// Base component for loading-overlay prefabs. <see cref="LoadingOverlay"/> owns show/hide;
    /// the game implements only spinner animation (<see cref="UIView.PlayShowAsync"/> — or the
    /// built-in scale/fade flags) and progress display.
    /// </summary>
    public abstract class LoadingView : UIView
    {
        /// <summary>Progress update (0-1). Ignore if there is no progress UI. Default does nothing.</summary>
        protected internal virtual void OnProgress(float progress01) { }
    }
}
