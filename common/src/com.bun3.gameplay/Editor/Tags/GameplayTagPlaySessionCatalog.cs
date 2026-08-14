#nullable enable
using System;
using System.IO;
using Bun3.Gameplay.Tags;
using UnityEditor;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>현재 Unity Play 전환에서 binary 검증을 마친 단 하나의 불변 Catalog를 보관합니다.</summary>
    public static class GameplayTagPlaySessionCatalog
    {
        private const string PathKey = "Bun3.Gameplay.Tags.PlaySession.Path";
        private const string CatalogIdKey = "Bun3.Gameplay.Tags.PlaySession.CatalogId";
        private const string FingerprintKey = "Bun3.Gameplay.Tags.PlaySession.Fingerprint";

        /// <summary>활성 Play 전환에서 준비한 Catalog이며 일반 Edit Mode에서는 null입니다.</summary>
        public static TagCatalog? Current { get; private set; }

        internal static void Freeze(TagCatalog catalog)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));
            if (Current is not null && !ReferenceEquals(Current, catalog))
            {
                throw new InvalidOperationException("The active Play session Catalog is already frozen.");
            }

            Current = catalog;
        }

        internal static void RememberPrepared(TagCatalog catalog, string binaryPath)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));
            if (binaryPath is null) throw new ArgumentNullException(nameof(binaryPath));
            SessionState.SetString(PathKey, Path.GetFullPath(binaryPath));
            SessionState.SetString(CatalogIdKey, catalog.CatalogId);
            SessionState.SetString(
                FingerprintKey,
                Convert.ToBase64String(catalog.Fingerprint.ToArray()));
        }

        internal static bool TryRestorePrepared(out string diagnostic)
        {
            if (Current is not null)
            {
                diagnostic = string.Empty;
                return true;
            }

            var path = SessionState.GetString(PathKey, string.Empty);
            var catalogId = SessionState.GetString(CatalogIdKey, string.Empty);
            var fingerprintText = SessionState.GetString(FingerprintKey, string.Empty);
            if (path.Length == 0 || catalogId.Length == 0 || fingerprintText.Length == 0)
            {
                diagnostic = "No prepared GameplayTag Catalog exists for this Play transition.";
                return false;
            }

            try
            {
                var fingerprint = Convert.FromBase64String(fingerprintText);
                using var input = File.OpenRead(path);
                var catalog = TagCatalogBinary.Load(
                    input,
                    TagCatalogExpectations.ForPublished(
                        catalogId, "0.0.0-dev", fingerprint));
                Freeze(catalog);
                diagnostic = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is IOException
                || exception is UnauthorizedAccessException
                || exception is FormatException
                || exception is TagCatalogFormatException
                || exception is TagCatalogCompatibilityException)
            {
                Current = null;
                diagnostic = "Prepared GameplayTag Catalog could not be restored: " + exception.Message;
                ForgetPrepared();
                return false;
            }
        }

        internal static void Clear()
        {
            Current = null;
            ForgetPrepared();
        }

        private static void ForgetPrepared()
        {
            SessionState.EraseString(PathKey);
            SessionState.EraseString(CatalogIdKey);
            SessionState.EraseString(FingerprintKey);
        }
    }
}
