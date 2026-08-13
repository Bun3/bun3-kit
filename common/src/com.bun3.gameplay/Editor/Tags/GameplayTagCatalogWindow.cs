#nullable enable
#pragma warning disable CS0618
using System;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal enum UnsavedChangesDecision
    {
        Save,
        Discard,
        Cancel
    }

    internal sealed class GameplayTagCatalogWindow : EditorWindow
    {
        [SerializeField] private TreeViewState _treeViewState = null!;

        private readonly GameplayTagCatalogWindowController _controller =
            new GameplayTagCatalogWindowController();
        private SearchField? _searchField;
        private GameplayTagTreeView _treeView = null!;
        private GameplayTagTreeModel? _model;
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
            saveChangesMessage = "Save changes to the current gameplay tag catalog?";
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            EnsureTreeViewState();
            SynchronizeUnsavedChanges();
        }

        private void OnDisable() =>
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;

        /// <summary>현재 게임플레이 태그 카탈로그의 변경 사항을 저장합니다.</summary>
        public override void SaveChanges()
        {
            if (!Execute(_controller.Save)) return;
            base.SaveChanges();
        }

        /// <summary>현재 게임플레이 태그 카탈로그의 저장하지 않은 변경 사항을 버립니다.</summary>
        public override void DiscardChanges()
        {
            _controller.DiscardChanges();
            SynchronizeUnsavedChanges();
            base.DiscardChanges();
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
                    Execute(() => _controller.RenameSubtree(selectedPath, _movePath));
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
            if (path.Length == 0 ||
                !TryPrepareForCatalogReplacement("Create Gameplay Tag Catalog")) return;
            Execute(() => _controller.New(path));
        }

        private void Open()
        {
            var path = EditorUtility.OpenFilePanel("Open Gameplay Tag Catalog", "", "json");
            if (path.Length == 0 ||
                !TryPrepareForCatalogReplacement("Open Gameplay Tag Catalog")) return;
            Execute(() => _controller.Open(path));
        }

        private void Reload()
        {
            if (!TryPrepareForCatalogReplacement("Reload Gameplay Tags")) return;

            Execute(() =>
            {
                if (!_controller.Reload(discardDirty: true))
                {
                    throw new InvalidOperationException("Gameplay tag reload was not allowed.");
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

        internal static UnsavedChangesDecision MapUnsavedChangesDialogResult(int result) => result switch
        {
            0 => UnsavedChangesDecision.Save,
            1 => UnsavedChangesDecision.Cancel,
            2 => UnsavedChangesDecision.Discard,
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };

        internal static bool TryResolveUnsavedChanges(
            UnsavedChangesDecision decision, Func<bool> save)
        {
            if (save is null) throw new ArgumentNullException(nameof(save));
            return decision switch
            {
                UnsavedChangesDecision.Save => save(),
                UnsavedChangesDecision.Discard => true,
                UnsavedChangesDecision.Cancel => false,
                _ => throw new ArgumentOutOfRangeException(nameof(decision))
            };
        }

        private bool TryPrepareForCatalogReplacement(string title)
        {
            if (!_controller.IsDirty) return true;
            var result = EditorUtility.DisplayDialogComplex(
                title,
                "Save changes to the current gameplay tag catalog?",
                "Save", "Cancel", "Discard");
            return TryResolveUnsavedChanges(
                MapUnsavedChangesDialogResult(result),
                () => Execute(_controller.Save));
        }

        private void BeforeAssemblyReload()
        {
            if (!_controller.IsDirty) return;
            var save = EditorUtility.DisplayDialog(
                "Reload Scripts",
                "Save changes to the current gameplay tag catalog before scripts reload?",
                "Save", "Discard");
            HandleBeforeAssemblyReload(save
                ? UnsavedChangesDecision.Save
                : UnsavedChangesDecision.Discard);
        }

        internal void HandleBeforeAssemblyReload(UnsavedChangesDecision decision)
        {
            if (!_controller.IsDirty) return;
            if (decision == UnsavedChangesDecision.Save)
            {
                _ = Execute(_controller.Save);
                return;
            }

            if (decision != UnsavedChangesDecision.Discard)
                throw new ArgumentException("Assembly reload cannot be cancelled.", nameof(decision));
            _controller.DiscardChanges();
            SynchronizeUnsavedChanges();
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

        private bool Execute(Action action)
        {
            if (_controller.TryExecute(action, out var error))
            {
                ReloadTree();
                SynchronizeUnsavedChanges();
                return true;
            }

            SynchronizeUnsavedChanges();
            GameplayTagValidationWindow.Show(
                _controller.FilePath.Length == 0 ? "GameplayTags.json" : _controller.FilePath,
                error!);
            return false;
        }

        private void SynchronizeUnsavedChanges() => hasUnsavedChanges = _controller.IsDirty;

        private void ReloadTree()
        {
            if (_controller.Session is null)
            {
                _model = null;
                _treeView.SetRows(Array.Empty<GameplayTagTreeRowModel>());
                _treeView.SynchronizeSelection(string.Empty);
                return;
            }

            _model = new GameplayTagTreeModel(_controller.Session);
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
