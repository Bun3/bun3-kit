#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using Bun3.Gameplay.TagSource.Tasks;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;

namespace Bun3.Gameplay.Tags.Cli.Tests;

[TestFixture]
public sealed class GameplayTagCliTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "bun3-tags-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test]
    public void Development_compile_reads_fixed_game_source_merges_metadata_and_writes_os_cache()
    {
        var projectRoot = CreateProject(GameJson(
            "game.only", "shared.path"));
        var first = Write("first.json", MetadataJson("package.one", "Package One", "packageJson", "package.one", "shared.path"));
        var second = Write("second.json", MetadataJson("native.two", "Native Two", "native", "native.two"));
        var catalogId = "cli-test-" + Guid.NewGuid().ToString("N");
        var output = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bun3", "GameplayTags", catalogId, "dev", "GameplayTags.catalog");

        try
        {
            var result = Run("compile", "--development", "--catalog-id", catalogId,
                "--project-root", projectRoot, "--source", second, "--source", first);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Stderr);
                Assert.That(File.Exists(output), Is.True);
                Assert.That(ReadCatalog(output, catalogId, "0.0.0-dev").Count, Is.EqualTo(8));
            });
        }
        finally
        {
            var catalogDirectory = Path.GetDirectoryName(output)!;
            if (Directory.Exists(catalogDirectory)) Directory.Delete(catalogDirectory, true);
        }
    }

    [Test]
    public void Failed_development_compile_preserves_previous_good_catalog()
    {
        var projectRoot = CreateProject(GameJson("state.ready"));
        var catalogId = "cli-test-" + Guid.NewGuid().ToString("N");
        var output = Bun3.Gameplay.Tags.TagCatalogDevelopmentPath.Get(catalogId);

        try
        {
            var good = Run("compile", "--development", "--catalog-id", catalogId, "--project-root", projectRoot);
            var previous = File.ReadAllBytes(output);
            File.WriteAllText(Path.Combine(projectRoot, "ProjectSettings", "GameplayTags.json"),
                GameJsonWithMissingRedirect(), new UTF8Encoding(false));

            var failed = Run("compile", "--development", "--catalog-id", catalogId, "--project-root", projectRoot);

            Assert.Multiple(() =>
            {
                Assert.That(good.ExitCode, Is.Zero, good.Stderr);
                Assert.That(failed.ExitCode, Is.EqualTo(2));
                Assert.That(failed.Stderr, Does.Contain("B3TAG2004"));
                Assert.That(File.ReadAllBytes(output), Is.EqualTo(previous));
            });
        }
        finally
        {
            var catalogDirectory = Path.GetDirectoryName(output)!;
            if (Directory.Exists(catalogDirectory)) Directory.Delete(catalogDirectory, true);
        }
    }

    [Test]
    public void Published_compile_requires_the_fixed_game_source_and_rejects_arbitrary_game_source_option()
    {
        var absentRoot = Path.Combine(_root, "absent");
        Directory.CreateDirectory(absentRoot);
        var output = Path.Combine(_root, "published.catalog");

        var missing = Run("compile", "--published", "--catalog-id", "sample", "--catalog-version", "1.0.0",
            "--project-root", absentRoot, "--output", output);
        var arbitrary = Run("compile", "--published", "--catalog-id", "sample", "--catalog-version", "1.0.0",
            "--project-root", absentRoot, "--output", output, "--game-source", Write("other.json", GameJson("wrong.path")));
        var validRoot = CreateProject(GameJson("state.ready"));
        var arbitrarySource = Run("compile", "--published", "--catalog-id", "sample", "--catalog-version", "1.0.0",
            "--project-root", validRoot, "--output", output, "--source", Write("game-as-source.json", GameJson("wrong.path")));

        Assert.Multiple(() =>
        {
            Assert.That(missing.ExitCode, Is.EqualTo(3));
            Assert.That(missing.Stderr, Does.Contain(Path.Combine("ProjectSettings", "GameplayTags.json")));
            Assert.That(arbitrary.ExitCode, Is.EqualTo(1));
            Assert.That(arbitrarySource.ExitCode, Is.EqualTo(2));
            Assert.That(File.Exists(output), Is.False);
        });
    }

    [Test]
    public void Published_identity_is_idempotent_only_for_identical_content()
    {
        var projectRoot = CreateProject(GameJson("state.ready"));
        var output = Path.Combine(_root, "published.catalog");
        var args = new[] { "compile", "--published", "--catalog-id", "sample", "--catalog-version", "1.0.0",
            "--project-root", projectRoot, "--output", output };
        var first = Run(args);
        var original = File.ReadAllBytes(output);
        var identical = Run(args);
        File.WriteAllText(Path.Combine(projectRoot, "ProjectSettings", "GameplayTags.json"), GameJson("state.changed"), new UTF8Encoding(false));
        var changed = Run(args);

        Assert.Multiple(() =>
        {
            Assert.That(first.ExitCode, Is.Zero, first.Stderr);
            Assert.That(identical.ExitCode, Is.Zero, identical.Stderr);
            Assert.That(changed.ExitCode, Is.EqualTo(2));
            Assert.That(changed.Stderr, Does.Contain("immutable").IgnoreCase);
            Assert.That(File.ReadAllBytes(output), Is.EqualTo(original));
        });
    }

    [Test]
    public void Inspect_prints_identity_lowercase_fingerprint_and_counts()
    {
        var projectRoot = CreateProject(GameJsonWithRedirect());
        var output = Path.Combine(_root, "inspect.catalog");
        var compiled = Run("compile", "--published", "--catalog-id", "sample", "--catalog-version", "2.1.0",
            "--project-root", projectRoot, "--output", output);

        var inspected = Run("inspect", output);

        Assert.Multiple(() =>
        {
            Assert.That(compiled.ExitCode, Is.Zero, compiled.Stderr);
            Assert.That(inspected.ExitCode, Is.Zero, inspected.Stderr);
            Assert.That(inspected.Stdout, Does.Contain("Catalog ID: sample"));
            Assert.That(inspected.Stdout, Does.Contain("Version: 2.1.0"));
            Assert.That(inspected.Stdout, Does.Match("Fingerprint: [0-9a-f]{64}"));
            Assert.That(inspected.Stdout, Does.Contain("Tags: 2"));
            Assert.That(inspected.Stdout, Does.Contain("Redirects: 1"));
        });
    }

    [Test]
    public void Extract_native_is_source_order_independent_and_writes_strict_canonical_metadata()
    {
        var sourceInfo = Write("SourceInfo.cs", """
            using Bun3.Gameplay.Tags;
            [assembly: GameplayTagSource("sample.native", "Sample Native")]
            public static class ZTags
            {
                [NativeGameplayTag("Ready")]
                public const string Ready = "State.Ready";
            }
            """);
        var moreTags = Write("MoreTags.cs", """
            using Bun3.Gameplay.Tags;
            namespace Sample.Native;

            public static class ATags
            {
                [NativeGameplayTag]
                public const string Jump = "Ability.Jump";
            }
            """);
        var firstOutput = Path.Combine(_root, "first-metadata.json");
        var secondOutput = Path.Combine(_root, "second-metadata.json");

        var first = Run("extract-native", "--output", firstOutput, sourceInfo, moreTags);
        var second = Run("extract-native", "--output", secondOutput, moreTags, sourceInfo);
        using var metadata = File.OpenRead(firstOutput);
        var document = TagSourceJson.LoadMetadata(metadata, firstOutput);

        Assert.Multiple(() =>
        {
            Assert.That(first.ExitCode, Is.Zero, first.Stderr);
            Assert.That(second.ExitCode, Is.Zero, second.Stderr);
            Assert.That(File.ReadAllBytes(secondOutput), Is.EqualTo(File.ReadAllBytes(firstOutput)));
            Assert.That(document.Descriptor.SourceId, Is.EqualTo("sample.native"));
            Assert.That(document.Descriptor.DisplayName, Is.EqualTo("Sample Native"));
            Assert.That(document.Descriptor.Kind, Is.EqualTo(TagSourceKind.Native));
            Assert.That(document.Tags.Select(tag => tag.Name), Is.EqualTo(new[] { "ability.jump", "state.ready" }));
            Assert.That(document.Tags.Select(tag => tag.Comment), Is.EqualTo(new[] { "", "Ready" }));
            Assert.That(document.Redirects, Is.Empty);
        });
    }

    [Test]
    public void Invalid_native_declaration_reports_validation_and_preserves_destination()
    {
        var source = Write("Invalid.cs", """
            using Bun3.Gameplay.Tags;
            [assembly: GameplayTagSource("sample.native", "Sample Native")]
            public static class Tags
            {
                [NativeGameplayTag]
                public static readonly string NotConstant = "State.Ready";
            }
            """);
        var output = Write("metadata.json", "previous-good");

        var result = Run("extract-native", "--output", output, source);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.Stderr, Does.Contain("const string"));
            Assert.That(File.ReadAllText(output), Is.EqualTo("previous-good"));
        });
    }

    [Test]
    public void Native_extractor_rejects_the_reserved_game_source_id()
    {
        var source = Write("ReservedSource.cs", """
            using Bun3.Gameplay.Tags;
            [assembly: GameplayTagSource("game", "Reserved")]
            public static class Tags
            {
                [NativeGameplayTag]
                public const string Ready = "State.Ready";
            }
            """);

        var result = NativeTagMetadataExtractor.Extract(
            new[] { source }, GetCompilerReferences(), "ReservedNativeSource");

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics, Has.Some.Contains("game"));
        });
    }

    [Test]
    public void Atomic_readback_failure_preserves_existing_destination()
    {
        var output = Write("atomic.catalog", "previous-good");

        Assert.Throws<InvalidDataException>(() => AtomicFileWriter.WriteVerified(
            output,
            stream => stream.Write(new byte[] { 1, 2, 3 }, 0, 3),
            _ => throw new InvalidDataException("readback rejected")));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(output), Is.EqualTo("previous-good"));
            Assert.That(Directory.GetFiles(_root, ".atomic.catalog.*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void Task_staged_metadata_readback_failure_preserves_destination_and_removes_temporary_file()
    {
        var output = Write("task-metadata.json", "previous-good");
        var method = typeof(ExtractNativeGameplayTagsTask).GetMethod(
            "WriteAtomically", BindingFlags.Static | BindingFlags.NonPublic);
        Exception? readbackError = null;
        try
        {
            method!.Invoke(null, new object[] { output, "{\"schemaVersion\":1" });
        }
        catch (TargetInvocationException exception)
        {
            readbackError = exception.InnerException;
        }

        Assert.Multiple(() =>
        {
            Assert.That(method, Is.Not.Null);
            Assert.That(readbackError, Is.TypeOf<InvalidDataException>());
            Assert.That(File.ReadAllText(output), Is.EqualTo("previous-good"));
            Assert.That(Directory.GetFiles(_root, ".task-metadata.json.*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void Task_staged_metadata_with_trailing_comma_preserves_destination_and_removes_temporary_file()
    {
        var output = Write("task-metadata-trailing-comma.json", "previous-good");
        const string invalidMetadata = """
            {
              "schemaVersion": 1,
              "source": { "id": "fixture.native", "displayName": "Fixture Native", "kind": "native" },
              "tags": [],
              "redirects": [],
            }
            """;
        var method = typeof(ExtractNativeGameplayTagsTask).GetMethod(
            "WriteAtomically", BindingFlags.Static | BindingFlags.NonPublic);
        Exception? readbackError = null;
        try
        {
            method!.Invoke(null, new object[] { output, invalidMetadata });
        }
        catch (TargetInvocationException exception)
        {
            readbackError = exception.InnerException;
        }

        Assert.Multiple(() =>
        {
            Assert.That(method, Is.Not.Null);
            Assert.That(readbackError, Is.TypeOf<InvalidDataException>());
            Assert.That(File.ReadAllText(output), Is.EqualTo("previous-good"));
            Assert.That(Directory.GetFiles(_root, ".task-metadata-trailing-comma.json.*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void Packaged_target_uses_platform_neutral_metadata_suffix()
    {
        var repositoryRoot = FindRepositoryRoot();
        var targetsPath = Path.Combine(repositoryRoot, "common", "src", "com.bun3.gameplay", "buildTransitive",
            "Bun3.Gameplay.NativeTags.targets");
        var targets = File.ReadAllText(targetsPath);

        Assert.Multiple(() =>
        {
            Assert.That(targets, Does.Contain("Bun3/GameplayTags/TagSource.json"));
            Assert.That(targets, Does.Not.Contain(@"Bun3\GameplayTags\TagSource.json"));
        });
    }

    [Test]
    public void External_pack_resolves_relative_intermediate_output_under_project_with_spaces()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectDirectory = Path.Combine(_root, "External Native Package With Spaces");
        Directory.CreateDirectory(projectDirectory);
        var relativeIntermediate = "relative intermediate " + Guid.NewGuid().ToString("N");
        var expectedIntermediateRoot = Path.Combine(projectDirectory, relativeIntermediate);
        var escapedRoot = Path.Combine(repositoryRoot, relativeIntermediate);
        var packageOutput = Path.Combine(_root, "external package output");
        var localFeed = Path.Combine(_root, "local package feed");
        var localPackageCache = Path.Combine(_root, "local package cache");
        var localPackageVersion = "0.8.0-local-" + Guid.NewGuid().ToString("N");
        var projectPath = Path.Combine(projectDirectory, "External Native Package.csproj");
        var gameplayProject = Path.Combine(repositoryRoot, "common", "src", "com.bun3.gameplay", "Bun3.Gameplay.csproj");
        var rootPackageResult = RunProcess(repositoryRoot, "dotnet", "pack", gameplayProject, "-c", "Release",
            "-o", localFeed, "-p:PackageVersion=" + localPackageVersion, "-nodeReuse:false");
        var localPackage = Path.Combine(localFeed, "Bun3.Gameplay." + localPackageVersion + ".nupkg");
        File.WriteAllText(projectPath, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>External.Native.Package.Fixture</PackageId>
                <Version>1.0.0</Version>
                <Bun3GameplayTagSource>true</Bun3GameplayTagSource>
                <IntermediateOutputPath>{{relativeIntermediate}}/</IntermediateOutputPath>
                <RestoreAdditionalProjectSources>{{SecurityElement.Escape(localFeed)}}</RestoreAdditionalProjectSources>
                <RestorePackagesPath>{{SecurityElement.Escape(localPackageCache)}}</RestorePackagesPath>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Bun3.Gameplay" Version="{{localPackageVersion}}" />
              </ItemGroup>
            </Project>
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(projectDirectory, "Native Tags.cs"), """
            using Bun3.Gameplay.Tags;
            [assembly: GameplayTagSource("external.native", "External Native")]
            public static class NativeTags
            {
                [NativeGameplayTag]
                public const string Ready = "State.Ready";
            }
            """, new UTF8Encoding(false));

        try
        {
            var result = RunProcess(repositoryRoot, "dotnet", "pack", projectPath, "-c", "Release",
                "-o", packageOutput, "-nodeReuse:false");
            var package = Path.Combine(packageOutput, "External.Native.Package.Fixture.1.0.0.nupkg");
            var tagSourceCount = File.Exists(package)
                ? CountZipEntries(package, "contentFiles/any/any/Bun3/GameplayTags/TagSource.json")
                : 0;
            var expectedMetadata = Directory.Exists(expectedIntermediateRoot)
                ? Directory.GetFiles(expectedIntermediateRoot, "TagSource.json", SearchOption.AllDirectories)
                : Array.Empty<string>();
            var escapedMetadata = Directory.Exists(escapedRoot)
                ? Directory.GetFiles(escapedRoot, "TagSource.json", SearchOption.AllDirectories)
                : Array.Empty<string>();
            var generatedImports = string.Join(Environment.NewLine,
                Directory.GetFiles(projectDirectory, "*.nuget.g.targets", SearchOption.AllDirectories)
                    .Select(File.ReadAllText));

            Assert.Multiple(() =>
            {
                Assert.That(rootPackageResult.ExitCode, Is.Zero, rootPackageResult.Output);
                Assert.That(File.Exists(localPackage), Is.True, "The unique local package must exist before consumer restore.");
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(expectedMetadata, Has.Exactly(1).EndsWith("TagSource.json"),
                    "Metadata must be rooted under the external project intermediate directory.\n"
                    + result.Output + "\nGenerated imports:\n" + generatedImports);
                Assert.That(escapedMetadata, Is.Empty, "Metadata must not escape to the pack process working directory.");
                Assert.That(tagSourceCount, Is.EqualTo(1));
            });
        }
        finally
        {
            if (Directory.Exists(escapedRoot)) Directory.Delete(escapedRoot, true);
        }
    }

    [TestCase("compile", "--development", "--published", "--catalog-id", "sample", "--project-root", ".")]
    [TestCase("compile", "--development", "--catalog-id", "a", "--catalog-id", "b", "--project-root", ".")]
    [TestCase("compile", "--development", "--catalog-id", "sample", "--project-root", ".", "--output", "bad")]
    [TestCase("extract-native", "--output", "a.json")]
    [TestCase("inspect")]
    [TestCase("unknown")]
    public void Invalid_command_shapes_return_usage(params string[] args)
    {
        var result = Run(args);

        Assert.That(result.ExitCode, Is.EqualTo(1), result.Stderr);
    }

    [Test]
    public void Development_path_uses_override_and_validates_catalog_id()
    {
        var result = Bun3.Gameplay.Tags.TagCatalogDevelopmentPath.Get("sample-game", _root);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Path.Combine(_root, "Bun3", "GameplayTags", "sample-game", "dev", "GameplayTags.catalog")));
            Assert.Throws<ArgumentException>(() => Bun3.Gameplay.Tags.TagCatalogDevelopmentPath.Get("../escape", _root));
        });
    }

    private (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = Program.Run(args, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private string CreateProject(string gameJson)
    {
        var projectRoot = Path.Combine(_root, "Project Root With Spaces");
        Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
        File.WriteAllText(Path.Combine(projectRoot, "ProjectSettings", "GameplayTags.json"), gameJson, new UTF8Encoding(false));
        return projectRoot;
    }

    private string Write(string relativePath, string contents)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents, new UTF8Encoding(false));
        return path;
    }

    private static Bun3.Gameplay.Tags.TagCatalog ReadCatalog(string path, string catalogId, string version)
    {
        using var input = File.OpenRead(path);
        var expectations = version == "0.0.0-dev"
            ? Bun3.Gameplay.Tags.TagCatalogExpectations.ForDevelopment(catalogId)
            : throw new InvalidOperationException();
        return Bun3.Gameplay.Tags.TagCatalogBinary.Load(input, expectations);
    }

    private static IReadOnlyList<string> GetCompilerReferences()
    {
        var references = new List<string>();
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            references.AddRange(trusted.Split(Path.PathSeparator));
        }

        references.Add(typeof(Bun3.Gameplay.Tags.GameplayTagSourceAttribute).Assembly.Location);
        return references;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Bun3.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static (int ExitCode, string Output) RunProcess(string workingDirectory, string fileName, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr);
    }

    private static int CountZipEntries(string packagePath, string entryPath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        return archive.Entries.Count(entry => string.Equals(entry.FullName, entryPath, StringComparison.Ordinal));
    }

    private static string GameJson(params string[] tags) =>
        "{\"schemaVersion\":1,\"tags\":["
        + string.Join(",", tags.Select(tag => "{\"name\":\"" + tag + "\",\"comment\":\"game\"}"))
        + "],\"redirects\":[]}";

    private static string GameJsonWithRedirect() =>
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"state.ready\",\"comment\":\"\"}],\"redirects\":[{\"from\":\"state.old\",\"to\":\"state.ready\"}]}";

    private static string GameJsonWithMissingRedirect() =>
        "{\"schemaVersion\":1,\"tags\":[],\"redirects\":[{\"from\":\"state.old\",\"to\":\"state.missing\"}]}";

    private static string MetadataJson(string id, string displayName, string kind, params string[] tags) =>
        "{\"schemaVersion\":1,\"source\":{\"id\":\"" + id + "\",\"displayName\":\"" + displayName
        + "\",\"kind\":\"" + kind + "\"},\"tags\":["
        + string.Join(",", tags.Select(tag => "{\"name\":\"" + tag + "\",\"comment\":\"metadata\"}"))
        + "],\"redirects\":[]}";
}
