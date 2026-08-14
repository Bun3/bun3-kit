#nullable enable
using System;
using System.IO;
using System.Text;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Editor.Tags
{
    internal static class GameplayTagCatalogFileAdapter
    {
        private const string EmptyGameSourceJson =
            "{\n"
            + "  \"schemaVersion\": 1,\n"
            + "  \"tags\": [],\n"
            + "  \"redirects\": []\n"
            + "}\n";
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static GameplayTagCatalogEditSession Load(string absolutePath)
        {
            return CreateSession(LoadGameSourceDocument(absolutePath));
        }

        internal static void Save(string absolutePath, GameplayTagCatalogEditSession session)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));
            SaveJson(absolutePath, session.Serialize());
        }

        internal static void SaveJson(string absolutePath, string json)
        {
            if (absolutePath is null) throw new ArgumentNullException(nameof(absolutePath));
            if (json is null) throw new ArgumentNullException(nameof(json));

            var bytes = StrictUtf8.GetBytes(json);
            _ = LoadGameSourceDocument(bytes, absolutePath);
            SaveBytes(absolutePath, bytes);
        }

        internal static void CreateGameSource(string absolutePath)
        {
            if (absolutePath is null) throw new ArgumentNullException(nameof(absolutePath));
            if (File.Exists(absolutePath))
            {
                throw new IOException("The fixed Game Source already exists: " + absolutePath);
            }

            SaveBytes(absolutePath, StrictUtf8.GetBytes(EmptyGameSourceJson));
        }

        internal static void ImportExisting(string sourcePath, string destinationPath)
        {
            ImportExisting(PrepareImport(sourcePath, destinationPath), destinationPath);
        }

        internal static TagSourceDocument PrepareImport(string sourcePath, string destinationPath)
        {
            if (sourcePath is null) throw new ArgumentNullException(nameof(sourcePath));
            if (destinationPath is null) throw new ArgumentNullException(nameof(destinationPath));

            var document = LoadGameSourceDocument(sourcePath);
            return LoadGameSourceDocument(
                Serialize(document),
                Path.GetFullPath(destinationPath));
        }

        internal static void ImportExisting(
            TagSourceDocument candidate,
            string destinationPath)
        {
            if (candidate is null) throw new ArgumentNullException(nameof(candidate));
            if (destinationPath is null) throw new ArgumentNullException(nameof(destinationPath));
            SaveBytes(destinationPath, Serialize(candidate));
        }

        internal static TagSourceDocument LoadGameSourceDocument(string absolutePath)
        {
            if (absolutePath is null) throw new ArgumentNullException(nameof(absolutePath));
            return LoadGameSourceDocument(File.ReadAllBytes(absolutePath), absolutePath);
        }

        internal static GameplayTagCatalogEditSession CreateSession(TagSourceDocument document)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            return GameplayTagCatalogEditSession.Open(StrictUtf8.GetString(Serialize(document)));
        }

        private static TagSourceDocument LoadGameSourceDocument(byte[] bytes, string origin)
        {
            using var stream = new MemoryStream(bytes, false);
            return TagSourceJson.LoadGame(stream, origin);
        }

        private static byte[] Serialize(TagSourceDocument document)
        {
            using var stream = new MemoryStream();
            TagSourceJson.WriteGame(stream, document);
            return stream.ToArray();
        }

        private static void SaveBytes(string absolutePath, byte[] bytes)
        {
            if (absolutePath is null) throw new ArgumentNullException(nameof(absolutePath));
            if (bytes is null) throw new ArgumentNullException(nameof(bytes));

            var fullPath = Path.GetFullPath(absolutePath);
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new ArgumentException("The destination directory is missing.", nameof(absolutePath));
            Directory.CreateDirectory(directory);

            var temporaryPath = Path.Combine(
                directory,
                "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(fullPath))
                {
                    File.Replace(temporaryPath, fullPath, null);
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
