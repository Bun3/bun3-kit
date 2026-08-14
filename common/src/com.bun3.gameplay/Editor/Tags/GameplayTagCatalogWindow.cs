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
        private const string ConfigureCatalogTitle = "Configure GameplayTag Catalog";
        private const string ProjectSettingsPath = "Project/Gameplay Tags";

        [SerializeField] private TreeViewState _treeViewState = null!;

        private GameplayTagCatalogWindowController _controller = null!;
        private SearchField? _searchField;
        private GameplayTagTreeView _treeView = null!;
        private GameplayTagTreeModel? _model;
        private string _search = string.Empty;
        private string _newTagName = string.Empty;
        private string _newTagComment = string.Empty;
        private string _catalogId = string.Empty;
        private bool _focusNewTagName;
        private bool _showRedirects = true;
        private Vector2 _redirectScroll;
        private double _nextWorkspaceRefresh;
        private Action<string, Exception>? _showValidationError;
        private Action<string, string>? _showConfigureWarning;
        private IReadOnlyList<GameplayTagRedirectRowModel> _redirectRows =
            Array.Empty<GameplayTagRedirectRowModel>();

        /// <summary>게임플레이 태그 카탈로그 창을 엽니다.</summary>
        [MenuItem("Gameplay/Tag Editor")]
        public static void OpenWindow()
        {
            var window = GetWindow<GameplayTagCatalogWindow>();
            window.Show();
        }

        private void OnEnable()
        {
            _controller ??= new GameplayTagCatalogWindowController();
            _search ??= string.Empty;
            _newTagName ??= string.Empty;
            _newTagComment ??= string.Empty;
            _catalogId = GameplayTagProjectSettings.ReadConfiguredCatalogId()
                ?? GameplayTagProjectSettings.GetSuggestedCatalogId(PlayerSettings.productName);
            titleContent = new GUIContent("Gameplay Tags");
            minSize = new Vector2(640f, 420f);
            saveChangesMessage = "Save changes to the current gameplay tag catalog?";
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            EditorApplication.update -= RefreshWorkspaceOnEditorUpdate;
            EditorApplication.update += RefreshWorkspaceOnEditorUpdate;
            EnsureTreeViewState();
            ReloadTree();
            SynchronizeUnsavedChanges();
        }

        private void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
            EditorApplication.update -= RefreshWorkspaceOnEditorUpdate;
        }

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
            HandleSaveShortcut(Event.current, EditorWindow.focusedWindow == this);
            EnsureTreeViewState();
            GameplayTagDiagnosticsPanel.Draw(_controller.Workspace.DiagnosticEntries);
            DrawConfigureCatalog();
            DrawToolbar();
            DrawSearch();
            DrawAddTag();
            DrawTagTree();
            DrawRedirects();
            DrawStatus();
        }

        internal bool HandleSaveShortcut(Event currentEvent, bool isFocused)
        {
            if (!isFocused
                || currentEvent.type != EventType.KeyDown
                || currentEvent.keyCode != KeyCode.S
                || (!currentEvent.control && !currentEvent.command)
                || currentEvent.shift
                || currentEvent.alt)
            {
                return false;
            }

            currentEvent.Use();
            if (_controller.Session is not null)
            {
                SaveChanges();
            }

            return true;
        }

        internal void SetValidationErrorHandler(Action<string, Exception> showValidationError) =>
            _showValidationError = showValidationError
                ?? throw new ArgumentNullException(nameof(showValidationError));

        internal bool RequiresCatalogConfiguration => _controller.RequiresCatalogConfiguration;

        internal void SetConfigureWarningHandler(Action<string, string> showConfigureWarning) =>
            _showConfigureWarning = showConfigureWarning
                ?? throw new ArgumentNullException(nameof(showConfigureWarning));

        internal bool ConfigureCatalog(string catalogId)
        {
            _catalogId = catalogId;
            if (_controller.TryExecute(() => _controller.ConfigureCatalog(catalogId), out var error))
            {
                ReloadTree();
                SynchronizeUnsavedChanges();
                Repaint();
                return true;
            }

            SynchronizeUnsavedChanges();
            ShowConfigureWarning(error!);
            return false;
        }

        internal static void OpenProjectSettings(Action<string> openProjectSettings)
        {
            if (openProjectSettings is null) throw new ArgumentNullException(nameof(openProjectSettings));
            openProjectSettings(ProjectSettingsPath);
        }

        private void EnsureTreeViewState()
        {
            if (_treeViewState is null)
            {
                _treeViewState = new TreeViewState();
            }

            if (_treeView is not null) return;

            _treeView = new GameplayTagTreeView(_treeViewState);
            _treeView.TagSelected += SelectTag;
            _treeView.RenameRequested += RequestRename;
            _treeView.CommentEditRequested += RequestComment;
            _treeView.SubTagRequested += selection => PrepareSubTag(selection.CanonicalPath);
            _treeView.CopyRequested += selection => CopyTag(selection.CanonicalPath);
            _treeView.FindReferencesRequested += FindTagReferences;
            _treeView.DeleteRequested += DeleteSelected;
            _treeView.CanEditGameSource = _controller.CanEditGameSource;
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField(
                _controller.GameSourcePath,
                EditorStyles.toolbarButton,
                GUILayout.ExpandWidth(true));
            using (new EditorGUI.DisabledScope(!_controller.CanCreateGameSource))
            {
                if (GUILayout.Button("Create Game Source", EditorStyles.toolbarButton)) CreateGameSource();
            }

            if (GUILayout.Button("Import Existing…", EditorStyles.toolbarButton)) ImportExisting();
            if (GUILayout.Button("Reload", EditorStyles.toolbarButton)) Reload();
            using (new EditorGUI.DisabledScope(_controller.Session is null || !_controller.IsDirty))
            {
                if (GUILayout.Button("Save", EditorStyles.toolbarButton)) Execute(_controller.Save);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawConfigureCatalog()
        {
            if (!RequiresCatalogConfiguration) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(ConfigureCatalogTitle, EditorStyles.boldLabel);
            _catalogId = EditorGUILayout.TextField("Catalog ID", _catalogId);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Settings"))
            {
                _ = ConfigureCatalog(_catalogId);
            }

            if (GUILayout.Button("Open Project Settings"))
            {
                OpenProjectSettings(path => { _ = SettingsService.OpenProjectSettings(path); });
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
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
            using (new EditorGUI.DisabledScope(
                !_controller.CanEditGameSource || _newTagName.Length == 0))
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
            var redirects = _redirectRows;
            var count = redirects.Count;
            _showRedirects = EditorGUILayout.Foldout(_showRedirects, "Redirects (" + count + ")");
            if (!_showRedirects || count == 0) return;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Find All References", GUILayout.Width(140f)))
            {
                FindAllReferences();
                GUIUtility.ExitGUI();
            }

            using (new EditorGUI.DisabledScope(!HasEditableRedirects(redirects)))
            {
                if (GUILayout.Button("Remove Obsolete Redirects", GUILayout.Width(180f)))
                {
                    RemoveObsoleteRedirects();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();

            _redirectScroll = EditorGUILayout.BeginScrollView(
                _redirectScroll, true, true, GUILayout.MaxHeight(120f));
            string? previousSourceId = null;
            for (var index = 0; index < count; index++)
            {
                var redirect = redirects[index];
                if (!string.Equals(previousSourceId, redirect.SourceId, StringComparison.Ordinal))
                {
                    previousSourceId = redirect.SourceId;
                    EditorGUILayout.LabelField(
                        redirect.SourceDisplayName + " (" + redirect.SourceId + ")"
                        + (redirect.IsReadOnly ? " — Read Only" : string.Empty),
                        EditorStyles.boldLabel);
                }

                var source = redirect.From;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(
                    CreateRedirectContent(redirect),
                    GUILayout.ExpandWidth(false));
                var find = GUILayout.Button("Find References", GUILayout.Width(120f));
                bool remove;
                using (new EditorGUI.DisabledScope(redirect.IsReadOnly))
                {
                    remove = GUILayout.Button("Remove Redirect", GUILayout.Width(120f));
                }
                EditorGUILayout.EndHorizontal();

                if (find)
                {
                    GameplayTagReferenceResultsWindow.Show(SearchRedirectReferences(new[] { source }));
                    GUIUtility.ExitGUI();
                }

                if (!remove) continue;

                RemoveRedirect(redirect.SourceId, source);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndScrollView();
        }

        private void FindAllReferences() =>
            GameplayTagReferenceResultsWindow.Show(
                SearchRedirectReferences(CollectRedirectSources()));

        /// <summary>단일 redirect를 참조 확인과 외부 데이터 경고를 거쳐 제거합니다.</summary>
        private void RemoveRedirect(string sourceId, string source)
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

            Execute(() => _controller.RemoveRedirects(sourceId, new[] { source }));
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
            var candidates = GameplayTagRedirectMaintenance.GetUnreferencedSources(
                _redirectRows, result);
            var selected = selectSources(candidates);
            return selected.Count > 0 && Execute(() => _controller.RemoveRedirects("game", selected));
        }

        private string[] CollectRedirectSources()
        {
            var redirects = _redirectRows;

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
                    _controller.GameSourcePath,
                    progress => EditorUtility.DisplayCancelableProgressBar(
                        "Find GameplayTag References", progress.DisplayPath, progress.Fraction))
                    .WithEnumerationErrors(files.Errors);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private GameplayTagReferenceSearchResult SearchTagReferences(string canonicalPath)
        {
            try
            {
                var files = GameplayTagProjectReferenceFiles.Enumerate();
                return new GameplayTagTextReferenceScanner(File.OpenText).SearchExact(
                    files.Files,
                    canonicalPath,
                    _controller.GameSourcePath,
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
        internal static GUIContent CreateRedirectContent(GameplayTagRedirectRowModel redirect)
        {
            var text = redirect.From + "  →  " + redirect.To;
            var tooltip = redirect.SourceDisplayName + " (" + redirect.SourceId + ")\n" + text;
            if (redirect.IsShadowed)
            {
                tooltip += "\n" + GameplayTagRedirectMaintenance.ShadowedRedirectWarning;
            }

            var content = new GUIContent(text, tooltip);
            if (redirect.IsShadowed)
            {
                content.image = EditorGUIUtility.IconContent("console.warnicon.sml").image;
            }

            return content;
        }

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

        private void CreateGameSource()
        {
            if (!TryPrepareForCatalogReplacement("Create Game Source")) return;
            Execute(_controller.CreateGameSource);
        }

        private void ImportExisting()
        {
            var path = EditorUtility.OpenFilePanel("Import Existing Gameplay Tag Source", "", "json");
            if (path.Length == 0 ||
                !TryPrepareForCatalogReplacement("Import Existing Gameplay Tag Source")) return;
            Execute(() => _controller.ImportExisting(path));
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

        private void RequestRename(GameplayTagTreeSelectionKey selection) =>
            ApplyRename(
                selection,
                GameplayTagEditDialog.ShowRename(selection.CanonicalPath));

        private void RequestComment(GameplayTagTreeSelectionKey selection) =>
            ApplyComment(
                selection,
                GameplayTagEditDialog.ShowComment(
                    selection.CanonicalPath,
                    FindComment(selection)));

        private void FindTagReferences(GameplayTagTreeSelectionKey selection) =>
            GameplayTagReferenceResultsWindow.Show(
                SearchTagReferences(selection.CanonicalPath));

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
            ApplyRename(new GameplayTagTreeSelectionKey("game", path), result);
        }

        private void ApplyRename(
            GameplayTagTreeSelectionKey selection,
            GameplayTagTextEditResult result)
        {
            if (!result.Accepted) return;
            GameplayTagRenameResult? rename = null;
            if (Execute(() => rename = _controller.RenameSubtree(
                    selection.SourceId,
                    selection.CanonicalPath,
                    result.Value)))
            {
                GameplayTagEditDialog.ShowShadowedRenameWarning(rename!);
            }
        }

        /// <summary>승인된 comment 결과를 적용하고 암시 부모를 명시 행으로 승격합니다.</summary>
        internal void ApplyComment(string path, GameplayTagTextEditResult result)
        {
            ApplyComment(new GameplayTagTreeSelectionKey("game", path), result);
        }

        private void ApplyComment(
            GameplayTagTreeSelectionKey selection,
            GameplayTagTextEditResult result)
        {
            if (result.Accepted)
            {
                Execute(() => _controller.SetComment(
                    selection.SourceId,
                    selection.CanonicalPath,
                    result.Value));
            }
        }

        private void DeleteSelected(GameplayTagTreeSelectionKey selection)
        {
            var result = SearchTagReferences(selection.CanonicalPath);
            TryDeleteSelected(
                selection,
                result,
                () => EditorUtility.DisplayDialog(
                    "Delete Gameplay Tag",
                    "Delete the exact declaration '" + selection.CanonicalPath + "'?",
                    "Delete Tag",
                    "Cancel"),
                GameplayTagReferenceResultsWindow.Show);
        }

        /// <summary>완전한 무참조 증거와 확인이 있을 때만 exact Source 선언을 삭제합니다.</summary>
        internal bool TryDeleteSelected(
            GameplayTagTreeSelectionKey selection,
            GameplayTagReferenceSearchResult result,
            Func<bool> confirmDelete,
            Action<GameplayTagReferenceSearchResult> showResults)
        {
            if (confirmDelete is null) throw new ArgumentNullException(nameof(confirmDelete));
            if (showResults is null) throw new ArgumentNullException(nameof(showResults));
            if (!result.IsComplete || result.Matches.Count > 0)
            {
                showResults(result);
                return false;
            }

            return confirmDelete()
                && Execute(() => _controller.DeleteExact(
                    selection.SourceId,
                    selection.CanonicalPath));
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

        private bool Execute(Action action)
        {
            if (_controller.TryExecute(action, out var error))
            {
                ReloadTree();
                SynchronizeUnsavedChanges();
                return true;
            }

            SynchronizeUnsavedChanges();
            ShowValidationError(error!);
            return false;
        }

        private void SynchronizeUnsavedChanges() => hasUnsavedChanges = _controller.IsDirty;

        private void RefreshWorkspaceOnEditorUpdate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling
                || EditorApplication.timeSinceStartup < _nextWorkspaceRefresh)
            {
                return;
            }

            _nextWorkspaceRefresh = EditorApplication.timeSinceStartup + 0.75d;
            try
            {
                if (!_controller.RefreshWorkspace()) return;
                ReloadTree();
                SynchronizeUnsavedChanges();
                Repaint();
            }
            catch (Exception exception)
            {
                ShowValidationError(exception);
            }
        }

        private void ShowValidationError(Exception error) =>
            (_showValidationError ?? GameplayTagValidationWindow.Show)(
                _controller.GameSourcePath,
                error);

        private void ShowConfigureWarning(Exception error) =>
            (_showConfigureWarning ?? GameplayTagDiagnosticsPanel.ShowWarning)(
                ConfigureCatalogTitle,
                error.Message);

        private void ReloadTree()
        {
            _treeView.CanEditGameSource = _controller.CanEditGameSource;
            if (_controller.Session is null)
            {
                _model = null;
                _redirectRows = Array.Empty<GameplayTagRedirectRowModel>();
                _treeView.SetRows(Array.Empty<GameplayTagTreeRowModel>(), isFiltering: false);
                _treeView.SynchronizeSelection(new GameplayTagTreeSelectionKey(string.Empty, string.Empty));
                return;
            }

            _model = _controller.Workspace.Snapshot is null
                ? new GameplayTagTreeModel(_controller.Session)
                : new GameplayTagTreeModel(_controller.Workspace.Snapshot);
            _redirectRows = GameplayTagRedirectMaintenance.CreateRows(_model.Snapshot);
            _treeView.SetRows(_model.Filter(_search), _search.Length > 0);
            _treeView.SynchronizeSelection(new GameplayTagTreeSelectionKey(
                _controller.SelectedSourceId,
                _controller.SelectedPath));
        }

        private void SelectTag(GameplayTagTreeSelectionKey selection)
        {
            _controller.Select(selection.SourceId, selection.CanonicalPath);
            Repaint();
        }

        private string FindComment(GameplayTagTreeSelectionKey selection)
        {
            if (_model is null) return string.Empty;
            var rows = _model.Filter(string.Empty);
            for (var index = 0; index < rows.Count; index++)
            {
                if (rows[index].SelectionKey.Equals(selection))
                {
                    return rows[index].Comment;
                }
            }

            return string.Empty;
        }

        private static bool HasEditableRedirects(
            IReadOnlyList<GameplayTagRedirectRowModel> redirects)
        {
            for (var index = 0; index < redirects.Count; index++)
            {
                if (!redirects[index].IsReadOnly) return true;
            }

            return false;
        }
    }
}
#pragma warning restore CS0618
