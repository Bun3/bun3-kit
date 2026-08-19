using System.Threading;
using Bun3.Unity.Core.Utils;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI
{
    /// <summary>
    /// Common base for UI prefab components with async show/hide transitions —
    /// inherited by popups (<c>Popups.Popup</c>), toasts (<c>Toasts.ToastView{TData}</c>),
    /// and the loading overlay (<c>Loading.LoadingView</c>).
    /// The owner (stack/queue/overlay) awaits the transition task. Enabling the built-in
    /// animation flags (scale pop / fade) runs a default transition without any animation code —
    /// overriding <see cref="PlayShowAsync"/>/<see cref="PlayHideAsync"/> bypasses the built-ins.
    /// </summary>
    public abstract class UIView : MonoBehaviour
    {
        private const float ScaleFrom = 0.7f;

        [SerializeField]
        [Tooltip("Use the built-in scale pop animation (0.7 to 1). Ignored when PlayShowAsync is overridden.")]
        private bool _scaleAnimation;

        [SerializeField]
        [Tooltip("Use the built-in fade animation (CanvasGroup alpha).")]
        private bool _fadeAnimation;

        [SerializeField]
        [Tooltip("Built-in animation duration (seconds, unscaled).")]
        private float _animationDuration = 0.15f;

        [SerializeField]
        [Tooltip("Built-in animation easing curve.")]
        private AnimationCurve _animationEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        /// <summary>
        /// Show-transition await point. The owner treats the view as shown only after this
        /// task completes. Default implementation runs the built-in animation flags,
        /// completing immediately when they are off.
        /// </summary>
        protected internal virtual UniTask PlayShowAsync(CancellationToken cancellationToken)
            => PlayBuiltInAnimationAsync(showing: true, cancellationToken);

        /// <summary>
        /// Hide-transition await point. The owner releases/reuses the instance only after this
        /// task completes. Default implementation runs the built-in animation flags in reverse,
        /// completing immediately when they are off.
        /// </summary>
        protected internal virtual UniTask PlayHideAsync(CancellationToken cancellationToken)
            => PlayBuiltInAnimationAsync(showing: false, cancellationToken);

        private async UniTask PlayBuiltInAnimationAsync(bool showing, CancellationToken cancellationToken)
        {
            if ((!_scaleAnimation && !_fadeAnimation) || _animationDuration <= 0f)
                return; // No animation — complete immediately (default behavior).

            var fadeGroup = _fadeAnimation ? gameObject.GetOrAdd<CanvasGroup>() : null;

            float elapsed = 0f;
            while (elapsed < _animationDuration)
            {
                float progress = elapsed / _animationDuration;
                ApplyBuiltInAnimation(showing ? progress : 1f - progress, fadeGroup);

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                elapsed += Time.unscaledDeltaTime;
            }

            ApplyBuiltInAnimation(showing ? 1f : 0f, fadeGroup);
        }

        private void ApplyBuiltInAnimation(float progress, CanvasGroup fadeGroup)
        {
            float eased = _animationEase.Evaluate(progress);

            if (_scaleAnimation)
                transform.localScale = Vector3.one * (ScaleFrom + (1f - ScaleFrom) * eased);

            if (fadeGroup)
                fadeGroup.alpha = eased;
        }
    }
}
