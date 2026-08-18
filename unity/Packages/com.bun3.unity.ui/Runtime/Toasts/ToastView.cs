using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Toasts
{
    /// <summary>
    /// 토스트 프리팹의 베이스 컴포넌트. <see cref="ToastQueue{TData}"/>가 생명주기를 소유하며,
    /// 게임은 데이터 바인딩(<see cref="OnData"/>)과 연출만 구현한다.
    /// 텍스트 바인딩은 ZString + TMP <c>SetText</c> 관례를 따를 것.
    /// </summary>
    public abstract class ToastView<TData> : MonoBehaviour
    {
        /// <summary>표시할 데이터를 바인딩한다. 재사용 인스턴스라 매 표시마다 호출된다.</summary>
        protected internal abstract void OnData(TData data);

        /// <summary>표시 연출 대기 지점. 기본 즉시 완료.</summary>
        protected internal virtual UniTask PlayShowAsync(CancellationToken cancellationToken)
            => UniTask.CompletedTask;

        /// <summary>숨김 연출 대기 지점. 기본 즉시 완료.</summary>
        protected internal virtual UniTask PlayHideAsync(CancellationToken cancellationToken)
            => UniTask.CompletedTask;

        /// <summary>표시 유지 시간 대기. 기본은 unscaled 딜레이 — 테스트/특수 연출은 오버라이드.</summary>
        protected internal virtual UniTask WaitAsync(float duration, CancellationToken cancellationToken)
            => UniTask.Delay(TimeSpan.FromSeconds(duration), ignoreTimeScale: true,
                cancellationToken: cancellationToken);
    }
}
