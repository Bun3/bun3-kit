// Util partial — Object 수명(SafeDestroy 등) 헬퍼 담당.
using UnityEngine;

namespace Bun3.Unity.Core.Utils
{
    public static partial class Util
    {
        /// <summary>
        /// 플레이 여부에 따라 <see cref="Object.Destroy(Object)"/>/<see cref="Object.DestroyImmediate(Object)"/>를
        /// 골라 파괴한다. 에디터(비플레이)와 런타임을 오가는 코드에서 분기 없이 쓴다.
        /// null이거나 이미 파괴된 대상은 무시한다.
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
