using System.Threading;
using Bun3.Unity.Core.Utils;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    // 내장 열림/닫힘 연출(레거시 animated/faded/animDuration 대응): 스케일 팝 + 페이드.
    // PlayOpenAsync/PlayCloseAsync 기본 구현이 이 플래그들을 실행한다 — 오버라이드하면 내장 연출은 무시된다.
    public abstract partial class Popup
    {
        private const float ScaleFrom = 0.7f; // 레거시 스케일 팝 시작값

        [SerializeField]
        [Tooltip("내장 스케일 팝 연출(0.7→1)을 쓴다. PlayOpenAsync를 오버라이드하면 무시된다.")]
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

        private async UniTask PlayBuiltInAnimationAsync(bool opening, CancellationToken cancellationToken)
        {
            if ((!_scaleAnimation && !_fadeAnimation) || _animationDuration <= 0f)
                return; // 연출 없음 — 즉시 완료(기존 기본 동작과 동일)

            var fadeGroup = _fadeAnimation ? gameObject.GetOrAdd<CanvasGroup>() : null;

            float elapsed = 0f;
            while (elapsed < _animationDuration)
            {
                float progress = elapsed / _animationDuration;
                ApplyBuiltInAnimation(opening ? progress : 1f - progress, fadeGroup);

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                elapsed += Time.unscaledDeltaTime;
            }

            ApplyBuiltInAnimation(opening ? 1f : 0f, fadeGroup);
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
