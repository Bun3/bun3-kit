using System.Threading.Tasks;
using Bun3.Server.Rpc;
using Google.Protobuf;

namespace Bun3.Server.Players
{
    /// <summary>
    /// accountKey당 1개, 재접속에 살아남는 단위. 상태(재화·인벤토리 등)는 이 파생
    /// 클래스에 둔다. 훅들은 레지스트리의 계정 키 스트라이프 락 안에서 실행되므로
    /// 훅 안에서 SignInAsync/Kick을 재호출하면 안 된다(교착).
    /// 킥된 옛 세션의 잔여 핸들러가 소유권 이전 직후 잠시 같은 Player를 볼 수 있다 —
    /// 저장 지점은 OnRetiredAsync 하나로 고정하는 이유다.
    /// </summary>
    public abstract class Player
    {
        /// <summary>불투명 신원 키 (권장 규약 "provider:subject"). SignIn 시 설정된다.</summary>
        public string AccountKey { get; internal set; } = "";

        /// <summary>접속 중이면 현재 세션, 유예 중이면 null.</summary>
        public RpcSession? CurrentSession { get; internal set; }

        /// <summary>현재 세션에 접속 중인지 여부.</summary>
        public bool IsConnected => CurrentSession != null;

        /// <summary>세션 바인딩 직후. isReconnect=true면 유예 재바인딩 또는 중복 로그인 이전.</summary>
        protected internal virtual ValueTask OnAttachedAsync(bool isReconnect) => default;

        /// <summary>연결 끊김(유예 시작) 시.</summary>
        protected internal virtual ValueTask OnDetachedAsync() => default;

        /// <summary>유예 만료·RetireAll 시 — 저장 지점. 이후 레지스트리에서 제거된다.</summary>
        protected internal virtual ValueTask OnRetiredAsync() => default;

        /// <summary>접속 중이면 현재 세션으로 푸시하고 true, 유예 중이면 false.</summary>
        public async ValueTask<bool> PushUpdateAsync(IMessage update)
        {
            var session = CurrentSession;
            if (session == null)
            {
                return false;
            }

            await session.SendUpdateAsync(update).ConfigureAwait(false);
            return true;
        }
    }
}
