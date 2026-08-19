// Util partial — 컴포넌트 취득·부착 헬퍼 담당.
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
