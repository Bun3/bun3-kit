using UnityEditor;
using UnityEngine;
using System.Linq;
using Voice = Bun3.Unity.Audio.SteamAudio.Editor.PlanarAcousticDiagnostics.Voice;
namespace Bun3.Unity.Audio.SteamAudio.Editor
{
    /// <summary>Reusable Inspector and Scene view presentation for planar native-path snapshots.</summary>
    public sealed class PlanarAcousticDiagnosticView
    {
        Vector2 scroll;
        int selectedSource, selectedPath;
        /// <summary>Draws source/path selection, distance curves and diagnostics.</summary>
        public void DrawInspector(PlanarAcousticDiagnostics.Snapshot snapshot)
        {
            EditorGUILayout.LabelField(snapshot.nativePathExtension ? "Native path diagnostics: supported" : "Native paths unavailable (distance statistics only)");
            if (snapshot.voices.Length > 0)
            {
                selectedSource = EditorGUILayout.Popup("Source", Mathf.Clamp(selectedSource, 0, snapshot.voices.Length - 1), snapshot.voices.Select(v => v.name).ToArray());
                var selected = snapshot.voices[selectedSource];
                int pathCount = selected.paths?.Length ?? 0;
                if (pathCount > 0) selectedPath = EditorGUILayout.IntSlider("Native path index", selectedPath, 0, pathCount - 1);
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var voice in snapshot.voices)
            {
                EditorGUILayout.LabelField(voice.name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(voice.attenuationEnabled ? $"Distance / volume curve, last key {voice.maximum:F2}m and beyond silent" : "Distance gain disabled (direction/occlusion retained)");
                EditorGUILayout.LabelField(voice.nativeDistancesAvailable ? $"Native distance {voice.nativeMin:F2}~{voice.nativeMax:F2} · evaluations {voice.nativeCount} · positive distance gains {voice.nativeNonzeroCount}" : "No native distance records");
                EditorGUILayout.LabelField($"Stereo width {voice.spatialBlend:F2} (0: mono, 1: binaural; shortest native path)");
                DrawCurve(voice);
                EditorGUILayout.LabelField($"Coverage {voice.routeCovered} · Path signal {voice.hasPath} · Blocked {voice.blocked}");
                if (voice.droppedPaths > 0) EditorGUILayout.HelpBox($"Diagnostic buffer overflow: {voice.droppedPaths}paths omitted", MessageType.Warning);
                if (voice.paths != null) for (int i = 0; i < voice.paths.Length; i++)
                {
                    var path = voice.paths[i];
                    EditorGUILayout.LabelField($"Path {i}: Distance {path.distance:F2} · Gain {path.gain:F4} · Weight {path.weight:F4} · SH input {path.gain * path.weight:F4}" + (path.truncated ? " (vertices truncated)" : ""));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        static void DrawCurve(Voice voice)
        {
            var rect = GUILayoutUtility.GetRect(100, 120, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(.12f,.12f,.12f));
            float end = Mathf.Max(1, voice.maximum * 1.1f, voice.nativeMax * 1.1f);
            if (voice.nativeDistancesAvailable)
            {
                float x = rect.x + voice.nativeMin / end * rect.width;
                float width = Mathf.Max(2, (voice.nativeMax - voice.nativeMin) / end * rect.width);
                EditorGUI.DrawRect(new Rect(x, rect.y, width, rect.height), new Color(1,.8f,0,.25f));
            }
            var points = new Vector3[129];
            for (int i = 0; i < points.Length; i++)
            {
                float d = end * i / (points.Length - 1);
                voice.evaluation ??= new Bun3.Unity.Audio.DistanceAttenuationCurve(voice.curve);
                float gain = voice.attenuationEnabled ? voice.evaluation.Evaluate(d) : 1;
                points[i] = new Vector3(rect.x + rect.width * i / (points.Length - 1), rect.yMax - gain * (rect.height - 15), 0);
            }
            Handles.BeginGUI();
            var previous = Handles.color; Handles.color = Color.cyan;
            Handles.DrawAAPolyLine(2, points); Handles.color = previous;
            Handles.EndGUI();
            GUI.Label(new Rect(rect.x + 4, rect.y + 2, rect.width, 20), $"Distance 0 ~ {end:F1} / Gain 0 ~ 1");
        }

        /// <summary>Draws captured native paths and the selected virtual source in the Scene view.</summary>
        public void DrawScene(PlanarAcousticDiagnostics.Snapshot snapshot)
        {
            if (snapshot == null) return;
            var previous = Handles.color;
            Handles.color = Color.cyan;
            Handles.DrawWireDisc(snapshot.listener, Vector3.forward, .3f);
            Handles.Label(snapshot.listener, "Listener");
            foreach (var voice in snapshot.voices)
            {
                Handles.color = voice.hasPath ? Color.green : Color.gray;
                Handles.DrawWireDisc(voice.position, Vector3.forward, .3f);
                Handles.Label(voice.position, voice.name);
            }
            if (selectedSource < snapshot.voices.Length)
            {
                var paths = snapshot.voices[selectedSource].paths;
                if (paths != null && selectedPath < paths.Length)
                {
                    var path = paths[selectedPath];
                    Handles.color = path.gain * path.weight > 0 ? Color.green : new Color(1, .5f, 0);
                    if (path.probes.Length == 0 && !path.truncated) Handles.DrawAAPolyLine(4, path.source, path.listener);
                    else if (path.probes.Length > 0)
                    {
                        if (path.probes.Length > 1) Handles.DrawAAPolyLine(4, path.probes);
                        Handles.DrawDottedLine(path.source, path.probes[0], 4);
                        if (!path.truncated) Handles.DrawDottedLine(path.probes[path.probes.Length - 1], path.listener, 4);
                    }
                    Handles.color = Color.magenta;
                    Handles.DrawDottedLine(path.listener, path.virtualSource, 5);
                    Handles.DrawWireDisc(path.virtualSource, Vector3.forward, .2f);
                    Handles.Label(path.virtualSource, $"Virtual source / evaluations Distance {path.distance:F2}");
                }
            }
            Handles.color = previous;
        }
}
}
