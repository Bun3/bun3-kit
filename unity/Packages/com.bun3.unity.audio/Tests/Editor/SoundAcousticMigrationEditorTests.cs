using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundAcousticMigrationEditorTests
    {
        private const string FixtureFolder = "Assets/TempSoundAcousticMigrationTests";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(FixtureFolder);
        }

        [Test]
        public void LegacyYamlAssetsPreserveReferencesAndValuesAcrossExplicitUpgrade()
        {
            AssetDatabase.CreateFolder("Assets", "TempSoundAcousticMigrationTests");
            var attenuation = ScriptableObject.CreateInstance<DistanceAttenuationProfile>();
            var blend = ScriptableObject.CreateInstance<SpatialBlendProfile>();
            var sharedSpatial = ScriptableObject.CreateInstance<SoundSpatialProfile>();
            AssetDatabase.CreateAsset(attenuation, $"{FixtureFolder}/Attenuation.asset");
            AssetDatabase.CreateAsset(blend, $"{FixtureFolder}/Blend.asset");
            sharedSpatial.MinDistance = 7f;
            sharedSpatial.MaxDistance = 70f;
            AssetDatabase.CreateAsset(sharedSpatial, $"{FixtureFolder}/SharedSpatial.asset");

            var spatialPath = $"{FixtureFolder}/LegacySpatial.asset";
            var defPath = $"{FixtureFolder}/LegacyDef.asset";
            File.WriteAllText(spatialPath, LegacyYaml<SoundSpatialProfile>(
                "LegacySpatial", attenuation, blend, null));
            File.WriteAllText(defPath, LegacyYaml<SoundDef>(
                "LegacyDef", attenuation, blend, sharedSpatial));
            AssetDatabase.ImportAsset(spatialPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(defPath, ImportAssetOptions.ForceSynchronousImport);

            var spatial = AssetDatabase.LoadAssetAtPath<SoundSpatialProfile>(spatialPath);
            var def = AssetDatabase.LoadAssetAtPath<SoundDef>(defPath);
            Assert.That(spatial.UpgradeAcoustics(), Is.True);
            AssertSettings(spatial.Acoustics.Local, attenuation, blend);
            Assert.That(spatial.UpgradeAcoustics(), Is.False);
            Assert.That(def.SpatialProfile, Is.SameAs(sharedSpatial));
            Assert.That(def.EffectiveAcoustics.MinDistance, Is.EqualTo(7f));
            Assert.That(def.EffectiveAcoustics.MaxDistance, Is.EqualTo(70f));
            Assert.That(def.UpgradeAcoustics(), Is.True);
            AssertSettings(def.Acoustics.Local, attenuation, blend);
            Assert.That(def.UpgradeAcoustics(), Is.False);
        }

        [Test]
        public void SoundSpatialProfile_UsesDedicatedAcousticInspector()
        {
            var profile = ScriptableObject.CreateInstance<SoundSpatialProfile>();
            UnityEditor.Editor editor = null;
            try
            {
                editor = UnityEditor.Editor.CreateEditor(profile);

                Assert.That(editor.GetType().Name, Is.EqualTo("SoundSpatialProfileEditor"));
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SoundDef_SerializedLocalAcousticEditCanBeUndone()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            try
            {
                _ = def.Acoustics;
                var serialized = new SerializedObject(def);
                var maxDistance = serialized.FindProperty("_acoustics")
                    .FindPropertyRelative(nameof(SoundAcousticSelection.Local))
                    .FindPropertyRelative(nameof(SoundAcousticSettings.MaxDistance));

                maxDistance.floatValue = 81f;
                serialized.ApplyModifiedProperties();
                Assert.That(def.Acoustics.Local.MaxDistance, Is.EqualTo(81f));

                Undo.PerformUndo();
                Assert.That(def.Acoustics.Local.MaxDistance, Is.EqualTo(30f));
            }
            finally
            {
                Undo.ClearUndo(def);
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void SoundDef_SerializedProfileAssignmentPreservesLocalAcoustics()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var profile = ScriptableObject.CreateInstance<SoundAcousticProfile>();
            try
            {
                def.MinDistance = 8f;
                var serialized = new SerializedObject(def);
                serialized.FindProperty("_acoustics")
                    .FindPropertyRelative(nameof(SoundAcousticSelection.Profile))
                    .objectReferenceValue = profile;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(def.Acoustics.Profile, Is.SameAs(profile));
                Assert.That(def.Acoustics.Local.MinDistance, Is.EqualTo(8f));
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(profile);
            }
        }

        private static string LegacyYaml<T>(
            string assetName,
            DistanceAttenuationProfile attenuation,
            SpatialBlendProfile blend,
            SoundSpatialProfile spatial)
            where T : ScriptableObject
        {
            var scriptHost = ScriptableObject.CreateInstance<T>();
            try
            {
                var script = MonoScript.FromScriptableObject(scriptHost);
                var scriptGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
                var attenuationGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(attenuation));
                var blendGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(blend));
                var spatialGuid = spatial == null
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(spatial));
                var spatialLine = spatial == null
                    ? string.Empty
                    : $"  SpatialProfile: {{fileID: 11400000, guid: {spatialGuid}, type: 2}}\n";
                return $"%YAML 1.1\n" +
                    $"%TAG !u! tag:unity3d.com,2011:\n" +
                    $"--- !u!114 &11400000\n" +
                    $"MonoBehaviour:\n" +
                    $"  m_ObjectHideFlags: 0\n" +
                    $"  m_CorrespondingSourceObject: {{fileID: 0}}\n" +
                    $"  m_PrefabInstance: {{fileID: 0}}\n" +
                    $"  m_PrefabAsset: {{fileID: 0}}\n" +
                    $"  m_GameObject: {{fileID: 0}}\n" +
                    $"  m_Enabled: 1\n" +
                    $"  m_EditorHideFlags: 0\n" +
                    $"  m_Script: {{fileID: 11500000, guid: {scriptGuid}, type: 3}}\n" +
                    $"  m_Name: {assetName}\n" +
                    $"  m_EditorClassIdentifier:\n" +
                    spatialLine +
                    $"  MinDistance: 3\n" +
                    $"  MaxDistance: 18\n" +
                    $"  DistanceAttenuation: 0\n" +
                    $"  AttenuationProfile: {{fileID: 11400000, guid: {attenuationGuid}, type: 2}}\n" +
                    $"  InheritSpatialBlend: 0\n" +
                    $"  SpatialBlendProfile: {{fileID: 11400000, guid: {blendGuid}, type: 2}}\n" +
                    $"  MonoDistance: 2\n" +
                    $"  FullSpatialDistance: 6\n";
            }
            finally
            {
                Object.DestroyImmediate(scriptHost);
            }
        }

        private static void AssertSettings(
            SoundAcousticSettings settings,
            DistanceAttenuationProfile attenuation,
            SpatialBlendProfile blend)
        {
            Assert.That(settings.MinDistance, Is.EqualTo(3f));
            Assert.That(settings.MaxDistance, Is.EqualTo(18f));
            Assert.That(settings.DistanceAttenuation, Is.False);
            Assert.That(settings.AttenuationProfile, Is.SameAs(attenuation));
            Assert.That(settings.InheritSpatialBlend, Is.False);
            Assert.That(settings.SpatialBlendProfile, Is.SameAs(blend));
            Assert.That(settings.MonoDistance, Is.EqualTo(2f));
            Assert.That(settings.FullSpatialDistance, Is.EqualTo(6f));
        }
    }
}
