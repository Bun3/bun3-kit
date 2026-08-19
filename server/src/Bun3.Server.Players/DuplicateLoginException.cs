using System;

namespace Bun3.Server.Players
{
    /// <summary>Thrown by SignInAsync under the RejectNew policy when the account is already connected.
    /// Game login handlers are encouraged to catch it and convert it to a game status code.</summary>
    public sealed class DuplicateLoginException : Exception
    {
        /// <summary>Account key of the attempted duplicate login.</summary>
        public string AccountKey { get; }

        /// <summary>Creates the exception for the given account key.</summary>
        public DuplicateLoginException(string accountKey)
            : base($"Account {accountKey} is already connected (RejectNew policy).")
        {
            AccountKey = accountKey;
        }
    }
}
