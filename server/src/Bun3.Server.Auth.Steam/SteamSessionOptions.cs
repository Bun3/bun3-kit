using System;

namespace Bun3.Server.Auth.Steam
{
    /// <summary>SteamSessionVerifier options — the game plugs in the two native calls as delegates
    /// (the framework does not depend on a Steamworks C# binding). Validated and snapshotted in the constructor.</summary>
    public sealed class SteamSessionOptions
    {
        /// <summary>Wraps SteamUser.BeginAuthSession — takes (ticket bytes, claimed SteamID64) and
        /// immediately returns the result code (EBeginAuthSessionResult, 0=OK). Required.</summary>
        public Func<byte[], ulong, int>? BeginSession { get; set; }

        /// <summary>Wraps SteamUser.EndAuthSession. Required.</summary>
        public Action<ulong>? EndSession { get; set; }

        /// <summary>Wait limit for the ValidateAuthTicketResponse callback — on expiry fails with AuthFailure.Timeout.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
    }
}
