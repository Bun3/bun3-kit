using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Auth.Steam
{
    /// <summary>Steam verifier for dedicated servers — verifies the ticket with a single HTTPS call to the
    /// Valve Web API (ISteamUserAuth/AuthenticateUserTicket). credential = the client ticket's hex string
    /// (output of GetAuthSessionTicket/GetAuthTicketForWebApi).</summary>
    public sealed class SteamWebApiVerifier : IIdentityVerifier
    {
        private const string Endpoint = "https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/";

        private readonly HttpClient _http;
        private readonly uint _appId;
        private readonly string _webApiKey;
        private readonly string? _identity;
        private readonly bool _rejectVacBanned;
        private readonly bool _rejectPublisherBanned;

        /// <inheritdoc />
        public string Provider => "steam";

        /// <summary>Creates the verifier. AppId 0 or an empty WebApiKey is rejected immediately (dies at boot).</summary>
        public SteamWebApiVerifier(HttpClient http, SteamWebApiOptions options)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            if (options is null) throw new ArgumentNullException(nameof(options));
            if (options.AppId == 0)
                throw new ArgumentException("AppId is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.WebApiKey))
                throw new ArgumentException("WebApiKey is required.", nameof(options));

            _appId = options.AppId;
            _webApiKey = options.WebApiKey;
            _identity = options.Identity;
            _rejectVacBanned = options.RejectVacBanned;
            _rejectPublisherBanned = options.RejectPublisherBanned;
        }

        /// <inheritdoc />
        public async ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default)
        {
            var ticket = credential?.Trim() ?? string.Empty;
            if (ticket.Length == 0 || !ticket.All(Uri.IsHexDigit))
                return SteamAuthResult.Fail(AuthFailure.InvalidCredential, "ticket must be a hex string", 0);

            var url = Endpoint +
                      "?key=" + Uri.EscapeDataString(_webApiKey) +
                      "&appid=" + _appId.ToString(CultureInfo.InvariantCulture) +
                      "&ticket=" + ticket;
            if (_identity != null)
                url += "&identity=" + Uri.EscapeDataString(_identity);

            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.GetProperty("response");

            if (root.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("errorcode", out var ec) ? ec.GetInt32() : 0;
                var desc = error.TryGetProperty("errordesc", out var ed) ? ed.GetString() : null;
                return SteamAuthResult.Fail(AuthFailure.Rejected, desc ?? "rejected by Valve", code);
            }

            var p = root.GetProperty("params");
            var result = p.GetProperty("result").GetString();
            if (result != "OK")
                return SteamAuthResult.Fail(AuthFailure.Rejected, "result=" + result, 0);

            var steamId = ulong.Parse(p.GetProperty("steamid").GetString()!, CultureInfo.InvariantCulture);
            var ownerSteamId = p.TryGetProperty("ownersteamid", out var os) && os.GetString() is { } ownerRaw
                ? ulong.Parse(ownerRaw, CultureInfo.InvariantCulture)
                : steamId;
            var vacBanned = p.TryGetProperty("vacbanned", out var vb) && vb.GetBoolean();
            var publisherBanned = p.TryGetProperty("publisherbanned", out var pb) && pb.GetBoolean();

            if (vacBanned && _rejectVacBanned)
                return SteamAuthResult.Fail(AuthFailure.Banned, "VAC banned", 0, steamId, ownerSteamId, vacBanned, publisherBanned);
            if (publisherBanned && _rejectPublisherBanned)
                return SteamAuthResult.Fail(AuthFailure.Banned, "publisher banned", 0, steamId, ownerSteamId, vacBanned, publisherBanned);

            return SteamAuthResult.Success(steamId, ownerSteamId, vacBanned, publisherBanned);
        }
    }
}
