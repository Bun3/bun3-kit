using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using Bun3.Server.GameplayTags;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
[NonParallelizable]
public sealed class GameplayTagHostingTests
{
    private const string OverrideEnvironmentVariable = "BUN3_GAMEPLAY_TAG_CATALOG_PATH";

    [Test]
    public async Task Development_host_loads_shared_cache_before_gameplay_service_starts()
    {
        using var fixture = CatalogFixture.CreateDevelopment("server-game");
        var starts = 0;
        using var host = BuildHost(fixture.LocalApplicationData, options =>
        {
            options.Mode = GameplayTagCatalogMode.LocalDevelopment;
            options.CatalogId = "server-game";
        }, () => starts++);

        await host.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(host.Services.GetRequiredService<TagCatalog>().CatalogId, Is.EqualTo("server-game"));
            Assert.That(starts, Is.EqualTo(1));
        });
        await host.StopAsync();
    }

    [Test]
    public void Missing_development_catalog_prevents_gameplay_service_start()
    {
        using var fixture = CatalogFixture.CreateEmpty();
        var starts = 0;
        using var host = BuildHost(fixture.LocalApplicationData, options =>
        {
            options.Mode = GameplayTagCatalogMode.LocalDevelopment;
            options.CatalogId = "server-game";
        }, () => starts++);

        Assert.ThrowsAsync<FileNotFoundException>(async () => await host.StartAsync());
        Assert.That(starts, Is.Zero);
    }

    [Test]
    public void Corrupt_development_catalog_throws_format_exception_before_gameplay_starts()
    {
        using var fixture = CatalogFixture.CreateDevelopment("server-game");
        File.WriteAllBytes(fixture.CatalogPath, "B3DK"u8.ToArray());
        var starts = 0;
        using var host = BuildHost(fixture.LocalApplicationData, options =>
        {
            options.Mode = GameplayTagCatalogMode.LocalDevelopment;
            options.CatalogId = "server-game";
        }, () => starts++);

        Assert.ThrowsAsync<TagCatalogFormatException>(async () => await host.StartAsync());
        Assert.That(starts, Is.Zero);
    }

    [Test]
    public void Mismatched_development_catalog_throws_compatibility_exception_before_gameplay_starts()
    {
        using var fixture = CatalogFixture.CreateDevelopment("another-game", "server-game");
        var starts = 0;
        using var host = BuildHost(fixture.LocalApplicationData, options =>
        {
            options.Mode = GameplayTagCatalogMode.LocalDevelopment;
            options.CatalogId = "server-game";
        }, () => starts++);

        Assert.ThrowsAsync<TagCatalogCompatibilityException>(async () => await host.StartAsync());
        Assert.That(starts, Is.Zero);
    }

    [Test]
    public async Task Development_mode_uses_explicit_environment_path()
    {
        using var cacheFixture = CatalogFixture.CreateEmpty();
        using var overrideFixture = CatalogFixture.CreateDevelopment("server-game");
        var previous = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        Environment.SetEnvironmentVariable(OverrideEnvironmentVariable, overrideFixture.CatalogPath);
        try
        {
            using var host = BuildHost(cacheFixture.LocalApplicationData, options =>
            {
                options.Mode = GameplayTagCatalogMode.LocalDevelopment;
                options.CatalogId = "server-game";
            });

            await host.StartAsync();

            Assert.That(host.Services.GetRequiredService<TagCatalog>().CatalogId, Is.EqualTo("server-game"));
            await host.StopAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable(OverrideEnvironmentVariable, previous);
        }
    }

    [Test]
    public async Task Packaged_mode_ignores_environment_override_and_accepts_uppercase_fingerprint()
    {
        using var packaged = CatalogFixture.CreatePublished("server-game", "2026.8.14");
        using var environment = CatalogFixture.CreateDevelopment("wrong-game");
        var previous = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        Environment.SetEnvironmentVariable(OverrideEnvironmentVariable, environment.CatalogPath);
        try
        {
            using var host = BuildHost(packaged.LocalApplicationData, options =>
            {
                options.Mode = GameplayTagCatalogMode.Packaged;
                options.CatalogId = "server-game";
                options.CatalogVersion = "2026.8.14";
                options.ExpectedFingerprint = packaged.Fingerprint.ToUpperInvariant();
                options.PackagedPath = packaged.CatalogPath;
            });

            await host.StartAsync();

            Assert.That(host.Services.GetRequiredService<TagCatalog>().CatalogVersion, Is.EqualTo("2026.8.14"));
            await host.StopAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable(OverrideEnvironmentVariable, previous);
        }
    }

    [Test]
    public async Task Runtime_and_server_adapter_load_the_same_binary_contract()
    {
        using var fixture = CatalogFixture.CreatePublished("server-game", "2026.8.14");
        using var directInput = File.OpenRead(fixture.CatalogPath);
        var direct = TagCatalogBinary.Load(
            directInput,
            TagCatalogExpectations.ForPublished(
                "server-game",
                "2026.8.14",
                Convert.FromHexString(fixture.Fingerprint)));
        using var host = BuildHost(fixture.LocalApplicationData, options =>
        {
            options.Mode = GameplayTagCatalogMode.Packaged;
            options.CatalogId = "server-game";
            options.CatalogVersion = "2026.8.14";
            options.ExpectedFingerprint = fixture.Fingerprint;
            options.PackagedPath = fixture.CatalogPath;
        });

        await host.StartAsync();
        var adapted = host.Services.GetRequiredService<TagCatalog>();

        AssertCatalogsMatch(direct, adapted);
        Assert.That(adapted.GetRequired("state.killed"), Is.EqualTo(direct.GetRequired("state.killed")));
        await host.StopAsync();
    }

    [Test]
    public void Packaged_mode_rejects_wrong_version_before_gameplay_starts()
    {
        using var fixture = CatalogFixture.CreatePublished("server-game", "2026.8.14");
        var starts = 0;
        using var host = BuildHost(fixture.LocalApplicationData, options =>
        {
            options.Mode = GameplayTagCatalogMode.Packaged;
            options.CatalogId = "server-game";
            options.CatalogVersion = "2026.8.15";
            options.ExpectedFingerprint = fixture.Fingerprint;
            options.PackagedPath = fixture.CatalogPath;
        }, () => starts++);

        Assert.ThrowsAsync<TagCatalogCompatibilityException>(async () => await host.StartAsync());
        Assert.That(starts, Is.Zero);
    }

    [Test]
    public void Packaged_mode_rejects_wrong_fingerprint_before_gameplay_starts()
    {
        using var fixture = CatalogFixture.CreatePublished("server-game", "2026.8.14");
        var starts = 0;
        using var host = BuildHost(fixture.LocalApplicationData, options =>
        {
            options.Mode = GameplayTagCatalogMode.Packaged;
            options.CatalogId = "server-game";
            options.CatalogVersion = "2026.8.14";
            options.ExpectedFingerprint = new string('0', 64);
            options.PackagedPath = fixture.CatalogPath;
        }, () => starts++);

        Assert.ThrowsAsync<TagCatalogCompatibilityException>(async () => await host.StartAsync());
        Assert.That(starts, Is.Zero);
    }

    [TestCase("", "2026.8.14", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [TestCase("server-game", "", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [TestCase("server-game", "2026.8.14", "abcd")]
    [TestCase("server-game", "2026.8.14", "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Packaged_mode_requires_exact_identity_configuration(
        string catalogId,
        string catalogVersion,
        string fingerprint)
    {
        using var fixture = CatalogFixture.CreatePublished("server-game", "2026.8.14");
        var starts = 0;
        using var host = BuildHost(fixture.LocalApplicationData, options =>
        {
            options.Mode = GameplayTagCatalogMode.Packaged;
            options.CatalogId = catalogId;
            options.CatalogVersion = catalogVersion;
            options.ExpectedFingerprint = fingerprint;
            options.PackagedPath = fixture.CatalogPath;
        }, () => starts++);

        Assert.ThrowsAsync<OptionsValidationException>(async () => await host.StartAsync());
        Assert.That(starts, Is.Zero);
    }

    [Test]
    public void Packaged_mode_rejects_null_fingerprint_before_gameplay_starts()
    {
        using var fixture = CatalogFixture.CreatePublished("server-game", "2026.8.14");
        var starts = 0;
        using var host = BuildHost(fixture.LocalApplicationData, options =>
        {
            options.Mode = GameplayTagCatalogMode.Packaged;
            options.CatalogId = "server-game";
            options.CatalogVersion = "2026.8.14";
            options.ExpectedFingerprint = null!;
            options.PackagedPath = fixture.CatalogPath;
        }, () => starts++);

        Assert.ThrowsAsync<OptionsValidationException>(async () => await host.StartAsync());
        Assert.That(starts, Is.Zero);
    }

    [Test]
    public async Task Configuration_section_binds_packaged_options()
    {
        using var fixture = CatalogFixture.CreatePublished("server-game", "2026.8.14");
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bun3:GameplayTags:Mode"] = "Packaged",
            ["Bun3:GameplayTags:CatalogId"] = "server-game",
            ["Bun3:GameplayTags:CatalogVersion"] = "2026.8.14",
            ["Bun3:GameplayTags:ExpectedFingerprint"] = fixture.Fingerprint,
            ["Bun3:GameplayTags:PackagedPath"] = fixture.CatalogPath,
        });
        builder.Services.AddGameplayTagCatalog();
        using var host = builder.Build();

        await host.StartAsync();

        Assert.That(host.Services.GetRequiredService<TagCatalog>().CatalogId, Is.EqualTo("server-game"));
        await host.StopAsync();
    }

    private static IHost BuildHost(
        string localApplicationData,
        Action<GameplayTagCatalogOptions> configure,
        Action? onGameplayStart = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddGameplayTagCatalog(options =>
        {
            options.LocalApplicationDataOverride = localApplicationData;
            configure(options);
        });
        if (onGameplayStart != null)
        {
            builder.Services.AddHostedService(_ => new GameplayHostedService(onGameplayStart));
        }

        return builder.Build();
    }

    private static void AssertCatalogsMatch(TagCatalog expected, TagCatalog actual)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.CatalogId, Is.EqualTo(expected.CatalogId));
            Assert.That(actual.CatalogVersion, Is.EqualTo(expected.CatalogVersion));
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            Assert.That(actual.Fingerprint.ToArray(), Is.EqualTo(expected.Fingerprint.ToArray()));
            for (var index = 0; index <= expected.Count; index++)
            {
                var expectedTag = expected.GetRequiredByIndex(checked((ushort)index));
                var actualTag = actual.GetRequiredByIndex(checked((ushort)index));
                Assert.That(actual.GetDisplayName(actualTag), Is.EqualTo(expected.GetDisplayName(expectedTag)));
                Assert.That(actual.GetParent(actualTag), Is.EqualTo(expected.GetParent(expectedTag)));
                for (var descendantIndex = 0; descendantIndex <= expected.Count; descendantIndex++)
                {
                    var expectedDescendant = expected.GetRequiredByIndex(checked((ushort)descendantIndex));
                    var actualDescendant = actual.GetRequiredByIndex(checked((ushort)descendantIndex));
                    Assert.That(
                        actual.IsAncestorOrSelf(actualTag, actualDescendant),
                        Is.EqualTo(expected.IsAncestorOrSelf(expectedTag, expectedDescendant)));
                }
            }
        });
    }

    private sealed class GameplayHostedService : IHostedService
    {
        private readonly Action _onStart;

        internal GameplayHostedService(Action onStart) => _onStart = onStart;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _onStart();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CatalogFixture : IDisposable
    {
        private CatalogFixture(string localApplicationData, string catalogPath, string fingerprint)
        {
            LocalApplicationData = localApplicationData;
            CatalogPath = catalogPath;
            Fingerprint = fingerprint;
        }

        internal string LocalApplicationData { get; }
        internal string CatalogPath { get; }
        internal string Fingerprint { get; }

        internal static CatalogFixture CreateEmpty()
        {
            var root = CreateRoot();
            return new CatalogFixture(root, TagCatalogDevelopmentPath.Get("server-game", root), string.Empty);
        }

        internal static CatalogFixture CreateDevelopment(string catalogId, string? pathCatalogId = null) =>
            Create(catalogId, "0.0.0-dev", pathCatalogId ?? catalogId, true);

        internal static CatalogFixture CreatePublished(string catalogId, string catalogVersion) =>
            Create(catalogId, catalogVersion, catalogId, false);

        private static CatalogFixture Create(
            string catalogId,
            string catalogVersion,
            string pathCatalogId,
            bool development)
        {
            var root = CreateRoot();
            var catalog = Compile(catalogId, catalogVersion);
            var path = development
                ? TagCatalogDevelopmentPath.Get(pathCatalogId, root)
                : Path.Combine(root, "published", "GameplayTags.catalog");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var output = File.Create(path))
            {
                TagCatalogBinaryWriter.Write(output, catalog);
            }

            return new CatalogFixture(root, path, Convert.ToHexString(catalog.Fingerprint).ToLowerInvariant());
        }

        private static TagCatalog Compile(string catalogId, string catalogVersion)
        {
            var source = new TagSourceDocument(
                new TagSourceDescriptor("server-tests", "Server Tests", TagSourceKind.PackageJson, true),
                "server-tests.json",
                new[]
                {
                    new TagSourceTag("state.rooted", "rooted"),
                    new TagSourceTag("ability.movement.jump", "jump"),
                    new TagSourceTag("state.dead.ghost", "ghost"),
                },
                new[] { new TagSourceRedirect("state.killed", "state.dead") });
            var compilation = TagCatalogCompiler.Compile(
                new[] { source },
                new TagCatalogIdentity(catalogId, catalogVersion));
            return compilation.Catalog!;
        }

        private static string CreateRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "Bun3.Server.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        public void Dispose()
        {
            if (Directory.Exists(LocalApplicationData))
            {
                Directory.Delete(LocalApplicationData, true);
            }
        }
    }
}
