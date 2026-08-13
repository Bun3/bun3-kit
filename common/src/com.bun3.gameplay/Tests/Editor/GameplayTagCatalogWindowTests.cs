#nullable enable
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
    public sealed class GameplayTagCatalogWindowTests
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
                window.Close();
            }
        }

        /// <summary>트리 행의 주석이 레이블 도구 설명으로 노출되는지 검증합니다.</summary>
        [Test]
        public void Tree_row_label_exposes_the_comment_as_a_tooltip()
        {
            var row = new GameplayTagTreeRowModel(
                index: 2, parentIndex: 1, path: "State.Dead", comment: "전투 불능", directMatch: true);

            var content = GameplayTagTreeView.CreateLabelContent(row);

            Assert.That(content.text, Is.EqualTo("Dead"));
            Assert.That(content.tooltip, Is.EqualTo("전투 불능"));
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

        [TestCase(0, UnsavedChangesDecision.Save)]
        [TestCase(1, UnsavedChangesDecision.Cancel)]
        [TestCase(2, UnsavedChangesDecision.Discard)]
        public void Unsaved_dialog_result_maps_to_the_matching_decision(
            int dialogResult, UnsavedChangesDecision expected)
        {
            Assert.That(GameplayTagCatalogWindow.MapUnsavedChangesDialogResult(dialogResult),
                Is.EqualTo(expected));
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

        [TestCase(UnsavedChangesDecision.Discard, true)]
        [TestCase(UnsavedChangesDecision.Cancel, false)]
        public void Discard_proceeds_and_cancel_preserves_the_dirty_session(
            UnsavedChangesDecision decision, bool expectedProceed)
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var controller = new GameplayTagCatalogWindowController();
            controller.New(path);
            controller.Add("State.Dead");

            var proceed = GameplayTagCatalogWindow.TryResolveUnsavedChanges(
                decision, () => controller.TryExecute(controller.Save, out _));

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
            Directory.Delete(_temporaryDirectory, true);

            var proceed = GameplayTagCatalogWindow.TryResolveUnsavedChanges(
                UnsavedChangesDecision.Save,
                () => controller.TryExecute(controller.Save, out _));

            Assert.That(proceed, Is.False);
            Assert.That(controller.IsDirty, Is.True);
            Assert.That(controller.Session!.Serialize(), Does.Contain("State.Dead"));
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
            controller.RelocateSubtree("State.Dead", "Condition.Deceased");
            Assert.That(controller.IsDirty, Is.True);
            Assert.That(controller.SelectedPath, Is.EqualTo("Condition.Deceased"));
            controller.Save();
            Assert.That(controller.IsDirty, Is.False);
            Assert.That(File.ReadAllText(path), Does.Contain("Condition.Deceased"));

            controller.Add("Condition.Stunned");
            Assert.That(controller.Reload(discardDirty: false), Is.False);
            Assert.That(controller.Session!.Serialize(), Does.Contain("Condition.Stunned"));
            Assert.That(controller.Reload(discardDirty: true), Is.True);
            Assert.That(controller.Session!.Serialize(), Does.Not.Contain("Condition.Stunned"));
            controller.Delete("Condition.Deceased", includeDescendants: false);
            Assert.That(controller.Session!.Serialize(), Does.Not.Contain("Condition.Deceased"));
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
                () => controller.RelocateSubtree("State.Dead", "State.Alive"), out var error);

            Assert.That(succeeded, Is.False);
            Assert.That(error, Is.Not.Null);
            Assert.That(controller.Session!.Serialize(), Is.EqualTo(before));
            var diagnostic = GameplayTagValidationWindow.FormatDiagnostic(path, error!);
            Assert.That(diagnostic, Does.Contain(path));
            Assert.That(diagnostic, Does.Contain("JSON path: tags[1].name"));
            Assert.That(diagnostic, Does.Contain("Line: 9, Position: 27"));
            Assert.That(diagnostic, Does.Contain("대소문자를 제외하고 중복된 태그 이름입니다."));
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
        public void Reload_tree_selects_controller_path_after_add_and_move()
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

                controller.RelocateSubtree("State.Dead", "Condition.Deceased");
                RefreshTree(window);

                AssertTreeSelection(window, "Condition.Deceased");
                Assert.That(controller.SelectedPath, Is.EqualTo("Condition.Deceased"));
            }
            finally
            {
                window.Close();
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
                window.Close();
            }
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
            var row = new GameplayTagCatalogViewModel(controller.Session!).Filter(string.Empty)
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
