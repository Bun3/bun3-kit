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

        internal Dictionary<Type, Type> OperationTypes { get; } = new Dictionary<Type, Type>();

        /// <summary>Registers a persistent operation created from the authenticated actor and request.</summary>
        /// <remarks>The factory must only describe the operation; access state in Validate or Apply.</remarks>
        public void OnPlayerOperation<TPlayer, TReq, TRes>(Func<string, TReq, PlayerOperation<TPlayer, TRes>> factory)
            where TPlayer : Player
            where TReq : class, IMessage<TReq>
            where TRes : class, IMessage<TRes>
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            Rpc.OnRequest<TReq, TRes>(async (session, request) =>
            {
                var playerSession = (PlayerSession<TPlayer>)(object)session;
                var player = playerSession.Player;
                if (player == null) return Reply<TRes>.Fail(RpcStatus.Unauthenticated);
                PlayerOperation<TPlayer, TRes> operation;
                try { operation = factory(player.AccountKey, request); }
                catch (ArgumentException) { return Reply<TRes>.Fail(RpcStatus.InvalidOperation); }
                catch (FormatException) { return Reply<TRes>.Fail(RpcStatus.InvalidOperation); }
                if (operation == null || operation.Actor.Kind != PlayerOperationActorKind.Player || !StringComparer.Ordinal.Equals(operation.ActorKey, player.AccountKey))
                    return Reply<TRes>.Fail(RpcStatus.InvalidOperation);
                var result = await playerSession.ExecuteOperationAsync(operation).ConfigureAwait(false);
                return result.Status == 0 ? Reply<TRes>.Ok(result.Value!) : Reply<TRes>.Fail(result.Status);
            });
            OperationTypes.Add(typeof(TReq), typeof(TPlayer));
        }

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
