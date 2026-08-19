namespace Bun3.Unity.Core.Utils
{
    /// <summary>General-purpose Unity extension methods.</summary>
    /// <remarks>Partial layout: this file (null checks) / Component / Event / Object (lifetime).</remarks>
    public static partial class Util
    {
        /// <summary>Treats both plain null and destroyed Unity objects as null.</summary>
        public static bool IsNull(this object obj)
        {
            switch (obj)
            {
                case null:
                case UnityEngine.Object unityObject when unityObject == null:
                    return true;
                default:
                    return false;
            }
        }
    }
}