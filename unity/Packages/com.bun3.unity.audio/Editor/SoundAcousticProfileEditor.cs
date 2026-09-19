using UnityEditor;

namespace Bun3.Unity.Audio.Editor
{
    [CustomEditor(typeof(SoundAcousticProfile))]
    internal sealed class SoundAcousticProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var settings = serializedObject.FindProperty(nameof(SoundAcousticProfile.Settings));
            SoundAcousticSettingsGUI.DrawLayout(settings);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
