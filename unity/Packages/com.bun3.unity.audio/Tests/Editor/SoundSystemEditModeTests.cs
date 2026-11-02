using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemEditModeTests
    {
        SoundCatalog catalog;
        SoundDef definition;
        GameObject root;

        [SetUp] public void Setup()
        {
            definition = ScriptableObject.CreateInstance<SoundDef>();
            catalog = ScriptableObject.CreateInstance<SoundCatalog>();
            catalog.SetEntries(new[] { new SoundCatalogEntry("edit.mode", definition) });
        }

        [TearDown] public void Cleanup()
        {
            SoundSystem.Shutdown();
            if (root != null) Object.DestroyImmediate(root);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(definition);
        }

        [Test] public void ShutdownOutsidePlayMode_DestroysRootWithoutError()
        {
            SoundSystem.Initialize(catalog, new SoundSystemConfig
            {
                SfxVoices = 1,
                OnSourceCreated = source => root = source.transform.parent.gameObject,
            });
            Assert.That(root, Is.Not.Null);

            SoundSystem.Shutdown();

            LogAssert.NoUnexpectedReceived();
            Assert.That(root == null, Is.True, "The root must be destroyed immediately outside Play Mode.");
            Assert.That(SoundSystem.Current, Is.Null);
        }
    }
}
