#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Checks GameplayTag catalog compatibility with a network peer.</summary>
    public static class TagCatalogCompatibility
    {
        /// <summary>Requires the peer fingerprint to match the local catalog exactly.</summary>
        /// <param name="local">Local catalog currently running.</param>
        /// <param name="peerFingerprint">Semantic fingerprint the peer provided during handshake.</param>
        /// <exception cref="ArgumentNullException"><paramref name="local"/> is null.</exception>
        /// <exception cref="TagCatalogCompatibilityException">The fingerprints do not match.</exception>
        public static void RequirePeerFingerprint(
            TagCatalog local,
            ReadOnlySpan<byte> peerFingerprint)
        {
            if (local is null) throw new ArgumentNullException(nameof(local));
            if (!local.MatchesFingerprint(peerFingerprint))
            {
                throw new TagCatalogCompatibilityException(
                    "Peer GameplayTag catalog semantic fingerprint differs from the local catalog.");
            }
        }
    }
}
