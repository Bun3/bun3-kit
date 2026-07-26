namespace Bun3.Unity.Core.Utils
{
    public static partial class Util
    {
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