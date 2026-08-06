using System.Globalization;

namespace Bun3.Server.Auth.Steam
{
    /// <summary>Steam 검증 판정 — 공통 판정에 Steam 디테일(밴 플래그, 원시 코드)을 더한다.</summary>
    public sealed class SteamAuthResult : AuthResult
    {
        /// <summary>검증된 SteamID64 — 실패 시에도 응답에 있었다면 채워진다(로그용).</summary>
        public ulong SteamId { get; }

        /// <summary>소유자 SteamID64 — 패밀리 공유로 빌린 계정이면 SteamId와 다르다.
        /// 네이티브 경로는 소유자 정보가 없어 SteamId와 같은 값이다.</summary>
        public ulong OwnerSteamId { get; }

        /// <summary>VAC 밴 여부(Web API 응답 기준).</summary>
        public bool VacBanned { get; }

        /// <summary>퍼블리셔 밴 여부(Web API 응답 기준).</summary>
        public bool PublisherBanned { get; }

        /// <summary>Valve 원시 코드 — Web API errorcode 또는 EAuthSessionResponse/EBeginAuthSessionResult.
        /// 로그·운영용이며 판정에는 쓰지 않는다.</summary>
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
