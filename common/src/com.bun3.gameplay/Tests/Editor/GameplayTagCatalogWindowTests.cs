#nullable enable
using System;
using System.IO;
using System.Text;
using Bun3.Gameplay.Editor.Tags;
using NUnit.Framework;
using UnityEditor;

namespace Bun3.Gameplay.Unity.Tests
{
    [TestFixture]
    public sealed class GameplayTagCatalogWindowTests
    {
        private string _temporaryDirectory = null!;

        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "bun3-tag-window-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, true);
            }
        }

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

        [Test]
        public void Tree_row_label_exposes_the_comment_as_a_tooltip()
        {
            var row = new GameplayTagTreeRowModel(
                index: 2, parentIndex: 1, path: "State.Dead", comment: "전투 불능", directMatch: true);

            var content = GameplayTagTreeView.CreateLabelContent(row);

            Assert.That(content.text, Is.EqualTo("Dead"));
            Assert.That(content.tooltip, Is.EqualTo("전투 불능"));
        }

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
    }
}
