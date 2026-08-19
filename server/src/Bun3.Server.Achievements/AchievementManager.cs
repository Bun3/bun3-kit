using System;
using Bun3.Server.Core;

namespace Bun3.Server.Achievements
{
    /// <summary>
    /// Per-player tracker for achievement progress, completion, claiming, and availability. Owned
    /// by the game's Player-derived class and lock-free under the same premise as player state
    /// (accessed only inside the session actor).
    /// Condition evaluation is the game's job — the game routes its events by identifier/tag and
    /// calls <see cref="Increase"/> / <see cref="IncreaseByTag(AchievementTag, long)"/>.
    /// Hot paths are allocation-free: array indexing, integer arithmetic, and cached delegate calls.
    /// The completion count is monotonic, so the same completion never fires twice.
    /// Repeatable achievements accumulate progress; claiming subtracts one target — while a claim
    /// is pending the UI shows min(Progress, Target)/Target as "10/10 [Claim]".
    /// </summary>
    /// <typeparam name="TDef">Game achievement definition type.</typeparam>
    public sealed partial class AchievementManager<TDef> where TDef : AchievementDefinition
    {
        private static readonly Func<long> DefaultClock = () => DateTime.UtcNow.Ticks;

        private readonly AchievementCatalog<TDef> _catalog;
        private readonly AchievementState[] _states;
        private readonly Action? _onDirty;
        private readonly Func<long> _utcNowTicks;
        private readonly Transaction? _transaction;

        /// <summary>Hook fired right after completion — (identifier, definition, new completion
        /// count). Called after state updates finish, so the hook may Increase/Activate other
        /// achievements (chain/tier setups). Auto-rewards: call <see cref="TryClaim"/> here, then
        /// the game grants.</summary>
        public Action<AchievementId, TDef, int>? OnCompleted { get; set; }

        /// <summary>Number of tracked achievements (= catalog definition count).</summary>
        public int Count => _states.Length;

        /// <summary>Creates the tracker. Initial availability follows each definition's
        /// InitialAvailability.</summary>
        /// <param name="catalog">Achievement definition catalog.</param>
        /// <param name="onDirty">Pass the Player's MarkDirty to flag the save sweep whenever state
        /// actually changes.</param>
        /// <param name="utcNowTicks">Completion time source (default UTC now) — an injection point
        /// for tests.</param>
        /// <param name="transaction">Session-wide transaction (<see cref="Transaction"/>) — when
        /// set, changes made while it is open are reversible and dirty/completion notifications are
        /// buffered until commit. Nothing is snapshotted when no transaction is open.</param>
        public AchievementManager(
            AchievementCatalog<TDef> catalog,
            Action? onDirty = null,
            Func<long>? utcNowTicks = null,
            Transaction? transaction = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _states = new AchievementState[catalog.Count];
            _onDirty = onDirty;
            _utcNowTicks = utcNowTicks ?? DefaultClock;
            _transaction = transaction;
            _txCaptured = new bool[catalog.Count];
            for (var i = 0; i < _states.Length; i++)
            {
                _states[i].Availability = catalog.GetDefinition(new AchievementId(i)).InitialAvailability;
            }
        }

        private ref AchievementState StateOf(AchievementId id)
        {
            if (!_catalog.Contains(id))
            {
                throw new ArgumentOutOfRangeException(nameof(id), "Not an identifier of this catalog.");
            }

            return ref _states[id.Index];
        }

        // ── Progress ─────────────────────────────────────────────────────────

        /// <summary>Increases progress and returns the new completion count. No-op (0) unless
        /// Active. amount 0 re-evaluates completion without changing progress (for re-judging
        /// after Restore with a lowered target).</summary>
        /// <exception cref="ArgumentOutOfRangeException">When amount is negative, or id is not an identifier of this catalog.</exception>
        public int Increase(AchievementId id, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Progress increase cannot be negative.");

            return IncreaseCore(id, _catalog.GetDefinition(id), amount);
        }

        /// <summary>Increases progress on every Active achievement carrying the tag and returns the
        /// total new completions. Each achievement is an independent counter, so each receives the
        /// same amount (no shared ledger) — this is what makes inclusive simultaneous tiers work.
        /// Targets are snapshotted at call time: achievements Activated inside a hook (chain tiers)
        /// do not receive this batch and start accruing from the next event (new-accrual semantics).
        /// Achievements Locked mid-batch are skipped.</summary>
        /// <exception cref="ArgumentOutOfRangeException">When amount is negative.</exception>
        public int IncreaseByTag(AchievementTag tag, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Progress increase cannot be negative.");

            ReadOnlySpan<AchievementId> indices = _catalog.GetIndicesByTag(tag);
            Span<ulong> eligible = stackalloc ulong[(indices.Length + 63) >> 6];   // Snapshot — stack memory, allocation-free and reentrancy-safe.
            for (var i = 0; i < indices.Length; i++)
            {
                if (_states[indices[i].Index].Availability == AchievementStatus.Active)
                {
                    eligible[i >> 6] |= 1UL << (i & 63);
                }
            }

            var total = 0;
            for (var i = 0; i < indices.Length; i++)
            {
                if ((eligible[i >> 6] & (1UL << (i & 63))) == 0)
                {
                    continue;
                }

                var id = indices[i];
                total += IncreaseCore(id, _catalog.GetDefinition(id), amount);
            }

            return total;
        }

        /// <summary>Increases only the tagged achievements passing the filter — for condition-value
        /// comparison. Passing a static lambda with <typeparamref name="TArg"/> keeps it
        /// allocation-free:
        /// <c>IncreaseByTag(tag, 1, level, static (def, lv) =&gt; def.MinLevel &lt;= lv)</c>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">When amount is negative.</exception>
        public int IncreaseByTag<TArg>(AchievementTag tag, long amount, TArg arg, Func<TDef, TArg, bool> filter)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Progress increase cannot be negative.");
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            ReadOnlySpan<AchievementId> indices = _catalog.GetIndicesByTag(tag);
            Span<ulong> eligible = stackalloc ulong[(indices.Length + 63) >> 6];   // Same snapshot semantics as IncreaseByTag.
            for (var i = 0; i < indices.Length; i++)
            {
                if (_states[indices[i].Index].Availability == AchievementStatus.Active && filter(_catalog.GetDefinition(indices[i]), arg))
                {
                    eligible[i >> 6] |= 1UL << (i & 63);
                }
            }

            var total = 0;
            for (var i = 0; i < indices.Length; i++)
            {
                if ((eligible[i >> 6] & (1UL << (i & 63))) == 0)
                {
                    continue;
                }

                var id = indices[i];
                total += IncreaseCore(id, _catalog.GetDefinition(id), amount);
            }

            return total;
        }

        /// <summary>Sets progress and returns the new completion count. No-op (0) unless Active.
        /// Lowering progress never decreases the completion count (monotonic rule — the basis of
        /// duplicate-completion prevention). Uses: syncing derived values at login (level,
        /// collection counts), saturating conversion of huge currencies.</summary>
        /// <exception cref="ArgumentOutOfRangeException">When value is negative, or id is not an identifier of this catalog.</exception>
        public int SetProgress(AchievementId id, long value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Progress cannot be negative.");

            var def = _catalog.GetDefinition(id);
            ref var state = ref StateOf(id);
            if (state.Availability != AchievementStatus.Active)
            {
                return 0;
            }

            var newProgress = !def.Repeatable && value > def.Target ? def.Target : value;
            return ApplyProgress(id, def, ref state, newProgress);
        }

        private int IncreaseCore(AchievementId id, TDef def, long amount)
        {
            ref var state = ref StateOf(id);
            if (state.Availability != AchievementStatus.Active)
            {
                return 0;
            }

            var progress = state.Progress;
            long newProgress;
            if (def.Repeatable)
            {
                // Overflow clamp — progress ≥ 0 and amount ≥ 0, so the subtraction comparison is safe.
                newProgress = amount > long.MaxValue - progress ? long.MaxValue : progress + amount;
            }
            else
            {
                newProgress = amount >= def.Target - progress ? def.Target : progress + amount;
            }

            return ApplyProgress(id, def, ref state, newProgress);
        }

        private int ApplyProgress(AchievementId id, TDef def, ref AchievementState state, long newProgress)
        {
            Capture(id);
            var changed = state.Progress != newProgress;
            state.Progress = newProgress;

            int newCompletions;
            if (def.Repeatable)
            {
                // Invariant: CompletedCount = ClaimedCount + Progress/Target — pairs with the claim subtraction.
                var pendingByProgress = newProgress / def.Target;
                var delta = pendingByProgress - (state.CompletedCount - state.ClaimedCount);
                if (delta <= 0)
                {
                    newCompletions = 0;
                }
                else
                {
                    var headroom = int.MaxValue - state.CompletedCount;
                    newCompletions = delta > headroom ? headroom : (int)delta;
                }
            }
            else
            {
                newCompletions = newProgress == def.Target && state.CompletedCount == 0 ? 1 : 0;
            }

            if (newCompletions > 0)
            {
                state.CompletedCount += newCompletions;
                state.LastCompletedAtUtcTicks = _utcNowTicks();
                changed = true;
            }

            if (changed)
            {
                NotifyDirty();
            }

            if (newCompletions > 0)
            {
                NotifyCompleted(id, newCompletions);
            }

            return newCompletions;
        }

        // ── Claiming ─────────────────────────────────────────────────────────

        /// <summary>If an unclaimed completion exists, increments the claim count and returns true.
        /// Repeatable achievements subtract one target from progress (starting the next cycle).
        /// Works regardless of availability — reward settlement must remain possible for
        /// rotated-out achievements. The game grants after a true return; claim all with
        /// <c>while (TryClaim(id))</c>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">When id is not an identifier of this catalog.</exception>
        public bool TryClaim(AchievementId id)
        {
            ref var state = ref StateOf(id);
            if (state.ClaimedCount >= state.CompletedCount)
            {
                return false;
            }

            Capture(id);
            state.ClaimedCount++;
            var def = _catalog.GetDefinition(id);
            if (def.Repeatable)
            {
                var remaining = state.Progress - def.Target;
                state.Progress = remaining > 0 ? remaining : 0;   // Floor at 0 to tolerate a lenient Restore.
            }

            NotifyDirty();
            return true;
        }

        /// <summary>Claimable count (completions − claims).</summary>
        /// <exception cref="ArgumentOutOfRangeException">When id is not an identifier of this catalog.</exception>
        public int GetClaimableCount(AchievementId id)
        {
            ref readonly var state = ref StateOf(id);
            return state.CompletedCount - state.ClaimedCount;
        }

        // ── Status ───────────────────────────────────────────────────────────

        /// <summary>Derives the lifecycle status. Locked/Ready return the stored value; the Active
        /// family derives from counters: claimable pending → Completed, non-repeatable fully
        /// claimed → Claimed (terminal), otherwise Active (repeatables return to Active after
        /// claiming). Stored-vs-derived mismatch is impossible by construction — Completed/Claimed
        /// are never stored.</summary>
        /// <exception cref="ArgumentOutOfRangeException">When id is not an identifier of this catalog.</exception>
        public AchievementStatus GetStatus(AchievementId id)
        {
            ref readonly var state = ref StateOf(id);
            if (state.Availability != AchievementStatus.Active)
            {
                return state.Availability;
            }
            if (state.CompletedCount > state.ClaimedCount)
            {
                return AchievementStatus.Completed;
            }
            if (state.CompletedCount > 0 && !_catalog.GetDefinition(id).Repeatable)
            {
                return AchievementStatus.Claimed;
            }

            return AchievementStatus.Active;
        }

        /// <summary>Locked → Ready transition (opened — listed, but not progressing yet). False in
        /// any other state. "Open the child on completion/claim" is done by the game in OnCompleted
        /// or its claim handler.</summary>
        /// <exception cref="ArgumentOutOfRangeException">When id is not an identifier of this catalog.</exception>
        public bool Unlock(AchievementId id)
        {
            ref var state = ref StateOf(id);
            if (state.Availability != AchievementStatus.Locked)
            {
                return false;
            }

            Capture(id);
            state.Availability = AchievementStatus.Ready;
            NotifyDirty();
            return true;
        }

        /// <summary>Locked/Ready → Active transition (progress starts). False if already Active.</summary>
        /// <exception cref="ArgumentOutOfRangeException">When id is not an identifier of this catalog.</exception>
        public bool Activate(AchievementId id)
        {
            ref var state = ref StateOf(id);
            if (state.Availability == AchievementStatus.Active)
            {
                return false;
            }

            Capture(id);
            state.Availability = AchievementStatus.Active;
            NotifyDirty();
            return true;
        }

        /// <summary>→ Locked transition (closed — rotated out, etc.). Counters are kept (rewinding
        /// is <see cref="Reset"/>). False if already Locked. Settling pending claims (mail, etc.)
        /// is the game's job.</summary>
        /// <exception cref="ArgumentOutOfRangeException">When id is not an identifier of this catalog.</exception>
        public bool Lock(AchievementId id)
        {
            ref var state = ref StateOf(id);
            if (state.Availability == AchievementStatus.Locked)
            {
                return false;
            }

            Capture(id);
            state.Availability = AchievementStatus.Locked;
            NotifyDirty();
            return true;
        }

        // ── Persistence ──────────────────────────────────────────────────────

        /// <summary>Views the state without copying — the game iterates this for save serialization.</summary>
        /// <exception cref="ArgumentOutOfRangeException">When id is not an identifier of this catalog.</exception>
        public ref readonly AchievementState GetState(AchievementId id) => ref StateOf(id);

        /// <summary>Load-time restore — fires neither hooks nor dirty. Invariant violations
        /// (negatives, claims &gt; completions, multiple completions on a non-repeatable, derived
        /// status stored in Availability) throw to surface save-data corruption; only excess
        /// progress on non-repeatables is clamped to the target (handles balance patches lowering
        /// the target — re-judge with the next Increase(id, 0)).
        /// Restoring a state whose completion count lags its progress fires the difference in bulk
        /// on the next Increase/SetProgress (at-least-once — the safe direction for crash recovery
        /// mid-completion).</summary>
        /// <exception cref="ArgumentException">When the state violates an invariant.</exception>
        /// <exception cref="ArgumentOutOfRangeException">When id is not an identifier of this catalog.</exception>
        public void Restore(AchievementId id, in AchievementState state)
        {
            if (state.Progress < 0 || state.CompletedCount < 0 || state.ClaimedCount < 0 || state.LastCompletedAtUtcTicks < 0)
            {
                throw new ArgumentException("Achievement state contains a negative value.", nameof(state));
            }
            if (state.ClaimedCount > state.CompletedCount)
            {
                throw new ArgumentException("Claim count exceeds completion count.", nameof(state));
            }
            if ((uint)state.Availability > (uint)AchievementStatus.Active)
            {
                throw new ArgumentException($"Only Locked/Ready/Active may be stored in Availability ({state.Availability}) — Completed/Claimed are derived.", nameof(state));
            }

            var def = _catalog.GetDefinition(id);
            if (!def.Repeatable && state.CompletedCount > 1)
            {
                throw new ArgumentException($"Non-repeatable achievement '{def.Id}' has a completion count above 1 ({state.CompletedCount}).", nameof(state));
            }

            ref var slot = ref StateOf(id);
            slot = state;
            if (!def.Repeatable && slot.Progress > def.Target)
            {
                slot.Progress = def.Target;
            }
        }

        /// <summary>Rewinds the counters (progress, completions, claims, timestamp) to zero — for
        /// daily/weekly cycle swaps. Availability is untouched (transitions go through
        /// Unlock/Activate/Lock separately). The completion count is monotonic, so SetProgress(0)
        /// cannot re-complete — this is the only place counters rewind together. Dirty fires once
        /// if anything changed; no hooks. Settle unclaimed rewards (mail, etc.) before Reset.</summary>
        /// <exception cref="ArgumentOutOfRangeException">When id is not an identifier of this catalog.</exception>
        public void Reset(AchievementId id)
        {
            ref var state = ref StateOf(id);
            if (state.Progress == 0 && state.CompletedCount == 0 && state.ClaimedCount == 0 && state.LastCompletedAtUtcTicks == 0)
            {
                return;
            }

            Capture(id);
            state.Progress = 0;
            state.CompletedCount = 0;
            state.ClaimedCount = 0;
            state.LastCompletedAtUtcTicks = 0;
            NotifyDirty();
        }
    }
}
