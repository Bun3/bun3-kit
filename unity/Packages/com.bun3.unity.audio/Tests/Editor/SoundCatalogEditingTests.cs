using System;
using System.Linq;
using Bun3.Unity.Audio.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundCatalogEditingTests
    {
        private const string Folder = "Assets/__Bun3AudioCatalogEditingTests";
        private SoundCatalog catalog;
        private string path;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__Bun3AudioCatalogEditingTests");
            catalog = ScriptableObject.CreateInstance<SoundCatalog>();
            path = Folder + "/Catalog.asset";
            AssetDatabase.CreateAsset(catalog, path);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.Refresh();
        }

        private int SubAssetCount() => AssetDatabase.LoadAllAssetsAtPath(path).Length;

        private void WriteRows(params (string key, SoundDef def)[] rows)
        {
            var so = new SerializedObject(catalog);
            var entries = so.FindProperty("entries");
            entries.arraySize = rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                var row = entries.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("key").stringValue = rows[i].key;
                row.FindPropertyRelative("definition").objectReferenceValue = rows[i].def;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void AddSound_CreatesHiddenSubAssetNamedByKey()
        {
            var def = SoundCatalogEditing.AddSound(catalog, "door");
            Assert.That(SoundCatalogEditing.IsOwnedBy(catalog, def), Is.True);
            Assert.That(def.hideFlags, Is.EqualTo(HideFlags.HideInHierarchy));
            Assert.That(def.name, Is.EqualTo("door"));
            Assert.That(catalog.Get("door"), Is.SameAs(def));
            Assert.DoesNotThrow(catalog.ValidateOrThrow);
        }

        [Test]
        public void Duplicate_CopiesValues()
        {
            var source = SoundCatalogEditing.AddSound(catalog, "a");
            source.Volume = new FloatRange(0.4f, 0.4f);
            var copy = SoundCatalogEditing.Duplicate(catalog, "a", "b");
            Assert.That(copy, Is.Not.SameAs(source));
            Assert.That(catalog.Get("b").Volume.Min, Is.EqualTo(0.4f));
            Assert.That(copy.name, Is.EqualTo("b"));
            Assert.That(SoundCatalogEditing.IsOwnedBy(catalog, copy), Is.True);
        }

        [Test]
        public void Rename_ChangesKeyAndName()
        {
            var def = SoundCatalogEditing.AddSound(catalog, "a");
            SoundCatalogEditing.Rename(catalog, "a", "b");
            Assert.That(catalog.Get("a"), Is.Null);
            Assert.That(catalog.Get("b"), Is.SameAs(def));
            Assert.That(def.name, Is.EqualTo("b"));
        }

        [Test]
        public void Delete_RemovesEntryAndSubAsset()
        {
            SoundCatalogEditing.AddSound(catalog, "a");
            SoundCatalogEditing.AddSound(catalog, "b");
            int before = SubAssetCount();
            SoundCatalogEditing.Delete(catalog, "a");
            Assert.That(catalog.Entries.Count, Is.EqualTo(1));
            Assert.That(catalog.Get("a"), Is.Null);
            Assert.That(SubAssetCount(), Is.EqualTo(before - 1));
        }

        [Test]
        public void Delete_ExternalDefinition_RemovesEntryOnly()
        {
            var external = ScriptableObject.CreateInstance<SoundDef>();
            AssetDatabase.CreateAsset(external, Folder + "/External.asset");
            catalog.SetEntries(new[] { new SoundCatalogEntry("x", external) });
            SoundCatalogEditing.Rename(catalog, "x", "y");
            Assert.That(external.name, Is.EqualTo("External"));
            SoundCatalogEditing.Delete(catalog, "y");
            Assert.That(catalog.Entries.Count, Is.Zero);
            Assert.That(AssetDatabase.LoadAssetAtPath<SoundDef>(Folder + "/External.asset"), Is.Not.Null);
        }

        [Test]
        public void AddAndRename_RejectBlankOrDuplicateKeys_WithoutChanges()
        {
            SoundCatalogEditing.AddSound(catalog, "a");
            SoundCatalogEditing.AddSound(catalog, "b");
            int before = SubAssetCount();
            Assert.Throws<ArgumentException>(() => SoundCatalogEditing.AddSound(catalog, " "));
            Assert.Throws<ArgumentException>(() => SoundCatalogEditing.AddSound(catalog, "a"));
            Assert.Throws<ArgumentException>(() => SoundCatalogEditing.Rename(catalog, "a", "b"));
            Assert.Throws<ArgumentException>(() => SoundCatalogEditing.Rename(catalog, "a", ""));
            Assert.Throws<ArgumentException>(() => SoundCatalogEditing.Rename(catalog, "zzz", "c"));
            Assert.Throws<ArgumentException>(() => SoundCatalogEditing.Duplicate(catalog, "a", "b"));
            Assert.Throws<ArgumentException>(() => SoundCatalogEditing.Delete(catalog, "zzz"));
            Assert.That(catalog.Entries.Count, Is.EqualTo(2));
            Assert.That(catalog.Entries[0].Key, Is.EqualTo("a"));
            Assert.That(catalog.Entries[1].Key, Is.EqualTo("b"));
            Assert.That(SubAssetCount(), Is.EqualTo(before));
        }

        [Test]
        public void Edits_RequireSavedCatalog()
        {
            var loose = ScriptableObject.CreateInstance<SoundCatalog>();
            try { Assert.Throws<InvalidOperationException>(() => SoundCatalogEditing.AddSound(loose, "a")); }
            finally { Object.DestroyImmediate(loose); }
        }

        [Test]
        public void Validate_ReportsInvalidEntriesPerRow()
        {
            var shared = SoundCatalogEditing.AddSound(catalog, "s1");
            var external = ScriptableObject.CreateInstance<SoundDef>();
            try
            {
                catalog.SetEntries(new[]
                {
                    new SoundCatalogEntry("s1", shared), new SoundCatalogEntry("s2", shared),
                    new SoundCatalogEntry("ext", external), new SoundCatalogEntry("ok", SoundCatalogEditing.AddSound(catalog, "tmp")),
                });
                Assert.That(SoundCatalogEditing.Validate(catalog), Is.EqualTo(new[]
                {
                    SoundCatalogIssue.SharedDefinition, SoundCatalogIssue.SharedDefinition,
                    SoundCatalogIssue.ExternalDefinition, SoundCatalogIssue.None,
                }));
            }
            finally { Object.DestroyImmediate(external); }
        }

        [Test]
        public void Validate_ReportsBlankDuplicateAndMissingRows()
        {
            var def = SoundCatalogEditing.AddSound(catalog, "tmp");
            WriteRows((" ", def), ("d", def), ("d", null), ("m", null), ("ok", def));
            Assert.That(SoundCatalogEditing.Validate(catalog), Is.EqualTo(new[]
            {
                SoundCatalogIssue.BlankKey, SoundCatalogIssue.DuplicateKey, SoundCatalogIssue.DuplicateKey,
                SoundCatalogIssue.MissingDefinition, SoundCatalogIssue.SharedDefinition,
            }));
        }

        [Test]
        public void StructuralEdits_WorkOnCatalogsWithInvalidRows()
        {
            var def = SoundCatalogEditing.AddSound(catalog, "tmp");
            WriteRows((" ", def), ("m", null), ("ok", def));
            int before = SubAssetCount();
            Assert.Throws<ArgumentException>(() => SoundCatalogEditing.AddSound(catalog, "ok"));
            Assert.That(SubAssetCount(), Is.EqualTo(before));
            SoundCatalogEditing.AddSound(catalog, "new");
            Assert.That(SubAssetCount(), Is.EqualTo(before + 1));
            SoundCatalogEditing.Delete(catalog, "m");
            SoundCatalogEditing.Rename(catalog, " ", "fixed");
            Assert.That(catalog.Entries.Select(e => e.Key), Is.EqualTo(new[] { "fixed", "ok", "new" }));
        }
    }
}
