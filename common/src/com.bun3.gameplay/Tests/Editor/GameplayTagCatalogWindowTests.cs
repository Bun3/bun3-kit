#nullable enable
#pragma warning disable CS0618
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Bun3.Gameplay.Editor.Tags;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace Bun3.Gameplay.Unity.Tests
{
    /// <summary>게임플레이 태그 카탈로그 편집기 창의 동작을 검증합니다.</summary>
    [TestFixture]
    internal sealed class GameplayTagCatalogWindowTests
    {
        private string _temporaryDirectory = null!;

        /// <summary>테스트마다 격리된 임시 카탈로그 디렉터리를 준비합니다.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "bun3-tag-window-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
        }

        /// <summary>테스트가 만든 임시 카탈로그 디렉터리를 정리합니다.</summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, true);
            }
        }

        /// <summary>카탈로그를 로드하지 않아도 편집기 창이 열리는지 검증합니다.</summary>
        [Test]
        public void Window_opens_without_loading_a_catalog()
        {
            var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
            try
            {
                Assert.That(window.titleContent.text, Is.EqualTo("Gameplay Tags"));
            }
            finally
            {
                CloseWithoutSaving(window);
            }
        }

        /// <summary>트리 행의 주석이 레이블 도구 설명으로 노출되는지 검증합니다.</summary>
        [Test]
        public void Tree_row_label_exposes_the_comment_as_a_tooltip()
        {
            var row = new GameplayTagTreeRowModel(
                index: 2,
                parentIndex: 1,
                path: "State.Dead",
                comment: "전투 불능",
                isExplicit: true,
                directMatch: true);

            var content = GameplayTagTreeView.CreateLabelContent(row);

            Assert.That(content.text, Is.EqualTo("Dead"));
            Assert.That(content.tooltip, Is.EqualTo("전투 불능"));
        }

        /// <summary>트리 행 레이블이 foldout과 계층 들여쓰기 뒤에서 시작하는지 검증합니다.</summary>
        [Test]
        public void Tree_row_label_starts_after_the_foldout_indent()
        {
            var tree = new GameplayTagTreeView(new TreeViewState());
            var item = new TreeViewItem(id: 2, depth: 2, displayName: "Dead");
            var rowRect = new UnityEngine.Rect(12f, 8f, 240f, 18f);

            var labelRect = tree.CalculateLabelRect(item, rowRect);

            Assert.That(labelRect.xMin, Is.GreaterThan(rowRect.xMin));
            Assert.That(labelRect.xMax, Is.EqualTo(rowRect.xMax));
        }

        /// <summary>redirect 행이 전체 경로를 잘림 없이 표시하고 도구 설명으로도 노출하는지 검증합니다.</summary>
        [Test]
        public void Redirect_row_shows_the_full_path_pair_as_text_and_tooltip()
        {
            var content = GameplayTagCatalogWindow.CreateRedirectContent(
                new EditableRedirectRow("State.Movement.Sprinting", "State.Movement.Running"));

            Assert.That(content.text,
                Is.EqualTo("State.Movement.Sprinting  →  State.Movement.Running"));
            Assert.That(content.tooltip, Is.EqualTo(content.text));
        }

        /// <summary>선택한 후보만 제거하고 창이 dirty가 되는지 검증합니다.</summary>
        [Test]
        public void Bulk_cleanup_removes_only_selected_sources_and_marks_the_window_dirty()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            File.WriteAllText(path,
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]," +
                "\"redirects\":[{\"from\":\"State.Killed\",\"to\":\"State.Dead\"}," +
                "{\"from\":\"State.Gone\",\"to\":\"State.Dead\"}]}");
            var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
            try
            {
                var controller = GetController(window);
                controller.Open(path);
                var result = GameplayTagReferenceSearchResult.Complete(
                    Array.Empty<GameplayTagReferenceMatch>());

                var applied = window.TryApplyBulkCleanup(
                    result,
                    candidates => new[] { candidates.Single(source => source == "State.Gone") });

                Assert.That(applied, Is.True);
                Assert.That(controller.IsDirty, Is.True);
                Assert.That(controller.Session!.Serialize(), Does.Contain("State.Killed"));
                Assert.That(controller.Session.Serialize(), Does.Not.Contain("State.Gone"));
            }
            finally
            {
                CloseWithoutSaving(window);
            }
        }

        /// <summary>불완전한 검색이 정리 selector를 열지 않는지 검증합니다.</summary>
        [Test]
        public void Incomplete_bulk_scan_never_opens_the_cleanup_selector()
        {
            var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
            var selectorCalls = 0;
            try
            {
                var applied = window.TryApplyBulkCleanup(
                    GameplayTagReferenceSearchResult.Incomplete(true, Array.Empty<string>()),
                    candidates =>
                    {
                        selectorCalls++;
                        return candidates;
                    });

                Assert.That(applied, Is.False);
                Assert.That(selectorCalls, Is.Zero);
            }
            finally
            {
                CloseWithoutSaving(window);
            }
        }

        /// <summary>후보를 하나도 고르지 않으면 세션을 dirty로 만들지 않는지 검증합니다.</summary>
        [Test]
        public void Bulk_cleanup_without_a_selection_leaves_the_session_clean()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            File.WriteAllText(path,
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]," +
                "\"redirects\":[{\"from\":\"State.Killed\",\"to\":\"State.Dead\"}]}");
            var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
            try
            {
                var controller = GetController(window);
                controller.Open(path);

                var applied = window.TryApplyBulkCleanup(
                    GameplayTagReferenceSearchResult.Complete(
                        Array.Empty<GameplayTagReferenceMatch>()),
                    _ => Array.Empty<string>());

                Assert.That(applied, Is.False);
                Assert.That(controller.IsDirty, Is.False);
                Assert.That(controller.Session!.Serialize(), Does.Contain("State.Killed"));
            }
            finally
            {
                CloseWithoutSaving(window);
            }
        }

        /// <summary>검색 확장이 임시이고 일반 확장 상태와 스크롤 위치가 복원되는지 검증합니다.</summary>
        [Test]
        public void Search_expansion_is_temporary_and_normal_expansion_and_scroll_are_restored()
        {
            var state = new TreeViewState { scrollPos = new UnityEngine.Vector2(23f, 47f) };
            var tree = new GameplayTagTreeView(state);
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead.Ghost\"},{\"name\":\"Ability.Jump\"}]}");
            var model = new GameplayTagTreeModel(session);
            var abilityId = model.Rows.Single(row => row.Path == "Ability").Index;
            var stateId = model.Rows.Single(row => row.Path == "State").Index;
            var deadId = model.Rows.Single(row => row.Path == "State.Dead").Index;
            tree.SetRows(model.Rows, isFiltering: false);
            tree.SetExpanded(abilityId, true);
            tree.SetExpanded(stateId, false);

            tree.SetRows(model.Filter("Ghost"), isFiltering: true);
            Assert.That(tree.IsExpanded(stateId), Is.True);
            Assert.That(tree.IsExpanded(deadId), Is.True);

            tree.SetRows(model.Rows, isFiltering: false);
            Assert.That(tree.IsExpanded(abilityId), Is.True);
            Assert.That(tree.IsExpanded(stateId), Is.False);
            Assert.That(state.scrollPos, Is.EqualTo(new UnityEngine.Vector2(23f, 47f)));
        }

        /// <summary>선택 동기화가 접힌 조상만 펼쳐 선택 행을 드러내는지 검증합니다.</summary>
        [Test]
        public void Synchronize_selection_expands_the_collapsed_ancestors_of_the_selected_tag()
        {
            var tree = new GameplayTagTreeView(new TreeViewState());
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead.Ghost\"},{\"name\":\"Ability.Jump\"}]}");
            var model = new GameplayTagTreeModel(session);
            var abilityId = model.Rows.Single(row => row.Path == "Ability").Index;
            var stateId = model.Rows.Single(row => row.Path == "State").Index;
            var deadId = model.Rows.Single(row => row.Path == "State.Dead").Index;
            tree.SetRows(model.Rows, isFiltering: false);
            tree.SetExpanded(abilityId, false);
            tree.SetExpanded(stateId, false);
            tree.SetExpanded(deadId, false);

            tree.SynchronizeSelection("state.dead.ghost");

            Assert.That(tree.IsExpanded(stateId), Is.True);
            Assert.That(tree.IsExpanded(deadId), Is.True);
            Assert.That(tree.IsExpanded(abilityId), Is.False);
        }

        /// <summary>암시 행과 명시 행 모두가 요청한 context action을 전달하는지 검증합니다.</summary>
        /// <param name="action">요청할 트리 context action입니다.</param>
        [TestCase(GameplayTagTreeAction.Rename)]
        [TestCase(GameplayTagTreeAction.EditComment)]
        [TestCase(GameplayTagTreeAction.AddSubTag)]
        [TestCase(GameplayTagTreeAction.Copy)]
        [TestCase(GameplayTagTreeAction.Delete)]
        public void Every_tree_row_dispatches_the_requested_context_action(GameplayTagTreeAction action)
        {
            foreach (var isExplicit in new[] { false, true })
            {
                var tree = new GameplayTagTreeView(new TreeViewState());
                var row = new GameplayTagTreeRowModel(1, 0, "State", "", isExplicit, false);
                tree.SetRows(new[] { row }, isFiltering: false);
                string? received = null;
                Subscribe(tree, action, path => received = path);

                tree.RequestAction(action, row.Index);

                Assert.That(received, Is.EqualTo("State"));
            }
        }

        private static void Subscribe(
            GameplayTagTreeView tree,
            GameplayTagTreeAction action,
            Action<string> handler)
        {
            switch (action)
            {
                case GameplayTagTreeAction.Rename: tree.RenameRequested += handler; break;
                case GameplayTagTreeAction.EditComment: tree.CommentEditRequested += handler; break;
                case GameplayTagTreeAction.AddSubTag: tree.SubTagRequested += handler; break;
                case GameplayTagTreeAction.Copy: tree.CopyRequested += handler; break;
                case GameplayTagTreeAction.Delete: tree.DeleteRequested += handler; break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        /// <summary>태그 편집기가 새 Gameplay 메뉴 경로를 사용하는지 검증합니다.</summary>
        [Test]
        public void Tag_editor_uses_the_gameplay_menu_path()
        {
            var method = typeof(GameplayTagCatalogWindow).GetMethod(
                nameof(GameplayTagCatalogWindow.OpenWindow), BindingFlags.Public | BindingFlags.Static)!;
            var menu = method.GetCustomAttribute<MenuItem>()!;
            Assert.That(menu.menuItem, Is.EqualTo("Gameplay/Tag Editor"));
        }

        /// <summary>이름 변경 요청이 읽기 전용 부모와 편집 가능한 세그먼트를 분리하는지 검증합니다.</summary>
        /// <param name="path">이름을 바꿀 전체 태그 경로입니다.</param>
        /// <param name="expectedParent">예상하는 읽기 전용 부모 경로입니다.</param>
        /// <param name="expectedSegment">예상하는 편집 가능한 마지막 세그먼트입니다.</param>
        [TestCase("State", "", "State")]
        [TestCase("State.Movement.Run", "State.Movement", "Run")]
        public void Rename_dialog_request_separates_the_readonly_parent_and_editable_segment(
            string path, string expectedParent, string expectedSegment)
        {
            var request = GameplayTagEditDialog.CreateRenameRequest(path);
            Assert.That(request.ParentPath, Is.EqualTo(expectedParent));
            Assert.That(request.InitialValue, Is.EqualTo(expectedSegment));
        }

        /// <summary>Add Sub-Tag가 입력만 채우고 Copy Tag가 전체 경로를 복사하는지 검증합니다.</summary>
        [Test]
        public void Add_sub_tag_only_prefills_the_add_form_and_copy_uses_the_full_path()
        {
            var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
            var previousClipboard = EditorGUIUtility.systemCopyBuffer;
            try
            {
                window.PrepareSubTag("State.Movement");
                Assert.That(GetPrivateString(window, "_newTagName"), Is.EqualTo("State.Movement."));
                Assert.That(GetController(window).Session, Is.Null);

                window.CopyTag("State.Movement");
                Assert.That(EditorGUIUtility.systemCopyBuffer, Is.EqualTo("State.Movement"));
            }
            finally
            {
                EditorGUIUtility.systemCopyBuffer = previousClipboard;
                CloseWithoutSaving(window);
            }
        }

        /// <summary>승인된 comment 편집이 암시 부모를 명시 작성 행으로 승격하는지 검증합니다.</summary>
        /// <param name="comment">적용할 comment 값입니다.</param>
        [TestCase("")]
        [TestCase("상태 루트")]
        public void Accepted_comment_edit_promotes_an_implicit_parent(string comment)
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
            try
            {
                var controller = GetController(window);
                controller.New(path);
                controller.Add("State.Dead");

                window.ApplyComment("State", GameplayTagTextEditResult.Accept(comment));

                Assert.That(controller.Session!.Tags.Any(row => row.Name == "State"), Is.True);
                Assert.That(controller.Session.Tags.Single(row => row.Name == "State").Comment,
                    Is.EqualTo(comment));
            }
            finally
            {
                CloseWithoutSaving(window);
            }
        }

        /// <summary>거부된 편집 결과가 세션을 전혀 바꾸지 않는지 검증합니다.</summary>
        [Test]
        public void Cancelled_edit_results_leave_the_session_untouched()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
            try
            {
                var controller = GetController(window);
                controller.New(path);
                controller.Add("State.Dead");
                var before = controller.Session!.Serialize();

                window.ApplyComment("State", GameplayTagTextEditResult.Cancelled);
                window.ApplyRename("State.Dead", GameplayTagTextEditResult.Cancelled);

                Assert.That(controller.Session!.Serialize(), Is.EqualTo(before));
            }
            finally
            {
                CloseWithoutSaving(window);
            }
        }

        /// <summary>승인된 이름 변경이 마지막 세그먼트만 바꾸고 선택을 새 경로로 옮기는지 검증합니다.</summary>
        [Test]
        public void Accepted_rename_changes_only_the_last_segment()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
            try
            {
                var controller = GetController(window);
                controller.New(path);
                controller.Add("State.Movement.Run");

                window.ApplyRename("State.Movement", GameplayTagTextEditResult.Accept("Motion"));

                Assert.That(controller.Session!.Serialize(), Does.Contain("State.Motion.Run"));
                Assert.That(controller.SelectedPath, Is.EqualTo("State.Motion"));
            }
            finally
            {
                CloseWithoutSaving(window);
            }
        }

        /// <summary>트리 context action event가 창에 정확히 한 번만 연결되는지 검증합니다.</summary>
        [Test]
        public void Tree_context_actions_are_wired_to_the_window_exactly_once()
        {
            var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
            try
            {
                var previousClipboard = EditorGUIUtility.systemCopyBuffer;
                try
                {
                    var controller = GetController(window);
                    controller.New(Path.Combine(_temporaryDirectory, "GameplayTags.json"));
                    controller.Add("State.Dead");
                    RefreshTree(window);
                    var tree = GetTree(window);
                    var row = new GameplayTagTreeModel(controller.Session!).Filter(string.Empty)
                        .Single(candidate => candidate.Path == "State.Dead");

                    tree.RequestAction(GameplayTagTreeAction.AddSubTag, row.Index);
                    Assert.That(GetPrivateString(window, "_newTagName"), Is.EqualTo("State.Dead."));

                    tree.RequestAction(GameplayTagTreeAction.Copy, row.Index);
                    Assert.That(EditorGUIUtility.systemCopyBuffer, Is.EqualTo("State.Dead"));
                }
                finally
                {
                    EditorGUIUtility.systemCopyBuffer = previousClipboard;
                }
            }
            finally
            {
                CloseWithoutSaving(window);
            }
        }

        /// <summary>leaf와 subtree 삭제가 서로 다른 확인 대화 상자를 한 번만 열고 취소를 전파하는지 검증합니다.</summary>
        /// <param name="hasDescendants">자식 태그 존재 여부입니다.</param>
        /// <param name="expectedTitle">예상하는 대화 상자 제목입니다.</param>
        /// <param name="expectedMessage">예상하는 대화 상자 메시지입니다.</param>
        /// <param name="expectedConfirm">예상하는 확인 버튼 문구입니다.</param>
        [TestCase(false, "Delete Gameplay Tag", "Delete the selected gameplay tag?", "Delete Tag")]
        [TestCase(
            true,
            "Delete Gameplay Tag Subtree",
            "The selected tag has descendants. Delete the full subtree?",
            "Delete Subtree")]
        public void Delete_confirmation_invokes_the_matching_dialog_once_and_honors_cancel(
            bool hasDescendants,
            string expectedTitle,
            string expectedMessage,
            string expectedConfirm)
        {
            var invocationCount = 0;

            var confirmed = GameplayTagCatalogWindow.ConfirmDelete(
                hasDescendants,
                (title, message, confirm, cancel) =>
                {
                    invocationCount++;
                    Assert.That(title, Is.EqualTo(expectedTitle));
                    Assert.That(message, Is.EqualTo(expectedMessage));
                    Assert.That(confirm, Is.EqualTo(expectedConfirm));
                    Assert.That(cancel, Is.EqualTo("Cancel"));
                    return false;
                });

            Assert.That(confirmed, Is.False);
            Assert.That(invocationCount, Is.EqualTo(1));
        }

        [TestCase(0, 0)]
        [TestCase(1, 2)]
        [TestCase(2, 1)]
        public void Unsaved_dialog_result_maps_to_the_matching_decision(
            int dialogResult, int expectedDecision)
        {
            Assert.That((int)GameplayTagCatalogWindow.MapUnsavedChangesDialogResult(dialogResult),
                Is.EqualTo(expectedDecision));
        }

        [Test]
        public void Save_decision_persists_the_real_dirty_session_before_replacement()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var controller = new GameplayTagCatalogWindowController();
            controller.New(path);
            controller.Add("State.Dead");

            var proceed = GameplayTagCatalogWindow.TryResolveUnsavedChanges(
                UnsavedChangesDecision.Save,
                () => controller.TryExecute(controller.Save, out _));

            Assert.That(proceed, Is.True);
            Assert.That(controller.IsDirty, Is.False);
            Assert.That(File.ReadAllText(path), Does.Contain("State.Dead"));
        }

        [TestCase(1, true)]
        [TestCase(2, false)]
        public void Discard_proceeds_and_cancel_preserves_the_dirty_session(
            int decisionValue, bool expectedProceed)
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var controller = new GameplayTagCatalogWindowController();
            controller.New(path);
            controller.Add("State.Dead");

            var proceed = GameplayTagCatalogWindow.TryResolveUnsavedChanges(
                (UnsavedChangesDecision)decisionValue,
                () => controller.TryExecute(controller.Save, out _));

            Assert.That(proceed, Is.EqualTo(expectedProceed));
            Assert.That(controller.IsDirty, Is.True);
            Assert.That(File.ReadAllText(path), Does.Not.Contain("State.Dead"));
            Assert.That(controller.Session!.Serialize(), Does.Contain("State.Dead"));
        }

        [Test]
        public void Failed_save_does_not_allow_catalog_replacement()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var controller = new GameplayTagCatalogWindowController();
            controller.New(path);
            controller.Add("State.Dead");
            File.Delete(path);
            Directory.CreateDirectory(path);

            var proceed = GameplayTagCatalogWindow.TryResolveUnsavedChanges(
                UnsavedChangesDecision.Save,
                () => controller.TryExecute(controller.Save, out _));

            Assert.That(proceed, Is.False);
            Assert.That(controller.IsDirty, Is.True);
            Assert.That(controller.Session!.Serialize(), Does.Contain("State.Dead"));
        }

        [Test]
        public void Synchronizing_a_dirty_controller_marks_the_window_unsaved()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
            try
            {
                var controller = GetController(window);
                controller.New(path);
                controller.Add("State.Dead");
                SynchronizeUnsavedChanges(window);

                Assert.That(window.hasUnsavedChanges, Is.True);
                Assert.That(window.saveChangesMessage, Does.Contain("gameplay tag catalog"));
            }
            finally
            {
                CloseWithoutSaving(window);
            }
        }

        [TestCase(0, true)]
        [TestCase(1, false)]
        public void Assembly_reload_resolves_the_real_dirty_session(
            int decisionValue, bool expectedSaved)
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
            try
            {
                var controller = GetController(window);
                controller.New(path);
                controller.Add("State.Dead");
                window.HandleBeforeAssemblyReload((UnsavedChangesDecision)decisionValue);

                Assert.That(File.ReadAllText(path).Contains("State.Dead"), Is.EqualTo(expectedSaved));
                Assert.That(controller.IsDirty, Is.False);
                Assert.That(window.hasUnsavedChanges, Is.False);
            }
            finally
            {
                CloseWithoutSaving(window);
            }
        }

        /// <summary>컨트롤러가 세션을 우회하지 않고 파일 및 작성 작업을 수행하는지 검증합니다.</summary>
        [Test]
        public void Controller_executes_file_and_authoring_workflow_without_bypassing_the_session()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var controller = new GameplayTagCatalogWindowController();
            controller.New(path);
            controller.Add("State.Dead", "사망");
            controller.SetComment("State.Dead", "전투 불능");
            controller.RenameSubtree("State.Dead", "Deceased");
            Assert.That(controller.IsDirty, Is.True);
            Assert.That(controller.SelectedPath, Is.EqualTo("State.Deceased"));
            controller.Save();
            Assert.That(controller.IsDirty, Is.False);
            Assert.That(File.ReadAllText(path), Does.Contain("State.Deceased"));

            controller.Add("State.Stunned");
            Assert.That(controller.Reload(discardDirty: false), Is.False);
            Assert.That(controller.Session!.Serialize(), Does.Contain("State.Stunned"));
            Assert.That(controller.Reload(discardDirty: true), Is.True);
            Assert.That(controller.Session!.Serialize(), Does.Not.Contain("State.Stunned"));
            controller.Delete("State.Deceased", includeDescendants: false);
            Assert.That(controller.Session!.Serialize(), Does.Not.Contain("State.Deceased"));
        }

        /// <summary>컨트롤러가 세그먼트 rename의 전체 반환 경로를 선택하고 dirty 상태가 되는지 검증합니다.</summary>
        [Test]
        public void Controller_selects_the_full_path_returned_by_segment_rename()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var controller = new GameplayTagCatalogWindowController();
            controller.New(path);
            controller.Add("State.Dead");

            controller.RenameSubtree("State.Dead", "Deceased");

            Assert.That(controller.SelectedPath, Is.EqualTo("State.Deceased"));
            Assert.That(controller.IsDirty, Is.True);
        }

        /// <summary>컨트롤러가 redirect 제거 수에 따라 dirty 상태를 바꾸고 결과를 저장하는지 검증합니다.</summary>
        [Test]
        public void Controller_removes_redirects_marks_dirty_and_persists_the_result()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            File.WriteAllText(path,
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]," +
                "\"redirects\":[{\"from\":\"State.Killed\",\"to\":\"State.Dead\"}]}",
                new UTF8Encoding(false, true));
            var controller = new GameplayTagCatalogWindowController();
            controller.Open(path);
            controller.Select("State.Dead");

            Assert.That(controller.RemoveRedirects(Array.Empty<string>()), Is.Zero);
            Assert.That(controller.IsDirty, Is.False);
            Assert.That(controller.RemoveRedirects(new[] { "State.Killed" }), Is.EqualTo(1));
            Assert.That(controller.IsDirty, Is.True);
            Assert.That(controller.SelectedPath, Is.EqualTo("State.Dead"));
            controller.Save();

            Assert.That(controller.IsDirty, Is.False);
            Assert.That(File.ReadAllText(path), Does.Not.Contain("State.Killed"));
        }

        /// <summary>실패한 명령이 상태를 보존하고 검증 진단을 만드는지 검증합니다.</summary>
        [Test]
        public void Failed_command_preserves_state_and_produces_validation_diagnostics()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            File.WriteAllText(path,
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"},{\"name\":\"State.Alive\"}]}",
                new UTF8Encoding(false, true));
            var controller = new GameplayTagCatalogWindowController();
            controller.Open(path);
            var before = controller.Session!.Serialize();

            var succeeded = controller.TryExecute(
                () => controller.RenameSubtree("State.Dead", "Alive"), out var error);

            Assert.That(succeeded, Is.False);
            Assert.That(error, Is.Not.Null);
            Assert.That(controller.Session!.Serialize(), Is.EqualTo(before));
            var diagnostic = GameplayTagValidationWindow.FormatDiagnostic(path, error!);
            Assert.That(diagnostic, Does.Contain(path));
            Assert.That(diagnostic, Does.Contain("The destination path is already active."));
        }

        /// <summary>카탈로그 예외가 없는 일반 예외는 최상위 메시지를 표시하는지 검증합니다.</summary>
        [Test]
        public void Validation_diagnostic_preserves_generic_top_level_error_message()
        {
            var diagnostic = GameplayTagValidationWindow.FormatDiagnostic(
                "GameplayTags.json",
                new InvalidOperationException("Generic editor failure."));

            Assert.That(diagnostic, Is.EqualTo("GameplayTags.json\nGeneric editor failure."));
        }

        /// <summary>명령이 세션을 변경한 뒤 실패해도 직렬화된 상태와 컨트롤러 상태를 복원하는지 검증합니다.</summary>
        [Test]
        public void TryExecute_restores_a_fresh_session_after_a_partially_applied_command()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var controller = new GameplayTagCatalogWindowController();
            controller.New(path);
            controller.Add("State.Dead");
            controller.Save();
            var sessionBefore = controller.Session;
            var serializedBefore = sessionBefore!.Serialize();
            var filePathBefore = controller.FilePath;
            var selectedPathBefore = controller.SelectedPath;
            var dirtyBefore = controller.IsDirty;

            var succeeded = controller.TryExecute(
                () =>
                {
                    controller.Add("State.Alive");
                    throw new InvalidOperationException("Command failed after mutation.");
                },
                out var error);

            Assert.That(succeeded, Is.False);
            Assert.That(error, Is.TypeOf<InvalidOperationException>());
            Assert.That(controller.Session, Is.Not.SameAs(sessionBefore));
            Assert.That(controller.Session!.Serialize(), Is.EqualTo(serializedBefore));
            Assert.That(controller.FilePath, Is.EqualTo(filePathBefore));
            Assert.That(controller.SelectedPath, Is.EqualTo(selectedPathBefore));
            Assert.That(controller.IsDirty, Is.EqualTo(dirtyBefore));
        }

        /// <summary>검증 진단에 JSON 경로, 줄, 위치가 포함되는지 검증합니다.</summary>
        [Test]
        public void Validation_diagnostic_includes_json_path_line_and_position()
        {
            const string invalid =
                "{\n  \"schemaVersion\":1,\n  \"tags\":[{\"name\":\"State_Bad\"}]\n}";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(invalid));
            var error = Assert.Throws<Bun3.Gameplay.Tags.TagCatalogException>(
                () => Bun3.Gameplay.Tags.TagCatalog.Load(stream));

            var diagnostic = GameplayTagValidationWindow.FormatDiagnostic("GameplayTags.json", error!);

            Assert.That(diagnostic, Does.Contain("tags[0].name"));
            Assert.That(diagnostic, Does.Contain(error!.LineNumber.ToString()));
            Assert.That(diagnostic, Does.Contain(error.LinePosition.ToString()));
        }

        /// <summary>작성 명령 뒤 트리가 컨트롤러가 선택한 새 경로를 선택하는지 검증합니다.</summary>
        [Test]
        public void Reload_tree_selects_controller_path_after_add_and_rename()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
            try
            {
                var controller = GetController(window);
                controller.New(path);
                controller.Add("State.Dead");
                RefreshTree(window);
                SetTreeSelection(window, controller, "State.Dead");

                controller.Add("State.Dead.Ghost");
                RefreshTree(window);

                AssertTreeSelection(window, "State.Dead.Ghost");
                Assert.That(controller.SelectedPath, Is.EqualTo("State.Dead.Ghost"));

                controller.RenameSubtree("State.Dead", "Deceased");
                RefreshTree(window);

                AssertTreeSelection(window, "State.Deceased");
                Assert.That(controller.SelectedPath, Is.EqualTo("State.Deceased"));
            }
            finally
            {
                CloseWithoutSaving(window);
            }
        }

        /// <summary>카탈로그 교체 뒤 트리가 남아 있던 시각 선택을 비우는지 검증합니다.</summary>
        /// <param name="operation">실행할 카탈로그 교체 명령입니다.</param>
        [TestCase("New")]
        [TestCase("Open")]
        [TestCase("Reload")]
        public void Reload_tree_clears_stale_selection_after_catalog_replacement(string operation)
        {
            var initialPath = Path.Combine(_temporaryDirectory, "InitialGameplayTags.json");
            var replacementPath = Path.Combine(_temporaryDirectory, "ReplacementGameplayTags.json");
            var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
            try
            {
                var controller = GetController(window);
                controller.New(initialPath);
                controller.Add("State.Dead");
                controller.Save();
                RefreshTree(window);
                SetTreeSelection(window, controller, "State.Dead");

                switch (operation)
                {
                    case "New":
                        controller.New(replacementPath);
                        break;
                    case "Open":
                        GameplayTagCatalogFileAdapter.Save(
                            replacementPath,
                            GameplayTagCatalogEditSession.Open(
                                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"Ability.Jump\"}]}"));
                        controller.Open(replacementPath);
                        break;
                    case "Reload":
                        Assert.That(controller.Reload(discardDirty: true), Is.True);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(operation));
                }

                RefreshTree(window);

                Assert.That(GetTree(window).GetSelection(), Is.Empty);
                Assert.That(controller.SelectedPath, Is.Empty);
            }
            finally
            {
                CloseWithoutSaving(window);
            }
        }

        private static string GetPrivateString(GameplayTagCatalogWindow window, string fieldName)
        {
            var field = typeof(GameplayTagCatalogWindow).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The expected Window field is missing.");
            return (string)(field.GetValue(window)
                ?? throw new InvalidOperationException("The expected Window value is missing."));
        }

        private static void CloseWithoutSaving(GameplayTagCatalogWindow window)
        {
            if (window.hasUnsavedChanges) window.DiscardChanges();
            window.Close();
        }

        private static void SynchronizeUnsavedChanges(GameplayTagCatalogWindow window)
        {
            var method = typeof(GameplayTagCatalogWindow).GetMethod(
                "SynchronizeUnsavedChanges", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The unsaved state synchronizer is missing.");
            method.Invoke(window, null);
        }

        private static GameplayTagCatalogWindowController GetController(GameplayTagCatalogWindow window)
        {
            var field = typeof(GameplayTagCatalogWindow).GetField(
                "_controller", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The catalog window controller field is missing.");
            return (GameplayTagCatalogWindowController)(field.GetValue(window)
                ?? throw new InvalidOperationException("The catalog window controller is missing."));
        }

        private static GameplayTagTreeView GetTree(GameplayTagCatalogWindow window)
        {
            var field = typeof(GameplayTagCatalogWindow).GetField(
                "_treeView", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The catalog window tree field is missing.");
            return (GameplayTagTreeView)(field.GetValue(window)
                ?? throw new InvalidOperationException("The catalog window tree is missing."));
        }

        private static void RefreshTree(GameplayTagCatalogWindow window)
        {
            var method = typeof(GameplayTagCatalogWindow).GetMethod(
                "ReloadTree", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The catalog window reload method is missing.");
            method.Invoke(window, null);
        }

        private static void SetTreeSelection(
            GameplayTagCatalogWindow window,
            GameplayTagCatalogWindowController controller,
            string path)
        {
            var row = new GameplayTagTreeModel(controller.Session!).Filter(string.Empty)
                .Single(candidate => string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
            GetTree(window).SetSelection(new[] { (int)row.Index }, TreeViewSelectionOptions.None);
            controller.Select(path);
        }

        private static void AssertTreeSelection(GameplayTagCatalogWindow window, string expectedPath)
        {
            var tree = GetTree(window);
            var selectedIds = tree.GetSelection().ToArray();
            Assert.That(selectedIds.Length, Is.EqualTo(1));
            Assert.That(tree.TryGetPath(selectedIds[0], out var actualPath), Is.True);
            Assert.That(actualPath, Is.EqualTo(expectedPath));
        }
    }
}
#pragma warning restore CS0618
