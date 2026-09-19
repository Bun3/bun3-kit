using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundAcousticSelectionEditorTests
    {
        [Test]
        public void SerializedProfileAssignment_LeavesLocalSettingsUnchanged()
        {
            var host = ScriptableObject.CreateInstance<SelectionHost>();
            var profile = ScriptableObject.CreateInstance<SoundAcousticProfile>();
            try
            {
                host.Selection.Local = SoundAcousticSettings.Default;
                var local = host.Selection.Local;
                local.MonoDistance = 8f;
                host.Selection.Local = local;
                var serialized = new SerializedObject(host);

                serialized.FindProperty(nameof(SelectionHost.Selection))
                    .FindPropertyRelative(nameof(SoundAcousticSelection.Profile)).objectReferenceValue = profile;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(host.Selection.Profile, Is.SameAs(profile));
                Assert.That(host.Selection.Local.MonoDistance, Is.EqualTo(8f));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SerializedLocalEdit_CanBeUndone()
        {
            var host = ScriptableObject.CreateInstance<SelectionHost>();
            try
            {
                host.Selection.Local = SoundAcousticSettings.Default;
                var serialized = new SerializedObject(host);
                var maxDistance = serialized.FindProperty(nameof(SelectionHost.Selection))
                    .FindPropertyRelative(nameof(SoundAcousticSelection.Local))
                    .FindPropertyRelative(nameof(SoundAcousticSettings.MaxDistance));

                maxDistance.floatValue = 81f;
                serialized.ApplyModifiedProperties();
                Assert.That(host.Selection.Local.MaxDistance, Is.EqualTo(81f));

                Undo.PerformUndo();
                Assert.That(host.Selection.Local.MaxDistance, Is.EqualTo(30f));
            }
            finally
            {
                Undo.ClearUndo(host);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SerializedMixedSelection_ReportsMixedProfileReferences()
        {
            var first = ScriptableObject.CreateInstance<SelectionHost>();
            var second = ScriptableObject.CreateInstance<SelectionHost>();
            var profile = ScriptableObject.CreateInstance<SoundAcousticProfile>();
            try
            {
                first.Selection.Profile = profile;
                second.Selection.Profile = null;
                var serialized = new SerializedObject(new UnityEngine.Object[] { first, second });
                var property = serialized.FindProperty(nameof(SelectionHost.Selection))
                    .FindPropertyRelative(nameof(SoundAcousticSelection.Profile));

                Assert.That(property.hasMultipleDifferentValues, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(profile);
            }
        }

        sealed class SelectionHost : ScriptableObject
        {
            public SoundAcousticSelection Selection = new();
        }
    }
}
