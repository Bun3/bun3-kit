using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.Editor
{
    [CustomEditor(typeof(SoundCatalog))]
    internal sealed class SoundCatalogEditor : UnityEditor.Editor
    {
        private const float TableHeight = 560f;

        private SoundCatalogTable _table;
        private Rect _rect;

        public override bool UseDefaultMargins() => false;

        public override bool RequiresConstantRepaint() => _table != null && _table.IsStale;

        public override void OnInspectorGUI()
        {
            var catalog = (SoundCatalog)target;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open in Window", EditorStyles.miniButton)) SoundCatalogWindow.Open(catalog);
            }
            _table ??= new SoundCatalogTable(catalog);
            var rect = GUILayoutUtility.GetRect(0f, TableHeight, GUILayout.ExpandWidth(true));
            // GetRect returns a placeholder during Layout; the detail pane's own layout needs the real one.
            if (Event.current.type == EventType.Layout) rect = _rect;
            else _rect = rect;
            _table.OnGUI(rect);
        }

        private void OnDisable() => _table?.Dispose();
    }
}
