#nullable enable
using System;
using System.Diagnostics;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// Declares the stable source identity of an assembly that provides native GameplayTag declarations.
    /// </summary>
    [Conditional("BUN3_GAMEPLAY_TAGS_AUTHORING")]
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class GameplayTagSourceAttribute : Attribute
    {
        /// <summary>
        /// Initializes the native GameplayTag source attribute.
        /// </summary>
        /// <param name="sourceId">Stable lowercase identifier of the source.</param>
        /// <param name="displayName">Source name shown to users.</param>
        public GameplayTagSourceAttribute(string sourceId, string displayName)
        {
            SourceId = sourceId;
            DisplayName = displayName;
        }

        /// <summary>
        /// Gets the stable lowercase identifier of the source.
        /// </summary>
        public string SourceId { get; }

        /// <summary>
        /// Gets the source name shown to users.
        /// </summary>
        public string DisplayName { get; }
    }
}
