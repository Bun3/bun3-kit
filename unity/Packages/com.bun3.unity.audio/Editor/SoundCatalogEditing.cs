using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Editor
{
    /// <summary>The first problem found in one catalog row.</summary>
    public enum SoundCatalogIssue
    {
        /// <summary>The row is fine.</summary>
        None,
        /// <summary>The key is empty or whitespace.</summary>
        BlankKey,
        /// <summary>Another row uses the same key.</summary>
        DuplicateKey,
        /// <summary>The row has no definition.</summary>
        MissingDefinition,
        /// <summary>The definition is not stored in the catalog's own file.</summary>
        ExternalDefinition,
        /// <summary>Another row uses the same definition.</summary>
        SharedDefinition,
    }

    /// <summary>Edits a saved <see cref="SoundCatalog"/> so each owned <see cref="SoundDef"/> is a hidden sub-asset of it.</summary>
    public static class SoundCatalogEditing
    {
        /// <summary>Adds a new definition named by <paramref name="key"/> as a sub-asset and appends its entry.</summary>
        public static SoundDef AddSound(SoundCatalog catalog, string key)
        {
            RequireSaved(catalog);
            var rows = new List<SoundCatalogEntry>(catalog.Entries);
            RequireFreeKey(rows, key);
            var def = ScriptableObject.CreateInstance<SoundDef>();
            rows.Add(new SoundCatalogEntry(key, def));
            Own(catalog, def, key, rows);
            return def;
        }

        /// <summary>Adds a sub-asset copy of the definition under <paramref name="sourceKey"/>, keyed <paramref name="newKey"/>.</summary>
        public static SoundDef Duplicate(SoundCatalog catalog, string sourceKey, string newKey)
        {
            RequireSaved(catalog);
            var rows = new List<SoundCatalogEntry>(catalog.Entries);
            var source = rows[IndexOf(rows, sourceKey)].Definition;
            if (source == null) throw new ArgumentException("The source entry has no definition.", nameof(sourceKey));
            RequireFreeKey(rows, newKey);
            var copy = Object.Instantiate(source);
            rows.Add(new SoundCatalogEntry(newKey, copy));
            Own(catalog, copy, newKey, rows);
            return copy;
        }

        /// <summary>Changes a key, and the definition's name too when the catalog owns it.</summary>
        public static void Rename(SoundCatalog catalog, string key, string newKey)
        {
            RequireSaved(catalog);
            var rows = new List<SoundCatalogEntry>(catalog.Entries);
            int index = IndexOf(rows, key);
            RequireFreeKey(rows, newKey);
            var def = rows[index].Definition;
            rows[index] = new SoundCatalogEntry(newKey, def);
            if (def != null && IsOwnedBy(catalog, def)) def.name = newKey;
            Commit(catalog, rows, def);
        }

        /// <summary>Removes an entry, and its definition too when the catalog owns it and no other entry uses it.</summary>
        public static void Delete(SoundCatalog catalog, string key)
        {
            RequireSaved(catalog);
            var rows = new List<SoundCatalogEntry>(catalog.Entries);
            int index = IndexOf(rows, key);
            var def = rows[index].Definition;
            rows.RemoveAt(index);
            bool destroy = def != null && IsOwnedBy(catalog, def) && rows.All(r => r.Definition != def);
            Commit(catalog, rows, def);
            if (!destroy) return;
            AssetDatabase.RemoveObjectFromAsset(def);
            Object.DestroyImmediate(def, true);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }

        /// <summary>True when the definition lives in the catalog's file and is not that file's main asset.</summary>
        public static bool IsOwnedBy(SoundCatalog catalog, SoundDef def)
        {
            if (catalog == null || def == null) return false;
            var path = AssetDatabase.GetAssetPath(catalog);
            return path.Length > 0 && AssetDatabase.GetAssetPath(def) == path && !AssetDatabase.IsMainAsset(def);
        }

        /// <summary>One issue per entry in order: the first of blank key, duplicate key, missing, external, shared definition.</summary>
        public static SoundCatalogIssue[] Validate(SoundCatalog catalog)
        {
            var rows = catalog.Entries;
            var result = new SoundCatalogIssue[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                var key = rows[i].Key;
                var def = rows[i].Definition;
                result[i] =
                    string.IsNullOrWhiteSpace(key) ? SoundCatalogIssue.BlankKey :
                    rows.Count(r => r.Key == key) > 1 ? SoundCatalogIssue.DuplicateKey :
                    def == null ? SoundCatalogIssue.MissingDefinition :
                    !IsOwnedBy(catalog, def) ? SoundCatalogIssue.ExternalDefinition :
                    rows.Count(r => r.Definition == def) > 1 ? SoundCatalogIssue.SharedDefinition :
                    SoundCatalogIssue.None;
            }
            return result;
        }

        private static void RequireSaved(SoundCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (AssetDatabase.GetAssetPath(catalog).Length == 0)
                throw new InvalidOperationException("The sound catalog must be a saved asset.");
        }

        private static void RequireFreeKey(List<SoundCatalogEntry> rows, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Keys must not be blank.", nameof(key));
            if (rows.Any(r => r.Key == key)) throw new ArgumentException($"Key '{key}' already exists.", nameof(key));
        }

        private static int IndexOf(List<SoundCatalogEntry> rows, string key)
        {
            int index = rows.FindIndex(r => r.Key == key);
            if (index < 0) throw new ArgumentException($"Key '{key}' does not exist.", nameof(key));
            return index;
        }

        // Adds the Def to the catalog file, and takes it out again if writing the rows fails.
        private static void Own(SoundCatalog catalog, SoundDef def, string key, List<SoundCatalogEntry> rows)
        {
            def.name = key;
            def.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(def, catalog);
            try { Commit(catalog, rows, def); }
            catch
            {
                AssetDatabase.RemoveObjectFromAsset(def);
                Object.DestroyImmediate(def, true);
                throw;
            }
        }

        private static void Commit(SoundCatalog catalog, List<SoundCatalogEntry> rows, SoundDef def)
        {
            // Not SetEntries: it rejects invalid rows, and removing them is how a user repairs a catalog.
            var so = new SerializedObject(catalog);
            var entries = so.FindProperty("entries");
            entries.arraySize = rows.Count;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = entries.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("key").stringValue = rows[i].Key;
                row.FindPropertyRelative("definition").objectReferenceValue = rows[i].Definition;
            }
            so.ApplyModifiedPropertiesWithoutUndo(); // also drops the catalog's cached lookup
            EditorUtility.SetDirty(catalog);
            if (def != null) EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssetIfDirty(catalog);
            Undo.ClearUndo(catalog);
            if (def != null) Undo.ClearUndo(def);
        }
    }
}
