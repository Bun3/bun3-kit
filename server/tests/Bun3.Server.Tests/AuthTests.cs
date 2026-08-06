using Bun3.Server.Auth;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class AuthTests
{
    [Test]
    public void ProviderIdentity_ToAccountKey_formats_provider_colon_subject()
    {
        var identity = new ProviderIdentity("steam", "76561198000000001");
        Assert.That(identity.ToAccountKey(), Is.EqualTo("steam:76561198000000001"));
    }

    [Test]
    public async Task GuestVerifier_accepts_and_trims_device_id()
    {
        var verifier = new GuestVerifier();
        Assert.That(verifier.Provider, Is.EqualTo("guest"));

        var result = await verifier.VerifyAsync("  device-abc  ");
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.None));
        Assert.That(result.Identity.Provider, Is.EqualTo("guest"));
        Assert.That(result.Identity.Subject, Is.EqualTo("device-abc"));
        Assert.That(result.Identity.ToAccountKey(), Is.EqualTo("guest:device-abc"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("dev:ice")]            // 키 규약 오염 — ':' 금지
    public async Task GuestVerifier_rejects_invalid_credential(string credential)
    {
        var result = await new GuestVerifier().VerifyAsync(credential);
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.InvalidCredential));
    }

    [Test]
    public async Task GuestVerifier_rejects_over_128_chars()
    {
        var result = await new GuestVerifier().VerifyAsync(new string('a', 129));
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.InvalidCredential));

        var ok = await new GuestVerifier().VerifyAsync(new string('a', 128));
        Assert.That(ok.Succeeded, Is.True);
    }
}
