#nullable enable
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal enum GameplayTagTreeAction
    {
        Rename,
        EditComment,
        AddSubTag,
        Copy,
        Delete
    }

    internal sealed class GameplayTagTreeView : TreeView
    {
        private readonly TreeViewState _state;
        private IReadOnlyList<GameplayTagTreeRowModel> _rows = Array.Empty<GameplayTagTreeRowModel>();
        private readonly Dictionary<int, GameplayTagTreeRowModel> _rowsById =
            new Dictionary<int, GameplayTagTreeRowModel>();
        private List<int>? _expandedBeforeFilter;
        private bool _isFiltering;

        internal event Action<string>? PathSelected;
        internal event Action<string>? RenameRequested;
        internal event Action<string>? CommentEditRequested;
        internal event Action<string>? SubTagRequested;
        internal event Action<string>? CopyRequested;
        internal event Action<string>? DeleteRequested;

        internal GameplayTagTreeView(TreeViewState state)
            : base(state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            showBorder = true;
            useScrollView = true;
            Reload();
        }

        internal void SetRows(IReadOnlyList<GameplayTagTreeRowModel> rows) => SetRows(rows, false);

        internal void SetRows(IReadOnlyList<GameplayTagTreeRowModel> rows, bool isFiltering)
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
                SetExpanded(_rows[index].Index, true);
            }
        }

        internal void RequestAction(GameplayTagTreeAction action, int id)
        {
            if (!TryGetPath(id, out var path)) throw new ArgumentOutOfRangeException(nameof(id));
            switch (action)
            {
                case GameplayTagTreeAction.Rename: RenameRequested?.Invoke(path); break;
                case GameplayTagTreeAction.EditComment: CommentEditRequested?.Invoke(path); break;
                case GameplayTagTreeAction.AddSubTag: SubTagRequested?.Invoke(path); break;
                case GameplayTagTreeAction.Copy: CopyRequested?.Invoke(path); break;
                case GameplayTagTreeAction.Delete: DeleteRequested?.Invoke(path); break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        internal bool TryGetPath(int id, out string path)
        {
            if (_rowsById.TryGetValue(id, out var row))
            {
                path = row.Path;
                return true;
            }

            path = string.Empty;
            return false;
        }

        internal void SynchronizeSelection(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));

            if (path.Length > 0)
            {
                foreach (var pair in _rowsById)
                {
                    if (!string.Equals(pair.Value.Path, path, StringComparison.OrdinalIgnoreCase)) continue;

                    // 선택 행이 접힌 조상 아래 숨지 않도록 조상만 펼치고 다른 확장 상태는 둔다.
                    for (int parent = pair.Value.ParentIndex;
                        _rowsById.TryGetValue(parent, out var ancestor);
                        parent = ancestor.ParentIndex)
                    {
                        SetExpanded(ancestor.Index, true);
                    }

                    SetSelection(new[] { pair.Key }, TreeViewSelectionOptions.RevealAndFrame);
                    return;
                }
            }

            SetSelection(Array.Empty<int>(), TreeViewSelectionOptions.None);
        }

        internal static GUIContent CreateLabelContent(GameplayTagTreeRowModel row)
        {
            var segmentOffset = row.Path.LastIndexOf('.') + 1;
            return new GUIContent(row.Path.Substring(segmentOffset), row.Comment);
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
                children = new List<TreeViewItem>(),
            };
            var items = new Dictionary<int, TreeViewItem>();
            for (var index = 0; index < _rows.Count; index++)
            {
                var row = _rows[index];
                var item = new TreeViewItem(row.Index, 0, CreateLabelContent(row).text);
                _rowsById.Add(item.id, row);
                if (!items.TryGetValue(row.ParentIndex, out var parent))
                {
                    parent = root;
                }

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
                GUI.Label(CalculateLabelRect(args.item, args.rowRect), CreateLabelContent(row));
                return;
            }

            base.RowGUI(args);
        }

        protected override void ContextClickedItem(int id)
        {
            if (!TryGetPath(id, out _)) return;

            var menu = new GenericMenu();
            AddActionItem(menu, "Rename", GameplayTagTreeAction.Rename, id);
            AddActionItem(menu, "Edit Comment", GameplayTagTreeAction.EditComment, id);
            AddActionItem(menu, "Add Sub-Tag", GameplayTagTreeAction.AddSubTag, id);
            AddActionItem(menu, "Copy Tag", GameplayTagTreeAction.Copy, id);
            AddActionItem(menu, "Delete Tag", GameplayTagTreeAction.Delete, id);
            menu.ShowAsContext();
            Event.current.Use();
        }

        private void AddActionItem(GenericMenu menu, string label, GameplayTagTreeAction action, int id)
        {
            menu.AddItem(new GUIContent(label), false, () => RequestAction(action, id));
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            base.SelectionChanged(selectedIds);
            if (selectedIds.Count == 1 && TryGetPath(selectedIds[0], out var path))
            {
                PathSelected?.Invoke(path);
            }
        }
    }
}
#pragma warning restore CS0618
