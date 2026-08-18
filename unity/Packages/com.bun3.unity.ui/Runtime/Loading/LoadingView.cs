using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Loading
{
    /// <summary>
    /// 로딩 오버레이 프리팹의 베이스 컴포넌트. <see cref="LoadingOverlay"/>가 표시/숨김을
    /// 소유하며, 게임은 스피너 연출과 진행률 표시만 구현한다.
    /// </summary>
    public abstract class LoadingView : MonoBehaviour
    {
        /// <summary>표시 연출 대기 지점. 기본 즉시 완료.</summary>
        protected internal virtual UniTask PlayShowAsync(CancellationToken cancellationToken)
            => UniTask.CompletedTask;

        /// <summary>숨김 연출 대기 지점. 기본 즉시 완료.</summary>
        protected internal virtual UniTask PlayHideAsync(CancellationToken cancellationToken)
            => UniTask.CompletedTask;

        /// <summary>진행률(0~1) 갱신. 진행률 UI가 없으면 무시하면 된다. 기본은 아무것도 안 한다.</summary>
        protected internal virtual void OnProgress(float progress01) { }
    }
}
