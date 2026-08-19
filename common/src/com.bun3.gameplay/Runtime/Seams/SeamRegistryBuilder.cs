#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Seams
{
    /// <summary>Builder that registers and validates seam contracts.</summary>
    public sealed class SeamRegistryBuilder
    {
        private readonly Dictionary<ushort, IMagnitudeCalc> _magnitudeCalcs = new();
        private readonly Dictionary<ushort, IExecutionCalc> _executionCalcs = new();
        private readonly Dictionary<ushort, ITargetSelector> _targetSelectors = new();
        private bool _built;

        /// <summary>Registers a magnitude calculation contract.</summary>
        /// <param name="tag">Tag to register under.</param>
        /// <param name="calc">Calculation implementation.</param>
        /// <exception cref="ArgumentNullException">Thrown when calc is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when called after Build, or the tag is already registered.</exception>
        public void RegisterMagnitudeCalc(GameplayTag tag, IMagnitudeCalc calc)
        {
            if (_built) throw new InvalidOperationException("Cannot register after Build.");
            if (calc == null)
                throw new ArgumentNullException(nameof(calc));
            if (_magnitudeCalcs.ContainsKey(tag.Index))
                throw new InvalidOperationException($"Tag {tag.Index} is already registered.");
            _magnitudeCalcs[tag.Index] = calc;
        }

        /// <summary>Registers an effect execution contract.</summary>
        /// <param name="tag">Tag to register under.</param>
        /// <param name="exec">Execution implementation.</param>
        /// <exception cref="ArgumentNullException">Thrown when exec is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when called after Build, or the tag is already registered.</exception>
        public void RegisterExecutionCalc(GameplayTag tag, IExecutionCalc exec)
        {
            if (_built) throw new InvalidOperationException("Cannot register after Build.");
            if (exec == null)
                throw new ArgumentNullException(nameof(exec));
            if (_executionCalcs.ContainsKey(tag.Index))
                throw new InvalidOperationException($"Tag {tag.Index} is already registered.");
            _executionCalcs[tag.Index] = exec;
        }

        /// <summary>Registers a target selector contract.</summary>
        /// <param name="tag">Tag to register under.</param>
        /// <param name="selector">Selector implementation.</param>
        /// <exception cref="ArgumentNullException">Thrown when selector is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when called after Build, or the tag is already registered.</exception>
        public void RegisterTargetSelector(GameplayTag tag, ITargetSelector selector)
        {
            if (_built) throw new InvalidOperationException("Cannot register after Build.");
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));
            if (_targetSelectors.ContainsKey(tag.Index))
                throw new InvalidOperationException($"Tag {tag.Index} is already registered.");
            _targetSelectors[tag.Index] = selector;
        }

        /// <summary>Builds the seam registry from the registered contracts.</summary>
        /// <param name="catalog">Tag catalog.</param>
        /// <returns>Built registry.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a tag is not under its reserved root, is the root tag itself, or the root tag is missing from the catalog.</exception>
        public SeamRegistry Build(TagCatalog catalog)
        {
            _built = true;
            const string magnitudeRoot = "calc.magnitude";
            const string executionRoot = "calc.execution";
            const string selectorRoot = "selector";

            GameplayTag magRootTag = GameplayTag.None;
            GameplayTag execRootTag = GameplayTag.None;
            GameplayTag selectorRootTag = GameplayTag.None;

            if (_magnitudeCalcs.Count > 0)
            {
                if (!catalog.TryGet(magnitudeRoot, out magRootTag))
                    throw new InvalidOperationException($"Reserved root tag missing from catalog: {magnitudeRoot}");
            }

            if (_executionCalcs.Count > 0)
            {
                if (!catalog.TryGet(executionRoot, out execRootTag))
                    throw new InvalidOperationException($"Reserved root tag missing from catalog: {executionRoot}");
            }

            if (_targetSelectors.Count > 0)
            {
                if (!catalog.TryGet(selectorRoot, out selectorRootTag))
                    throw new InvalidOperationException($"Reserved root tag missing from catalog: {selectorRoot}");
            }

            foreach (var kvp in _magnitudeCalcs)
            {
                var tag = new GameplayTag(kvp.Key);
                if (tag == magRootTag)
                    throw new InvalidOperationException($"The root tag itself cannot be registered: {magnitudeRoot}");
                if (!catalog.IsAncestorOrSelf(magRootTag, tag))
                    throw new InvalidOperationException($"Tag {tag.Index} is not under root {magnitudeRoot}.");
            }

            foreach (var kvp in _executionCalcs)
            {
                var tag = new GameplayTag(kvp.Key);
                if (tag == execRootTag)
                    throw new InvalidOperationException($"The root tag itself cannot be registered: {executionRoot}");
                if (!catalog.IsAncestorOrSelf(execRootTag, tag))
                    throw new InvalidOperationException($"Tag {tag.Index} is not under root {executionRoot}.");
            }

            foreach (var kvp in _targetSelectors)
            {
                var tag = new GameplayTag(kvp.Key);
                if (tag == selectorRootTag)
                    throw new InvalidOperationException($"The root tag itself cannot be registered: {selectorRoot}");
                if (!catalog.IsAncestorOrSelf(selectorRootTag, tag))
                    throw new InvalidOperationException($"Tag {tag.Index} is not under root {selectorRoot}.");
            }

            return new SeamRegistry(
                new Dictionary<ushort, IMagnitudeCalc>(_magnitudeCalcs),
                new Dictionary<ushort, IExecutionCalc>(_executionCalcs),
                new Dictionary<ushort, ITargetSelector>(_targetSelectors));
        }
    }
}
