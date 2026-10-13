using System;

namespace Bun3.State
{
    /// <summary>Marks a partial class for tracked state generation.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TrackedStateAttribute : Attribute
    {
    }
}
