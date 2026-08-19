using System.Globalization;

namespace Bun3.Server.Auth.Steam
{
    /// <summary>Steam verification verdict — adds Steam details (ban flags, raw codes) to the common verdict.</summary>
    public sealed class SteamAuthResult : AuthResult
    {
        /// <summary>Verified SteamID64 — also populated on failure when present in the response (for logging).</summary>
        public ulong SteamId { get; }

        /// <summary>Owner SteamID64 — differs from SteamId for accounts borrowed via Family Sharing.
        /// The native path has no owner info, so it equals SteamId there.</summary>
        public ulong OwnerSteamId { get; }

        /// <summary>Whether VAC banned (per Web API response).</summary>
        public bool VacBanned { get; }

        /// <summary>Whether publisher banned (per Web API response).</summary>
        public bool PublisherBanned { get; }

        /// <summary>Raw Valve code — Web API errorcode or EAuthSessionResponse/EBeginAuthSessionResult.
        /// For logging/ops only; never used for the verdict.</summary>
        public int ValveErrorCode { get; }

        private SteamAuthResult(
            bool succeeded, ProviderIdentity identity, AuthFailure failure, string? error,
            ulong steamId, ulong ownerSteamId, bool vacBanned, bool publisherBanned, int valveErrorCode)
            : base(succeeded, identity, failure, error)
        {
            SteamId = steamId;
            OwnerSteamId = ownerSteamId;
            VacBanned = vacBanned;
            PublisherBanned = publisherBanned;
            ValveErrorCode = valveErrorCode;
        }

        internal static SteamAuthResult Success(ulong steamId, ulong ownerSteamId, bool vacBanned, bool publisherBanned) =>
            new SteamAuthResult(
                true,
                new ProviderIdentity("steam", steamId.ToString(CultureInfo.InvariantCulture)),
                AuthFailure.None, null,
                steamId, ownerSteamId, vacBanned, publisherBanned, 0);

        internal static SteamAuthResult Fail(
            AuthFailure failure, string? error, int valveErrorCode,
            ulong steamId = 0, ulong ownerSteamId = 0, bool vacBanned = false, bool publisherBanned = false) =>
            new SteamAuthResult(false, default, failure, error, steamId, ownerSteamId, vacBanned, publisherBanned, valveErrorCode);
    }
}
