using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundCatalogTests
    {
        SoundCatalog catalog;
        SoundDef definition;
        [SetUp] public void Setup()
        { catalog = ScriptableObject.CreateInstance<SoundCatalog>(); definition = ScriptableObject.CreateInstance<SoundDef>(); }
        [TearDown] public void Cleanup() { Object.DestroyImmediate(catalog); Object.DestroyImmediate(definition); }
        [Test] public void ArbitraryKeysAreOrdinalAndWarmLookupDoesNotAllocate()
        {
            catalog.SetEntries(new[] { new SoundCatalogEntry("custom.new.effect", definition) });
            Assert.That(catalog.Get("custom.new.effect"), Is.SameAs(definition));
            Assert.That(catalog.Get("CUSTOM.new.effect"), Is.Null);
            Assert.That(catalog.Get(null), Is.Null);
            for (int i = 0; i < 100; i++) catalog.Get("custom.new.effect");
            Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            TestDelegate warmLookup = () =>
            {
                for (int i = 0; i < 1000; i++) catalog.Get("custom.new.effect");
            };
            warmLookup();
            Assert.That(warmLookup, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
        [Test] public void InvalidReplacementLeavesPreviousMappingsIntact()
        {
            catalog.SetEntries(new[] { new SoundCatalogEntry("valid", definition) });
            Assert.Throws<ArgumentException>(() => catalog.SetEntries(new[] { new SoundCatalogEntry(" ", definition) }));
            Assert.Throws<ArgumentException>(() => catalog.SetEntries(new[] { new SoundCatalogEntry("missing", null) }));
            Assert.Throws<ArgumentException>(() => catalog.SetEntries(new[] { new SoundCatalogEntry("same", definition), new SoundCatalogEntry("same", definition) }));
            Assert.That(catalog.Get("valid"), Is.SameAs(definition));
        }
        [Test] public void ReplacementCopiesInputAndCanAddKeysWithoutAnEnum()
        {
            var entries = new[] { new SoundCatalogEntry("first", definition) };
            catalog.SetEntries(entries);
            entries[0] = new SoundCatalogEntry("second", definition);
            Assert.That(catalog.Get("first"), Is.SameAs(definition));
            catalog.SetEntries(entries);
            Assert.That(catalog.Get("first"), Is.Null);
            Assert.That(catalog.Get("second"), Is.SameAs(definition));
        }
    }
}
