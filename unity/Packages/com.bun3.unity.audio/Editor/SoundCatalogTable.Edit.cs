// Editing: cell and bulk edits, the cell menu, and adding, duplicating, renaming and deleting sounds.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Bun3.Unity.Audio.Editor
{
    internal sealed partial class SoundCatalogTable
    {
        private const string RenameNote = "Game code that uses the old key string is not updated.";

        private static readonly GUIContent OverrideHint = new("Right-click → Override with local value to edit");
        private const string AddSoundTooltip = "Add a sound with a new key. Its definition is stored inside this catalog.";
        private const string BulkTooltip =
            "Type a value and press Enter to set it on every selected row that is shown. A value from a profile is overridden first.";
        private static readonly GUIContent AddSoundContent = new("+ Add Sound");
        private static readonly GUIContent BulkContent = new();
        private static readonly Regex RangePattern = new(@"^\s*(-?[\d.]+)\s*(?:[-–~\s]\s*(-?[\d.]+))?\s*$");

        private Column _column = Column.Volume;
        private Row _editRow;
        private Column _editColumn;
        private string _bulkVolume = string.Empty;
        private string _bulkPriority = string.Empty;
        private Action _pending;
        private Rect _menuAt;

        // Click on a value cell remembers its column for Enter; Loop's box and Spatial change at once; double-click edits.
        partial void CellInput(Rect rect, Row row, Column column)
        {
            var current = Event.current;
            if (!rect.Contains(current.mousePosition)) return;
            if (current.type == EventType.ContextClick)
            {
                CellMenu(rect, row, column).ShowAsContext();
                current.Use();
                return;
            }
            if (current.type != EventType.MouseDown || current.button != 0 || row.Def == null) return;
            if (column == Column.Preview)
            {
                if (row.ClipCount > 0) SoundPreview.Play(row.Def);
                current.Use();
                return;
            }
            if (Columns[(int)column].Field == SoundDefField.None) return;
            _column = column;
            var instant = column == Column.Spatial || (column == Column.Loop && CheckRect(rect).Contains(current.mousePosition));
            if (!instant && current.clickCount != 2) return;
            BeginEdit(row, column);
            current.Use();
        }

        private void KeyInput()
        {
            var current = Event.current;
            if (current.type != EventType.KeyDown || (current.keyCode != KeyCode.Return && current.keyCode != KeyCode.KeypadEnter)) return;
            var rows = VisibleSelection();
            if (rows.Count != 1) return;
            BeginEdit(rows[0], _column);
            current.Use();
        }

        private void BeginEdit(Row row, Column column)
        {
            var field = Columns[(int)column].Field;
            if (row.Def == null || field == SoundDefField.None) return;
            if ((row.Inherited & field) != 0)
            {
                EditorWindow.focusedWindow?.ShowNotification(OverrideHint);
                return;
            }
            switch (column)
            {
                case Column.Loop:
                    Apply(new[] { row }, column, (!row.Loop).ToString(), false);
                    return;
                case Column.Spatial:
                    var menu = new GenericMenu();
                    foreach (SpatialMode mode in Enum.GetValues(typeof(SpatialMode)))
                        menu.AddItem(new GUIContent(mode.ToString()), mode == row.Spatial, () => Apply(new[] { row }, column, mode.ToString(), false));
                    menu.ShowAsContext();
                    return;
            }
            // The tree's rename field edits the cell; it starts from the item's display name.
            _editRow = row;
            _editColumn = column;
            row.displayName = EditText(row, column);
            if (!_view.BeginRename(row)) EndEdit(null);
        }

        private void EndEdit(string text)
        {
            var row = _editRow;
            _editRow = null;
            if (row == null) return;
            row.displayName = row.Key;
            if (text != null) Apply(new[] { row }, _editColumn, text, false);
        }

        private GenericMenu CellMenu(Rect cell, Row row, Column column)
        {
            _menuAt = GUIUtility.GUIToScreenRect(cell);
            var menu = new GenericMenu();
            var (label, _, field, _) = Columns[(int)column];
            if (row.Def != null && field != SoundDefField.None)
            {
                var profile = row.Profiles[(int)SoundDefOverrides.GroupOf(field)];
                if (profile != null)
                {
                    if ((row.Inherited & field) != 0) menu.AddItem(new GUIContent("Override with local value"), false, () => SetOverride(row, field, true));
                    else menu.AddItem(new GUIContent($"Revert to profile ({ProfileName(profile)})"), false, () => SetOverride(row, field, false));
                    menu.AddItem(new GUIContent("Select profile asset"), false, () =>
                    {
                        Selection.activeObject = profile;
                        EditorGUIUtility.PingObject(profile);
                    });
                }
                var targets = VisibleSelection();
                targets.Remove(row);
                AddItem(menu, $"Copy {label} to selected rows", targets.Count > 0, () => Apply(targets, column, EditText(row, column), true));
                menu.AddSeparator(string.Empty);
            }
            AddItem(menu, "Preview", row.ClipCount > 0, () => SoundPreview.Play(row.Def));
            menu.AddSeparator(string.Empty);
            // Key-based edits act on the first row with the key, so a later duplicate of it offers none.
            var keyed = _rows.FindIndex(r => r.Key == row.Key) == row.Index;
            AddItem(menu, "Duplicate sound…", keyed && row.Def != null, () => Prompt("Duplicate sound", "Duplicate", row.Key, null,
                key => SoundCatalogEditing.Duplicate(_catalog, row.Key, key)));
            AddItem(menu, "Rename key…", keyed, () => Prompt("Rename key", "Rename", row.Key, RenameNote,
                key => SoundCatalogEditing.Rename(_catalog, row.Key, key)));
            if (row.Issue == SoundCatalogIssue.SharedDefinition)
                menu.AddItem(new GUIContent("Duplicate for this key"), false, () => Defer(() => Separate(row.Key)));
            menu.AddSeparator(string.Empty);
            AddItem(menu, "Delete sound", keyed, () => Defer(() => Delete(row.Key)));
            return menu;
        }

        private static void AddItem(GenericMenu menu, string text, bool enabled, GenericMenu.MenuFunction action)
        {
            if (enabled) menu.AddItem(new GUIContent(text), false, action);
            else menu.AddDisabledItem(new GUIContent(text));
        }

        private void SetOverride(Row row, SoundDefField field, bool overridden)
        {
            var serialized = SerializedOf(row.Def);
            serialized.Update();
            if (overridden) SoundDefOverrides.Override(serialized, field);
            else SoundDefOverrides.Revert(serialized, field);
            _view.Repaint();
        }

        // Rows the user can see among the selected ones: not filtered out and not inside a collapsed group.
        private List<Row> VisibleSelection()
        {
            var rows = new List<Row>();
            foreach (var id in _view.GetSelection())
            {
                if (!_shown.Contains(id)) continue;
                var row = _rows[id - 1];
                if (_grouped && !_view.IsExpanded(_groupIds[row.Group])) continue;
                rows.Add(row);
            }
            return rows;
        }

        // Writes one parsed value into each row's local field as one undo step. A row whose field comes from its
        // profile is skipped, or overridden first when overrideInherited is set. Unparsable text changes nothing.
        private void Apply(IReadOnlyList<Row> rows, Column column, string text, bool overrideInherited)
        {
            var write = Parse(column, text);
            if (write == null) return;
            var field = Columns[(int)column].Field;
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            foreach (var row in rows)
            {
                if (row.Def == null) continue;
                var serialized = SerializedOf(row.Def);
                serialized.Update();
                if (SoundDefOverrides.IsInherited(serialized, field))
                {
                    if (!overrideInherited) continue;
                    SoundDefOverrides.Override(serialized, field);
                }
                write(serialized.FindProperty(SoundDefOverrides.PropertyPath(field)));
                serialized.ApplyModifiedProperties();
            }
            Undo.CollapseUndoOperations(group);
            Undo.SetCurrentGroupName($"Set {Columns[(int)column].Label}");
            _view.Repaint();
        }

        private static Action<SerializedProperty> Parse(Column column, string text)
        {
            var invariant = CultureInfo.InvariantCulture;
            switch (column)
            {
                case Column.Volume when TryParseRange(text, out var volume):
                    return p => p.boxedValue = new FloatRange(Mathf.Clamp01(volume.Min), Mathf.Clamp01(volume.Max));
                case Column.Pitch when TryParseRange(text, out var pitch):
                    return p => p.boxedValue = pitch;
                case Column.Loop when bool.TryParse(text, out var loop):
                    return p => p.boolValue = loop;
                case Column.VolumeGroup:
                    return p => p.stringValue = text.Trim();
                case Column.MaxInstances when int.TryParse(text, NumberStyles.Integer, invariant, out var max):
                    return p => p.intValue = Mathf.Max(0, max);
                case Column.Cooldown when TryNumber(text.TrimEnd('s', ' '), out var cooldown):
                    return p => p.floatValue = Mathf.Max(0f, cooldown);
                case Column.Priority when int.TryParse(text, NumberStyles.Integer, invariant, out var priority):
                    return p => p.intValue = Mathf.Clamp(priority, 0, 255);
                case Column.Spatial when Enum.TryParse<SpatialMode>(text, out var spatial):
                    return p => p.intValue = (int)spatial;
                default:
                    return null;
            }
        }

        // Accepts "0.75-0.9", "0.75–0.9", "0.75~0.9" or "0.75 0.9", or one number for both ends. A comma is not a
        // separator, so a comma decimal such as "0,5" is refused instead of read as a range.
        private static bool TryParseRange(string text, out FloatRange range)
        {
            range = default;
            var match = RangePattern.Match(text);
            if (!match.Success || !TryNumber(match.Groups[1].Value, out var a)) return false;
            var b = a;
            if (match.Groups[2].Success && !TryNumber(match.Groups[2].Value, out b)) return false;
            range = new FloatRange(Mathf.Min(a, b), Mathf.Max(a, b));
            return true;
        }

        private static bool TryNumber(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);

        // The text a cell edit starts from, which Parse reads back; also how a value is copied to other rows.
        private static string EditText(Row row, Column column) => column switch
        {
            Column.Volume => RangeText(row.Volume),
            Column.Pitch => RangeText(row.Pitch),
            Column.Loop => row.Loop.ToString(),
            Column.VolumeGroup => row.VolumeGroup,
            Column.MaxInstances => row.MaxInstances.ToString(CultureInfo.InvariantCulture),
            Column.Cooldown => row.Cooldown.ToString("R", CultureInfo.InvariantCulture),
            Column.Priority => row.Priority.ToString(CultureInfo.InvariantCulture),
            Column.Spatial => row.Spatial.ToString(),
            _ => string.Empty,
        };

        private static string RangeText(FloatRange range)
        {
            var min = range.Min.ToString("R", CultureInfo.InvariantCulture);
            return range.Min == range.Max ? min : min + "-" + range.Max.ToString("R", CultureInfo.InvariantCulture);
        }

        private void BulkGUI(ref Rect area, List<Row> rows)
        {
            var bar = new Rect(area.x, area.yMax - EditorStyles.toolbar.fixedHeight, area.width, EditorStyles.toolbar.fixedHeight);
            area.yMax = bar.yMin;
            BulkContent.text = $"Set on {rows.Count} rows:";
            BulkContent.tooltip = Tr(BulkTooltip);
            GUILayout.BeginArea(bar, EditorStyles.toolbar);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(BulkContent, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                _bulkVolume = BulkField("Volume", _bulkVolume, Column.Volume, rows);
                _bulkPriority = BulkField("Priority", _bulkPriority, Column.Priority, rows);
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndArea();
        }

        private string BulkField(string label, string text, Column column, List<Row> rows)
        {
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            EditorGUI.BeginChangeCheck();
            text = EditorGUILayout.DelayedTextField(text, EditorStyles.toolbarTextField, GUILayout.Width(64f));
            if (!EditorGUI.EndChangeCheck()) return text;
            Apply(rows, column, text, true);
            return string.Empty;
        }

        private void AddSoundGUI(Rect rect)
        {
            AddSoundContent.tooltip = Tr(AddSoundTooltip);
            if (GUI.Button(rect, AddSoundContent, EditorStyles.toolbarButton))
                PopupWindow.Show(rect, NewPrompt("Add Sound", "Add", string.Empty, null, key => SoundCatalogEditing.AddSound(_catalog, key)));
        }

        // Menu work that reloads the tree or opens a popup runs after a repaint has drawn everything, so it never
        // interrupts the tree or a Layout pass (a popup ends the GUI pass it opens in).
        private void Defer(Action action)
        {
            _pending = action;
            _view.Repaint();
        }

        private void RunPending()
        {
            if (_pending == null || Event.current.type != EventType.Repaint) return;
            var action = _pending;
            _pending = null;
            action();
        }

        private void Prompt(string title, string button, string key, string note, Action<string> edit) =>
            Defer(() => PopupWindow.Show(GUIUtility.ScreenToGUIRect(_menuAt), NewPrompt(title, button, key, note, edit)));

        private KeyPrompt NewPrompt(string title, string button, string key, string note, Action<string> edit) =>
            new(title, button, key, note, newKey =>
            {
                edit(newKey);
                Select(newKey);
            });

        // Gives the key its own copy of a definition another row also uses; the row moves to the end of the catalog.
        private void Separate(string key)
        {
            var temporary = key + "#" + Guid.NewGuid().ToString("N");
            SoundCatalogEditing.Duplicate(_catalog, key, temporary);
            SoundCatalogEditing.Delete(_catalog, key);
            SoundCatalogEditing.Rename(_catalog, temporary, key);
            Select(key);
        }

        private void Delete(string key)
        {
            var message = SoundCatalogEditing.DeletesDefinition(_catalog, key)
                ? $"Delete '{key}' and its definition from this catalog? This cannot be undone; restore it from version control."
                : $"Remove the entry '{key}' from this catalog? Only the entry is removed; a definition is deleted only when this " +
                  "catalog stores it and no other row uses it. This cannot be undone; restore it from version control.";
            if (EditorUtility.DisplayDialog("Delete sound", message, "Delete", "Cancel"))
                SoundCatalogEditing.Delete(_catalog, key);
        }

        // Selects the row of key, clearing the search and chip when they hide it.
        private void Select(string key)
        {
            Refresh();
            var row = _rows.Find(r => r.Key == key);
            if (row == null) return;
            if (!_shown.Contains(row.id))
            {
                SetChip(AllChip);
                SetSearch(string.Empty);
            }
            _view.SetSelection(new[] { row.id }, TreeViewSelectionOptions.RevealAndFrame);
            _view.Repaint();
        }

        private sealed partial class View
        {
            protected override void KeyEvent() => _table.KeyInput();

            protected override bool CanRename(TreeViewItem<int> item) => item == _table._editRow;

            protected override Rect GetRenameRect(Rect rowRect, int row, TreeViewItem<int> item)
            {
                var cell = multiColumnHeader.GetCellRect(multiColumnHeader.GetVisibleColumnIndex((int)_table._editColumn), rowRect);
                CenterRectUsingSingleLineHeight(ref cell);
                return cell;
            }

            protected override void RenameEnded(RenameEndedArgs args) =>
                _table.EndEdit(args.acceptedRename && args.newName != args.originalName ? args.newName : null);
        }

        // A key field; the edit gets the trimmed key and throws ArgumentException for a key it refuses, which is shown and
        // leaves the prompt open.
        private sealed class KeyPrompt : PopupWindowContent
        {
            private const string FieldName = "Bun3SoundKey";
            private readonly string _title;
            private readonly string _button;
            private readonly string _note;
            private readonly Action<string> _edit;
            private string _key;
            private string _error;
            private bool _focused;

            public KeyPrompt(string title, string button, string key, string note, Action<string> edit)
            {
                _title = title;
                _button = button;
                _key = key;
                _note = note;
                _edit = edit;
            }

            public override Vector2 GetWindowSize() => new(340f, 112f);

            public override void OnGUI(Rect rect)
            {
                var current = Event.current;
                if (current.type == EventType.KeyDown && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter))
                {
                    current.Use();
                    Submit();
                }
                GUILayout.Label(_title, EditorStyles.boldLabel);
                GUI.SetNextControlName(FieldName);
                _key = EditorGUILayout.TextField(_key);
                if (!_focused)
                {
                    EditorGUI.FocusTextInControl(FieldName);
                    _focused = true;
                }
                if (_note != null) GUILayout.Label(_note, EditorStyles.miniLabel);
                if (_error != null) GUILayout.Label(_error, Styles.Error);
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(_button, GUILayout.Width(90f))) Submit();
                }
            }

            private void Submit()
            {
                try
                {
                    _edit(_key.Trim());
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException)
                {
                    _error = e.Message.Split('\n')[0];
                    return;
                }
                editorWindow.Close();
                GUIUtility.ExitGUI();
            }
        }
    }
}
