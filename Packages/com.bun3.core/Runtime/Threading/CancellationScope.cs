using System;
using System.Threading;

namespace Bun3.Core.Threading
{
    /// <summary>
    /// A structured cancellation-lifetime scope: a linked <see cref="CancellationTokenSource"/> in
    /// disposable form. Cancelling or disposing a scope cancels every child scope created from it, so
    /// a nested sequence can be torn down as a unit. Implements <see cref="IDisposable"/> for use with
    /// <c>using</c>; disposing cancels the scope.
    /// </summary>
    /// <remarks>
    /// This type depends only on the BCL — it knows nothing about Unity or any async library.
    /// Unity/UniTask conveniences (owner-lifetime creation, launching UniTask work) live in
    /// <c>CancellationScopeExtensions</c>.
    /// <para>
    /// Cancellation is cooperative: forward <see cref="Token"/> to the awaits inside a sequence so it
    /// propagates promptly. Child scopes are cancelled when this scope cancels; still dispose them
    /// (e.g. with <c>using</c>) when they outlive a single sequence, so their link is released.
    /// </para>
    /// </remarks>
    public sealed class CancellationScope : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly CancellationToken _token;
        private bool _disposed;

        private CancellationScope(CancellationToken parent)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(parent);
            // Cache the token so it stays readable — as an already-cancelled token — after Dispose,
            // instead of throwing ObjectDisposedException from _cts.Token.
            _token = _cts.Token;
        }

        /// <summary>The scope's cancellation token. Forward this to every await inside the scope.</summary>
        public CancellationToken Token => _token;

        /// <summary>True once the scope has been cancelled or disposed.</summary>
        public bool IsCancellationRequested => _token.IsCancellationRequested;

        /// <summary>Creates a root scope, optionally linked to an external parent token.</summary>
        public static CancellationScope Create(CancellationToken parent = default)
        {
            return new CancellationScope(parent);
        }

        /// <summary>
        /// Creates a child scope. Cancelling or disposing this scope cancels the child; the child can
        /// also be cancelled independently without affecting this scope. If this scope is already
        /// cancelled, the returned child is created already-cancelled.
        /// </summary>
        public CancellationScope CreateChild()
        {
            return new CancellationScope(_token);
        }

        /// <summary>Cancels the scope and every child linked to it. Idempotent.</summary>
        public void Cancel()
        {
            if (_disposed)
                return;
            _cts.Cancel();
        }

        /// <summary>Cancels the scope and releases the underlying token source. Idempotent.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
