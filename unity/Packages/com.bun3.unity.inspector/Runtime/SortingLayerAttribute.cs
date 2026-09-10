using System;
using UnityEngine;

namespace Bun3.Unity.Inspector
{
    /// <summary>Displays an integer sorting layer ID or string sorting layer name as a dropdown.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SortingLayerAttribute : PropertyAttribute
    {
    }
}
