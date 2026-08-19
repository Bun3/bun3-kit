// Util partial — Object lifetime helpers (SafeDestroy etc.).
using UnityEngine;

namespace Bun3.Unity.Core.Utils
{
    public static partial class Util
    {
        /// <summary>
        /// Destroys via <see cref="Object.Destroy(Object)"/> in play mode or
        /// <see cref="Object.DestroyImmediate(Object)"/> in edit mode.
        /// Ignores null or already-destroyed targets.
        /// </summary>
        public static void SafeDestroy(this Object target)
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
