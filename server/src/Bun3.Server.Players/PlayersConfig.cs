using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Bun3.Server.Rpc;
using Google.Protobuf;

namespace Bun3.Server.Players
{
    /// <summary>RpcConfig 래퍼 — 미인증 세션에도 허용할 요청(로그인 등)을 함께 기록한다.</summary>
    public sealed class PlayersConfig<TSession> where TSession : Session
    {
        /// <summary>내부 Rpc 등록표. RpcServer 생성 시 이걸 넘긴다.</summary>
        public RpcConfig<TSession> Rpc { get; } = new RpcConfig<TSession>();

        /// <summary>미인증 세션에도 허용된 요청 타입 목록.</summary>
        internal HashSet<Type> UnauthenticatedTypes { get; } = new HashSet<Type>();

        /// <summary>인증된 세션만 접근 가능한 일반 요청 등록.</summary>
        public void OnRequest<TReq, TRes>(Func<TSession, TReq, ValueTask<Reply<TRes>>> handler)
            where TReq : class, IMessage<TReq>
            where TRes : class, IMessage<TRes>
            => Rpc.OnRequest(TrackDispatchSettlement(handler));

        /// <summary>미인증 세션에도 허용되는 요청 등록 (로그인 등).</summary>
        public void OnRequestUnauthenticated<TReq, TRes>(Func<TSession, TReq, ValueTask<Reply<TRes>>> handler)
            where TReq : class, IMessage<TReq>
            where TRes : class, IMessage<TRes>
        {
            Rpc.OnRequest(TrackDispatchSettlement(handler));
            UnauthenticatedTypes.Add(typeof(TReq));
        }

        /// <summary>
        /// 핸들러 호출 구간을 IDispatchSettlement로 감싼다. 세션이 방금 자신의 요청
        /// 안에서 SignInAsync로 새 Player를 만든 직후, 그 응답을 보내기 전에 다른
        /// 세션의 중복 로그인이 이 세션을 킥해 응답이 유실되는 경합을 PlayerRegistry가
        /// 피할 수 있도록 "핸들러가 아직 처리 중"이라는 신호를 남긴다.
        /// </summary>
        private static Func<TSession, TReq, ValueTask<Reply<TRes>>> TrackDispatchSettlement<TReq, TRes>(
            Func<TSession, TReq, ValueTask<Reply<TRes>>> handler)
            where TReq : class, IMessage<TReq>
            where TRes : class, IMessage<TRes>
        {
            return async (session, request) =>
            {
                var tracker = session as IDispatchSettlement;
                tracker?.MarkDispatchStart();
                try
                {
                    return await handler(session, request).ConfigureAwait(false);
                }
                finally
                {
                    tracker?.MarkDispatchEnd();
                }
            };
        }
    }
}
