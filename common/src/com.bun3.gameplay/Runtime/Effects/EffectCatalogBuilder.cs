#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Effects
{
    /// <summary>Builder that collects effect specs and validates/compiles them all in Build.</summary>
    public sealed class EffectCatalogBuilder
    {
        private readonly List<EffectSpec> _specs = new List<EffectSpec>();

        /// <summary>Registers an effect spec. Validation happens all at once in Build.</summary>
        /// <param name="spec">Effect spec to register.</param>
        /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
        public void Add(EffectSpec spec)
        {
            if (spec is null) throw new ArgumentNullException(nameof(spec));
            _specs.Add(spec);
        }

        /// <summary>Validates and compiles the registered specs into an effect catalog.</summary>
        /// <param name="tags">Catalog used for tag resolution.</param>
        /// <param name="seams">Seam registry used to resolve CalcTag/SelectorTag.</param>
        /// <param name="attributes">Attribute registry used to validate Operand attribute references.</param>
        /// <returns>The built effect catalog.</returns>
        /// <exception cref="InvalidOperationException">Any spec violates a validation rule.</exception>
        public EffectCatalog Build(TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            // Pass 1: name -> id. Filled completely first so forward references (chains to later effects) work.
            var nameToId = new Dictionary<string, int>(_specs.Count, StringComparer.Ordinal);
            for (var i = 0; i < _specs.Count; i++)
            {
                var name = _specs[i].Name;
                if (string.IsNullOrEmpty(name))
                {
                    throw new InvalidOperationException($"Effect name cannot be empty (registration index {i}).");
                }

                if (!nameToId.TryAdd(name, i))
                {
                    throw new InvalidOperationException($"Duplicate effect name: {name}");
                }
            }

            // Pass 2: resolve/validate/compile each spec.
            var compiled = new CompiledEffectSpec[_specs.Count];
            for (var i = 0; i < _specs.Count; i++)
            {
                compiled[i] = CompileSpec(_specs[i], _specs, nameToId, tags, seams, attributes);
            }

            var warnings = DetectChainCycles(compiled);
            DetectLevelFromStackScaleWithStackWarnings(_specs, warnings);
            return new EffectCatalog(nameToId, compiled, warnings);
        }

        private static CompiledEffectSpec CompileSpec(
            EffectSpec spec, List<EffectSpec> allSpecs, Dictionary<string, int> nameToId,
            TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            ValidateDurationTypeFields(spec);
            ValidateExecutionEligibility(spec);
            ValidateStackOverflowConsistency(spec);
            ValidateInstantOrPeriodicModifierOps(spec);
            ValidateStackVocabConsistency(spec);
            ValidateDurationScaleFields(spec);
            ValidateDrFields(spec);

            var grantedTags = MagnitudeCompiler.ResolveTags(spec.GrantedTags, $"Effect '{spec.Name}'", tags, "granted");
            var assetTags = MagnitudeCompiler.ResolveTags(spec.AssetTags, $"Effect '{spec.Name}'", tags, "asset");
            var immunityTags = MagnitudeCompiler.ResolveTags(spec.ImmunityTags, $"Effect '{spec.Name}'", tags, "immunity");
            var removeOnApplyTags = MagnitudeCompiler.ResolveTags(spec.RemoveOnApplyTags, $"Effect '{spec.Name}'", tags, "RemoveOnApply");

            var modifiers = new CompiledModifier[spec.Modifiers.Count];
            for (var i = 0; i < spec.Modifiers.Count; i++)
            {
                var m = spec.Modifiers[i];
                if (!attributes.Contains(m.AttributeId))
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': references unregistered attribute {m.AttributeId}.");
                }

                var (magBase, perLevel, calc, byLevel, tail, increment) =
                    MagnitudeCompiler.Resolve(m.Magnitude, $"Effect '{spec.Name}'", spec.MaxLevel, tags, seams, attributes);
                modifiers[i] = new CompiledModifier(
                    m.AttributeId, m.Op, magBase, perLevel, calc, byLevel, tail, increment, m.ScaleWithStack);
            }

            var executions = new CompiledExecution[spec.Executions.Count];
            for (var i = 0; i < spec.Executions.Count; i++)
            {
                var e = spec.Executions[i];
                var calc = MagnitudeCompiler.ResolveExecutionCalc(e.CalcTag, $"Effect '{spec.Name}'", tags, seams);
                var inputs = new Operand[e.Inputs.Count];
                for (var j = 0; j < e.Inputs.Count; j++)
                {
                    MagnitudeCompiler.ValidateOperandAttribute(e.Inputs[j], $"Effect '{spec.Name}'", attributes);
                    inputs[j] = e.Inputs[j];
                }

                executions[i] = new CompiledExecution(calc, inputs);
            }

            var applicationConditions = ResolveConditions(
                spec.ApplicationConditions, $"Effect '{spec.Name}'", attributes, allowSourceAttribute: true);
            var ongoingConditions = ResolveConditions(
                spec.OngoingConditions, $"Effect '{spec.Name}'", attributes, allowSourceAttribute: false);

            var chains = new CompiledChain[spec.Chains.Count];
            for (var i = 0; i < spec.Chains.Count; i++)
            {
                chains[i] = ResolveChain(spec.Chains[i], spec, allSpecs, nameToId, tags, seams, attributes);
            }

            var overflowEffectId = -1;
            if (spec.Stack.OnOverflow == StackOverflow.ApplyEffect)
            {
                var overflowName = spec.Stack.OverflowEffectName;
                if (string.IsNullOrEmpty(overflowName) || !nameToId.TryGetValue(overflowName!, out overflowEffectId))
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': cannot resolve OverflowEffectName: {overflowName}");
                }
            }

            CompiledMagnitude? chanceToApply = null;
            if (spec.ChanceToApply != null)
            {
                var (chanceBase, chancePerLevel, chanceCalc, chanceByLevel, chanceTail, chanceIncrement) =
                    MagnitudeCompiler.Resolve(spec.ChanceToApply, $"Effect '{spec.Name}'", spec.MaxLevel, tags, seams, attributes);
                chanceToApply = new CompiledMagnitude(
                    chanceBase, chancePerLevel, chanceCalc, chanceByLevel, chanceTail, chanceIncrement);
            }

            var durationPerLevel = spec.DurationPerLevel?.ToArray();

            CompiledMagnitude? durationScale = null;
            if (spec.DurationScale != null)
            {
                var (scaleBase, scalePerLevel, scaleCalc, scaleByLevel, scaleTail, scaleIncrement) =
                    MagnitudeCompiler.Resolve(spec.DurationScale, $"Effect '{spec.Name}'", spec.MaxLevel, tags, seams, attributes);
                durationScale = new CompiledMagnitude(
                    scaleBase, scalePerLevel, scaleCalc, scaleByLevel, scaleTail, scaleIncrement);
            }

            var drCategory = GameplayTag.None;
            if (!string.IsNullOrEmpty(spec.DrCategory) && !tags.TryGet(spec.DrCategory!, out drCategory))
            {
                throw new InvalidOperationException(
                    $"Effect '{spec.Name}': cannot resolve DR category tag: {spec.DrCategory}");
            }

            var drStageMultipliers = spec.DrStageMultipliers.Count > 0
                ? spec.DrStageMultipliers.ToArray()
                : Array.Empty<BigNum>();

            return new CompiledEffectSpec(
                spec.Name, spec.MaxLevel, spec.DurationType, spec.DurationTicks, spec.PeriodTicks, spec.Stack,
                modifiers, executions, applicationConditions, ongoingConditions,
                grantedTags, assetTags, immunityTags, chains, overflowEffectId,
                removeOnApplyTags, chanceToApply, durationPerLevel, durationScale,
                drCategory, spec.DrWindowTicks, drStageMultipliers);
        }

        private static void ValidateDurationTypeFields(EffectSpec spec)
        {
            switch (spec.DurationType)
            {
                case EffectDurationType.Instant:
                    if (spec.DurationTicks != 0 || spec.PeriodTicks != 0)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{spec.Name}': Instant requires DurationTicks/PeriodTicks to be 0.");
                    }

                    if (spec.OngoingConditions.Count != 0)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{spec.Name}': Instant cannot have OngoingConditions.");
                    }

                    if (spec.GrantedTags.Count != 0)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{spec.Name}': Instant cannot have GrantedTags.");
                    }

                    if (spec.Stack.MaxStack != 0)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{spec.Name}': Instant requires Stack.MaxStack to be 0.");
                    }

                    break;

                case EffectDurationType.Duration:
                    // With DurationPerLevel, DurationTicks must stay unused (0) — the mutual-exclusion
                    // rule is validated by ValidateDurationScaleFields.
                    if (spec.DurationPerLevel == null && spec.DurationTicks <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{spec.Name}': Duration requires DurationTicks > 0 (or use DurationPerLevel).");
                    }

                    break;

                case EffectDurationType.Infinite:
                    if (spec.DurationTicks != 0)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{spec.Name}': Infinite requires DurationTicks to be 0.");
                    }

                    break;
            }
        }

        private static void ValidateExecutionEligibility(EffectSpec spec)
        {
            if (spec.Executions.Count > 0
                && spec.DurationType != EffectDurationType.Instant
                && spec.PeriodTicks <= 0)
            {
                throw new InvalidOperationException(
                    $"Effect '{spec.Name}': Executions are only allowed for Instant or PeriodTicks > 0.");
            }
        }

        // Instant or periodic (PeriodTicks > 0) specs allow Add modifiers only. Multiply/Override carry
        // aggregation semantics (non-periodic Duration/Infinite only) and do not exist on paths that apply
        // immediately without an instance — a percent change is equivalently Add + a self-referencing
        // operand (e.g. Operand.Attribute(id, -0.3) = "reduce current value by 30%").
        private static void ValidateInstantOrPeriodicModifierOps(EffectSpec spec)
        {
            if (spec.DurationType != EffectDurationType.Instant && spec.PeriodTicks <= 0) return;

            for (var i = 0; i < spec.Modifiers.Count; i++)
            {
                if (spec.Modifiers[i].Op != AttributeModifierOp.Add)
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': Instant/periodic modifiers only allow Add — " +
                        "express percent changes as Add + a self-referencing operand (e.g. Operand.Attribute(id, -0.3)).");
                }
            }
        }

        private static void ValidateStackOverflowConsistency(EffectSpec spec)
        {
            if (spec.Stack.MaxStack == 0
                && (spec.Stack.OnOverflow == StackOverflow.ApplyEffect
                    || spec.Stack.OnReapply == StackReapply.AddStack))
            {
                throw new InvalidOperationException(
                    $"Effect '{spec.Name}': ApplyEffect/AddStack policies cannot be used when Stack.MaxStack is 0.");
            }
        }

        private static void ValidateStackVocabConsistency(EffectSpec spec)
        {
            if (spec.Stack.LevelFromStack && spec.Stack.MaxStack == 0)
            {
                throw new InvalidOperationException(
                    $"Effect '{spec.Name}': LevelFromStack requires Stack.MaxStack > 0.");
            }

            if (spec.Stack.OnReapply == StackReapply.ExtendCapped)
            {
                if (spec.DurationType != EffectDurationType.Duration)
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': StackReapply.ExtendCapped requires DurationType Duration.");
                }

                if (spec.Stack.ExtendCapMultiplier < BigNum.One)
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': Stack.ExtendCapMultiplier must be at least 1.");
                }
            }
        }

        // DurationPerLevel/DurationScale consistency — both are Duration-only; DurationPerLevel is
        // mutually exclusive with DurationTicks (DurationTicks must be 0) and must match MaxLevel in length.
        private static void ValidateDurationScaleFields(EffectSpec spec)
        {
            if (spec.DurationPerLevel != null)
            {
                if (spec.DurationType != EffectDurationType.Duration)
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': DurationPerLevel requires DurationType Duration.");
                }

                if (spec.DurationTicks != 0)
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': DurationPerLevel cannot be combined with DurationTicks (mutually exclusive).");
                }

                if (spec.MaxLevel < 1)
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': DurationPerLevel requires MaxLevel >= 1 to be declared.");
                }

                if (spec.DurationPerLevel.Count != spec.MaxLevel)
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': DurationPerLevel length ({spec.DurationPerLevel.Count}) must "
                        + $"match MaxLevel ({spec.MaxLevel}).");
                }
            }

            if (spec.DurationScale != null && spec.DurationType != EffectDurationType.Duration)
            {
                throw new InvalidOperationException(
                    $"Effect '{spec.Name}': DurationScale requires DurationType Duration.");
            }
        }

        // DR field consistency — with DrCategory set: Duration-only, window at least 1 tick, stage
        // multipliers non-empty and all non-negative.
        private static void ValidateDrFields(EffectSpec spec)
        {
            if (string.IsNullOrEmpty(spec.DrCategory)) return;

            if (spec.DurationType != EffectDurationType.Duration)
            {
                throw new InvalidOperationException(
                    $"Effect '{spec.Name}': DrCategory requires DurationType Duration.");
            }

            if (spec.DrWindowTicks < 1)
            {
                throw new InvalidOperationException(
                    $"Effect '{spec.Name}': DrCategory requires DrWindowTicks >= 1.");
            }

            if (spec.DrStageMultipliers.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Effect '{spec.Name}': DrCategory requires non-empty DrStageMultipliers.");
            }

            for (var i = 0; i < spec.DrStageMultipliers.Count; i++)
            {
                if (spec.DrStageMultipliers[i].Sign < 0)
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': DrStageMultipliers must be non-negative (index {i}).");
                }
            }
        }

        // LevelFromStack combined with a ScaleWithStack=true modifier compounds level curve × stack
        // multiplier — not an error (may be intended content), but accumulated as a Build warning so the
        // author is aware.
        private static void DetectLevelFromStackScaleWithStackWarnings(List<EffectSpec> specs, List<string> warnings)
        {
            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                if (!spec.Stack.LevelFromStack) continue;

                for (var m = 0; m < spec.Modifiers.Count; m++)
                {
                    if (!spec.Modifiers[m].ScaleWithStack) continue;
                    warnings.Add(
                        $"[warn] Effect '{spec.Name}': LevelFromStack and ScaleWithStack=true are both enabled, "
                        + "so level curve × stack multiplier apply compounded.");
                    break;
                }
            }
        }

        // OngoingConditions cannot have SourceAttribute operands.
        private static CompiledCondition[] ResolveConditions(
            List<ConditionDef> defs, string specName, AttributeRegistry attributes, bool allowSourceAttribute)
        {
            var result = new CompiledCondition[defs.Count];
            for (var i = 0; i < defs.Count; i++)
            {
                var c = defs[i];
                if (!allowSourceAttribute
                    && (c.Left.Kind == OperandKind.SourceAttribute || c.Right.Kind == OperandKind.SourceAttribute))
                {
                    throw new InvalidOperationException(
                        $"{specName}: OngoingConditions cannot have SourceAttribute operands.");
                }

                MagnitudeCompiler.ValidateOperandAttribute(c.Left, specName, attributes);
                MagnitudeCompiler.ValidateOperandAttribute(c.Right, specName, attributes);
                result[i] = new CompiledCondition(c.Left, c.Op, c.Right);
            }

            return result;
        }

        // Resolves SelectorTag (if any) and EffectName. LevelRule.Fixed also validates that FixedLevel
        // does not exceed the target spec's declared MaxLevel.
        private static CompiledChain ResolveChain(
            ChainEdgeDef edge, EffectSpec ownerSpec, List<EffectSpec> allSpecs, Dictionary<string, int> nameToId,
            TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            if (string.IsNullOrEmpty(edge.EffectName) || !nameToId.TryGetValue(edge.EffectName, out var effectId))
            {
                throw new InvalidOperationException(
                    $"Effect '{ownerSpec.Name}': cannot resolve chain target effect: {edge.EffectName}");
            }

            if (edge.LevelRule == ChainLevelRule.Fixed)
            {
                var targetMaxLevel = allSpecs[effectId].MaxLevel;
                if (targetMaxLevel > 0 && edge.FixedLevel > targetMaxLevel)
                {
                    throw new InvalidOperationException(
                        $"Effect '{ownerSpec.Name}': FixedLevel ({edge.FixedLevel}) for chain target '{edge.EffectName}' "
                        + $"exceeds MaxLevel ({targetMaxLevel}).");
                }
            }

            ITargetSelector? selector = null;
            if (!string.IsNullOrEmpty(edge.SelectorTag))
            {
                selector = MagnitudeCompiler.ResolveTargetSelector(edge.SelectorTag!, $"Effect '{ownerSpec.Name}'", tags, seams);
            }

            var conditions = ResolveConditions(edge.Conditions, $"Effect '{ownerSpec.Name}'", attributes, allowSourceAttribute: true);
            return new CompiledChain(
                edge.Trigger, effectId, selector, edge.SelectorParams.ToArray(),
                conditions, edge.LevelRule, edge.FixedLevel);
        }

        // Cycle detection via DFS gray/black coloring following OnApplication edges only. A cycle passing
        // through any Duration/Period spec is "low" severity, otherwise "high".
        private static List<string> DetectChainCycles(CompiledEffectSpec[] compiled)
        {
            var warnings = new List<string>();
            var adjacency = new List<int>[compiled.Length];
            for (var i = 0; i < compiled.Length; i++)
            {
                var list = new List<int>();
                foreach (var chain in compiled[i].Chains)
                {
                    if (chain.Trigger == ChainTrigger.OnApplication)
                    {
                        list.Add(chain.EffectId);
                    }
                }

                adjacency[i] = list;
            }

            var color = new byte[compiled.Length];   // 0=white, 1=gray (on stack), 2=black (done)
            var stack = new List<int>();

            void Visit(int node)
            {
                color[node] = 1;
                stack.Add(node);
                foreach (var next in adjacency[node])
                {
                    if (color[next] == 1)
                    {
                        var startIndex = stack.IndexOf(next);
                        var cycleNodes = stack.GetRange(startIndex, stack.Count - startIndex);
                        var hasDurationOrPeriod = cycleNodes.Exists(
                            n => compiled[n].DurationTicks > 0 || compiled[n].PeriodTicks > 0);
                        var severity = hasDurationOrPeriod ? "low" : "high";
                        var names = string.Join(" -> ", cycleNodes.ConvertAll(n => compiled[n].Name));
                        warnings.Add($"[{severity}] Chain cycle: {names} -> {compiled[next].Name}");
                    }
                    else if (color[next] == 0)
                    {
                        Visit(next);
                    }
                }

                stack.RemoveAt(stack.Count - 1);
                color[node] = 2;
            }

            for (var i = 0; i < compiled.Length; i++)
            {
                if (color[i] == 0)
                {
                    Visit(i);
                }
            }

            return warnings;
        }
    }
}
