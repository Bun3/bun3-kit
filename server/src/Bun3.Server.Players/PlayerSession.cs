using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;
using Google.Protobuf;

namespace Bun3.Server.Players
{
    /// <summary>
    /// Session base with the Player lifecycle attached. Must be created through a factory
    /// wrapped by PlayerRegistry.Wrap (attaches the registry and allowlist).
    /// </summary>
    public abstract class PlayerSession<TPlayer> : RpcSession where TPlayer : Player
    {
        private PlayerRegistry<TPlayer>? _registry;
        private HashSet<Type>? _unauthenticatedTypes;
        private HashSet<Type>? _operationTypes;
        private int _signingIn;
        private readonly object _bindingSync = new object();
        private bool _sessionClosed;
        internal long BindingGeneration;

        /// <summary>Creates a session bound to the given connection.</summary>
        protected PlayerSession(IConnection connection) : base(connection) { }

        /// <summary>Non-null after authentication. The gate blocks unauthenticated requests, so it is never null in handlers.</summary>
        public TPlayer? Player { get; private set; }

        /// <summary>Whether this session is bound to a Player.</summary>
        public bool IsAuthenticated => Player != null;

        /// <summary>
        /// Framework entry point called after credential verification (the game's job). Handles fresh
        /// load / grace rebinding / duplicate-login transfer. Concurrent or repeated calls on the same
        /// session are rejected atomically with InvalidOperationException; under RejectNew an already
        /// connected account throws DuplicateLoginException. On failure (exception) the guard is
        /// released, allowing retry.
        /// </summary>
        public async ValueTask<SignInResult<TPlayer>> SignInAsync(string accountKey)
        {
            PlayerExecutionLane.ThrowIfOwned();
            if (Interlocked.CompareExchange(ref _signingIn, 1, 0) != 0)
            {
                throw new InvalidOperationException("Session is already authenticated or SignInAsync is in progress.");
            }

            try
            {
                return await RequireRegistry().SignInAsync(this, accountKey).ConfigureAwait(false);
            }
            catch
            {
                Interlocked.Exchange(ref _signingIn, 0);
                throw;
            }
        }

        /// <summary>Session-closed hook (called after detach handling).
        /// Note: a session kicked by duplicate login (NewWins) runs this with Player already
        /// detached to null at transfer time — do not try to save the Player here.
        /// Save only in Player.OnRetiredAsync.</summary>
        protected virtual ValueTask OnPlayerSessionClosedAsync(Exception? error) => default;

        /// <summary>Gates requests by authentication state and allowlist. Sealed because the Players layer owns it.</summary>
        protected sealed override int OnGateRequest(Type requestType) =>
            Player != null || (_unauthenticatedTypes != null && _unauthenticatedTypes.Contains(requestType))
                ? RpcStatus.Ok
                : RpcStatus.Unauthenticated;

        /// <summary>Installs the ambient holder synchronously for the entire session actor.</summary>
        protected sealed override IDisposable? EnterExecutionScope() => PlayerExecutionLane.Install(this);

        /// <summary>Protects the handler and any login lease through response object creation.</summary>
        protected sealed override async ValueTask<(int Status, IMessage? Response)> InvokeRequestAsync(
            IMessage request, RpcRequestHandler handler)
        {
            using var admission = RequireRegistry().EnterWork();
            if (!admission.Entered) return (RpcStatus.InvalidOperation, null);
            var lane = PlayerExecutionLane.Current!;
            lane.BeginRequest();
            try
            {
                if (_operationTypes != null && _operationTypes.Contains(request.GetType()))
                {
                    if (Player == null) return (RpcStatus.Unauthenticated, null);
                    return await handler(this, request).ConfigureAwait(false);
                }
                var player = Player;
                if (player != null)
                {
                    var generation = BindingGeneration;
                    var registry = RequireRegistry();
                    lane.TakeOwnership(await registry.AcquireAsync(player.AccountKey).ConfigureAwait(false));
                    if (!registry.IsCurrent(this, player, generation)) return (RpcStatus.Unauthenticated, null);
                    lane.SetMutation(player, registry.SupportsAudit);
                }
                else if (_unauthenticatedTypes == null || !_unauthenticatedTypes.Contains(request.GetType()))
                {
                    return (RpcStatus.Unauthenticated, null);
                }
                return await handler(this, request).ConfigureAwait(false);
            }
            finally { lane.EndRequest(); }
        }

        /// <summary>Executes posted player work only while its captured binding remains current.</summary>
        protected sealed override async ValueTask OnPostedWorkAsync(Func<ValueTask> work)
        {
            using var admission = RequireRegistry().EnterWork();
            if (!admission.Entered) return;
            var player = Player;
            if (player == null) return;
            var generation = BindingGeneration;
            var lane = PlayerExecutionLane.Current!;
            var registry = RequireRegistry();
            lane.BeginRequest();
            try
            {
                lane.TakeOwnership(await registry.AcquireAsync(player.AccountKey).ConfigureAwait(false));
                if (registry.IsCurrent(this, player, generation))
                { lane.SetMutation(player, registry.SupportsAudit); await work().ConfigureAwait(false); }
            }
            finally { lane.EndRequest(); }
        }

        /// <summary>Session close handling — delegates detach to the registry, then calls the game hook. Sealed.</summary>
        protected sealed override async ValueTask OnSessionClosedAsync(Exception? error)
        {
            lock (_bindingSync) _sessionClosed = true;
            var registry = _registry;
            if (registry != null)
            {
                await registry.HandleSessionClosedAsync(this).ConfigureAwait(false);
            }

            await OnPlayerSessionClosedAsync(error).ConfigureAwait(false);
        }

        internal void AttachPlayers(PlayerRegistry<TPlayer> registry, HashSet<Type> unauthenticatedTypes, HashSet<Type> operationTypes)
        {
            _registry = registry;
            _unauthenticatedTypes = unauthenticatedTypes;
            _operationTypes = operationTypes;
        }

        internal ValueTask<PlayerOperationResult<TResult>> ExecuteOperationAsync<TResult>(PlayerOperation<TPlayer, TResult> operation)
            where TResult : class => RequireRegistry().ExecuteForSessionAsync(this, operation, default);

        /// <summary>Freezes operation updates for delivery only after the common transaction commits.</summary>
        public sealed override ValueTask SendUpdateAsync(IMessage update)
        {
            var notifications = PlayerExecutionLane.Current?.Notifications;
            if (notifications == null) return base.SendUpdateAsync(update);
            var player = Player;
            if (player == null || !PlayerExecutionLane.Current!.CanMutate(player))
                throw new InvalidOperationException("Operation updates require an active participant mutation.");
            try { notifications.Add(player, this, EncodeUpdate(update)); }
            catch (Exception ex) { notifications.Error = ex; throw; }
            return default;
        }

        internal void SetPlayer(TPlayer? player) => Player = player;

        internal bool TryBindPlayer(TPlayer player, long generation)
        {
            // Closing either observes this binding or prevents it, even if it completed during loading.
            lock (_bindingSync)
            {
                if (_sessionClosed || !Connection.IsOpen) return false;
                BindingGeneration = generation;
                Player = player;
                return true;
            }
        }

        private PlayerRegistry<TPlayer> RequireRegistry() =>
            _registry ?? throw new InvalidOperationException(
                "Registry not attached — PlayerSession must be created through a factory wrapped by PlayerRegistry.Wrap.");
    }
}
