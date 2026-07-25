using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Core.Threading
{
    /// <summary>
    /// Unity- and UniTask-facing conveniences for <see cref="CancellationScope"/>, kept off the core
    /// type so it stays dependency-light. These extensions add the references to
    /// <see cref="MonoBehaviour"/> and UniTask.
    /// </summary>
    public static class CancellationScopeExtensions
    {
        /// <summary>
        /// Creates a <see cref="CancellationScope"/> tied to the component's lifetime: it cancels
        /// automatically when <paramref name="owner"/> is destroyed (via Unity's built-in
        /// <see cref="MonoBehaviour.destroyCancellationToken"/>).
        /// </summary>
        public static CancellationScope CreateCancellationScope(this MonoBehaviour owner)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            return CancellationScope.Create(owner.destroyCancellationToken);
        }

        /// <summary>
        /// Launches a fire-and-forget operation bound to the scope's token. Cancellation is silent;
        /// any other exception surfaces through UniTask's unobserved-exception handler.
        /// To wait for completion instead, await the operation directly with
        /// <see cref="CancellationScope.Token"/> (for example <c>await UniTask.WhenAll(...)</c>).
        /// </summary>
        public static void Run(this CancellationScope scope, Func<CancellationToken, UniTask> operation)
        {
            if (scope == null)
                throw new ArgumentNullException(nameof(scope));
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            operation(scope.Token).Forget();
        }
    }
}
