using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Auth
{
    /// <summary>Per-provider credential verifier — layer 1 of the Players identity model.
    /// The game's login handler calls it, builds the accountKey from the verified identity, and passes it to SignInAsync.</summary>
    public interface IIdentityVerifier
    {
        /// <summary>Provider name (lowercase convention) — matches the issued ProviderIdentity.Provider.</summary>
        string Provider { get; }

        /// <summary>Verifies the credential. Rejections come as failure values, infrastructure problems as exceptions.
        /// Credential encoding is provider-defined (see each verifier's docs).</summary>
        ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default);
    }
}
