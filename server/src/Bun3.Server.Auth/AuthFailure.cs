namespace Bun3.Server.Auth
{
    /// <summary>Verification failure reason — a provider-agnostic common vocabulary. Games map these to their own proto error codes.</summary>
    public enum AuthFailure
    {
        /// <summary>No failure (success).</summary>
        None = 0,

        /// <summary>Malformed credential — empty device-id, hex parse failure, convention violation.</summary>
        InvalidCredential = 1,

        /// <summary>Rejected by the provider — forged/expired ticket, invalid token.</summary>
        Rejected = 2,

        /// <summary>Provider ban — VAC/publisher ban (when the rejection option is enabled).</summary>
        Banned = 3,

        /// <summary>Reserved — claimed identity differs from the verified one (never produced by current implementations; for future providers).</summary>
        IdentityMismatch = 4,

        /// <summary>Verification response timed out (e.g. native callback never arrived).</summary>
        Timeout = 5,
    }
}
