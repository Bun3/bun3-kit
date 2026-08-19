namespace Bun3.Server.Auth
{
    /// <summary>Verification verdict — expected rejections surface as values, only infrastructure failures as exceptions.</summary>
    public class AuthResult
    {
        /// <summary>Whether verification succeeded.</summary>
        public bool Succeeded { get; }

        /// <summary>The verified identity — valid only on success.</summary>
        public ProviderIdentity Identity { get; }

        /// <summary>Failure reason — valid only on failure.</summary>
        public AuthFailure Failure { get; }

        /// <summary>Log-only description — never put it on the wire.</summary>
        public string? Error { get; }

        /// <summary>Constructor for derived result types (provider-specific details).</summary>
        protected AuthResult(bool succeeded, ProviderIdentity identity, AuthFailure failure, string? error)
        {
            Succeeded = succeeded;
            Identity = identity;
            Failure = failure;
            Error = error;
        }

        /// <summary>Creates a success verdict.</summary>
        public static AuthResult Success(ProviderIdentity identity) =>
            new AuthResult(true, identity, AuthFailure.None, null);

        /// <summary>Creates a failure verdict.</summary>
        public static AuthResult Fail(AuthFailure failure, string? error = null) =>
            new AuthResult(false, default, failure, error);
    }
}
