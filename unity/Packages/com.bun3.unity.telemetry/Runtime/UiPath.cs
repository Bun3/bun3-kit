using Cysharp.Text;
using UnityEngine;

namespace Bun3.Unity.Telemetry
{
    static class UiPath
    {
        /// <summary>Hierarchy path joined with "/" from the root, without "(Clone)".</summary>
        internal static string Of(Transform t)
        {
            if (t == null) return "";
            var sb = ZString.CreateStringBuilder();
            try
            {
                Append(ref sb, t);
                return sb.ToString();
            }
            finally { sb.Dispose(); }
        }

        static void Append(ref Utf16ValueStringBuilder sb, Transform t)
        {
            if (t.parent != null)
            {
                Append(ref sb, t.parent);
                sb.Append('/');
            }
            sb.Append(Segment(t.name));
        }

        // Dropdown items are named "Item 2: option text". The option text is not kept.
        static string Segment(string name)
        {
            name = name.Replace("(Clone)", "");
            if (!name.StartsWith("Item ")) return name;
            var i = 5;
            while (i < name.Length && char.IsDigit(name[i])) i++;
            return i > 5 && i < name.Length && name[i] == ':' ? name.Substring(0, i) : name;
        }
    }
}
