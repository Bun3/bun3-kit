using System;

namespace Bun3.State
{
    /// <summary>Marks a field whose generated property participates in state tracking.</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class TrackedAttribute : Attribute
    {
    }
}
