#nullable enable
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal interface IGameplayTagProjectionRow
    {
        int Id { get; }
        int ParentId { get; }
        string DisplaySegment { get; }
    }

    /// <summary>Source와 merged projection이 공유하는 트리 계층, scroll 및 행 geometry입니다.</summary>
    internal abstract class GameplayTagProjectionTreeView<TRow> : TreeView
        where TRow : struct, IGameplayTagProjectionRow
    {
        private readonly TreeViewState _state;
        private IReadOnlyList<TRow> _rows = Array.Empty<TRow>();
        private readonly Dictionary<int, TRow> _rowsById = new Dictionary<int, TRow>();
        private List<int>? _expandedBeforeFilter;
        private bool _isFiltering;
        private bool _suppressSelectionChanged;

        protected GameplayTagProjectionTreeView(TreeViewState state)
            : base(state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            showBorder = true;
            useScrollView = true;
            Reload();
        }

        internal bool UsesScrollView => useScrollView;

        protected void SetProjectionRows(IReadOnlyList<TRow> rows, bool isFiltering)
        {
            if (rows is null) throw new ArgumentNullException(nameof(rows));
            if (isFiltering && !_isFiltering)
            {
                _expandedBeforeFilter = new List<int>(_state.expandedIDs);
            }

            if (!isFiltering && _isFiltering && _expandedBeforeFilter is not null)
            {
                _state.expandedIDs = new List<int>(_expandedBeforeFilter);
            }

            _rows = rows;
            _isFiltering = isFiltering;
            Reload();
            if (!isFiltering) return;
            for (var index = 0; index < _rows.Count; index++)
            {
                SetExpanded(_rows[index].Id, true);
            }
        }

        protected bool TryGetRow(int id, out TRow row) => _rowsById.TryGetValue(id, out row);

        protected void SynchronizeSelection(Predicate<TRow> matches)
        {
            if (matches is null) throw new ArgumentNullException(nameof(matches));
            _suppressSelectionChanged = true;
            try
            {
                foreach (var pair in _rowsById)
                {
                    if (!matches(pair.Value)) continue;
                    ExpandAncestors(pair.Value);
                    SetSelection(new[] { pair.Key }, TreeViewSelectionOptions.RevealAndFrame);
                    return;
                }

                SetSelection(Array.Empty<int>(), TreeViewSelectionOptions.None);
            }
            finally
            {
                _suppressSelectionChanged = false;
            }
        }

        /// <summary>foldout과 계층 들여쓰기를 제외한 트리 행 레이블 영역을 계산합니다.</summary>
        internal Rect CalculateLabelRect(TreeViewItem item, Rect rowRect)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            rowRect.xMin += GetContentIndent(item);
            return rowRect;
        }

        protected override TreeViewItem BuildRoot()
        {
            _rowsById.Clear();
            var root = new TreeViewItem
            {
                id = 0,
                depth = -1,
                displayName = "Root",
                children = new List<TreeViewItem>()
            };
            var items = new Dictionary<int, TreeViewItem>();
            for (var index = 0; index < _rows.Count; index++)
            {
                var row = _rows[index];
                var item = new TreeViewItem(row.Id, 0, row.DisplaySegment);
                _rowsById.Add(item.id, row);
                if (!items.TryGetValue(row.ParentId, out var parent)) parent = root;
                if (parent.children is null) parent.children = new List<TreeViewItem>();
                parent.children.Add(item);
                items.Add(item.id, item);
            }

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            if (_rowsById.TryGetValue(args.item.id, out var row))
            {
                GUI.Label(CalculateLabelRect(args.item, args.rowRect), CreateRowContent(row));
                return;
            }

            base.RowGUI(args);
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            base.SelectionChanged(selectedIds);
            if (!_suppressSelectionChanged
                && selectedIds.Count == 1
                && _rowsById.TryGetValue(selectedIds[0], out var row))
            {
                RowSelected(row);
            }
        }

        protected abstract GUIContent CreateRowContent(TRow row);

        protected abstract void RowSelected(TRow row);

        private void ExpandAncestors(TRow row)
        {
            for (var parent = row.ParentId;
                _rowsById.TryGetValue(parent, out var ancestor);
                parent = ancestor.ParentId)
            {
                SetExpanded(ancestor.Id, true);
            }
        }
    }

    [Flags]
    internal enum GameplayTagTreeAction
    {
        None = 0,
        Rename = 1 << 0,
        EditComment = 1 << 1,
        AddSubTag = 1 << 2,
        Copy = 1 << 3,
        FindReferences = 1 << 4,
        Delete = 1 << 5
    }

    internal sealed class GameplayTagTreeView : GameplayTagProjectionTreeView<GameplayTagTreeRowModel>
    {
        internal event Action<GameplayTagTreeSelectionKey>? TagSelected;
        internal event Action<GameplayTagTreeSelectionKey>? RenameRequested;
        internal event Action<GameplayTagTreeSelectionKey>? CommentEditRequested;
        internal event Action<GameplayTagTreeSelectionKey>? SubTagRequested;
        internal event Action<GameplayTagTreeSelectionKey>? CopyRequested;
        internal event Action<GameplayTagTreeSelectionKey>? FindReferencesRequested;
        internal event Action<GameplayTagTreeSelectionKey>? DeleteRequested;

        internal bool CanEditGameSource { get; set; } = true;

        internal GameplayTagTreeView(TreeViewState state)
            : base(state)
        {
        }

        internal void SetRows(IReadOnlyList<GameplayTagTreeRowModel> rows) => SetRows(rows, false);

        internal void SetRows(IReadOnlyList<GameplayTagTreeRowModel> rows, bool isFiltering) =>
            SetProjectionRows(rows, isFiltering);

        internal void RequestAction(GameplayTagTreeAction action, int id)
        {
            if (!TryGetRow(id, out var row)) throw new ArgumentOutOfRangeException(nameof(id));
            if ((GetAvailableActions(row, CanEditGameSource) & action) != action
                || !IsSingleAction(action))
            {
                throw new InvalidOperationException("The selected Source row does not permit this action.");
            }

            var key = row.SelectionKey;
            switch (action)
            {
                case GameplayTagTreeAction.Rename: RenameRequested?.Invoke(key); break;
                case GameplayTagTreeAction.EditComment: CommentEditRequested?.Invoke(key); break;
                case GameplayTagTreeAction.AddSubTag: SubTagRequested?.Invoke(key); break;
                case GameplayTagTreeAction.Copy: CopyRequested?.Invoke(key); break;
                case GameplayTagTreeAction.FindReferences: FindReferencesRequested?.Invoke(key); break;
                case GameplayTagTreeAction.Delete: DeleteRequested?.Invoke(key); break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        internal static GameplayTagTreeAction GetAvailableActions(GameplayTagTreeRowModel row)
            => GetAvailableActions(row, canEditGameSource: true);

        internal static GameplayTagTreeAction GetAvailableActions(
            GameplayTagTreeRowModel row,
            bool canEditGameSource)
        {
            if (row.IsSourceRoot) return GameplayTagTreeAction.None;
            if (row.IsReadOnly || !canEditGameSource)
            {
                return GameplayTagTreeAction.Copy | GameplayTagTreeAction.FindReferences;
            }

            var actions = GameplayTagTreeAction.Rename
                | GameplayTagTreeAction.EditComment
                | GameplayTagTreeAction.AddSubTag
                | GameplayTagTreeAction.Copy
                | GameplayTagTreeAction.FindReferences;
            return row.IsExplicit ? actions | GameplayTagTreeAction.Delete : actions;
        }

        internal bool TryGetPath(int id, out string path)
        {
            if (TryGetRow(id, out var row) && !row.IsSourceRoot)
            {
                path = row.Path;
                return true;
            }

            path = string.Empty;
            return false;
        }

        internal void SynchronizeSelection(GameplayTagTreeSelectionKey key)
        {
            if (key.CanonicalPath.Length == 0)
            {
                SetSelection(Array.Empty<int>(), TreeViewSelectionOptions.None);
                return;
            }

            SynchronizeSelection(row => row.SelectionKey.Equals(key));
        }

        internal void SynchronizeSelection(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
            {
                SetSelection(Array.Empty<int>(), TreeViewSelectionOptions.None);
                return;
            }

            SynchronizeSelection(row => !row.IsSourceRoot
                && string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase));
        }

        internal static GUIContent CreateLabelContent(GameplayTagTreeRowModel row)
        {
            if (row.IsSourceRoot)
            {
                var label = row.DisplayName + (row.IsReadOnly ? "  [Read Only]" : string.Empty);
                var tooltip = "Source: " + row.SourceId
                    + (row.IsReadOnly ? "\nThis Source is read-only." : "\nThis Source is editable.");
                return new GUIContent(label, tooltip);
            }

            var lastDot = row.Path.LastIndexOf('.');
            return new GUIContent(row.Path.Substring(lastDot + 1), row.Comment);
        }

        protected override void ContextClickedItem(int id)
        {
            if (!TryGetRow(id, out var row)) return;
            var actions = GetAvailableActions(row, CanEditGameSource);
            if (actions == GameplayTagTreeAction.None) return;

            var menu = new GenericMenu();
            AddActionItem(menu, actions, "Rename", GameplayTagTreeAction.Rename, id);
            AddActionItem(menu, actions, "Edit Comment", GameplayTagTreeAction.EditComment, id);
            AddActionItem(menu, actions, "Add Sub-Tag", GameplayTagTreeAction.AddSubTag, id);
            AddActionItem(menu, actions, "Copy Tag", GameplayTagTreeAction.Copy, id);
            AddActionItem(menu, actions, "Find References", GameplayTagTreeAction.FindReferences, id);
            AddActionItem(menu, actions, "Delete Tag", GameplayTagTreeAction.Delete, id);
            menu.ShowAsContext();
            Event.current.Use();
        }

        protected override GUIContent CreateRowContent(GameplayTagTreeRowModel row) =>
            CreateLabelContent(row);

        protected override void RowSelected(GameplayTagTreeRowModel row)
        {
            if (!row.IsSourceRoot) TagSelected?.Invoke(row.SelectionKey);
        }

        private void AddActionItem(
            GenericMenu menu,
            GameplayTagTreeAction available,
            string label,
            GameplayTagTreeAction action,
            int id)
        {
            if ((available & action) == action)
            {
                menu.AddItem(new GUIContent(label), false, () => RequestAction(action, id));
            }
        }

        private static bool IsSingleAction(GameplayTagTreeAction action) =>
            action != GameplayTagTreeAction.None && (((int)action & ((int)action - 1)) == 0);
    }
}
#pragma warning restore CS0618
