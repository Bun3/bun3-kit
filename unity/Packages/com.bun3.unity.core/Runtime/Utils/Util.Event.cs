// Util partial — UnityEvent helpers.
using UnityEngine;
using UnityEngine.Events;

namespace Bun3.Unity.Core.Utils
{
    public static partial class Util
    {
        public static void ReplaceListener<T>(this UnityEvent<T> unityEvent, UnityAction<T> listener)
        {
            unityEvent.RemoveAllListeners();
            unityEvent.AddListener(listener);
        }
    }
}
