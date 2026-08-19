namespace Bun3.Server.Auth.Steam
{
    /// <summary>SteamWebApiVerifier options — validated and snapshotted in the constructor; later changes are ignored.</summary>
    public sealed class SteamWebApiOptions
    {
        /// <summary>Steam AppId. 0 is rejected in the constructor.</summary>
        public uint AppId { get; set; }

        /// <summary>Publisher Web API Key — a server secret. Inject only via environment variables/configuration; never commit.</summary>
        public string WebApiKey { get; set; } = "";

        /// <summary>The same identity string when the ticket was issued via GetAuthTicketForWebApi("identity") — appended to the query.</summary>
        public string? Identity { get; set; }

        /// <summary>Treat VAC bans as failure, same as forged/expired (default). When off: success + flag.</summary>
        public bool RejectVacBanned { get; set; } = true;

        /// <summary>Treat publisher bans as failure (default). When off: success + flag.</summary>
        public bool RejectPublisherBanned { get; set; } = true;
    }
}
