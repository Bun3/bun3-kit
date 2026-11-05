// Columns: definitions, header and group tooltips, sort order and cell drawing.
using System.Globalization;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Bun3.Unity.Audio.Editor
{
    internal sealed partial class SoundCatalogTable
    {
        /// <summary>The table columns in display order.</summary>
        internal enum Column
        {
            /// <summary>Selection check box.</summary>
            Select,
            /// <summary>Preview button.</summary>
            Preview,
            /// <summary>Catalog key.</summary>
            Key,
            /// <summary>Clip count.</summary>
            Clips,
            /// <summary>Effective volume range.</summary>
            Volume,
            /// <summary>Effective pitch range.</summary>
            Pitch,
            /// <summary>Effective loop flag.</summary>
            Loop,
            /// <summary>Effective volume group.</summary>
            VolumeGroup,
            /// <summary>Effective instance limit.</summary>
            MaxInstances,
            /// <summary>Effective request cooldown.</summary>
            Cooldown,
            /// <summary>Effective channel priority.</summary>
            Priority,
            /// <summary>Effective spatial mode.</summary>
            Spatial,
        }

        private const float VolumeBarWidth = 44f;

        private static readonly (string Label, float Width, SoundDefField Field, string Tooltip)[] Columns =
        {
            ("", 22f, SoundDefField.None,
                "Select rows for bulk editing. Ctrl+click adds a row, Shift+click selects a range."),
            ("", 24f, SoundDefField.None,
                "Preview with this sound's volume and pitch. Disabled when the sound has no clips."),
            ("Key", 240f, SoundDefField.None,
                "Name passed to SoundSystem.Play. Also the name of the definition stored in this catalog."),
            ("Clips", 76f, SoundDefField.None,
                "Number of AudioClips. One is picked at random on each play.\n0 means nothing plays; the fallback key plays instead when one is given."),
            ("Volume", 124f, SoundDefField.Volume,
                "Volume from 0 to 1, picked between Min and Max on each play. Set both to the same value for a fixed level.\nThe player's volume setting for this sound's Volume Group is applied on top."),
            ("Pitch", 84f, SoundDefField.Pitch,
                "Pitch multiplier picked between Min and Max on each play. 1 is the original pitch; a range like 0.95–1.05 adds slight variation."),
            ("Loop", 50f, SoundDefField.Loop,
                "Repeats until the code stops it. A charge sound uses this loop as its rising layer."),
            ("Volume Group", 96f, SoundDefField.VolumeGroup,
                "Which player volume setting scales this sound, e.g. sfx, music or ui."),
            ("Max", 46f, SoundDefField.MaxInstances,
                "Maximum copies of this sound playing at once. ∞ means no limit.\nWhen the limit is reached, the oldest copy is replaced."),
            ("Cooldown", 70f, SoundDefField.Cooldown,
                "Minimum seconds between play requests. Requests inside this window are ignored. — means no limit."),
            ("Priority", 60f, SoundDefField.Priority,
                "0 is the most important, 255 the least (default 128).\nWhen every channel is busy, the least important sound is replaced. A new sound less important than all playing ones does not play."),
            ("Spatial", 80f, SoundDefField.Spatial,
                "None: heard the same everywhere.\nPositional or Follow: heard from a world position, with distance falloff and occlusion."),
        };

        private static readonly (Column First, Column Last, GUIContent Content)[] Bands =
        {
            (Column.Volume, Column.Loop, new GUIContent("Playback",
                "How the sound plays: volume, pitch and looping. Can come from a shared Playback profile.")),
            (Column.VolumeGroup, Column.VolumeGroup, new GUIContent("Routing",
                "Which player volume setting and mixer group the sound uses. Can come from a shared Routing profile.")),
            (Column.MaxInstances, Column.Priority, new GUIContent("Concurrency",
                "How overlapping requests are handled: instance limit, request cooldown and channel priority. Can come from a shared Concurrency profile.")),
            (Column.Spatial, Column.Spatial, new GUIContent("Spatial",
                "Whether the sound has a position in the world. Can come from a shared Spatial profile.")),
        };

        private static readonly GUIContent Cell = new();
        private static readonly GUIContent Tip = new();
        private static readonly Color ErrorColor = new(0.9f, 0.25f, 0.2f);
        private static readonly Color ErrorTint = new(0.9f, 0.25f, 0.2f, 0.1f);
        private static readonly Color WarningColor = new(0.96f, 0.74f, 0.01f);
        private static readonly Color BarBack = new(0.13f, 0.13f, 0.13f);
        private static readonly Color BarFill = new(0.31f, 0.51f, 0.72f);
        private static readonly Color BarRange = new(0.5f, 0.66f, 0.84f, 0.55f);
        private static readonly Color InheritedFill = new(0.4f, 0.4f, 0.4f);
        private static readonly Color InheritedRange = new(0.5f, 0.5f, 0.5f, 0.45f);

        private static MultiColumnHeaderState CreateHeaderState()
        {
            var columns = new MultiColumnHeaderState.Column[Columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                columns[i] = new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent(Columns[i].Label, Columns[i].Tooltip),
                    width = Columns[i].Width,
                    minWidth = i == (int)Column.Key ? 120f : 20f,
                    autoResize = i == (int)Column.Key,
                    canSort = i >= (int)Column.Key,
                    allowToggleVisibility = false,
                    sortedAscending = false,
                };
            }
            return new MultiColumnHeaderState(columns);
        }

        // Volume and Pitch sort by the middle of their range; an unlimited Max sorts above every limit.
        private static int Compare(Row a, Row b, Column column) => column switch
        {
            Column.Key => string.CompareOrdinal(a.Key, b.Key),
            Column.Clips => a.ClipCount.CompareTo(b.ClipCount),
            Column.Volume => Middle(a.Volume).CompareTo(Middle(b.Volume)),
            Column.Pitch => Middle(a.Pitch).CompareTo(Middle(b.Pitch)),
            Column.Loop => a.Loop.CompareTo(b.Loop),
            Column.VolumeGroup => string.CompareOrdinal(a.VolumeGroup, b.VolumeGroup),
            Column.MaxInstances => Limit(a.MaxInstances).CompareTo(Limit(b.MaxInstances)),
            Column.Cooldown => a.Cooldown.CompareTo(b.Cooldown),
            Column.Priority => a.Priority.CompareTo(b.Priority),
            Column.Spatial => a.Spatial.CompareTo(b.Spatial),
            _ => 0,
        };

        private static float Middle(FloatRange range) => (range.Min + range.Max) * 0.5f;

        private static int Limit(int maxInstances) => maxInstances <= 0 ? int.MaxValue : maxInstances;

        private static string Text(Row row, Column column) => column switch
        {
            Column.Volume => Range(row.Volume),
            Column.Pitch => Range(row.Pitch),
            Column.VolumeGroup => row.VolumeGroup,
            Column.MaxInstances => row.MaxInstances <= 0 ? "∞" : row.MaxInstances.ToString(CultureInfo.InvariantCulture),
            Column.Cooldown => row.Cooldown <= 0f ? "—" : row.Cooldown.ToString("0.###", CultureInfo.InvariantCulture) + "s",
            Column.Priority => row.Priority.ToString(CultureInfo.InvariantCulture),
            Column.Spatial => row.Spatial.ToString(),
            _ => string.Empty,
        };

        private static string Range(FloatRange range) => range.Min == range.Max
            ? range.Min.ToString("0.00", CultureInfo.InvariantCulture)
            : range.Min.ToString("0.00", CultureInfo.InvariantCulture) + "–" + range.Max.ToString("0.00", CultureInfo.InvariantCulture);

        private static string Describe(SoundCatalogIssue issue) => issue switch
        {
            SoundCatalogIssue.BlankKey => "The key is blank.",
            SoundCatalogIssue.DuplicateKey => "Another row uses the same key.",
            SoundCatalogIssue.MissingDefinition => "This row has no definition.",
            SoundCatalogIssue.ExternalDefinition => "The definition is an asset outside this catalog file.",
            SoundCatalogIssue.SharedDefinition => "Another row uses the same definition.",
            _ => null,
        };

        private static void RowBackground(Rect rowRect, Row row)
        {
            if (row.Issue == SoundCatalogIssue.None || Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(rowRect, ErrorTint);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 2f, rowRect.height), ErrorColor);
        }

        private void GroupGUI(Rect rowRect, GroupItem group)
        {
            if (Event.current.type != EventType.Repaint) return;
            var rect = _view.FoldoutCell(rowRect);
            rect.xMin += _view.Indent(group);
            rect.xMax = rowRect.xMax;
            Cell.text = group.displayName;
            rect.xMin += Draw(rect, Cell, EditorStyles.boldLabel) + 6f;
            Cell.text = group.Count.ToString(CultureInfo.InvariantCulture);
            rect.xMin += Draw(rect, Cell, Styles.Dim) + 10f;
            if (group.Missing == 0) return;
            rect.xMin += Icon(rect, Styles.Warning.image);
            Cell.text = $"{group.Missing} without clips";
            Draw(rect, Cell, Styles.Warn);
        }

        private void CellGUI(Rect rect, Row row, Column column)
        {
            switch (column)
            {
                case Column.Select:
                    var selected = _view.IsSelected(row.id);
                    var box = new Rect(rect.center.x - 7f, rect.center.y - 8f, 16f, 16f);
                    if (GUI.Toggle(box, selected, GUIContent.none, EditorStyles.toggle) != selected) ToggleSelected(row.id);
                    return;
                case Column.Key:
                    KeyGUI(rect, row);
                    return;
            }
            if (Event.current.type != EventType.Repaint || row.Def == null) return;
            switch (column)
            {
                case Column.Preview:
                    using (new EditorGUI.DisabledScope(row.ClipCount == 0))
                        Icon(new Rect(rect.center.x - 8f, rect.y, 16f, rect.height), Styles.Play.image);
                    return;
                case Column.Clips:
                    ClipsGUI(rect, row);
                    return;
                default:
                    ValueGUI(rect, row, column);
                    return;
            }
        }

        private void KeyGUI(Rect rect, Row row)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_grouped) rect.xMin += _view.Indent(row);
            if (row.MissingClips) EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), WarningColor);
            Tip.tooltip = Describe(row.Issue);
            if (Tip.tooltip != null) GUI.Label(rect, Tip);
            rect.xMin += 5f;
            rect.xMin += Draw(rect, row.KeyPrefix, Styles.KeyPrefix);
            Draw(rect, row.KeyName, Styles.KeyName);
        }

        private static void ClipsGUI(Rect rect, Row row)
        {
            if (row.MissingClips)
            {
                rect.xMin += Icon(rect, Styles.Warning.image);
                Cell.text = "0";
                Draw(rect, Cell, Styles.Warn);
                return;
            }
            Cell.text = row.Addressable ? "Addressable" : row.ClipCount.ToString(CultureInfo.InvariantCulture);
            Draw(rect, Cell, row.Addressable ? EditorStyles.miniLabel : EditorStyles.label);
        }

        private static void ValueGUI(Rect rect, Row row, Column column)
        {
            var field = Columns[(int)column].Field;
            var inherited = (row.Inherited & field) != 0;
            var overridden = (row.Overridden & field) != 0;
            if (inherited || overridden)
            {
                var profile = row.Profiles[(int)SoundDefOverrides.GroupOf(field)];
                Tip.tooltip = (inherited ? "From " : "Overrides ") + profile.name;
                GUI.Label(rect, Tip);
            }
            if (overridden) SoundOverrideGUI.OverrideBar(rect);
            using (new EditorGUI.DisabledScope(inherited))
            {
                if (inherited) Icon(new Rect(rect.xMax - 14f, rect.y, 12f, rect.height), Styles.Linked.image);
                if (column == Column.Volume) VolumeBar(ref rect, row.Volume, inherited);
                if (column == Column.Loop)
                {
                    EditorStyles.toggle.Draw(new Rect(rect.x + 4f, rect.center.y - 8f, 16f, 16f), false, false, row.Loop, false);
                    return;
                }
                Cell.text = Text(row, column);
                Draw(rect, Cell, overridden ? EditorStyles.boldLabel : EditorStyles.label);
            }
        }

        private static void VolumeBar(ref Rect rect, FloatRange volume, bool inherited)
        {
            var bar = new Rect(rect.x + 4f, rect.center.y - 3f, VolumeBarWidth, 6f);
            var min = Mathf.Clamp01(Mathf.Min(volume.Min, volume.Max));
            var max = Mathf.Clamp01(Mathf.Max(volume.Min, volume.Max));
            EditorGUI.DrawRect(bar, BarBack);
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * min, bar.height), inherited ? InheritedFill : BarFill);
            EditorGUI.DrawRect(new Rect(bar.x + bar.width * min, bar.y, bar.width * (max - min), bar.height), inherited ? InheritedRange : BarRange);
            rect.xMin = bar.xMax + 4f;
        }

        // Draws a label vertically centered in the row and returns its width.
        private static float Draw(Rect rect, GUIContent content, GUIStyle style)
        {
            var width = style.CalcSize(content).x;
            style.Draw(new Rect(rect.x, rect.center.y - 9f, Mathf.Min(width, rect.width), 18f), content, false, false, false, false);
            return width;
        }

        // Draws a 16px icon at the left of the rect, faded while GUI is disabled, and returns its width.
        private static float Icon(Rect rect, Texture image)
        {
            var color = GUI.color;
            if (!GUI.enabled) GUI.color = new Color(color.r, color.g, color.b, color.a * 0.5f);
            GUI.DrawTexture(new Rect(rect.x, rect.center.y - 8f, Mathf.Min(16f, rect.width), 16f), image, ScaleMode.ScaleToFit);
            GUI.color = color;
            return 18f;
        }

        private static class Styles
        {
            public static readonly GUIStyle Dim = new(EditorStyles.label) { normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };
            public static readonly GUIStyle Warn = new(EditorStyles.label) { normal = { textColor = WarningColor } };
            public static readonly GUIStyle KeyPrefix = new(Dim) { padding = new RectOffset(0, 0, 1, 2) };
            public static readonly GUIStyle KeyName = new(EditorStyles.boldLabel) { padding = new RectOffset(0, 0, 1, 2) };
            public static readonly GUIContent Play = EditorGUIUtility.IconContent("PlayButton");
            public static readonly GUIContent Linked = EditorGUIUtility.IconContent("Linked");
            public static readonly GUIContent Warning = EditorGUIUtility.IconContent("console.warnicon.sml");
        }

        // A column header with a profile group band above it; each click on a column cycles descending, ascending, unsorted.
        private sealed class Header : MultiColumnHeader
        {
            private const float BandHeight = 18f;

            public Header(MultiColumnHeaderState state) : base(state) => height += BandHeight;

            public override void OnGUI(Rect rect, float xScroll)
            {
                var band = new Rect(rect.x, rect.y, rect.width, BandHeight);
                base.OnGUI(new Rect(rect.x, band.yMax, rect.width, rect.height - BandHeight), xScroll);
                GUI.Label(band, GUIContent.none, DefaultStyles.background);
                GUI.BeginClip(band, new Vector2(-xScroll, 0f), Vector2.zero, false);
                foreach (var (first, last, content) in Bands)
                {
                    var span = Rect.MinMaxRect(GetColumnRect((int)first).xMin, 0f, GetColumnRect((int)last).xMax, BandHeight);
                    GUI.Label(span, content, DefaultStyles.columnHeaderCenterAligned);
                    if (Event.current.type == EventType.Repaint)
                        EditorGUI.DrawRect(new Rect(span.xMax - 1f, 3f, 1f, BandHeight - 6f), new Color(0f, 0f, 0f, 0.3f));
                }
                GUI.EndClip();
            }

            protected override void ColumnHeaderClicked(MultiColumnHeaderState.Column column, int columnIndex)
            {
                if (sortedColumnIndex != columnIndex)
                {
                    column.sortedAscending = false;
                    state.sortedColumnIndex = columnIndex;
                }
                else if (!column.sortedAscending) column.sortedAscending = true;
                else state.sortedColumnIndex = -1;
                OnSortingChanged();
            }
        }
    }
}
