#nullable enable
#pragma warning disable CS0618
using System;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal sealed class GameplayTagCatalogWindow : EditorWindow
    {
        [SerializeField] private TreeViewState _treeViewState = null!;

        private readonly GameplayTagCatalogWindowController _controller =
            new GameplayTagCatalogWindowController();
        private SearchField? _searchField;
        private GameplayTagTreeView _treeView = null!;
        private GameplayTagCatalogViewModel? _model;
        private string _search = string.Empty;
        private string _newRootPath = string.Empty;
        private string _newRootComment = string.Empty;
        private string _newChildSegment = string.Empty;
        private string _comment = string.Empty;
        private string _movePath = string.Empty;

        /// <summary>게임플레이 태그 카탈로그 창을 엽니다.</summary>
        [MenuItem("Bun3/Gameplay Tags")]
        public static void OpenWindow()
        {
            var window = GetWindow<GameplayTagCatalogWindow>();
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Gameplay Tags");
            minSize = new Vector2(640f, 420f);
            EnsureTreeViewState();
        }

        private void OnGUI()
        {
            EnsureTreeViewState();
            DrawToolbar();
            DrawContent();
            DrawStatus();
        }

        private void EnsureTreeViewState()
        {
            if (_treeViewState is null)
            {
                _treeViewState = new TreeViewState();
            }

            if (_treeView is null)
            {
                _treeView = new GameplayTagTreeView(_treeViewState);
                _treeView.PathSelected += SelectPath;
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField(
                _controller.FilePath.Length == 0 ? "No catalog loaded" : _controller.FilePath,
                EditorStyles.toolbarButton,
                GUILayout.ExpandWidth(true));
            if (GUILayout.Button("New", EditorStyles.toolbarButton)) CreateNew();
            if (GUILayout.Button("Open", EditorStyles.toolbarButton)) Open();
            if (GUILayout.Button("Reload", EditorStyles.toolbarButton)) Reload();
            using (new EditorGUI.DisabledScope(_controller.Session is null || !_controller.IsDirty))
            {
                if (GUILayout.Button("Save", EditorStyles.toolbarButton)) Execute(_controller.Save);
            }

            var updatedSearch = (_searchField ??= new SearchField()).OnToolbarGUI(_search);
            if (!string.Equals(updatedSearch, _search, StringComparison.Ordinal))
            {
                _search = updatedSearch;
                ReloadTree();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawContent()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.Width(Mathf.Max(300f, position.width * 0.56f)));
            var treeRect = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUIStyle.none,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            _treeView.OnGUI(treeRect);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(GUILayout.MinWidth(250f));
            DrawDetail();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDetail()
        {
            EditorGUILayout.LabelField("Details", EditorStyles.boldLabel);
            var selectedPath = _controller.SelectedPath;
            EditorGUILayout.LabelField("Path", selectedPath.Length == 0 ? "(none)" : selectedPath);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Add Root", EditorStyles.boldLabel);
            _newRootPath = EditorGUILayout.TextField("Path", _newRootPath);
            _newRootComment = EditorGUILayout.TextField("Comment", _newRootComment);
            using (new EditorGUI.DisabledScope(_controller.Session is null || _newRootPath.Length == 0))
            {
                if (GUILayout.Button("Add Root"))
                {
                    Execute(() => _controller.Add(_newRootPath, _newRootComment));
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selected Tag", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_controller.Session is null || selectedPath.Length == 0))
            {
                _comment = EditorGUILayout.TextField("Comment", _comment);
                if (GUILayout.Button("Set Comment"))
                {
                    Execute(() => _controller.SetComment(selectedPath, _comment));
                }

                _newChildSegment = EditorGUILayout.TextField("Child", _newChildSegment);
                if (GUILayout.Button("Add Child") && _newChildSegment.Length > 0)
                {
                    Execute(() => _controller.Add(selectedPath + "." + _newChildSegment));
                }

                _movePath = EditorGUILayout.TextField("Rename/Move", _movePath);
                if (GUILayout.Button("Rename/Move") && _movePath.Length > 0)
                {
                    Execute(() => _controller.RelocateSubtree(selectedPath, _movePath));
                }

                if (GUILayout.Button("Delete")) DeleteSelected(selectedPath);
            }
        }

        private void DrawStatus()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            if (_model is null)
            {
                EditorGUILayout.LabelField("No catalog loaded");
            }
            else
            {
                EditorGUILayout.LabelField("Fingerprint: " + _model.FingerprintPrefix);
                EditorGUILayout.LabelField("Active: " + _model.ActiveCount);
                EditorGUILayout.LabelField(_controller.IsDirty ? "Dirty" : "Saved");
            }

            EditorGUILayout.EndHorizontal();
        }

        private void CreateNew()
        {
            var path = EditorUtility.SaveFilePanel("Create Gameplay Tag Catalog", "", "GameplayTags", "json");
            if (path.Length == 0) return;
            Execute(() => _controller.New(path));
        }

        private void Open()
        {
            var path = EditorUtility.OpenFilePanel("Open Gameplay Tag Catalog", "", "json");
            if (path.Length == 0) return;
            Execute(() => _controller.Open(path));
        }

        private void Reload()
        {
            var discardDirty = false;
            if (_controller.IsDirty)
            {
                discardDirty = EditorUtility.DisplayDialogComplex(
                    "Reload Gameplay Tags",
                    "Discard unsaved gameplay tag edits?",
                    "Discard and Reload",
                    "Cancel",
                    string.Empty) == 0;
                if (!discardDirty) return;
            }

            Execute(() =>
            {
                if (!_controller.Reload(discardDirty))
                {
                    throw new InvalidOperationException("Unsaved gameplay tag edits were not discarded.");
                }
            });
        }

        private void DeleteSelected(string selectedPath)
        {
            var hasDescendants = HasDescendants(selectedPath);
            if (!ConfirmDelete(hasDescendants, EditorUtility.DisplayDialog))
            {
                return;
            }

            Execute(() => _controller.Delete(selectedPath, hasDescendants));
        }

        internal static bool ConfirmDelete(
            bool hasDescendants,
            Func<string, string, string, string, bool> displayDialog)
        {
            if (displayDialog is null) throw new ArgumentNullException(nameof(displayDialog));

            return hasDescendants
                ? displayDialog(
                    "Delete Gameplay Tag Subtree",
                    "The selected tag has descendants. Delete the full subtree?",
                    "Delete Subtree",
                    "Cancel")
                : displayDialog(
                    "Delete Gameplay Tag",
                    "Delete the selected gameplay tag?",
                    "Delete Tag",
                    "Cancel");
        }

        private bool HasDescendants(string path)
        {
            if (_model is null) return false;
            var rows = _model.Filter(string.Empty);
            for (var index = 0; index < rows.Count; index++)
            {
                var candidate = rows[index].Path;
                if (candidate.Length > path.Length
                    && candidate.StartsWith(path, StringComparison.OrdinalIgnoreCase)
                    && candidate[path.Length] == '.')
                {
                    return true;
                }
            }

            return false;
        }

        private void Execute(Action action)
        {
            if (_controller.TryExecute(action, out var error))
            {
                ReloadTree();
                return;
            }

            GameplayTagValidationWindow.Show(
                _controller.FilePath.Length == 0 ? "GameplayTags.json" : _controller.FilePath,
                error!);
        }

        private void ReloadTree()
        {
            if (_controller.Session is null)
            {
                _model = null;
                _treeView.SetRows(Array.Empty<GameplayTagTreeRowModel>());
                _treeView.SynchronizeSelection(string.Empty);
                return;
            }

            _model = new GameplayTagCatalogViewModel(_controller.Session);
            _treeView.SetRows(_model.Filter(_search));
            _treeView.SynchronizeSelection(_controller.SelectedPath);
            if (_controller.SelectedPath.Length > 0)
            {
                _comment = FindComment(_controller.SelectedPath);
                _movePath = _controller.SelectedPath;
            }
        }

        private void SelectPath(string path)
        {
            _controller.Select(path);
            _comment = FindComment(path);
            _movePath = path;
            Repaint();
        }

        private string FindComment(string path)
        {
            if (_model is null) return string.Empty;
            var rows = _model.Filter(string.Empty);
            for (var index = 0; index < rows.Count; index++)
            {
                if (string.Equals(rows[index].Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    return rows[index].Comment;
                }
            }

            return string.Empty;
        }
    }
}
#pragma warning restore CS0618
