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
        /// <param name="def">Sound definition to play.</param>
        /// <param name="ct">Cancelling stops the voice and throws <see cref="OperationCanceledException"/>.</param>
        /// <param name="fadeIn">When > 0, ramps volume from silence over this many seconds.</param>
        public UniTask PlayAsync(SoundDef def, CancellationToken ct = default, float fadeIn = 0f)
            => WaitInternal(Play(def, fadeIn), ct);

        /// <summary>Positional variant of <see cref="PlayAsync(SoundDef, CancellationToken, float)"/>.</summary>
        /// <param name="def">Sound definition to play.</param>
        /// <param name="position">Fixed world position for the voice.</param>
        /// <param name="ct">Cancelling stops the voice and throws <see cref="OperationCanceledException"/>.</param>
        /// <param name="fadeIn">When > 0, ramps volume from silence over this many seconds.</param>
        public UniTask PlayAsync(SoundDef def, Vector3 position, CancellationToken ct = default, float fadeIn = 0f)
            => WaitInternal(Play(def, position, fadeIn), ct);

        /// <summary>Following variant of <see cref="PlayAsync(SoundDef, CancellationToken, float)"/>.</summary>
        /// <param name="def">Sound definition to play.</param>
        /// <param name="follow">Transform to track every frame.</param>
        /// <param name="ct">Cancelling stops the voice and throws <see cref="OperationCanceledException"/>.</param>
        /// <param name="fadeIn">When > 0, ramps volume from silence over this many seconds.</param>
        public UniTask PlayAsync(SoundDef def, Transform follow, CancellationToken ct = default, float fadeIn = 0f)
            => WaitInternal(Play(def, follow, fadeIn), ct);

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
