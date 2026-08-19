namespace Bun3.Server.Auth
{
    /// <summary>Identity that passed provider verification — a (provider, provider-unique id) pair.</summary>
    public readonly struct ProviderIdentity
    {
        /// <summary>Provider name (lowercase convention) — "guest", "steam", etc.</summary>
        public string Provider { get; }

        /// <summary>Provider-unique id — SteamID64, device-id, etc.</summary>
        public string Subject { get; }

        /// <summary>Creates the identity.</summary>
        public ProviderIdentity(string provider, string subject)
        {
            Provider = provider;
            Subject = subject;
        }

        /// <summary>Builds the accountKey string in the recommended Players convention ("provider:subject").
        /// Games with account linking use the link-table lookup result ("acct:{id}") instead.</summary>
        public string ToAccountKey() => $"{Provider}:{Subject}";
    }
}
