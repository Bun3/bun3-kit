namespace Bun3.Server.GameplayTags
{
    /// <summary>Server catalog options bound from configuration section <c>Bun3:GameplayTags</c>.</summary>
    public sealed class GameplayTagCatalogOptions
    {
        /// <summary>Section name used for configuration binding.</summary>
        public const string SectionName = "Bun3:GameplayTags";

        /// <summary>Execution mode for locating and validating the catalog artifact.</summary>
        public GameplayTagCatalogMode Mode { get; set; }

        /// <summary>Exact catalog ID identifying the game product.</summary>
        public string CatalogId { get; set; } = string.Empty;

        /// <summary>Exact catalog version required in Packaged mode.</summary>
        public string CatalogVersion { get; set; } = string.Empty;

        /// <summary>64-digit SHA-256 hex pinned by build metadata in Packaged mode.</summary>
        public string ExpectedFingerprint { get; set; } = string.Empty;

        /// <summary>Catalog path resolved against the application base in Packaged mode.</summary>
        public string PackagedPath { get; set; } = "Content/GameplayTags.catalog";

        internal string? LocalApplicationDataOverride { get; set; }

        internal static bool IsFingerprintHex(string? value)
        {
            if (value is null || value.Length != 64)
            {
                return false;
            }

            foreach (var character in value)
            {
                if (!((character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
