#nullable enable
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Unity.Tests
{
    /// <summary>Verifies the Unity authoring reference and runtime catalog resolve contract.</summary>
    [TestFixture]
    public sealed class GameplayTagRefTests
    {
        /// <summary>Verifies a new reference stores a valid path as canonical lowercase.</summary>
        [Test]
        public void Constructor_stores_a_canonical_lowercase_path()
        {
            var reference = new GameplayTagRef("Ability.Attack.Heavy");

            Assert.That(reference.Path, Is.EqualTo("ability.attack.heavy"));
            Assert.That(reference.IsEmpty, Is.False);
        }

        /// <summary>Verifies an empty reference resolves cleanly to None.</summary>
        [Test]
        public void Empty_reference_resolves_to_none()
        {
            var catalog = CreateCatalog("ability.attack");

            var resolved = default(GameplayTagRef).TryResolve(catalog, out var tag);

            Assert.That(resolved, Is.True);
            Assert.That(tag, Is.EqualTo(GameplayTag.None));
            Assert.That(default(GameplayTagRef).ResolveRequired(catalog),
                Is.EqualTo(GameplayTag.None));
        }

        /// <summary>Verifies a reference built from an empty string behaves as the same value as None.</summary>
        [Test]
        public void Empty_string_constructor_creates_none()
        {
            var catalog = CreateCatalog("ability.attack");
            var reference = new GameplayTagRef(string.Empty);

            Assert.That(reference.Path, Is.Empty);
            Assert.That(reference.IsEmpty, Is.True);
            Assert.That(reference, Is.EqualTo(GameplayTagRef.None));
            Assert.That(reference.TryResolve(catalog, out var tag), Is.True);
            Assert.That(tag, Is.EqualTo(GameplayTag.None));
            Assert.That(reference.ResolveRequired(catalog), Is.EqualTo(GameplayTag.None));
        }

        /// <summary>Verifies registered and unregistered paths are distinguished exactly against the current catalog.</summary>
        [Test]
        public void Resolve_uses_the_supplied_catalog()
        {
            var catalog = CreateCatalog("ability.attack");
            var known = new GameplayTagRef("ability.attack");
            var missing = new GameplayTagRef("ability.missing");

            Assert.That(known.TryResolve(catalog, out var tag), Is.True);
            Assert.That(catalog.GetDisplayName(tag), Is.EqualTo("ability.attack"));
            Assert.That(missing.TryResolve(catalog, out var missingTag), Is.False);
            Assert.That(missingTag, Is.EqualTo(GameplayTag.None));
            Assert.Throws<KeyNotFoundException>(() => missing.ResolveRequired(catalog));
        }

        /// <summary>Verifies a new reference does not accept invalid tag syntax.</summary>
        [Test]
        public void Constructor_rejects_invalid_tag_syntax()
        {
            Assert.Throws<ArgumentNullException>(() => new GameplayTagRef(null!));
            Assert.Throws<ArgumentException>(() => new GameplayTagRef("bad..tag"));
        }

        /// <summary>Verifies an existing asset's malformed raw string is left unchanged and only resolve fails.</summary>
        [Test]
        public void Deserialized_invalid_raw_path_is_preserved_and_try_resolve_returns_false()
        {
            var host = ScriptableObject.CreateInstance<TagRefHost>();
            try
            {
                var serialized = new SerializedObject(host);
                var path = serialized.FindProperty("_tag").FindPropertyRelative("_path");
                path.stringValue = "Legacy..Broken";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var resolved = host.Tag.TryResolve(CreateCatalog("ability.attack"), out var tag);

                Assert.That(host.Tag.Path, Is.EqualTo("Legacy..Broken"));
                Assert.That(resolved, Is.False);
                Assert.That(tag, Is.EqualTo(GameplayTag.None));
                Assert.Throws<ArgumentException>(
                    () => host.Tag.ResolveRequired(CreateCatalog("ability.attack")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>Verifies reference equality is decided by the ordinal value of the stored canonical path.</summary>
        [Test]
        public void Equality_uses_the_stored_canonical_path()
        {
            var first = new GameplayTagRef("Ability.Attack");
            var second = new GameplayTagRef("ability.attack");
            var other = new GameplayTagRef("ability.defend");

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(first == second, Is.True);
            Assert.That(first != other, Is.True);
        }

        private static TagCatalog CreateCatalog(params string[] paths)
        {
            var tags = new TagSourceTag[paths.Length];
            for (var index = 0; index < paths.Length; index++)
            {
                tags[index] = new TagSourceTag(paths[index], string.Empty);
            }

            var source = new TagSourceDocument(
                new TagSourceDescriptor("game", "Game", TagSourceKind.GameJson, false),
                "ProjectSettings/GameplayTags.json",
                tags,
                Array.Empty<TagSourceRedirect>());
            var compilation = TagCatalogCompiler.Compile(
                new[] { source },
                new TagCatalogIdentity("tag-ref-tests", "0.0.0-dev"));
            Assert.That(compilation.Succeeded, Is.True);
            return compilation.Catalog!;
        }

        private sealed class TagRefHost : ScriptableObject
        {
            [SerializeField]
            private GameplayTagRef _tag = default;

            internal GameplayTagRef Tag => _tag;
        }
    }
}
#pragma warning restore CS0618
