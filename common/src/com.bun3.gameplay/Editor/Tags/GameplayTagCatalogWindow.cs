#nullable enable
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
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
        private const string NewTagNameControl = "GameplayTag.NewTagName";

        [SerializeField] private TreeViewState _treeViewState = null!;

        private readonly GameplayTagCatalogWindowController _controller =
            new GameplayTagCatalogWindowController();
        private SearchField? _searchField;
        private GameplayTagTreeView _treeView = null!;
        private GameplayTagTreeModel? _model;
        private string _search = string.Empty;
        private string _newTagName = string.Empty;
        private string _newTagComment = string.Empty;
        private bool _focusNewTagName;
        private bool _showRedirects = true;
        private Vector2 _redirectScroll;

        /// <summary>게임플레이 태그 카탈로그 창을 엽니다.</summary>
        [MenuItem("Gameplay/Tag Editor")]
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
            DrawSearch();
            DrawAddTag();
            DrawTagTree();
            DrawRedirects();
            DrawStatus();
        }

        private void EnsureTreeViewState()
        {
            if (_treeViewState is null)
            {
                _treeViewState = new TreeViewState();
            }

            if (_treeView is not null) return;

            _treeView = new GameplayTagTreeView(_treeViewState);
            _treeView.PathSelected += SelectPath;
            _treeView.RenameRequested += RequestRename;
            _treeView.CommentEditRequested += RequestComment;
            _treeView.SubTagRequested += PrepareSubTag;
            _treeView.CopyRequested += CopyTag;
            _treeView.DeleteRequested += DeleteSelected;
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

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSearch()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var updatedSearch = (_searchField ??= new SearchField()).OnToolbarGUI(_search);
            EditorGUILayout.EndHorizontal();
            if (string.Equals(updatedSearch, _search, StringComparison.Ordinal)) return;

            _search = updatedSearch;
            ReloadTree();
        }

        private void DrawAddTag()
        {
            EditorGUILayout.LabelField("Add New Gameplay Tag", EditorStyles.boldLabel);
            GUI.SetNextControlName(NewTagNameControl);
            _newTagName = EditorGUILayout.TextField("Tag Name", _newTagName);
            _newTagComment = EditorGUILayout.TextField("Comment", _newTagComment);
            using (new EditorGUI.DisabledScope(_controller.Session is null || _newTagName.Length == 0))
            {
                if (GUILayout.Button("Add"))
                {
                    var added = _newTagName;
                    var comment = _newTagComment;
                    if (Execute(() => _controller.Add(added, comment)))
                    {
                        _newTagName = string.Empty;
                        _newTagComment = string.Empty;
                    }
                }
            }

            if (!_focusNewTagName || Event.current.type != EventType.Repaint) return;

            EditorGUI.FocusTextInControl(NewTagNameControl);
            _focusNewTagName = false;
        }

        private void DrawTagTree()
        {
            var treeRect = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUIStyle.none,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            _treeView.OnGUI(treeRect);
        }

        private void DrawRedirects()
        {
            var redirects = _controller.Session?.Redirects;
            var count = redirects is null ? 0 : redirects.Count;
            _showRedirects = EditorGUILayout.Foldout(_showRedirects, "Redirects (" + count + ")");
            if (!_showRedirects || count == 0) return;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Find All References", GUILayout.Width(140f)))
            {
                FindAllReferences();
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button("Remove Obsolete Redirects", GUILayout.Width(180f)))
            {
                RemoveObsoleteRedirects();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();

            _redirectScroll = EditorGUILayout.BeginScrollView(
                _redirectScroll, true, true, GUILayout.MaxHeight(120f));
            for (var index = 0; index < count; index++)
            {
                var source = redirects![index].From;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(
                    CreateRedirectContent(redirects[index]),
                    GUILayout.ExpandWidth(false));
                var find = GUILayout.Button("Find References", GUILayout.Width(120f));
                var remove = GUILayout.Button("Remove Redirect", GUILayout.Width(120f));
                EditorGUILayout.EndHorizontal();

                if (find)
                {
                    GameplayTagReferenceResultsWindow.Show(SearchRedirectReferences(new[] { source }));
                    GUIUtility.ExitGUI();
                }

                if (!remove) continue;

                RemoveRedirect(source);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndScrollView();
        }

        private void FindAllReferences() =>
            GameplayTagReferenceResultsWindow.Show(
                SearchRedirectReferences(CollectRedirectSources()));

        /// <summary>단일 redirect를 참조 확인과 외부 데이터 경고를 거쳐 제거합니다.</summary>
        private void RemoveRedirect(string source)
        {
            var result = SearchRedirectReferences(new[] { source });
            if (!result.IsComplete)
            {
                // 검색이 불완전하면 override조차 제공하지 않고 결과만 보여 준다.
                GameplayTagReferenceResultsWindow.Show(result);
                return;
            }

            if (result.Matches.Count > 0)
            {
                var decision = GameplayTagRedirectMaintenance.MapReferencedDialogResult(
                    EditorUtility.DisplayDialogComplex(
                        "Remove Gameplay Tag Redirect",
                        source + " still has " + result.Matches.Count
                            + " text match(es) in this project.",
                        "Open References", "Cancel", "Remove Anyway"));
                if (decision == ReferencedRedirectDecision.OpenReferences)
                {
                    GameplayTagReferenceResultsWindow.Show(result);
                    return;
                }

                if (decision == ReferencedRedirectDecision.Cancel) return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Remove Gameplay Tag Redirect",
                    source + "\n\n" + (result.Matches.Count > 0
                        ? GameplayTagRedirectMaintenance.ExternalDataScopeWarning
                        : GameplayTagRedirectMaintenance.NoProjectReferencesWarning),
                    "Remove", "Cancel"))
            {
                return;
            }

            Execute(() => _controller.RemoveRedirects(new[] { source }));
        }

        private void RemoveObsoleteRedirects()
        {
            var result = SearchRedirectReferences(CollectRedirectSources());
            if (!result.IsComplete)
            {
                GameplayTagReferenceResultsWindow.Show(result);
                return;
            }

            TryApplyBulkCleanup(result, candidates =>
            {
                if (candidates.Count == 0)
                {
                    GameplayTagReferenceResultsWindow.Show(result);
                    return candidates;
                }

                return GameplayTagRedirectCleanupDialog.ShowModal(candidates);
            });
        }

        /// <summary>완전한 검색 결과에서 사용자가 고른 redirect만 제거합니다.</summary>
        /// <param name="result">참조 검색 결과입니다.</param>
        /// <param name="selectSources">후보 중 제거할 old path를 고르는 선택기입니다.</param>
        internal bool TryApplyBulkCleanup(
            GameplayTagReferenceSearchResult result,
            Func<IReadOnlyList<string>, IReadOnlyList<string>> selectSources)
        {
            if (selectSources is null) throw new ArgumentNullException(nameof(selectSources));
            if (!result.IsComplete) return false;
            var session = _controller.Session
                ?? throw new InvalidOperationException("No gameplay tag catalog is open.");
            var candidates = GameplayTagRedirectMaintenance.GetUnreferencedSources(
                session.Redirects, result);
            var selected = selectSources(candidates);
            return selected.Count > 0 && Execute(() => _controller.RemoveRedirects(selected));
        }

        private string[] CollectRedirectSources()
        {
            var redirects = _controller.Session?.Redirects;
            if (redirects is null) return Array.Empty<string>();

            var sources = new string[redirects.Count];
            for (var index = 0; index < redirects.Count; index++)
            {
                sources[index] = redirects[index].From;
            }

            return sources;
        }

        private GameplayTagReferenceSearchResult SearchRedirectReferences(IReadOnlyList<string> sources)
        {
            try
            {
                var files = GameplayTagProjectReferenceFiles.Enumerate();
                // 훑지 못한 디렉터리가 있으면 검색 자체가 성공해도 증거가 불완전하다.
                return new GameplayTagTextReferenceScanner(File.OpenText).Search(
                    files.Files,
                    sources,
                    _controller.FilePath,
                    progress => EditorUtility.DisplayCancelableProgressBar(
                        "Find GameplayTag References", progress.DisplayPath, progress.Fraction))
                    .WithEnumerationErrors(files.Errors);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>redirect 행의 전체 표시 문구를 도구 설명까지 담은 content로 만듭니다.</summary>
        internal static GUIContent CreateRedirectContent(EditableRedirectRow redirect)
        {
            var text = redirect.From + "  →  " + redirect.To;
            return new GUIContent(text, text);
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

        private void RequestRename(string path) =>
            ApplyRename(path, GameplayTagEditDialog.ShowRename(path));

        private void RequestComment(string path) =>
            ApplyComment(path, GameplayTagEditDialog.ShowComment(path, FindComment(path)));

        /// <summary>선택한 경로 뒤에 구분자를 붙여 추가 입력을 준비하고 focus합니다.</summary>
        internal void PrepareSubTag(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));

            _newTagName = path + ".";
            _focusNewTagName = true;
            Repaint();
        }

        /// <summary>표시용 전체 경로를 시스템 clipboard에 복사합니다.</summary>
        internal void CopyTag(string path) =>
            EditorGUIUtility.systemCopyBuffer = path ?? throw new ArgumentNullException(nameof(path));

        /// <summary>승인된 이름 변경 결과를 마지막 세그먼트 rename으로 적용합니다.</summary>
        internal void ApplyRename(string path, GameplayTagTextEditResult result)
        {
            if (result.Accepted) Execute(() => _controller.RenameSubtree(path, result.Value));
        }

        /// <summary>승인된 comment 결과를 적용하고 암시 부모를 명시 행으로 승격합니다.</summary>
        internal void ApplyComment(string path, GameplayTagTextEditResult result)
        {
            if (result.Accepted) Execute(() => _controller.SetComment(path, result.Value));
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
                _treeView.SetRows(Array.Empty<GameplayTagTreeRowModel>(), isFiltering: false);
                _treeView.SynchronizeSelection(string.Empty);
                return;
            }

            _model = new GameplayTagTreeModel(_controller.Session);
            _treeView.SetRows(_model.Filter(_search), _search.Length > 0);
            _treeView.SynchronizeSelection(_controller.SelectedPath);
        }

        private void SelectPath(string path)
        {
            _controller.Select(path);
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
