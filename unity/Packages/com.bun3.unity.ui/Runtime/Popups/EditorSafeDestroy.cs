using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>플레이 여부에 따라 Destroy/DestroyImmediate를 고르는 내부 헬퍼.</summary>
    internal static class EditorSafeDestroy
    {
        internal static void Destroy(Object target)
        {
            if (!target)
                return;

            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
