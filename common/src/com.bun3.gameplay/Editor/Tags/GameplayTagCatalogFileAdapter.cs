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
            SaveJson(absolutePath, session.Serialize(), stagedReadback: null);
        }

        internal static void Save(
            string absolutePath,
            GameplayTagCatalogEditSession session,
            Action<Stream> stagedReadback)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));
            if (stagedReadback is null) throw new ArgumentNullException(nameof(stagedReadback));
            SaveJson(absolutePath, session.Serialize(), stagedReadback);
        }

        internal static void SaveJson(string absolutePath, string json)
        {
            SaveJson(absolutePath, json, stagedReadback: null);
        }

        private static void SaveJson(
            string absolutePath,
            string json,
            Action<Stream>? stagedReadback)
        {
            if (absolutePath is null) throw new ArgumentNullException(nameof(absolutePath));
            if (json is null) throw new ArgumentNullException(nameof(json));

            var bytes = StrictUtf8.GetBytes(json);
            _ = LoadGameSourceDocument(bytes, absolutePath);
            SaveBytes(absolutePath, bytes, stagedReadback);
        }

        internal static void CreateGameSource(string absolutePath)
        {
            CreateGameSourceCore(absolutePath, stagedReadback: null);
        }

        internal static void CreateGameSource(
            string absolutePath,
            Action<Stream> stagedReadback)
        {
            if (stagedReadback is null) throw new ArgumentNullException(nameof(stagedReadback));
            CreateGameSourceCore(absolutePath, stagedReadback);
        }

        private static void CreateGameSourceCore(
            string absolutePath,
            Action<Stream>? stagedReadback)
        {
            if (absolutePath is null) throw new ArgumentNullException(nameof(absolutePath));
            if (File.Exists(absolutePath))
            {
                throw new IOException("The fixed Game Source already exists: " + absolutePath);
            }

            SaveBytes(
                absolutePath,
                StrictUtf8.GetBytes(EmptyGameSourceJson),
                stagedReadback);
        }

        internal static void ImportExisting(string sourcePath, string destinationPath)
        {
            ImportExistingCore(sourcePath, destinationPath, stagedReadback: null);
        }

        internal static void ImportExisting(
            string sourcePath,
            string destinationPath,
            Action<Stream> stagedReadback)
        {
            if (stagedReadback is null) throw new ArgumentNullException(nameof(stagedReadback));
            ImportExistingCore(sourcePath, destinationPath, stagedReadback);
        }

        private static void ImportExistingCore(
            string sourcePath,
            string destinationPath,
            Action<Stream>? stagedReadback)
        {
            ImportExisting(
                PrepareImport(sourcePath, destinationPath),
                destinationPath,
                stagedReadback);
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
            ImportExisting(candidate, destinationPath, stagedReadback: null);
        }

        private static void ImportExisting(
            TagSourceDocument candidate,
            string destinationPath,
            Action<Stream>? stagedReadback)
        {
            if (candidate is null) throw new ArgumentNullException(nameof(candidate));
            if (destinationPath is null) throw new ArgumentNullException(nameof(destinationPath));
            SaveBytes(destinationPath, Serialize(candidate), stagedReadback);
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

        private static void SaveBytes(
            string absolutePath,
            byte[] bytes,
            Action<Stream>? stagedReadback)
        {
            if (absolutePath is null) throw new ArgumentNullException(nameof(absolutePath));
            if (bytes is null) throw new ArgumentNullException(nameof(bytes));

            var fullPath = Path.GetFullPath(absolutePath);
            AtomicFileWriter.WriteVerified(
                fullPath,
                output => output.Write(bytes, 0, bytes.Length),
                input =>
                {
                    stagedReadback?.Invoke(input);
                    input.Position = 0;
                    _ = TagSourceJson.LoadGame(input, fullPath);
                });
        }
    }
}
