using Bun3.Server.Auth;
using Bun3.Server.Auth.Steam;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class SteamSessionVerifierTests
{
    private const ulong SteamId = 76561198000000001UL;
    private const string Credential = "76561198000000001:a1b2c3d4";

    private sealed class Harness
    {
        public readonly List<(byte[] Ticket, ulong SteamId)> BeginCalls = new();
        public readonly List<ulong> EndCalls = new();
        public int BeginResult;
        public SteamSessionVerifier Verifier;

        public Harness(TimeSpan? timeout = null)
        {
            Verifier = new SteamSessionVerifier(new SteamSessionOptions
            {
                BeginSession = (ticket, steamId) => { BeginCalls.Add((ticket, steamId)); return BeginResult; },
                EndSession = steamId => EndCalls.Add(steamId),
                Timeout = timeout ?? TimeSpan.FromSeconds(5),
            });
        }
    }

    [Test]
    public async Task Ok_callback_completes_verification()
    {
        var h = new Harness();
        Assert.That(h.Verifier.Provider, Is.EqualTo("steam"));

        var verify = h.Verifier.VerifyAsync(Credential).AsTask();
        h.Verifier.HandleValidateResult(SteamId, 0);   // k_EAuthSessionResponseOK

        var result = (SteamAuthResult)await verify;
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Identity.ToAccountKey(), Is.EqualTo("steam:76561198000000001"));
        Assert.That(result.OwnerSteamId, Is.EqualTo(SteamId));   // 네이티브 경로: 소유자 정보 없음
        Assert.That(h.BeginCalls, Has.Count.EqualTo(1));
        Assert.That(h.BeginCalls[0].SteamId, Is.EqualTo(SteamId));
        Assert.That(h.BeginCalls[0].Ticket, Is.EqualTo(new byte[] { 0xa1, 0xb2, 0xc3, 0xd4 }));
        Assert.That(h.EndCalls, Is.Empty);   // 성공 시 세션 유지 — 정리는 게임 몫
    }

    [TestCase("not-a-number:a1b2")]
    [TestCase("76561198000000001")]        // 구분자 없음
    [TestCase("76561198000000001:")]       // 티켓 없음
    [TestCase("76561198000000001:a1b")]    // 홀수 hex
    [TestCase("76561198000000001:zz")]     // hex 아님
    [TestCase("0:a1b2")]                   // steamId 0
    public async Task Malformed_credential_fails_without_begin_call(string credential)
    {
        var h = new Harness();
        var result = await h.Verifier.VerifyAsync(credential);

        Assert.That(result.Failure, Is.EqualTo(AuthFailure.InvalidCredential));
        Assert.That(h.BeginCalls, Is.Empty);
    }

    [Test]
    public async Task Immediate_begin_failure_maps_to_rejected()
    {
        var h = new Harness { BeginResult = 1 };   // k_EBeginAuthSessionResultInvalidTicket
        var result = (SteamAuthResult)await h.Verifier.VerifyAsync(Credential);

        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Rejected));
        Assert.That(result.ValveErrorCode, Is.EqualTo(1));
        Assert.That(result.SteamId, Is.EqualTo(SteamId));
        Assert.That(h.EndCalls, Is.EqualTo(new[] { SteamId }));   // 실패 정리 규약
    }

    [Test]
    public async Task Rejecting_callback_maps_to_rejected_and_ends_session()
    {
        var h = new Harness();
        var verify = h.Verifier.VerifyAsync(Credential).AsTask();
        h.Verifier.HandleValidateResult(SteamId, 6);   // k_EAuthSessionResponseAuthTicketCanceled

        var result = (SteamAuthResult)await verify;
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Rejected));
        Assert.That(result.ValveErrorCode, Is.EqualTo(6));
        Assert.That(result.SteamId, Is.EqualTo(SteamId));
        Assert.That(h.EndCalls, Is.EqualTo(new[] { SteamId }));
    }

    [TestCase(3)]   // k_EAuthSessionResponseVACBanned
    [TestCase(9)]   // k_EAuthSessionResponsePublisherIssuedBan
    public async Task Ban_callback_maps_to_banned(int response)
    {
        var h = new Harness();
        var verify = h.Verifier.VerifyAsync(Credential).AsTask();
        h.Verifier.HandleValidateResult(SteamId, response);

        var result = (SteamAuthResult)await verify;
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Banned));
        Assert.That(result.ValveErrorCode, Is.EqualTo(response));
    }

    [Test]
    public async Task Callback_timeout_fails_with_timeout_and_ends_session()
    {
        var h = new Harness(timeout: TimeSpan.FromMilliseconds(50));
        var result = (SteamAuthResult)await h.Verifier.VerifyAsync(Credential);

        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Timeout));
        Assert.That(result.SteamId, Is.EqualTo(SteamId));
        Assert.That(h.EndCalls, Is.EqualTo(new[] { SteamId }));
    }

    [Test]
    public async Task Concurrent_verify_for_same_steamid_fails_fast()
    {
        var h = new Harness();
        var first = h.Verifier.VerifyAsync(Credential).AsTask();

        var second = await h.Verifier.VerifyAsync(Credential);
        Assert.That(second.Failure, Is.EqualTo(AuthFailure.Rejected));
        Assert.That(h.BeginCalls, Has.Count.EqualTo(1));   // 두 번째는 Begin 미호출

        h.Verifier.HandleValidateResult(SteamId, 0);
        Assert.That((await first).Succeeded, Is.True);
    }

    [Test]
    public async Task Late_invalidation_raises_event()
    {
        var h = new Harness();
        var invalidated = new List<(ulong SteamId, int Code)>();
        h.Verifier.SessionInvalidated += (steamId, code) => invalidated.Add((steamId, code));

        var verify = h.Verifier.VerifyAsync(Credential).AsTask();
        h.Verifier.HandleValidateResult(SteamId, 0);
        await verify;

        h.Verifier.HandleValidateResult(SteamId, 6);   // 접속 승인 후 티켓 취소
        Assert.That(invalidated, Is.EqualTo(new[] { (SteamId, 6) }));
    }

    [Test]
    public void Ok_callback_without_pending_is_ignored()
    {
        var h = new Harness();
        var invalidated = 0;
        h.Verifier.SessionInvalidated += (_, _) => invalidated++;

        h.Verifier.HandleValidateResult(SteamId, 0);   // pending 없음 + OK → 무시
        Assert.That(invalidated, Is.Zero);
    }

    [Test]
    public void External_cancellation_throws_and_ends_session()
    {
        var h = new Harness();
        using var cts = new CancellationTokenSource();
        var verify = h.Verifier.VerifyAsync(Credential, cts.Token).AsTask();
        cts.Cancel();

        Assert.ThrowsAsync<TaskCanceledException>(async () => await verify);
        Assert.That(h.EndCalls, Is.EqualTo(new[] { SteamId }));
    }

    [Test]
    public void EndSession_forwards_to_delegate()
    {
        var h = new Harness();
        h.Verifier.EndSession(SteamId);
        Assert.That(h.EndCalls, Is.EqualTo(new[] { SteamId }));
    }

    [Test]
    public void Constructor_rejects_missing_delegates()
    {
        Assert.Throws<ArgumentException>(() => new SteamSessionVerifier(new SteamSessionOptions
        {
            BeginSession = null,
            EndSession = _ => { },
        }));
        Assert.Throws<ArgumentException>(() => new SteamSessionVerifier(new SteamSessionOptions
        {
            BeginSession = (_, _) => 0,
            EndSession = null,
        }));
    }
}
