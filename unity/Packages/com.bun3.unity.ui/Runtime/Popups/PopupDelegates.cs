using System.Threading;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// Creates and returns the popup instance for a key. Prefab loading strategy
    /// (Resources/Addressables/pool) and parent-transform placement are entirely the game's job.
    /// </summary>
    /// <remarks>
    /// The returned instance is owned by <see cref="PopupStack"/> and handed back via
    /// <see cref="PopupReleaser"/> on close. The cancellation token fires on
    /// <see cref="PopupStack.Clear"/>/<see cref="PopupStack.Dispose"/>.
    /// </remarks>
    public delegate UniTask<Popup> PopupFactory(PopupKey key, CancellationToken cancellationToken);

    /// <summary>
    /// Takes back a closed popup instance. Default implementation is <c>Destroy</c>;
    /// games using pooling replace it with their return logic.
    /// </summary>
    public delegate void PopupReleaser(Popup popup);
}
