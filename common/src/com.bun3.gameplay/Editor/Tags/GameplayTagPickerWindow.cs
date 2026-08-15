#nullable enable
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal readonly struct GameplayTagPickerRowRects
    {
        internal GameplayTagPickerRowRects(Rect nameRect, Rect sourceRect)
        {
            NameRect = nameRect;
            SourceRect = sourceRect;
        }

        internal Rect NameRect { get; }
        internal Rect SourceRect { get; }
    }

    internal static class GameplayTagPickerRowGeometry
    {
        internal static GameplayTagPickerRowRects Calculate(
            Rect labelRect,
            float sourceWidth,
            float spacing)
        {
            var safeSourceWidth = Math.Min(Math.Max(0f, sourceWidth), labelRect.width);
            var sourceRect = new Rect(
                labelRect.xMax - safeSourceWidth,
                labelRect.y,
                safeSourceWidth,
                labelRect.height);
            var nameWidth = Math.Max(0f, sourceRect.xMin - spacing - labelRect.xMin);
            var nameRect = new Rect(labelRect.x, labelRect.y, nameWidth, labelRect.height);
            return new GameplayTagPickerRowRects(nameRect, sourceRect);
        }
    }

    internal sealed class GameplayTagPickerTreeView : GameplayTagProjectionTreeView<GameplayTagPickerRow>
    {
        internal event Action<string>? PathSelected;
        private string _currentCanonicalPath = string.Empty;

        internal GameplayTagPickerTreeView(TreeViewState state)
            : base(state)
        {
        }

        internal void SetRows(IReadOnlyList<GameplayTagPickerRow> rows, bool isFiltering) =>
            SetProjectionRows(rows, isFiltering);

        internal bool TryGetCanonicalPath(int id, out string canonicalPath)
        {
            if (TryGetRow(id, out var row))
            {
                canonicalPath = row.CanonicalPath;
                return true;
            }

            canonicalPath = string.Empty;
            return false;
        }

        internal void SynchronizeSelection(string canonicalPath)
        {
            if (canonicalPath is null) throw new ArgumentNullException(nameof(canonicalPath));
            SetCurrentPath(canonicalPath);
            if (canonicalPath.Length == 0)
            {
                SetSelection(Array.Empty<int>(), TreeViewSelectionOptions.None);
                return;
            }

            SynchronizeSelection(row => string.Equals(
                row.CanonicalPath,
                canonicalPath,
                StringComparison.OrdinalIgnoreCase));
        }

        internal void SetCurrentPath(string canonicalPath)
        {
            _currentCanonicalPath = canonicalPath ?? throw new ArgumentNullException(nameof(canonicalPath));
        }

        internal bool IsCurrent(GameplayTagPickerRow row) =>
            string.Equals(row.CanonicalPath, _currentCanonicalPath, StringComparison.OrdinalIgnoreCase);

        internal static GUIContent CreateNameContent(
            GameplayTagPickerRow row,
            bool isCurrent,
            Texture? checkImage)
        {
            var tooltip = row.CanonicalPath + "\n" + row.SourceDetails;
            if (!isCurrent) return new GUIContent(row.DisplaySegment, tooltip);
            return checkImage is null
                ? new GUIContent("\u2713 " + row.DisplaySegment, tooltip)
                : new GUIContent(row.DisplaySegment, checkImage, tooltip);
        }

        internal static GUIContent CreateSourceContent(GameplayTagPickerRow row)
        {
            var suffix = row.SourceCount == 1 ? " source" : " sources";
            return new GUIContent(
                row.SourceCount + suffix,
                row.CanonicalPath + "\n" + row.SourceDetails);
        }

        protected override GUIContent CreateRowContent(GameplayTagPickerRow row) =>
            CreateNameContent(row, IsCurrent(row), checkImage: null);

        protected override void RowGUI(RowGUIArgs args)
        {
            if (!TryGetRow(args.item.id, out var row))
            {
                base.RowGUI(args);
                return;
            }

            var labelRect = CalculateLabelRect(args.item, args.rowRect);
            var sourceContent = CreateSourceContent(row);
            var sourceWidth = EditorStyles.miniLabel.CalcSize(sourceContent).x;
            var rects = GameplayTagPickerRowGeometry.Calculate(labelRect, sourceWidth, 8f);
            var checkImage = EditorGUIUtility.IconContent("TestPassed").image;
            GUI.Label(rects.NameRect, CreateNameContent(row, IsCurrent(row), checkImage));
            GUI.Label(rects.SourceRect, sourceContent, EditorStyles.miniLabel);
        }

        protected override void RowSelected(GameplayTagPickerRow row) =>
            PathSelected?.Invoke(row.CanonicalPath);
    }

    internal sealed class GameplayTagPickerWindow : EditorWindow
    {
        [SerializeField]
        private TreeViewState _treeState = new TreeViewState();
        private GameplayTagPickerTreeView? _treeView;
        private GameplayTagPickerModel? _model;
        private Action<string>? _onSelected;
        private IReadOnlyList<string> _persistentDiagnostics = Array.Empty<string>();
        private IReadOnlyList<GameplayTagWorkspaceDiagnostic> _diagnosticEntries =
            Array.Empty<GameplayTagWorkspaceDiagnostic>();
        private Func<GameplayTagEditorWorkspace>? _workspaceProvider;
        private string _search = string.Empty;
        private string _currentRawValue = string.Empty;
        private bool _canSelect;
        private double _nextWorkspaceRefresh;

        internal GameplayTagPickerModel? Model => _model;
        internal string CurrentRawValue => _currentRawValue;
        internal bool CanSelect => _canSelect;
        internal IReadOnlyList<string> PersistentDiagnostics => _persistentDiagnostics;

        internal static GameplayTagPickerWindow Show(
            GameplayTagWorkspaceSnapshot snapshot,
            string selectedPath,
            Action<string> onSelected)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
            var window = CreateInstance<GameplayTagPickerWindow>();
            window.Initialize(snapshot, selectedPath, onSelected);
            window.ShowUtility();
            window.Focus();
            return window;
        }

        internal static GameplayTagPickerWindow Show(
            GameplayTagEditorWorkspace workspace,
            string selectedPath,
            Action<string> onSelected)
        {
            if (workspace is null) throw new ArgumentNullException(nameof(workspace));
            var window = CreateInstance<GameplayTagPickerWindow>();
            window.Initialize(workspace, selectedPath, onSelected);
            window.ShowUtility();
            window.Focus();
            return window;
        }

        internal static GameplayTagPickerWindow ShowLive(
            Func<GameplayTagEditorWorkspace> workspaceProvider,
            string selectedPath,
            Action<string> onSelected)
        {
            if (workspaceProvider is null) throw new ArgumentNullException(nameof(workspaceProvider));
            var window = CreateInstance<GameplayTagPickerWindow>();
            window.Initialize(workspaceProvider, selectedPath, onSelected);
            window.ShowUtility();
            window.Focus();
            return window;
        }

        internal void Initialize(
            GameplayTagWorkspaceSnapshot snapshot,
            string selectedPath,
            Action<string> onSelected)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
            Configure(
                snapshot,
                selectedPath,
                onSelected,
                canSelect: true,
                Array.Empty<GameplayTagWorkspaceDiagnostic>());
        }

        internal void Initialize(
            GameplayTagEditorWorkspace workspace,
            string selectedPath,
            Action<string> onSelected)
        {
            if (workspace is null) throw new ArgumentNullException(nameof(workspace));
            Configure(
                workspace.Snapshot,
                selectedPath,
                onSelected,
                workspace.CanBuildCatalog,
                workspace.DiagnosticEntries);
        }

        internal void Initialize(
            Func<GameplayTagEditorWorkspace> workspaceProvider,
            string selectedPath,
            Action<string> onSelected)
        {
            _workspaceProvider = workspaceProvider
                ?? throw new ArgumentNullException(nameof(workspaceProvider));
            if (selectedPath is null) throw new ArgumentNullException(nameof(selectedPath));
            _currentRawValue = selectedPath;
            _onSelected = onSelected ?? throw new ArgumentNullException(nameof(onSelected));
            _search = string.Empty;
            RefreshWorkspace(_workspaceProvider());
        }

        internal void RefreshWorkspace(GameplayTagEditorWorkspace workspace)
        {
            if (workspace is null) throw new ArgumentNullException(nameof(workspace));
            ApplyWorkspace(
                workspace.Snapshot,
                workspace.CanBuildCatalog,
                workspace.DiagnosticEntries);
        }

        internal bool TrySelect(int rowId)
        {
            EnsureTree();
            if (!_canSelect || !_treeView!.TryGetCanonicalPath(rowId, out var canonicalPath)) return false;
            ApplySelection(canonicalPath, closeWindow: false);
            return true;
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("GameplayTag Picker");
            minSize = new Vector2(360f, 280f);
            EnsureTree();
            EditorApplication.update -= RefreshWorkspaceOnEditorUpdate;
            EditorApplication.update += RefreshWorkspaceOnEditorUpdate;
        }

        private void OnDisable() =>
            EditorApplication.update -= RefreshWorkspaceOnEditorUpdate;

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Current value");
            EditorGUILayout.SelectableLabel(
                _currentRawValue,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            GameplayTagDiagnosticsPanel.Draw(_diagnosticEntries);

            EditorGUI.BeginChangeCheck();
            var search = EditorGUILayout.TextField("Search", _search);
            if (EditorGUI.EndChangeCheck())
            {
                _search = search;
                ReloadRows();
            }

            var treeRect = GUILayoutUtility.GetRect(
                0f,
                100000f,
                0f,
                100000f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            EditorGUI.BeginDisabledGroup(!_canSelect);
            _treeView!.OnGUI(treeRect);
            EditorGUI.EndDisabledGroup();
        }

        private void Configure(
            GameplayTagWorkspaceSnapshot? snapshot,
            string selectedPath,
            Action<string> onSelected,
            bool canSelect,
            IReadOnlyList<GameplayTagWorkspaceDiagnostic> diagnostics)
        {
            if (selectedPath is null) throw new ArgumentNullException(nameof(selectedPath));
            _onSelected = onSelected ?? throw new ArgumentNullException(nameof(onSelected));
            _workspaceProvider = null;
            _currentRawValue = selectedPath;
            _search = string.Empty;
            ApplyWorkspace(snapshot, canSelect, diagnostics);
        }

        private void ApplyWorkspace(
            GameplayTagWorkspaceSnapshot? snapshot,
            bool canSelect,
            IReadOnlyList<GameplayTagWorkspaceDiagnostic> diagnostics)
        {
            if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));

            var entries = new GameplayTagWorkspaceDiagnostic[diagnostics.Count];
            var messages = new string[diagnostics.Count];
            for (var index = 0; index < entries.Length; index++)
            {
                entries[index] = diagnostics[index];
                messages[index] = diagnostics[index].Message;
            }

            _diagnosticEntries = Array.AsReadOnly(entries);
            _persistentDiagnostics = Array.AsReadOnly(messages);
            _canSelect = canSelect && snapshot is not null;
            _model = snapshot is null ? null : new GameplayTagPickerModel(snapshot);
            EnsureTree();
            ReloadRows();
            _treeView!.SynchronizeSelection(_currentRawValue);
        }

        private void RefreshWorkspaceOnEditorUpdate()
        {
            if (_workspaceProvider is null
                || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling
                || EditorApplication.timeSinceStartup < _nextWorkspaceRefresh)
            {
                return;
            }

            _nextWorkspaceRefresh = EditorApplication.timeSinceStartup + 0.75d;
            RefreshWorkspace(_workspaceProvider());
            Repaint();
        }

        private void EnsureTree()
        {
            if (_treeView is not null) return;
            _treeView = new GameplayTagPickerTreeView(_treeState);
            _treeView.PathSelected += SelectPath;
        }

        private void ReloadRows()
        {
            EnsureTree();
            var rows = _model is null
                ? Array.Empty<GameplayTagPickerRow>()
                : _model.Filter(_search);
            _treeView!.SetRows(rows, _search.Length > 0);
        }

        private void SelectPath(string canonicalPath)
        {
            if (!_canSelect) return;
            ApplySelection(canonicalPath, closeWindow: true);
        }

        private void ApplySelection(string canonicalPath, bool closeWindow)
        {
            _currentRawValue = canonicalPath;
            _treeView?.SetCurrentPath(canonicalPath);
            _onSelected!(canonicalPath);
            if (closeWindow) Close();
        }
    }
}
#pragma warning restore CS0618
