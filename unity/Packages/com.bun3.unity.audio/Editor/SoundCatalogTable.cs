// Table state, row model, finding (search, chips, sorting, grouping), selection and layout.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Editor
{
    /// <summary>
    /// The table of one <see cref="SoundCatalog"/>, drawn by its inspector and by <see cref="SoundCatalogWindow"/>.
    /// Rows are captured by <see cref="Refresh"/>, which runs again on the next layout after undo, redo, a reimport, a change
    /// to the catalog's entries, or an unsaved edit to the catalog, its definitions or their profiles.
    /// Search, chip, sort and grouping persist per catalog for the editor session.
    /// Partial layout:
    /// <list type="bullet">
    /// <item><description>SoundCatalogTable.cs: row model, refresh, finding, selection and layout.</description></item>
    /// <item><description>SoundCatalogTable.Columns.cs: columns, tooltips, header and cell drawing.</description></item>
    /// </list>
    /// </summary>
    internal sealed partial class SoundCatalogTable : IDisposable
    {
        private const string AllChip = "all";
        private const string VolumeGroupChip = "group:";

        private static readonly (string Id, string Label, Func<Row, bool> Test)[] FixedChips =
        {
            (AllChip, "All", _ => true),
            ("missing", "Missing clips", r => r.MissingClips),
            ("clips", "Has clips", r => r.Def != null && !r.MissingClips),
            ("profile", "Uses profile", r => r.UsesProfile),
            ("overrides", "Overrides", r => r.HasOverrides),
            ("spatial", "Spatial", r => r.Def != null && r.Spatial != SpatialMode.None),
        };

        private static readonly SoundCatalogIssue[] Issues = (SoundCatalogIssue[])Enum.GetValues(typeof(SoundCatalogIssue));

        private readonly SoundCatalog _catalog;
        private readonly string _sessionKey;
        private readonly List<Row> _rows = new();
        private readonly Dictionary<SoundDef, SerializedObject> _serialized = new();
        private readonly HashSet<Object> _watched = new();
        private readonly List<Chip> _chips = new();
        private readonly Dictionary<string, int> _groupIds = new(StringComparer.Ordinal);
        private readonly HashSet<int> _shown = new();
        private readonly SearchField _searchField = new();
        private readonly TreeViewState<int> _state = new();
        private readonly View _view;
        private string _search;
        private string[] _words;
        private string _chip;
        private bool _grouped;
        private bool _dirty;
        private int _dirtyCount;
        private SoundCatalogIssue[] _issues;
        private GUIContent _problems;

        /// <summary>Creates the table of <paramref name="catalog"/> and captures its rows.</summary>
        public SoundCatalogTable(SoundCatalog catalog)
        {
            _catalog = catalog;
            _sessionKey = $"Bun3.Audio.SoundCatalogTable.{AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog))}.";
            _search = SessionState.GetString(_sessionKey + "Search", string.Empty);
            _words = Words(_search);
            _chip = SessionState.GetString(_sessionKey + "Chip", AllChip);
            _grouped = SessionState.GetBool(_sessionKey + "Grouped", false);

            var header = new Header(CreateHeaderState());
            var sorted = SessionState.GetInt(_sessionKey + "Sort", -1);
            if (sorted >= 0 && sorted < Columns.Length && header.state.columns[sorted].canSort)
            {
                header.state.columns[sorted].sortedAscending = SessionState.GetBool(_sessionKey + "Ascending", false);
                header.state.sortedColumnIndex = sorted;
            }
            header.sortingChanged += OnSortingChanged;
            header.ResizeToFit();
            _view = new View(this, _state, header);

            Undo.undoRedoPerformed += MarkDirty;
            EditorApplication.projectChanged += MarkDirty;
            Refresh();
        }

        /// <summary>Every catalog entry in catalog order, as captured by the last <see cref="Refresh"/>.</summary>
        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>The selected rows in catalog order. Rows hidden by the search or a chip are never selected.</summary>
        public List<Row> SelectedRows()
        {
            var selected = new List<Row>();
            foreach (var row in _rows)
                if (_view.IsSelected(row.id)) selected.Add(row);
            return selected;
        }

        /// <summary>
        /// Whether the captured rows may be out of date. A change saved in the same frame it was made is seen only when it
        /// changes the catalog's entries, so code that edits definitions or profiles and saves them calls <see cref="Refresh"/>.
        /// </summary>
        public bool IsStale => _dirty || EntriesChanged() || DirtyCount() != _dirtyCount;

        /// <summary>Captures every entry again and redraws. Editing code calls this after changing the catalog.</summary>
        public void Refresh()
        {
            // Issues depend only on keys, definitions and their files, so value edits skip the costly validation.
            if (_dirty || _issues == null || EntriesChanged()) _issues = SoundCatalogEditing.Validate(_catalog);
            _dirty = false;
            _rows.Clear();
            _watched.Clear();
            _watched.Add(_catalog);
            var entries = _catalog.Entries;
            var issues = _issues;
            var issueCounts = new int[Issues.Length];
            var live = new HashSet<SoundDef>();
            for (int i = 0; i < entries.Count; i++)
            {
                var row = new Row(i, entries[i].Key, entries[i].Definition, issues[i]);
                issueCounts[(int)row.Issue]++;
                if (row.Def != null)
                {
                    live.Add(row.Def);
                    _watched.Add(row.Def);
                    Capture(row, SerializedOf(row.Def));
                }
                _rows.Add(row);
            }

            var stale = new List<SoundDef>();
            foreach (var def in _serialized.Keys)
                if (!live.Contains(def)) stale.Add(def);
            foreach (var def in stale)
            {
                _serialized[def].Dispose();
                _serialized.Remove(def);
            }

            BuildChips();
            _problems = Problems(issueCounts);
            _dirtyCount = DirtyCount();
            Reload();
        }

        /// <summary>Draws the toolbar, chips, problem banner, table and status bar inside <paramref name="rect"/>.</summary>
        public void OnGUI(Rect rect)
        {
            if (_catalog == null) return;
            if (Event.current.type == EventType.Layout && IsStale) Refresh();

            var toolbar = new Rect(rect.x, rect.y, rect.width, EditorStyles.toolbar.fixedHeight);
            GUI.Box(toolbar, GUIContent.none, EditorStyles.toolbar);
            var line = EditorGUIUtility.singleLineHeight;
            var search = new Rect(toolbar.x + 4f, toolbar.y + 2f, Mathf.Clamp(toolbar.width - 150f, 80f, 360f), line);
            var text = _searchField.OnToolbarGUI(search, _search);
            if (text != _search) SetSearch(text);
            var grouped = EditorGUI.ToggleLeft(new Rect(search.xMax + 10f, toolbar.y + 1f, 130f, line), "Group by prefix", _grouped);
            if (grouped != _grouped) SetGrouped(grouped);
            rect.yMin = ChipsGUI(new Rect(rect.x, toolbar.yMax, rect.width, rect.height));

            if (_problems != null)
            {
                var height = Mathf.Max(2f * line, EditorStyles.helpBox.CalcHeight(_problems, rect.width - 48f));
                EditorGUI.HelpBox(new Rect(rect.x + 4f, rect.y, rect.width - 8f, height), _problems.text, MessageType.Error);
                rect.yMin += height + 4f;
            }

            var status = new Rect(rect.x, rect.yMax - EditorStyles.toolbar.fixedHeight, rect.width, EditorStyles.toolbar.fixedHeight);
            rect.yMax = status.yMin;
            DetailGUI(ref rect);
            _view.OnGUI(rect);
            StatusGUI(status);
        }

        /// <summary>Stops listening for changes and releases the cached serialized definitions.</summary>
        public void Dispose()
        {
            Undo.undoRedoPerformed -= MarkDirty;
            EditorApplication.projectChanged -= MarkDirty;
            foreach (var serialized in _serialized.Values) serialized.Dispose();
            _serialized.Clear();
        }

        // Hooks for the editing parts: input on a cell before it draws, and panels taken out of the table area.
        partial void CellInput(Rect rect, Row row, Column column);
        partial void DetailGUI(ref Rect area);

        private void Capture(Row row, SerializedObject serialized)
        {
            var def = row.Def;
            serialized.Update();
            row.ClipCount = def.Clips?.Length ?? 0;
#if BUN3_ADDRESSABLES
            row.Addressable = row.ClipCount == 0 && def.AddressableClips is { Length: > 0 };
#endif
            row.MissingClips = row.ClipCount == 0 && !row.Addressable;
            row.Volume = def.EffectiveVolume;
            row.Pitch = def.EffectivePitch;
            row.Loop = def.EffectiveLoop;
            row.VolumeGroup = def.EffectiveVolumeGroup ?? string.Empty;
            row.MaxInstances = def.EffectiveMaxInstances;
            row.Cooldown = def.EffectiveCooldown;
            row.Priority = def.EffectivePriority;
            row.Spatial = def.EffectiveSpatial;
            row.Profiles[(int)SoundProfileGroup.Playback] = def.PlaybackProfile;
            row.Profiles[(int)SoundProfileGroup.Routing] = def.RoutingProfile;
            row.Profiles[(int)SoundProfileGroup.Concurrency] = def.ConcurrencyProfile;
            row.Profiles[(int)SoundProfileGroup.Spatial] = def.SpatialProfile;
            for (int group = 0; group < row.Profiles.Length; group++)
            {
                if (row.Profiles[group] == null) continue;
                _watched.Add(row.Profiles[group]);
                foreach (var field in SoundDefOverrides.FieldsOf((SoundProfileGroup)group))
                {
                    if (SoundDefOverrides.IsInherited(serialized, field)) row.Inherited |= field;
                    else row.Overridden |= field;
                }
            }

            // The def's own acoustic selection only matters while its Acoustics field is not inherited.
            var acousticProfile = (row.Inherited & SoundDefField.Acoustics) == 0 ? def.Acoustics.Profile : null;
            var acousticOverrides = false;
            if (acousticProfile != null)
            {
                _watched.Add(acousticProfile);
                var selection = serialized.FindProperty(SoundDefOverrides.PropertyPath(SoundDefField.Acoustics));
                foreach (var field in SoundDefOverrides.AcousticFields)
                    acousticOverrides |= !SoundDefOverrides.IsAcousticInherited(selection, field);
            }
            row.UsesProfile = row.Inherited != SoundDefField.None || row.Overridden != SoundDefField.None || acousticProfile != null;
            row.HasOverrides = row.Overridden != SoundDefField.None || acousticOverrides;
        }

        private SerializedObject SerializedOf(SoundDef def)
        {
            if (_serialized.TryGetValue(def, out var serialized) && serialized.targetObject != null) return serialized;
            serialized?.Dispose();
            return _serialized[def] = new SerializedObject(def);
        }

        private void BuildChips()
        {
            _chips.Clear();
            foreach (var (id, label, test) in FixedChips) AddChip(id, label, test);
            var groups = new List<string>();
            foreach (var row in _rows)
                if (row.Def != null && !groups.Contains(row.VolumeGroup)) groups.Add(row.VolumeGroup);
            foreach (var group in groups)
                AddChip(VolumeGroupChip + group, group.Length == 0 ? "(no group)" : group, r => r.Def != null && r.VolumeGroup == group);
        }

        private void AddChip(string id, string label, Func<Row, bool> test)
        {
            var count = 0;
            foreach (var row in _rows)
                if (test(row)) count++;
            _chips.Add(new Chip(id, new GUIContent($"{label}  {count}"), test, count));
        }

        private Chip ChipOf(string id)
        {
            foreach (var chip in _chips)
                if (chip.Id == id) return chip;
            return _chips[0];
        }

        private static GUIContent Problems(int[] issueCounts)
        {
            var total = 0;
            foreach (var issue in Issues)
                if (issue != SoundCatalogIssue.None) total += issueCounts[(int)issue];
            if (total == 0) return null;
            var text = $"{Plural(total, "row has", "rows have")} problems and {(total == 1 ? "is" : "are")} marked red:";
            foreach (var issue in Issues)
                if (issue != SoundCatalogIssue.None && issueCounts[(int)issue] > 0)
                    text += $"\n• {Plural(issueCounts[(int)issue], "row", "rows")} {Summary(issue)}";
            return new GUIContent(text);
        }

        private static string Summary(SoundCatalogIssue issue) => issue switch
        {
            SoundCatalogIssue.BlankKey => "with a blank key.",
            SoundCatalogIssue.DuplicateKey => "with a key another row also uses.",
            SoundCatalogIssue.MissingDefinition => "with no definition.",
            SoundCatalogIssue.ExternalDefinition =>
                "using a definition asset outside this catalog file. They still play, but each sound's definition belongs inside the catalog.",
            _ => "sharing a definition with another row.",
        };

        private static string Plural(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

        private static string[] Words(string search) =>
            search.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        private bool IsShown(Row row, Chip chip)
        {
            if (!chip.Test(row)) return false;
            foreach (var word in _words)
                if (row.Key.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private TreeViewItem<int> BuildRoot()
        {
            var root = new TreeViewItem<int>(0, -1) { children = new List<TreeViewItem<int>>() };
            var chip = ChipOf(_chip);
            var shown = new List<Row>();
            foreach (var row in _rows)
                if (IsShown(row, chip)) shown.Add(row);

            var header = _view.multiColumnHeader;
            var sorted = header.sortedColumnIndex;
            if (sorted >= 0)
            {
                var sign = header.IsSortedAscending(sorted) ? 1 : -1;
                shown.Sort((a, b) =>
                {
                    var order = sign * Compare(a, b, (Column)sorted);
                    return order != 0 ? order : a.Index.CompareTo(b.Index);
                });
            }

            _shown.Clear();
            var groups = _grouped ? new Dictionary<string, GroupItem>(StringComparer.Ordinal) : null;
            foreach (var row in shown)
            {
                _shown.Add(row.id);
                if (groups == null)
                {
                    row.depth = 0;
                    root.AddChild(row);
                    continue;
                }
                if (!groups.TryGetValue(row.Group, out var group))
                {
                    groups.Add(row.Group, group = new GroupItem(GroupId(row.Group), row.Group));
                    root.AddChild(group);
                }
                row.depth = 1;
                group.AddChild(row);
                group.Count++;
                if (row.MissingClips) group.Missing++;
            }
            return root;
        }

        // Ids stay stable for the table's lifetime so collapsed groups stay collapsed; new groups start expanded.
        private int GroupId(string prefix)
        {
            if (_groupIds.TryGetValue(prefix, out var id)) return id;
            _groupIds.Add(prefix, id = -(_groupIds.Count + 1));
            var expanded = _state.expandedIDs;
            var at = expanded.BinarySearch(id);
            if (at < 0) expanded.Insert(~at, id);
            return id;
        }

        private void Reload()
        {
            _view.Reload();
            var selection = _view.GetSelection();
            var kept = new List<int>(selection.Count);
            foreach (var id in selection)
                if (_shown.Contains(id)) kept.Add(id);
            if (kept.Count != selection.Count) _view.SetSelection(kept);
        }

        private void SetSearch(string search)
        {
            _search = search ?? string.Empty;
            _words = Words(_search);
            SessionState.SetString(_sessionKey + "Search", _search);
            Reload();
        }

        private void SetChip(string id)
        {
            _chip = id;
            SessionState.SetString(_sessionKey + "Chip", id);
            Reload();
        }

        private void SetGrouped(bool grouped)
        {
            _grouped = grouped;
            SessionState.SetBool(_sessionKey + "Grouped", grouped);
            Reload();
        }

        private void OnSortingChanged(MultiColumnHeader header)
        {
            var sorted = header.sortedColumnIndex;
            SessionState.SetInt(_sessionKey + "Sort", sorted);
            if (sorted >= 0) SessionState.SetBool(_sessionKey + "Ascending", header.IsSortedAscending(sorted));
            Reload();
        }

        private void ToggleSelected(int id)
        {
            var selection = new List<int>(_view.GetSelection());
            if (!selection.Remove(id)) selection.Add(id);
            _view.SetSelection(selection, TreeViewSelectionOptions.FireSelectionChanged);
        }

        private float ChipsGUI(Rect rect)
        {
            var height = EditorGUIUtility.singleLineHeight;
            var current = ChipOf(_chip);
            float x = rect.x + 4f, y = rect.y + 4f;
            foreach (var chip in _chips)
            {
                var width = EditorStyles.miniButton.CalcSize(chip.Content).x + 6f;
                if (x + width > rect.xMax - 4f && x > rect.x + 4f)
                {
                    x = rect.x + 4f;
                    y += height + 3f;
                }
                var on = chip == current;
                if (GUI.Toggle(new Rect(x, y, width, height), on, chip.Content, EditorStyles.miniButton) != on)
                    SetChip(on ? AllChip : chip.Id);
                x += width + 3f;
            }
            return y + height + 4f;
        }

        private void StatusGUI(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.Box(rect, GUIContent.none, EditorStyles.toolbar);
            var selected = 0;
            foreach (var id in _view.GetSelection())
                if (id > 0) selected++;
            GUI.Label(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height),
                $"{_rows.Count} sounds   {_shown.Count} shown   {ChipOf("missing").Count} without clips   " +
                $"{ChipOf("profile").Count} use profiles   {ChipOf("overrides").Count} with overrides   {selected} selected",
                EditorStyles.miniLabel);
        }

        private void MarkDirty()
        {
            _dirty = true;
            _view.Repaint();
        }

        private bool EntriesChanged()
        {
            var entries = _catalog.Entries;
            if (entries.Count != _rows.Count) return true;
            for (int i = 0; i < entries.Count; i++)
                if ((entries[i].Key ?? string.Empty) != _rows[i].Key || !ReferenceEquals(entries[i].Definition, _rows[i].Def)) return true;
            return false;
        }

        // Unsaved edits raise an object's dirty count; saving resets it.
        private int DirtyCount()
        {
            var count = 0;
            foreach (var target in _watched)
                if (target != null) count += EditorUtility.GetDirtyCount(target);
            return count;
        }

        private static string PrefixOf(string key)
        {
            var first = key.IndexOf('.');
            if (first < 0) return key;
            var second = key.IndexOf('.', first + 1);
            return key.Substring(0, second < 0 ? first : second);
        }

        /// <summary>One catalog entry as captured by the last refresh. Its tree id is its entry index plus one.</summary>
        internal sealed class Row : TreeViewItem<int>
        {
            /// <summary>Creates the row of entry <paramref name="index"/>.</summary>
            public Row(int index, string key, SoundDef def, SoundCatalogIssue issue) : base(index + 1, 0, key ?? string.Empty)
            {
                Index = index;
                Key = key ?? string.Empty;
                Def = def;
                Issue = issue;
                Group = PrefixOf(Key);
                var dot = Key.LastIndexOf('.');
                KeyPrefix = new GUIContent(Key.Substring(0, dot + 1));
                KeyName = new GUIContent(Key.Substring(dot + 1));
            }

            /// <summary>The entry index in the catalog.</summary>
            public readonly int Index;
            /// <summary>The entry key; empty when blank.</summary>
            public readonly string Key;
            /// <summary>The entry definition, or null when missing.</summary>
            public readonly SoundDef Def;
            /// <summary>The first problem of the entry.</summary>
            public readonly SoundCatalogIssue Issue;
            /// <summary>The prefix the row is grouped under.</summary>
            public readonly string Group;
            /// <summary>The key up to and including its last dot, and the rest.</summary>
            public readonly GUIContent KeyPrefix, KeyName;
            /// <summary>Profiles indexed by <see cref="SoundProfileGroup"/>; null where none is assigned.</summary>
            public readonly ScriptableObject[] Profiles = new ScriptableObject[4];
            /// <summary>Fields that use their profile's value, and fields that override an assigned profile.</summary>
            public SoundDefField Inherited, Overridden;
            /// <summary>Whether any group profile or the def's own acoustic profile is assigned.</summary>
            public bool UsesProfile;
            /// <summary>Whether any field or acoustic setting overrides an assigned profile.</summary>
            public bool HasOverrides;
            /// <summary>The number of direct clips.</summary>
            public int ClipCount;
            /// <summary>No direct clips, but Addressable clips.</summary>
            public bool Addressable;
            /// <summary>Nothing to play: no direct and no Addressable clips.</summary>
            public bool MissingClips;
            /// <summary>The effective Volume and Pitch.</summary>
            public FloatRange Volume, Pitch;
            /// <summary>The effective Loop.</summary>
            public bool Loop;
            /// <summary>The effective VolumeGroup; empty when null.</summary>
            public string VolumeGroup;
            /// <summary>The effective MaxInstances and Priority.</summary>
            public int MaxInstances, Priority;
            /// <summary>The effective Cooldown.</summary>
            public float Cooldown;
            /// <summary>The effective Spatial mode.</summary>
            public SpatialMode Spatial;
        }

        private sealed class GroupItem : TreeViewItem<int>
        {
            public GroupItem(int id, string prefix) : base(id, 0, prefix) { }
            public int Count;
            public int Missing;
        }

        private sealed class Chip
        {
            public Chip(string id, GUIContent content, Func<Row, bool> test, int count)
            {
                Id = id;
                Content = content;
                Test = test;
                Count = count;
            }

            public readonly string Id;
            public readonly GUIContent Content;
            public readonly Func<Row, bool> Test;
            public readonly int Count;
        }

        private sealed class View : TreeView<int>
        {
            private readonly SoundCatalogTable _table;

            public View(SoundCatalogTable table, TreeViewState<int> state, MultiColumnHeader header) : base(state, header)
            {
                _table = table;
                rowHeight = 20f;
                showAlternatingRowBackgrounds = true;
                showBorder = true;
                columnIndexForTreeFoldouts = (int)Column.Key;
            }

            public float Indent(TreeViewItem<int> item) => GetContentIndent(item);

            public Rect FoldoutCell(Rect rowRect) => GetCellRectForTreeFoldouts(rowRect);

            protected override TreeViewItem<int> BuildRoot() => _table.BuildRoot();

            protected override void RowGUI(RowGUIArgs args)
            {
                if (args.item is GroupItem group)
                {
                    _table.GroupGUI(args.rowRect, group);
                    return;
                }
                var row = (Row)args.item;
                RowBackground(args.rowRect, row);
                for (int i = 0; i < args.GetNumVisibleColumns(); i++)
                {
                    var rect = args.GetCellRect(i);
                    var column = (Column)args.GetColumn(i);
                    _table.CellInput(rect, row, column);
                    _table.CellGUI(rect, row, column);
                }
            }
        }
    }
}
