using System.Threading;
using Bun3.Unity.Core.Utils;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI
{
    /// <summary>
    /// 비동기 표시/숨김 전이를 갖는 UI 프리팹 컴포넌트의 공통 베이스 —
    /// 팝업(<c>Popups.Popup</c>), 토스트(<c>Toasts.ToastView{TData}</c>),
    /// 로딩 오버레이(<c>Loading.LoadingView</c>)가 상속한다.
    /// 소유자(스택/큐/오버레이)가 전이 태스크의 완료를 대기하며, 내장 연출 플래그(스케일 팝/페이드)를
    /// 켜면 연출 코드 없이 기본 전이가 실행된다 — <see cref="PlayShowAsync"/>/<see cref="PlayHideAsync"/>를
    /// 오버라이드하면 내장 연출은 무시된다.
    /// </summary>
    public abstract class UIView : MonoBehaviour
    {
        private const float ScaleFrom = 0.7f; // 레거시 스케일 팝 시작값

        [SerializeField]
        [Tooltip("내장 스케일 팝 연출(0.7→1)을 쓴다. PlayShowAsync를 오버라이드하면 무시된다.")]
        private bool _scaleAnimation;

        [SerializeField]
        [Tooltip("내장 페이드 연출(CanvasGroup alpha)을 쓴다.")]
        private bool _fadeAnimation;

        [SerializeField]
        [Tooltip("내장 연출 시간(초, unscaled).")]
        private float _animationDuration = 0.15f;

        [SerializeField]
        [Tooltip("내장 연출 이징 커브.")]
        private AnimationCurve _animationEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        /// <summary>
        /// 표시 연출 대기 지점. 소유자는 이 태스크가 끝나야 표시 완료로 취급한다.
        /// 기본 구현은 내장 연출 플래그를 실행하고, 플래그가 꺼져 있으면 즉시 완료.
        /// </summary>
        protected internal virtual UniTask PlayShowAsync(CancellationToken cancellationToken)
            => PlayBuiltInAnimationAsync(showing: true, cancellationToken);

        /// <summary>
        /// 숨김 연출 대기 지점. 소유자는 이 태스크가 끝나야 인스턴스를 해제/재사용한다.
        /// 기본 구현은 내장 연출 플래그를 역방향으로 실행하고, 플래그가 꺼져 있으면 즉시 완료.
        /// </summary>
        protected internal virtual UniTask PlayHideAsync(CancellationToken cancellationToken)
            => PlayBuiltInAnimationAsync(showing: false, cancellationToken);

        private async UniTask PlayBuiltInAnimationAsync(bool showing, CancellationToken cancellationToken)
        {
            if ((!_scaleAnimation && !_fadeAnimation) || _animationDuration <= 0f)
                return; // 연출 없음 — 즉시 완료(기본 동작)

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
