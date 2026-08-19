#nullable enable
using System;
using System.Diagnostics;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// Marks a public const string field as a native GameplayTag declaration.
    /// </summary>
    [Conditional("BUN3_GAMEPLAY_TAGS_AUTHORING")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class NativeGameplayTagAttribute : Attribute
    {
        /// <summary>
        /// Initializes the native GameplayTag attribute.
        /// </summary>
        /// <param name="comment">Per-source description shown in authoring tools.</param>
        public NativeGameplayTagAttribute(string comment = "")
        {
            Comment = comment;
        }

        /// <summary>
        /// Gets the per-source description shown in authoring tools.
        /// </summary>
        public string Comment { get; }
    }
}
