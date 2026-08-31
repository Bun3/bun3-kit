// UniTask entry points: awaitable play/stop built on VoiceSlot.Completion sources.
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        /// <summary>Plays and completes when the voice ends (natural end, steal, or Stop). Cancelling <paramref name="ct"/> stops the voice.</summary>
        public UniTask PlayAsync(SoundDef def, CancellationToken ct = default)
            => WaitInternal(Play(def), ct);

        /// <summary>Positional variant of <see cref="PlayAsync(SoundDef, CancellationToken)"/>.</summary>
        public UniTask PlayAsync(SoundDef def, Vector3 position, CancellationToken ct = default)
            => WaitInternal(Play(def, position), ct);

        /// <summary>Following variant of <see cref="PlayAsync(SoundDef, CancellationToken)"/>.</summary>
        public UniTask PlayAsync(SoundDef def, Transform follow, CancellationToken ct = default)
            => WaitInternal(Play(def, follow), ct);

        /// <summary>
        /// Awaits the given handle's voice ending. Lazily creates the pooled completion source
        /// the first time a handle is awaited — no allocation on the hot play path otherwise.
        /// </summary>
        internal UniTask WaitInternal(SoundHandle handle, CancellationToken ct)
        {
            if (!TryGetSlot(handle, out var slot))
            {
                return UniTask.CompletedTask;
            }
            ref var voice = ref Table.Slots[slot];
            voice.Completion ??= AutoResetUniTaskCompletionSource.Create();
            var task = voice.Completion.Task;
            return ct.CanBeCanceled ? WithCancellation(task, handle, ct) : task;
        }

        /// <summary>
        /// Cancellation is a cold path: <see cref="UniTask.AttachExternalCancellation"/> and the
        /// enclosing async state machine both allocate here, which is acceptable off the hot
        /// play/tick paths.
        /// </summary>
        private static async UniTask WithCancellation(UniTask task, SoundHandle handle, CancellationToken ct)
        {
            try
            {
                await task.AttachExternalCancellation(ct);
            }
            catch (OperationCanceledException)
            {
                handle.Stop();
                throw;
            }
        }
    }
}
