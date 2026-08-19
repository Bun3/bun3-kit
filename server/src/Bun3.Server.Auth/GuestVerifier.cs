using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Auth
{
    /// <summary>Guest verifier — there is no credential to verify, so it performs only trust-boundary (format) validation.
    /// credential = client device-id. Allowed characters are alphanumerics and <c>- _ .</c> only —
    /// this blocks injection of control characters/newlines flowing through AccountKey into logs/DB keys.
    /// It fundamentally trusts the client's claim; it sits behind the same contract so switching to Steam
    /// etc. changes only one verifier line in the login handler.</summary>
    public sealed class GuestVerifier : IIdentityVerifier
    {
        /// <summary>Maximum device-id length.</summary>
        public const int MaxSubjectLength = 128;

        /// <inheritdoc />
        public string Provider => "guest";

        /// <inheritdoc />
        public ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default)
        {
            var subject = credential?.Trim() ?? string.Empty;
            if (subject.Length == 0 || subject.Length > MaxSubjectLength || !IsValidSubject(subject))
                return new ValueTask<AuthResult>(AuthResult.Fail(AuthFailure.InvalidCredential, "invalid device id"));

            return new ValueTask<AuthResult>(AuthResult.Success(new ProviderIdentity(Provider, subject)));
        }

        // Allowlist validation — rejects every other character, including ':' (the provider separator).
        private static bool IsValidSubject(string subject)
        {
            foreach (var c in subject)
            {
                var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                    || c == '-' || c == '_' || c == '.';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
