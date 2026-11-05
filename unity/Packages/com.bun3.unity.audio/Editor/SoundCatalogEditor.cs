using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.Editor
{
    [CustomEditor(typeof(SoundCatalog))]
    internal sealed class SoundCatalogEditor : UnityEditor.Editor
    {
        private const float TableHeight = 560f;

        private SoundCatalogTable _table;

        public override bool UseDefaultMargins() => false;

        public override void OnInspectorGUI()
        {
            var catalog = (SoundCatalog)target;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open in Window", EditorStyles.miniButton)) SoundCatalogWindow.Open(catalog);
            }
            _table ??= new SoundCatalogTable(catalog);
            _table.OnGUI(GUILayoutUtility.GetRect(0f, TableHeight, GUILayout.ExpandWidth(true)));
        }

        private void OnDisable() => _table?.Dispose();
    }
}
