using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;

namespace Bun3.State.Package.Tests
{
    public sealed class StatePackageTests
    {
        [Test]
        public void Runtime_package_generates_CSharp9_consumer_and_reports_direct_field_write()
        {
            var root = FindRepositoryRoot();
            var temporary = Path.Combine(Path.GetTempPath(), "bun3-state-package-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                var feed = Path.Combine(temporary, "feed");
                var consumer = Path.Combine(temporary, "consumer");
                var cache = Path.Combine(temporary, "packages");
                Directory.CreateDirectory(feed);
                Directory.CreateDirectory(consumer);
                foreach (var fixture in Directory.GetFiles(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Consumer")))
                    File.Copy(fixture, Path.Combine(consumer, Path.GetFileName(fixture)));
                File.WriteAllText(Path.Combine(consumer, "NuGet.Config"),
                    "<configuration><packageSources><clear/><add key=\"local\" value=\"" + SecurityElement.Escape(feed) + "\"/></packageSources></configuration>");

                var project = Path.Combine(root, "common", "src", "Bun3.State", "Bun3.State.csproj");
                var repositoryAssets = new[] { "Bun3.State", "Bun3.State.Generator" }
                    .Select(name => Path.Combine(root, "common", "artifacts", "obj", name, "project.assets.json"))
                    .ToArray();
                var assetsBefore = repositoryAssets.Select(file => File.Exists(file) ? File.ReadAllText(file) : null).ToArray();
                var pack = Run(root, cache, "pack", project, "-c", "Release", "-o", feed,
                    "--artifacts-path", Path.Combine(temporary, "artifacts"));
                Assert.That(pack.ExitCode, Is.Zero, pack.Output);
                for (var i = 0; i < repositoryAssets.Length; i++)
                {
                    var assetsAfter = File.Exists(repositoryAssets[i]) ? File.ReadAllText(repositoryAssets[i]) : null;
                    Assert.That(assetsAfter == assetsBefore[i], Is.True,
                        $"Package consumption must preserve repository restore assets: {repositoryAssets[i]}");
                }
                var nupkg = Path.Combine(feed, "Bun3.State.0.1.0.nupkg");
                Assert.That(File.Exists(nupkg), Is.True, pack.Output);
                using (var zip = ZipFile.OpenRead(nupkg))
                {
                    var entries = zip.Entries.Select(entry => entry.FullName).ToArray();
                    Assert.That(entries, Does.Contain("lib/netstandard2.1/Bun3.State.dll"));
                    Assert.That(entries, Does.Contain("analyzers/dotnet/cs/Bun3.State.Generator.dll"));
                    Assert.That(entries.Any(name => name.Contains("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)), Is.False);
                    var nuspec = zip.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
                    using var reader = new StreamReader(nuspec.Open());
                    var metadata = reader.ReadToEnd();
                    Assert.That(metadata, Does.Not.Contain("Microsoft.CodeAnalysis"));
                    Assert.That(metadata, Does.Not.Contain("Bun3.Server"));
                    Assert.That(metadata, Does.Not.Contain("Google.Protobuf"));
                    Assert.That(metadata, Does.Not.Contain("protobuf-net"));
                }

                var run = Run(consumer, cache, "run", "--project", "Consumer.csproj", "-c", "Release");
                Assert.That(run.ExitCode, Is.Zero, run.Output);
                Assert.That(run.Output, Does.Contain("Level=5"));
                var deps = File.ReadAllText(Path.Combine(consumer, "bin", "Release", "net10.0", "Consumer.deps.json"));
                Assert.That(deps, Does.Not.Contain("Microsoft.CodeAnalysis"));

                File.AppendAllText(Path.Combine(consumer, "EquipmentState.cs"),
                    "\nnamespace PackageConsumer { public sealed partial class EquipmentState { public void InvalidWrite() { _level = 9; } } }\n");
                var invalid = Run(consumer, cache, "build", "Consumer.csproj", "-c", "Release", "--no-restore");
                Assert.That(invalid.ExitCode, Is.Not.Zero, invalid.Output);
                Assert.That(invalid.Output, Does.Contain("B3STATE0004"));
                TestContext.Out.WriteLine($"package={nupkg}; consumer Level=5; B3STATE0004 confirmed; isolated cache={cache}");
            }
            finally
            {
                var full = Path.GetFullPath(temporary);
                var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(full).StartsWith("bun3-state-package-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to clean a path outside the test temporary directory.");
                Directory.Delete(full, true);
            }
        }

        private static string FindRepositoryRoot()
        {
            for (var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); current != null; current = current.Parent)
                if (File.Exists(Path.Combine(current.FullName, "Bun3.sln"))) return current.FullName;
            throw new DirectoryNotFoundException("Bun3.sln was not found above the test output.");
        }

        private static (int ExitCode, string Output) Run(string directory, string cache, params string[] arguments)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            start.Environment["NUGET_PACKAGES"] = cache;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
    }
}
