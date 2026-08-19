using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>
    /// An item instance held by an inventory. Unstackable definitions hold amount 1 per
    /// instance; stackable definitions (including currencies) merge amounts into a singleton
    /// instance per definition. Amounts are <see cref="BigNum"/> — long converts implicitly,
    /// and BigNum addition is lossy, absorbing terms outside the significant digits
    /// (idle-game amount semantics; full consumption is exactly Zero).
    /// <see cref="State"/> is game-owned opaque state (level, exp, options, etc.) —
    /// call <see cref="MarkChanged"/> after mutating it so save/sync tracking sees the change.
    /// </summary>
    /// <typeparam name="TState">Game-defined instance state type.</typeparam>
    public sealed class ItemInstance<TState>
    {
        private uint _flags;
        private long _expiresAtTicksUtc;

        internal ItemInstance(
            ItemInventory<TState> owner,
            long instanceId,
            ItemId item,
            BigNum quantity,
            uint flags,
            long expiresAtTicksUtc,
            TState state)
        {
            _owner = owner;
            InstanceId = instanceId;
            Item = item;
            Quantity = quantity;
            _flags = flags;
            _expiresAtTicksUtc = expiresAtTicksUtc;
            State = state;
        }

        internal ItemInventory<TState>? _owner;
        internal bool IsNew;
        internal bool Changed;

        /// <summary>Unique instance id — from the issuer seam or a loaded external authoritative id (DB, Steam).</summary>
        public long InstanceId { get; }

        /// <summary>Definition identifier (immutable).</summary>
        public ItemId Item { get; }

        /// <summary>Held amount. Always 1 for unstackables. Mutated only by the framework.</summary>
        public BigNum Quantity { get; internal set; }

        /// <summary>State bit flags — semantics belong to the game/platform (e.g. in use, locked,
        /// untradable). Instances matching the inventory's removeBlockingFlags mask cannot be
        /// consumed. The setter feeds change tracking automatically.</summary>
        public uint Flags
        {
            get => _flags;
            set
            {
                if (_flags != value)
                {
                    _flags = value;
                    MarkChanged();
                }
            }
        }

        /// <summary>Expiry time (UTC ticks). 0 = no expiry. Extension rules (accumulate/renew/cap)
        /// are defined by how the game updates this value. The framework has no clock, so expiry
        /// detection and handling happen via <see cref="ItemInventory{TState}.CollectExpired"/> with
        /// game-injected current time (expiry does not auto-delete). The setter feeds change
        /// tracking automatically.</summary>
        public long ExpiresAtTicksUtc
        {
            get => _expiresAtTicksUtc;
            set
            {
                if (_expiresAtTicksUtc != value)
                {
                    _expiresAtTicksUtc = value;
                    MarkChanged();
                }
            }
        }

        /// <summary>Game-owned state — the framework does not interpret it.</summary>
        public TState State { get; }

        /// <summary>Call after mutating <see cref="State"/> — feeds change tracking (Updated) and
        /// the inventory's onChanged (save cadence). Ignored after removal from the inventory.</summary>
        public void MarkChanged()
        {
            Changed = true;
            _owner?.OnInstanceChanged();
        }
    }
}
