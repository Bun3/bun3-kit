using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Bun3.Server.Rpc;
using Google.Protobuf;

namespace Bun3.Server.Players
{
    /// <summary>RpcConfig wrapper — also records requests allowed for unauthenticated sessions (login etc.).</summary>
    public sealed class PlayersConfig<TSession> where TSession : Session
    {
        /// <summary>Inner Rpc registration table. Pass this when creating the RpcServer.</summary>
        public RpcConfig<TSession> Rpc { get; } = new RpcConfig<TSession>();

        /// <summary>Request types allowed for unauthenticated sessions.</summary>
        internal HashSet<Type> UnauthenticatedTypes { get; } = new HashSet<Type>();

        /// <summary>Registers a regular request accessible only to authenticated sessions.</summary>
        public void OnRequest<TReq, TRes>(Func<TSession, TReq, ValueTask<Reply<TRes>>> handler)
            where TReq : class, IMessage<TReq>
            where TRes : class, IMessage<TRes>
            => Rpc.OnRequest(handler);

        /// <summary>Registers a request also allowed for unauthenticated sessions (login etc.).</summary>
        public void OnRequestUnauthenticated<TReq, TRes>(Func<TSession, TReq, ValueTask<Reply<TRes>>> handler)
            where TReq : class, IMessage<TReq>
            where TRes : class, IMessage<TRes>
        {
            Rpc.OnRequest(handler);
            UnauthenticatedTypes.Add(typeof(TReq));
        }
    }
}
