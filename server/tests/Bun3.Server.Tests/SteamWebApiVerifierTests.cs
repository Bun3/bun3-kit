using System.Net;
using Bun3.Server.Auth;
using Bun3.Server.Auth.Steam;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class SteamWebApiVerifierTests
{
    private const string OkJson =
        """{"response":{"params":{"result":"OK","steamid":"76561198000000001","ownersteamid":"76561198000000002","vacbanned":false,"publisherbanned":false}}}""";
    private const string VacBannedJson =
        """{"response":{"params":{"result":"OK","steamid":"76561198000000001","ownersteamid":"76561198000000001","vacbanned":true,"publisherbanned":false}}}""";
    private const string PublisherBannedJson =
        """{"response":{"params":{"result":"OK","steamid":"76561198000000001","ownersteamid":"76561198000000001","vacbanned":false,"publisherbanned":true}}}""";
    private const string ErrorJson =
        """{"response":{"error":{"errorcode":101,"errordesc":"Invalid ticket"}}}""";

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => Json(OkJson);
        public HttpRequestMessage? LastRequest;

        public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(Respond(request));
        }
    }

    private static (SteamWebApiVerifier Verifier, FakeHandler Handler) Create(
        Action<SteamWebApiOptions>? configure = null)
    {
        var handler = new FakeHandler();
        var options = new SteamWebApiOptions { AppId = 480, WebApiKey = "test-key" };
        configure?.Invoke(options);
        return (new SteamWebApiVerifier(new HttpClient(handler), options), handler);
    }

    [Test]
    public async Task Ok_response_produces_steam_identity_with_flags()
    {
        var (verifier, _) = Create();
        Assert.That(verifier.Provider, Is.EqualTo("steam"));

        var result = await verifier.VerifyAsync("a1b2c3");
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Identity.ToAccountKey(), Is.EqualTo("steam:76561198000000001"));

        var steam = (SteamAuthResult)result;
        Assert.That(steam.SteamId, Is.EqualTo(76561198000000001UL));
        Assert.That(steam.OwnerSteamId, Is.EqualTo(76561198000000002UL));   // 패밀리 공유
        Assert.That(steam.VacBanned, Is.False);
        Assert.That(steam.PublisherBanned, Is.False);
    }

    [Test]
    public async Task Request_carries_key_appid_ticket_and_identity_query()
    {
        var (verifier, handler) = Create(o => o.Identity = "login");
        await verifier.VerifyAsync("A1B2");

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.That(handler.LastRequest.RequestUri.AbsoluteUri,
            Does.StartWith("https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/"));
        Assert.That(query, Does.Contain("key=test-key"));
        Assert.That(query, Does.Contain("appid=480"));
        Assert.That(query, Does.Contain("ticket=A1B2"));
        Assert.That(query, Does.Contain("identity=login"));
    }

    [Test]
    public async Task Valve_error_maps_to_rejected_with_valve_code()
    {
        var (verifier, handler) = Create();
        handler.Respond = _ => FakeHandler.Json(ErrorJson);

        var result = (SteamAuthResult)await verifier.VerifyAsync("a1b2");
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Rejected));
        Assert.That(result.ValveErrorCode, Is.EqualTo(101));
    }

    [Test]
    public async Task Vac_ban_rejected_by_default_with_flags_preserved()
    {
        var (verifier, handler) = Create();
        handler.Respond = _ => FakeHandler.Json(VacBannedJson);

        var result = (SteamAuthResult)await verifier.VerifyAsync("a1b2");
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Banned));
        Assert.That(result.VacBanned, Is.True);
        Assert.That(result.SteamId, Is.EqualTo(76561198000000001UL));
    }

    [Test]
    public async Task Vac_ban_passes_when_reject_disabled()
    {
        var (verifier, handler) = Create(o => o.RejectVacBanned = false);
        handler.Respond = _ => FakeHandler.Json(VacBannedJson);

        var result = (SteamAuthResult)await verifier.VerifyAsync("a1b2");
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.VacBanned, Is.True);    // 입장 판단용 플래그는 유지
    }

    [Test]
    public async Task Publisher_ban_rejected_by_default()
    {
        var (verifier, handler) = Create();
        handler.Respond = _ => FakeHandler.Json(PublisherBannedJson);

        var result = (SteamAuthResult)await verifier.VerifyAsync("a1b2");
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Banned));
        Assert.That(result.PublisherBanned, Is.True);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("xyz!")]     // hex 아님
    public async Task Invalid_ticket_format_fails_without_http_call(string credential)
    {
        var (verifier, handler) = Create();
        var result = await verifier.VerifyAsync(credential);

        Assert.That(result.Failure, Is.EqualTo(AuthFailure.InvalidCredential));
        Assert.That(handler.LastRequest, Is.Null);   // HTTP 미호출
    }

    [Test]
    public void Http_failure_propagates_as_exception()
    {
        var (verifier, handler) = Create();
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        Assert.ThrowsAsync<HttpRequestException>(async () => await verifier.VerifyAsync("a1b2"));
    }

    [Test]
    public void Constructor_rejects_missing_appid_or_key()
    {
        var http = new HttpClient(new FakeHandler());
        Assert.Throws<ArgumentException>(() =>
            new SteamWebApiVerifier(http, new SteamWebApiOptions { AppId = 0, WebApiKey = "k" }));
        Assert.Throws<ArgumentException>(() =>
            new SteamWebApiVerifier(http, new SteamWebApiOptions { AppId = 480, WebApiKey = " " }));
    }
}
