using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.Editor
{
    /// <summary>A resizable window showing the table of one <see cref="SoundCatalog"/>.</summary>
    internal sealed class SoundCatalogWindow : EditorWindow
    {
        [SerializeField] private SoundCatalog _catalog;

        private SoundCatalogTable _table;

        /// <summary>Opens the window, showing <paramref name="catalog"/> when it is not null.</summary>
        internal static void Open(SoundCatalog catalog)
        {
            var window = GetWindow<SoundCatalogWindow>("Sound Catalog");
            window.minSize = new Vector2(480f, 240f);
            if (catalog != null) window.SetCatalog(catalog);
        }

        [MenuItem("Window/Bun3/Sound Catalog")]
        private static void OpenFromMenu() => Open(Selection.activeObject as SoundCatalog);

        private void OnGUI()
        {
            var line = new Rect(4f, 4f, position.width - 8f, EditorGUIUtility.singleLineHeight);
            var picked = (SoundCatalog)EditorGUI.ObjectField(line, "Catalog", _catalog, typeof(SoundCatalog), false);
            if (picked != _catalog) SetCatalog(picked);
            if (_catalog == null)
            {
                ReleaseTable();
                EditorGUI.HelpBox(new Rect(4f, line.yMax + 4f, position.width - 8f, 2f * line.height),
                    "Pick a Sound Catalog to show its sounds.", MessageType.Info);
                return;
            }
            _table ??= new SoundCatalogTable(_catalog);
            _table.OnGUI(new Rect(0f, line.yMax + 4f, position.width, position.height - line.yMax - 4f));
        }

        private void SetCatalog(SoundCatalog catalog)
        {
            if (catalog == _catalog && _table != null) return;
            ReleaseTable();
            _catalog = catalog;
            Repaint();
        }

        private void ReleaseTable()
        {
            _table?.Dispose();
            _table = null;
        }

        private void OnInspectorUpdate()
        {
            if (_table != null && _table.IsStale) Repaint();
        }

        private void OnDisable() => ReleaseTable();
    }
}
