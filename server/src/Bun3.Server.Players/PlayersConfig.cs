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
            => Rpc.OnRequest(handler);

        /// <summary>미인증 세션에도 허용되는 요청 등록 (로그인 등).</summary>
        public void OnRequestUnauthenticated<TReq, TRes>(Func<TSession, TReq, ValueTask<Reply<TRes>>> handler)
            where TReq : class, IMessage<TReq>
            where TRes : class, IMessage<TRes>
        {
            Rpc.OnRequest(handler);
            UnauthenticatedTypes.Add(typeof(TReq));
        }
    }
}
