using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Toasts
{
    /// <summary>
    /// Base component for toast prefabs. <see cref="ToastQueue{TData}"/> owns the lifecycle;
    /// the game implements only data binding (<see cref="OnData"/>) and animation
    /// (<see cref="UIView.PlayShowAsync"/> — or the built-in scale/fade flags).
    /// Text binding should follow the ZString + TMP <c>SetText</c> convention.
    /// </summary>
    public abstract class ToastView<TData> : UIView
    {
        /// <summary>Binds the data to display. Called on every show — the instance is reused.</summary>
        protected internal abstract void OnData(TData data);

        /// <summary>Waits out the hold time. Default is an unscaled delay — override for tests/special effects.</summary>
        protected internal virtual UniTask WaitAsync(float duration, CancellationToken cancellationToken)
            => UniTask.Delay(TimeSpan.FromSeconds(duration), ignoreTimeScale: true,
                cancellationToken: cancellationToken);
    }
}
