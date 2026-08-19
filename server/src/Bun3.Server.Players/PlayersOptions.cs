using System;

namespace Bun3.Server.Players
{
    /// <summary>How a new login is handled while the same account is connected.</summary>
    public enum DuplicateLoginPolicy
    {
        /// <summary>Kick the existing connection and rebind to the new session (default).</summary>
        NewWins,

        /// <summary>Reject the new login — SignInAsync throws DuplicateLoginException.</summary>
        RejectNew,
    }

    /// <summary>PlayerRegistry behavior options. Hosting (AddPlayerServer) binds from
    /// configuration section "Bun3:Players", then applies the lambda.</summary>
    public sealed class PlayersOptions
    {
        /// <summary>Section name used for configuration binding.</summary>
        public const string SectionName = "Bun3:Players";

        /// <summary>Reconnect grace during which a disconnected Player stays in memory. Zero retires immediately.</summary>
        public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>Duplicate-login handling policy.</summary>
        public DuplicateLoginPolicy DuplicatePolicy { get; set; } = DuplicateLoginPolicy.NewWins;

        /// <summary>OnTickAsync call interval for connected players.</summary>
        public TimeSpan PlayerTickInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Periodic save interval for dirty players — upper bound on loss at crash.</summary>
        public TimeSpan SaveInterval { get; set; } = TimeSpan.FromSeconds(30);
    }
}
