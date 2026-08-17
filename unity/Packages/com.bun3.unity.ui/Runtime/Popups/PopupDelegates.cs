using System.Threading;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 키에 해당하는 팝업 인스턴스를 만들어 돌려준다. 프리팹 로딩 방식
    /// (Resources/Addressables/풀 등)과 부모 트랜스폼 배치는 전부 게임 몫이다.
    /// </summary>
    /// <remarks>
    /// 반환된 인스턴스는 <see cref="PopupStack"/>이 소유하며, 닫힐 때
    /// <see cref="PopupReleaser"/>로 되돌려준다. 취소 토큰은 스택이
    /// <see cref="PopupStack.Clear"/>/<see cref="PopupStack.Dispose"/>될 때 발화한다.
    /// </remarks>
    public delegate UniTask<Popup> PopupFactory(PopupKey key, CancellationToken cancellationToken);

    /// <summary>
    /// 닫힌 팝업 인스턴스를 되돌려받는다. 기본 구현은 <c>Destroy</c>이며,
    /// 풀링을 쓰는 게임은 반납 로직으로 교체한다.
    /// </summary>
    public delegate void PopupReleaser(Popup popup);
}
