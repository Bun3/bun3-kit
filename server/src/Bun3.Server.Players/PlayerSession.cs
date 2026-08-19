using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;

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
        private int _signingIn;

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

        /// <summary>Session close handling — delegates detach to the registry, then calls the game hook. Sealed.</summary>
        protected sealed override async ValueTask OnSessionClosedAsync(Exception? error)
        {
            var registry = _registry;
            if (registry != null)
            {
                await registry.HandleSessionClosedAsync(this).ConfigureAwait(false);
            }

            await OnPlayerSessionClosedAsync(error).ConfigureAwait(false);
        }

        internal void AttachPlayers(PlayerRegistry<TPlayer> registry, HashSet<Type> unauthenticatedTypes)
        {
            _registry = registry;
            _unauthenticatedTypes = unauthenticatedTypes;
        }

        internal void SetPlayer(TPlayer? player) => Player = player;

        private PlayerRegistry<TPlayer> RequireRegistry() =>
            _registry ?? throw new InvalidOperationException(
                "Registry not attached — PlayerSession must be created through a factory wrapped by PlayerRegistry.Wrap.");
    }
}
