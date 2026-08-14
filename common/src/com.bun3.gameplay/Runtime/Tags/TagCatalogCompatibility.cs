#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>네트워크 peer와 GameplayTag Catalog 호환성을 검사합니다.</summary>
    public static class TagCatalogCompatibility
    {
        /// <summary>peer fingerprint가 로컬 Catalog와 정확히 같은지 요구합니다.</summary>
        /// <param name="local">현재 실행 중인 로컬 Catalog입니다.</param>
        /// <param name="peerFingerprint">peer가 handshake에서 제공한 semantic fingerprint입니다.</param>
        /// <exception cref="ArgumentNullException"><paramref name="local"/>이 null인 경우입니다.</exception>
        /// <exception cref="TagCatalogCompatibilityException">fingerprint가 일치하지 않는 경우입니다.</exception>
        public static void RequirePeerFingerprint(
            TagCatalog local,
            ReadOnlySpan<byte> peerFingerprint)
        {
            if (local is null) throw new ArgumentNullException(nameof(local));
            if (!local.MatchesFingerprint(peerFingerprint))
            {
                throw new TagCatalogCompatibilityException(
                    "peer GameplayTag Catalog semantic fingerprint가 로컬 Catalog와 다릅니다.");
            }
        }
    }
}
