using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bun3.BadWords.Editor
{
    /// <summary>Checks text line by line against the default filter or a field marked with <see cref="BadWordTesterSourceAttribute"/>.</summary>
    internal sealed class BadWordTesterWindow : EditorWindow
    {
        internal const string DefaultSourceName = "Default";

        [SerializeField] string sourceName = DefaultSourceName;
        [SerializeField] string text = "";
        Vector2 scroll;
        List<Source> sources;

        [MenuItem("Window/Bun3/Bad Word Tester")]
        static void Open() => GetWindow<BadWordTesterWindow>("Bad Word Tester");

        // Runs again after every domain reload, so edited consumer entries are picked up.
        void OnEnable() => sources = FindSources();

        void OnGUI()
        {
            var names = new string[sources.Count];
            int index = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                names[i] = sources[i].Name;
                if (names[i] == sourceName) index = i;
            }
            Source source = sources[EditorGUILayout.Popup("Filter", index, names)];
            sourceName = source.Name;
            if (source.Error != null)
            {
                EditorGUILayout.HelpBox(source.Error, MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Text (one per line)");
            text = EditorGUILayout.TextArea(text, GUILayout.MinHeight(80));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0) continue;
                string match = source.Filter.Explain(line);
                if (match == null) EditorGUILayout.LabelField(line, "pass");
                else EditorGUILayout.LabelField(line, "BLOCKED  " + match, EditorStyles.boldLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        internal static List<Source> FindSources()
        {
            var found = new List<Source>();
            foreach (FieldInfo field in TypeCache.GetFieldsWithAttribute<BadWordTesterSourceAttribute>()) found.Add(Load(field));
            found.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            found.Insert(0, new Source(DefaultSourceName, BadWordFilter.Default, null));
            return found;
        }

        static Source Load(FieldInfo field)
        {
            string name = field.DeclaringType.FullName + "." + field.Name;
            if (!field.IsStatic || field.FieldType != typeof(BadWordFilter))
                return new Source(name, null, "[BadWordTesterSource] needs a static BadWordFilter field.");
            try
            {
                var filter = (BadWordFilter)field.GetValue(null);
                return filter == null ? new Source(name, null, "The field is null.") : new Source(name, filter, null);
            }
            catch (Exception e)
            {
                while (e.InnerException != null) e = e.InnerException;
                return new Source(name, null, e.Message);
            }
        }

        internal readonly struct Source
        {
            public readonly string Name;
            public readonly BadWordFilter Filter;
            public readonly string Error;

            public Source(string name, BadWordFilter filter, string error)
            {
                Name = name;
                Filter = filter;
                Error = error;
            }
        }
    }
}
