#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Thrown when a B3DK catalog differs from the ID, version, or fingerprint the executable requires.</summary>
    public sealed class TagCatalogCompatibilityException : Exception
    {
        /// <summary>Creates the exception with a compatibility error description.</summary>
        /// <param name="message">Compatibility error description.</param>
        public TagCatalogCompatibilityException(string message) : base(message)
        {
        }
    }
}
