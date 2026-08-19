#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Thrown when a B3DK catalog is malformed, corrupted, or structurally invalid.</summary>
    public sealed class TagCatalogFormatException : Exception
    {
        /// <summary>Creates the exception with an error description.</summary>
        /// <param name="message">Format error description.</param>
        public TagCatalogFormatException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with an error description and a cause.</summary>
        /// <param name="message">Format error description.</param>
        /// <param name="innerException">Exception that caused the format error.</param>
        public TagCatalogFormatException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
