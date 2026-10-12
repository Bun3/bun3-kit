#nullable enable
using System;
using Bun3.Gameplay.Seams;

namespace Bun3.Gameplay.Effects
{
    /// <summary>Selector tags the framework registers on every seam registry.</summary>
    public static class BuiltInSelectors
    {
        /// <summary>Tag path of the selector picking the source of the triggering gameplay event.</summary>
        public const string EventSourceTag = "selector.event.source";

        /// <summary>Tag path of the selector picking the target of the triggering gameplay event.</summary>
        public const string EventTargetTag = "selector.event.target";

        internal static readonly ITargetSelector EventSource = new EventSourceSelector();

        internal static readonly ITargetSelector EventTarget = new EventTargetSelector();

        private sealed class EventSourceSelector : ITargetSelector
        {
            public int Select(in SelectorContext ctx, Span<TargetId> results)
            {
                if (results.Length == 0) return 1;
                results[0] = ctx.EventSource;
                return 1;
            }
        }

        private sealed class EventTargetSelector : ITargetSelector
        {
            public int Select(in SelectorContext ctx, Span<TargetId> results)
            {
                if (results.Length == 0) return 1;
                results[0] = ctx.EventTarget;
                return 1;
            }
        }
    }
}
