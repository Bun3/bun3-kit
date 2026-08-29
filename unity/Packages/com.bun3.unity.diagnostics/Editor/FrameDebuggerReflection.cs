using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditorInternal;

namespace Bun3.Unity.Diagnostics
{
    // Name-based reflection over UnityEditor's internal FrameDebuggerUtility. Bind() reports what
    // is missing instead of throwing, so editor upgrades fail loudly in the binding test rather
    // than silently at dump time. FrameDebuggerUtility lives in a split editor module assembly, so
    // the type is searched across all loaded assemblies, not typeof(Editor).Assembly.
    internal static class FrameDebuggerReflection
    {
        const BindingFlags Flags =
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static Type s_Util;
        static MethodInfo s_GetFrameEventData;
        static MethodInfo s_GetFrameEvents;
        static MethodInfo s_GetBatchBreakCauseStrings;
        static MethodInfo s_SetEnabled;
        static PropertyInfo s_Limit;

        internal static bool IsBound { get; private set; }

        internal static List<string> Bind()
        {
            var missing = new List<string>();
            IsBound = false;
            s_Util = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .FirstOrDefault(t => t.Name == "FrameDebuggerUtility");
            if (s_Util == null)
            {
                missing.Add("FrameDebuggerUtility");
                return missing;
            }

            s_GetFrameEventData = s_Util.GetMethods(Flags).FirstOrDefault(m => m.Name == "GetFrameEventData");
            if (s_GetFrameEventData == null)
                missing.Add("FrameDebuggerUtility.GetFrameEventData");
            s_GetFrameEvents = s_Util.GetMethods(Flags).FirstOrDefault(m => m.Name == "GetFrameEvents");
            if (s_GetFrameEvents == null)
                missing.Add("FrameDebuggerUtility.GetFrameEvents");
            s_Limit = s_Util.GetProperties(Flags).FirstOrDefault(p => p.Name == "limit");
            if (s_Limit == null)
                missing.Add("FrameDebuggerUtility.limit");
            if (s_Util.GetProperties(Flags).FirstOrDefault(p => p.Name == "count") == null
                && s_Util.GetFields(Flags).FirstOrDefault(f => f.Name == "count") == null)
                missing.Add("FrameDebuggerUtility.count");

            IsBound = missing.Count == 0;

            // Optional members: their absence only degrades features (raw cause indexes, no
            // auto-enable) and must not block dumping, but the binding test still reports them.
            s_GetBatchBreakCauseStrings = s_Util.GetMethods(Flags).FirstOrDefault(m => m.Name == "GetBatchBreakCauseStrings");
            if (s_GetBatchBreakCauseStrings == null)
                missing.Add("FrameDebuggerUtility.GetBatchBreakCauseStrings (optional)");
            s_SetEnabled = s_Util.GetMethods(Flags).FirstOrDefault(m => m.Name == "SetEnabled");
            if (s_SetEnabled == null)
                missing.Add("FrameDebuggerUtility.SetEnabled (optional)");

            return missing;
        }

        internal static int GetCount() => Convert.ToInt32(GetStatic("count") ?? 0);

        internal static void SetLimit(int value)
        {
            s_Limit.SetValue(null, value);
            InternalEditorUtility.RepaintAllViews();
        }

        internal static void Repaint() => InternalEditorUtility.RepaintAllViews();

        internal static bool TryEnable()
        {
            if (s_SetEnabled == null)
                return false;
            try
            {
                s_SetEnabled.Invoke(null, new object[] { true, ProfilerDriver.connectedProfiler });
                InternalEditorUtility.RepaintAllViews();
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static string[] GetEventTypeNames(int count)
        {
            var names = new string[count];
            var events = (Array)s_GetFrameEvents.Invoke(null, null);
            for (int i = 0; i < count; i++)
            {
                names[i] = "?";
                if (i >= events.Length)
                    continue;
                object ev = events.GetValue(i);
                names[i] = (GetMember(ev, "type") ?? GetMember(ev, "Type"))?.ToString() ?? "?";
            }

            return names;
        }

        internal static string[] GetBreakCauseStrings()
        {
            try
            {
                return (string[])s_GetBatchBreakCauseStrings?.Invoke(null, null);
            }
            catch
            {
                return null;
            }
        }

        internal static Type GetEventDataType()
        {
            if (s_GetFrameEventData == null)
                return null;
            var pt = s_GetFrameEventData.GetParameters()[1].ParameterType;
            return pt.IsByRef ? pt.GetElementType() : pt;
        }

        internal static bool TryGetEventData(int index, out Dictionary<string, string> fields)
        {
            fields = null;
            var ps = s_GetFrameEventData.GetParameters();
            object[] args =
            {
                index,
                ps[1].ParameterType.IsByRef ? null : Activator.CreateInstance(GetEventDataType()),
            };
            bool ok;
            try
            {
                ok = (bool)s_GetFrameEventData.Invoke(null, args);
            }
            catch
            {
                return false;
            }

            if (!ok || args[1] == null)
                return false;

            // Right after moving the limit, the previous event's data can linger; require an
            // index match before trusting the payload.
            object fei = GetMember(args[1], "frameEventIndex") ?? GetMember(args[1], "FrameEventIndex");
            if (fei != null && Convert.ToInt32(fei) != index)
                return false;

            fields = Flatten(args[1]);
            return true;
        }

        static Dictionary<string, string> Flatten(object data)
        {
            var fields = new Dictionary<string, string>();
            foreach (var fi in data.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object v;
                try
                {
                    v = fi.GetValue(data);
                }
                catch
                {
                    continue;
                }

                if (v == null)
                    continue;
                var vt = v.GetType();
                if (vt.IsPrimitive || vt.IsEnum || v is string)
                    fields[fi.Name.TrimStart('m', '_')] = v.ToString();
                else if (v is UnityEngine.Object uo && uo)
                    fields[fi.Name.TrimStart('m', '_')] = uo.name;
            }

            return fields;
        }

        static object GetStatic(string name)
        {
            var p = s_Util.GetProperty(name, Flags);
            if (p != null)
                return p.GetValue(null);
            return s_Util.GetField(name, Flags)?.GetValue(null);
        }

        static object GetMember(object o, string name)
        {
            var t = o.GetType();
            var f = t.GetField(name, Flags) ?? t.GetField("m_" + char.ToUpper(name[0]) + name.Substring(1), Flags);
            if (f != null)
                return f.GetValue(o);
            var p = t.GetProperty(name, Flags);
            return p != null && p.GetIndexParameters().Length == 0 ? p.GetValue(o) : null;
        }
    }
}
