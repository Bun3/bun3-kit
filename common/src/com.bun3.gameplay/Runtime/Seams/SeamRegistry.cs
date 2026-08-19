#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Seams
{
    /// <summary>Registry managing the registered seam contracts.</summary>
    public sealed class SeamRegistry
    {
        private readonly IReadOnlyDictionary<ushort, IMagnitudeCalc> _magnitudeCalcs;
        private readonly IReadOnlyDictionary<ushort, IExecutionCalc> _executionCalcs;
        private readonly IReadOnlyDictionary<ushort, ITargetSelector> _targetSelectors;

        internal SeamRegistry(
            IReadOnlyDictionary<ushort, IMagnitudeCalc> magnitudeCalcs,
            IReadOnlyDictionary<ushort, IExecutionCalc> executionCalcs,
            IReadOnlyDictionary<ushort, ITargetSelector> targetSelectors)
        {
            _magnitudeCalcs = magnitudeCalcs;
            _executionCalcs = executionCalcs;
            _targetSelectors = targetSelectors;
        }

        /// <summary>Returns the magnitude calculation contract registered for the tag.</summary>
        /// <param name="tag">Tag to look up.</param>
        /// <returns>Registered contract.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the tag is not registered.</exception>
        internal IMagnitudeCalc GetMagnitudeCalc(GameplayTag tag)
        {
            if (_magnitudeCalcs.TryGetValue(tag.Index, out var calc))
                return calc;
            throw new KeyNotFoundException($"No magnitude calc registered for tag {tag.Index}.");
        }

        /// <summary>Returns the effect execution contract registered for the tag.</summary>
        /// <param name="tag">Tag to look up.</param>
        /// <returns>Registered contract.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the tag is not registered.</exception>
        internal IExecutionCalc GetExecutionCalc(GameplayTag tag)
        {
            if (_executionCalcs.TryGetValue(tag.Index, out var exec))
                return exec;
            throw new KeyNotFoundException($"No execution calc registered for tag {tag.Index}.");
        }

        /// <summary>Returns the target selector contract registered for the tag.</summary>
        /// <param name="tag">Tag to look up.</param>
        /// <returns>Registered contract.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the tag is not registered.</exception>
        internal ITargetSelector GetTargetSelector(GameplayTag tag)
        {
            if (_targetSelectors.TryGetValue(tag.Index, out var selector))
                return selector;
            throw new KeyNotFoundException($"No target selector registered for tag {tag.Index}.");
        }

        /// <summary>Tries to get the magnitude calculation contract registered for the tag.</summary>
        /// <param name="tag">Tag to look up.</param>
        /// <param name="calc">Found contract.</param>
        /// <returns>True when the tag is registered.</returns>
        internal bool TryGetMagnitudeCalc(GameplayTag tag, out IMagnitudeCalc? calc)
        {
            return _magnitudeCalcs.TryGetValue(tag.Index, out calc);
        }

        /// <summary>Tries to get the effect execution contract registered for the tag.</summary>
        /// <param name="tag">Tag to look up.</param>
        /// <param name="exec">Found contract.</param>
        /// <returns>True when the tag is registered.</returns>
        internal bool TryGetExecutionCalc(GameplayTag tag, out IExecutionCalc? exec)
        {
            return _executionCalcs.TryGetValue(tag.Index, out exec);
        }

        /// <summary>Tries to get the target selector contract registered for the tag.</summary>
        /// <param name="tag">Tag to look up.</param>
        /// <param name="selector">Found contract.</param>
        /// <returns>True when the tag is registered.</returns>
        internal bool TryGetTargetSelector(GameplayTag tag, out ITargetSelector? selector)
        {
            return _targetSelectors.TryGetValue(tag.Index, out selector);
        }
    }
}
