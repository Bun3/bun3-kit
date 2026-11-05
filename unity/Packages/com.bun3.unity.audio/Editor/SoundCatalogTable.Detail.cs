// Detail pane: the one selected sound's definition inspector below the table; two or more selected rows get the bulk bar.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.Editor
{
    internal sealed partial class SoundCatalogTable
    {
        private const float DetailHeight = 320f;

        private UnityEditor.Editor _detail;
        private Vector2 _detailScroll;
        private List<Row> _paneRows = new();

        partial void DetailGUI(ref Rect area)
        {
            // GUILayout measures the panes in the Layout event, so what they show changes only then.
            if (Event.current.type == EventType.Layout) _paneRows = VisibleSelection();
            var rows = _paneRows;
            if (rows.Count > 1)
            {
                BulkGUI(ref area, rows);
                return;
            }
            if (rows.Count == 0 || rows[0].Def == null) return;

            var pane = new Rect(area.x, area.yMax - Mathf.Min(DetailHeight, area.height * 0.5f), area.width, 0f);
            pane.yMax = area.yMax;
            area.yMax = pane.yMin - 1f;
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(pane.x, pane.y - 1f, pane.width, 1f), new Color(0f, 0f, 0f, 0.4f));

            // The Def inspector draws the same per-field override rules; its x=0 override bar lands on the pane's left edge.
            UnityEditor.Editor.CreateCachedEditor(rows[0].Def, typeof(SoundDefEditor), ref _detail);
            var hierarchyMode = EditorGUIUtility.hierarchyMode;
            var wideMode = EditorGUIUtility.wideMode;
            EditorGUIUtility.hierarchyMode = true;
            EditorGUIUtility.wideMode = pane.width > 330f;
            try
            {
                GUILayout.BeginArea(pane);
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
                {
                    EditorGUILayout.LabelField(rows[0].Key, EditorStyles.boldLabel);
                    _detail.OnInspectorGUI();
                }
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
            }
            finally
            {
                EditorGUIUtility.hierarchyMode = hierarchyMode;
                EditorGUIUtility.wideMode = wideMode;
            }
        }

        private void ReleaseDetail()
        {
            if (_detail != null) Object.DestroyImmediate(_detail);
            _detail = null;
        }
    }
}
