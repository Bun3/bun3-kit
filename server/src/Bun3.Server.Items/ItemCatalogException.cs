using System;

namespace Bun3.Server.Items
{
    /// <summary>Catalog construction or validation failure — a startup-time error that must block server start.</summary>
    public sealed class ItemCatalogException : Exception
    {
        /// <summary>Creates the exception with a message.</summary>
        public ItemCatalogException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with a message and inner exception.</summary>
        public ItemCatalogException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
