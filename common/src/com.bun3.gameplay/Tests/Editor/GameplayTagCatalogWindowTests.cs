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
            Assert.That(diagnostic, Does.Contain(error!.Message));
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
