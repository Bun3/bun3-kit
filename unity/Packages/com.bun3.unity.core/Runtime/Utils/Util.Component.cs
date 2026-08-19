// Util partial — component get/attach helpers.
using UnityEngine;

namespace Bun3.Unity.Core.Utils
{
    public static partial class Util
    {
        public static T GetOrAdd<T>(this GameObject obj) where T : Component
        {
            if (!obj.TryGetComponent(out T component))
                component = obj.AddComponent<T>();
            return component;
        }
    }
}
