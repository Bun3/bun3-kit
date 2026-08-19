using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// Base component for game popup prefabs. <see cref="PopupStack"/> owns the lifecycle;
    /// the game implements only open/close animation and back-key response via virtual methods.
    /// </summary>
    /// <remarks>
    /// <see cref="Key"/>/<see cref="Layer"/>/<see cref="Phase"/> are set by the stack — the game only reads them.
    /// Instance creation (<see cref="PopupFactory"/>) and release (<see cref="PopupReleaser"/>) belong to the game,
    /// so this class never touches transform/canvas placement.
    /// Open/close animation is done by overriding <see cref="UIView.PlayShowAsync"/>/<see cref="UIView.PlayHideAsync"/> —
    /// the stack awaits completion during <see cref="PopupPhase.Opening"/>/<see cref="PopupPhase.Closing"/>.
    /// <br/>
    /// Partial layout: this file (lifecycle/hooks) / CloseScope (close locking) / Interaction (input guard, dim click).
    /// </remarks>
    public abstract partial class Popup : UIView
    {
        [SerializeField]
        [Tooltip("Dim background enabled only when this is the topmost popup. Leave empty for popups without a dim.")]
        private GameObject _backgroundDim;

        private UniTaskCompletionSource _closedSource;

        /// <summary>
        /// Dim background. Null means no dim. On every order change the stack enables the dim of
        /// only the <b>topmost popup that has one</b> — a dimless popup floating above it keeps
        /// this popup's dim active.
        /// </summary>
        public GameObject BackgroundDim
        {
            get => _backgroundDim;
            set => _backgroundDim = value;
        }

        /// <summary>Key that opened this instance. Retains the last value when not in a stack.</summary>
        public PopupKey Key { get; private set; }

        /// <summary>Sorting layer. Higher is on top; within a layer, later pushes are on top.</summary>
        public int Layer { get; private set; }

        /// <summary>Current lifecycle phase.</summary>
        public PopupPhase Phase { get; private set; }

        /// <summary>Owning stack, or null when not in a stack.</summary>
        public PopupStack Stack { get; private set; }

        /// <summary>Marks that a close was requested during the open transition and must run after it completes.</summary>
        internal bool CloseRequested;

        /// <summary>Closes this popup via its owning stack. Ignored when not in a stack.</summary>
        public void Close() => Stack?.Close(this);

        /// <summary>
        /// Waits until this popup is closed and removed from the stack. Completes immediately
        /// if already closed. Used for confirm-dialog responses, reward animation chains, etc.
        /// </summary>
        public UniTask WaitUntilClosedAsync()
        {
            if (Phase == PopupPhase.None)
                return UniTask.CompletedTask;

            _closedSource ??= new UniTaskCompletionSource();
            return _closedSource.Task;
        }

        /// <summary>
        /// Called when the back key (ESC/Android back) is routed to this popup. Return
        /// <c>true</c> (default) to proceed with closing, or <c>false</c> to refuse
        /// (the key press is still consumed).
        /// </summary>
        protected internal virtual bool OnBackRequested() => true;

        /// <summary>
        /// Notified by the stack to every popup whenever stack order changes (open/close/Focus).
        /// Dim toggling (<see cref="BackgroundDim"/>) is handled by the stack separately from this
        /// hook, so add only extra presentation here (sound, focus moves). Default does nothing.
        /// </summary>
        /// <param name="stackIndex">Index within the stack (0 = bottom).</param>
        /// <param name="isTopmost">Whether this is the topmost popup of the whole stack.</param>
        protected internal virtual void OnStackOrderChanged(int stackIndex, bool isTopmost) { }

        internal void Attach(PopupStack stack, PopupKey key, int layer)
        {
            Stack = stack;
            Key = key;
            Layer = layer;
            Phase = PopupPhase.Opening;
            CloseRequested = false;
            _closeScopeCount = 0; // Pool reuse: a previous session's locks must not leak into the new session.

            SetUpInteractionForSession();
            OnAttached();
        }

        /// <summary>Reset point for assembly-internal derived types at session start (stack insertion).</summary>
        private protected virtual void OnAttached() { }

        internal void SetPhase(PopupPhase phase) => Phase = phase;

        internal void Detach()
        {
            // Even on a forced release (Clear) while locked, notify so presentation
            // (spinner/raycast block) does not stay on, and bump the generation to invalidate
            // late Dispose calls from surviving previous-session scopes.
            if (_closeScopeCount > 0)
            {
                _closeScopeCount = 0;
                OnCloseBlockedChanged(false);
            }

            _closeScopeVersion++;

            Phase = PopupPhase.None;
            Stack = null;

            var source = _closedSource;
            _closedSource = null;
            source?.TrySetResult();
        }
    }

    /// <summary>
    /// Base for popups that return a result. Inherited by popups that must hand one value
    /// back to the caller on close, such as confirm dialogs (<c>Popup&lt;bool&gt;</c>) or
    /// item pickers (<c>Popup&lt;ItemInstance&gt;</c>).
    /// </summary>
    /// <remarks>
    /// Popup code calls <see cref="SetResult"/> before closing; the caller receives it via
    /// <see cref="WaitForResultAsync"/> (or <see cref="PopupStack.PushForResultAsync{TResult}"/>).
    /// If closed without <see cref="SetResult"/> (back key, cancel button, etc.),
    /// <c>defaultResult</c> is returned — "cancel" needs no extra code.
    /// The result resets per session on pool reuse.
    /// </remarks>
    public abstract class Popup<TResult> : Popup
    {
        private TResult _result;
        private bool _hasResult;

        /// <summary>Records the result before closing. The last value wins when called multiple times.</summary>
        protected void SetResult(TResult result)
        {
            _result = result;
            _hasResult = true;
        }

        /// <summary>
        /// Waits until closed, then returns the result — <paramref name="defaultResult"/> if closed
        /// without <see cref="SetResult"/>. Completes immediately if already closed.
        /// </summary>
        public async UniTask<TResult> WaitForResultAsync(TResult defaultResult = default)
        {
            await WaitUntilClosedAsync();
            return _hasResult ? _result : defaultResult;
        }

        private protected override void OnAttached()
        {
            _result = default;
            _hasResult = false;
        }
    }
}
