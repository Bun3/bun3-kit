namespace Bun3.Unity.Core.Utils
{
    /// <summary>Unity 범용 확장 메서드 모음입니다.</summary>
    /// <remarks>partial 구성: 이 파일(null 판정) / Component(컴포넌트) / Event(이벤트) / Object(수명).</remarks>
    public static partial class Util
    {
        /// <summary>순수 null과 Unity 파괴 오브젝트(수명 종료)를 함께 null로 판정합니다.</summary>
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