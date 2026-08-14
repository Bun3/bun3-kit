#nullable enable
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal sealed class GameplayTagPickerTreeView : GameplayTagProjectionTreeView<GameplayTagPickerRow>
    {
        internal event Action<string>? PathSelected;

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

        internal static GUIContent CreateLabelContent(GameplayTagPickerRow row)
        {
            var sourceLabel = row.SourceCount == 1 ? " source" : " sources";
            var text = row.DisplaySegment + "  " + row.SourceCount + sourceLabel;
            var tooltip = row.CanonicalPath + "\n" + row.SourceDetails;
            return new GUIContent(text, tooltip);
        }

        protected override GUIContent CreateRowContent(GameplayTagPickerRow row) =>
            CreateLabelContent(row);

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
        private string _search = string.Empty;
        private string _currentRawValue = string.Empty;
        private bool _canSelect;

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

        internal void Initialize(
            GameplayTagWorkspaceSnapshot snapshot,
            string selectedPath,
            Action<string> onSelected)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
            Configure(snapshot, selectedPath, onSelected, canSelect: true, Array.Empty<string>());
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
                workspace.Diagnostics);
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
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Current value");
            EditorGUILayout.SelectableLabel(
                _currentRawValue,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            for (var index = 0; index < _persistentDiagnostics.Count; index++)
            {
                EditorGUILayout.HelpBox(
                    _persistentDiagnostics[index],
                    _canSelect ? MessageType.Warning : MessageType.Error);
            }

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
            IReadOnlyList<string> diagnostics)
        {
            if (selectedPath is null) throw new ArgumentNullException(nameof(selectedPath));
            _onSelected = onSelected ?? throw new ArgumentNullException(nameof(onSelected));
            if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));

            var diagnosticCopy = new string[diagnostics.Count];
            for (var index = 0; index < diagnosticCopy.Length; index++)
            {
                diagnosticCopy[index] = diagnostics[index];
            }

            _persistentDiagnostics = Array.AsReadOnly(diagnosticCopy);
            _currentRawValue = selectedPath;
            _canSelect = canSelect && snapshot is not null;
            _model = snapshot is null ? null : new GameplayTagPickerModel(snapshot);
            _search = string.Empty;
            EnsureTree();
            ReloadRows();
            _treeView!.SynchronizeSelection(selectedPath);
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
            _onSelected!(canonicalPath);
            if (closeWindow) Close();
        }
    }
}
#pragma warning restore CS0618
