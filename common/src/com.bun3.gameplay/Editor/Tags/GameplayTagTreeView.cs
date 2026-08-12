#nullable enable
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal sealed class GameplayTagTreeView : TreeView
    {
        private IReadOnlyList<GameplayTagTreeRowModel> _rows = Array.Empty<GameplayTagTreeRowModel>();
        private readonly Dictionary<int, GameplayTagTreeRowModel> _rowsById =
            new Dictionary<int, GameplayTagTreeRowModel>();

        internal event Action<string>? PathSelected;

        internal GameplayTagTreeView(TreeViewState state)
            : base(state)
        {
            showBorder = true;
            Reload();
        }

        internal void SetRows(IReadOnlyList<GameplayTagTreeRowModel> rows)
        {
            _rows = rows ?? throw new ArgumentNullException(nameof(rows));
            Reload();
            for (var index = 0; index < _rows.Count; index++)
            {
                SetExpanded(_rows[index].Index, true);
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

        internal static GUIContent CreateLabelContent(GameplayTagTreeRowModel row)
        {
            var segmentOffset = row.Path.LastIndexOf('.') + 1;
            return new GUIContent(row.Path.Substring(segmentOffset), row.Comment);
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
                GUI.Label(args.rowRect, CreateLabelContent(row));
                return;
            }

            base.RowGUI(args);
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
