namespace Bun3.Server.Players
{
    /// <summary>Result of SignInAsync.</summary>
    public readonly struct SignInResult<TPlayer> where TPlayer : Player
    {
        /// <summary>The bound Player (fresh load or existing rebinding).</summary>
        public TPlayer Player { get; }

        /// <summary>True when an existing Player was reused (grace rebinding or duplicate-login transfer).</summary>
        public bool IsReconnect { get; }

        /// <summary>Creates the result from the bound Player and reconnect flag.</summary>
        public SignInResult(TPlayer player, bool isReconnect)
        {
            Player = player;
            IsReconnect = isReconnect;
        }
    }
}
