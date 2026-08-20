#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Effects
{
    /// <summary>Builder that collects effect defs and validates/compiles them all in Build.</summary>
    public sealed class EffectCatalogBuilder
    {
        private readonly List<EffectDef> _defs = new List<EffectDef>();

        /// <summary>Registers an effect def. Validation happens all at once in Build.</summary>
        /// <param name="def">Effect def to register.</param>
        /// <exception cref="ArgumentNullException"><paramref name="def"/> is null.</exception>
        public void Add(EffectDef def)
        {
            if (def is null) throw new ArgumentNullException(nameof(def));
            _defs.Add(def);
        }

        /// <summary>Validates and compiles the registered defs into an effect catalog.</summary>
        /// <param name="tags">Catalog used for tag resolution.</param>
        /// <param name="seams">Seam registry used to resolve CalcTag/SelectorTag.</param>
        /// <param name="attributes">Attribute registry used to validate Operand attribute references.</param>
        /// <returns>The built effect catalog.</returns>
        /// <exception cref="InvalidOperationException">Any def violates a validation rule.</exception>
        public EffectCatalog Build(TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            // Pass 1: name -> id. Filled completely first so forward references (chains to later effects) work.
            var nameToId = new Dictionary<string, int>(_defs.Count, StringComparer.Ordinal);
            for (var i = 0; i < _defs.Count; i++)
            {
                var name = _defs[i].Name;
                if (string.IsNullOrEmpty(name))
                {
                    throw new InvalidOperationException($"Effect name cannot be empty (registration index {i}).");
                }

                if (!nameToId.TryAdd(name, i))
                {
                    throw new InvalidOperationException($"Duplicate effect name: {name}");
                }
            }

            // Pass 2: resolve/validate/compile each def.
            var compiled = new CompiledEffect[_defs.Count];
            for (var i = 0; i < _defs.Count; i++)
            {
                compiled[i] = Compile(_defs[i], _defs, nameToId, tags, seams, attributes);
            }

            var warnings = DetectChainCycles(compiled);
            DetectLevelFromStackScaleWithStackWarnings(_defs, warnings);
            ValidateEventMagnitudeReachability(_defs, nameToId);
            return new EffectCatalog(nameToId, compiled, warnings);
        }

        private static CompiledEffect Compile(
            EffectDef def, List<EffectDef> allDefs, Dictionary<string, int> nameToId,
            TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            ValidateDurationTypeFields(def);
            ValidateExecutionEligibility(def);
            ValidateStackOverflowConsistency(def);
            ValidateInstantOrPeriodicModifierOps(def);
            ValidateStackVocabConsistency(def);
            ValidateDurationScaleFields(def);
            ValidateDrFields(def);
            ValidateOngoingConditionsNoEventMagnitude(def);
            ValidatePeriodicNoEventMagnitudeInModifiersOrExecutions(def);

            for (var i = 0; i < def.GrantedAbilities.Count; i++)
            {
                if (string.IsNullOrEmpty(def.GrantedAbilities[i]))
                    throw new InvalidOperationException($"Effect '{def.Name}': GrantedAbilities entry is empty.");
            }

            var grantedAbilityNames = def.GrantedAbilities.ToArray();

            var grantedTags = MagnitudeCompiler.ResolveTags(def.GrantedTags, $"Effect '{def.Name}'", tags, "granted");
            var assetTags = MagnitudeCompiler.ResolveTags(def.AssetTags, $"Effect '{def.Name}'", tags, "asset");
            var immunityTags = MagnitudeCompiler.ResolveTags(def.ImmunityTags, $"Effect '{def.Name}'", tags, "immunity");
            var removeOnApplyTags = MagnitudeCompiler.ResolveTags(def.RemoveOnApplyTags, $"Effect '{def.Name}'", tags, "RemoveOnApply");

            var modifiers = new CompiledModifier[def.Modifiers.Count];
            for (var i = 0; i < def.Modifiers.Count; i++)
            {
                var m = def.Modifiers[i];
                if (!attributes.Contains(m.AttributeId))
                {
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': references unregistered attribute {m.AttributeId}.");
                }

                var (magBase, perLevel, calc, calcTag, byLevel, tail, increment) =
                    MagnitudeCompiler.Resolve(m.Magnitude, $"Effect '{def.Name}'", def.MaxLevel, tags, seams, attributes);
                modifiers[i] = new CompiledModifier(
                    m.AttributeId, m.Op, magBase, perLevel, calc, calcTag, byLevel, tail, increment,
                    m.ScaleWithStack);
            }

            var executions = new CompiledExecution[def.Executions.Count];
            for (var i = 0; i < def.Executions.Count; i++)
            {
                var e = def.Executions[i];
                var calc = MagnitudeCompiler.ResolveExecutionCalc(
                    e.CalcTag, $"Effect '{def.Name}'", tags, seams, out var calcTag);
                var inputs = new Operand[e.Inputs.Count];
                for (var j = 0; j < e.Inputs.Count; j++)
                {
                    MagnitudeCompiler.ValidateOperandAttribute(e.Inputs[j], $"Effect '{def.Name}'", attributes);
                    inputs[j] = e.Inputs[j];
                }

                executions[i] = new CompiledExecution(calc, calcTag, inputs);
            }

            var applicationConditions = ResolveConditions(
                def.ApplicationConditions, $"Effect '{def.Name}'", attributes, allowSourceAttribute: true);
            var ongoingConditions = ResolveConditions(
                def.OngoingConditions, $"Effect '{def.Name}'", attributes, allowSourceAttribute: false);

            var chains = new CompiledChainEdge[def.Chains.Count];
            for (var i = 0; i < def.Chains.Count; i++)
            {
                chains[i] = ResolveChain(def.Chains[i], def, allDefs, nameToId, tags, seams, attributes);
            }

            var overflowEffectId = -1;
            if (def.Stack.OnOverflow == StackOverflow.ApplyEffect)
            {
                var overflowName = def.Stack.OverflowEffectName;
                if (string.IsNullOrEmpty(overflowName) || !nameToId.TryGetValue(overflowName!, out overflowEffectId))
                {
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': cannot resolve OverflowEffectName: {overflowName}");
                }
            }

            CompiledMagnitude? chanceToApply = null;
            if (def.ChanceToApply != null)
            {
                var (chanceBase, chancePerLevel, chanceCalc, chanceCalcTag, chanceByLevel, chanceTail,
                        chanceIncrement) =
                    MagnitudeCompiler.Resolve(def.ChanceToApply, $"Effect '{def.Name}'", def.MaxLevel, tags, seams, attributes);
                chanceToApply = new CompiledMagnitude(
                    chanceBase, chancePerLevel, chanceCalc, chanceCalcTag, chanceByLevel, chanceTail,
                    chanceIncrement);
            }

            var durationPerLevel = def.DurationPerLevel?.ToArray();

            CompiledMagnitude? durationScale = null;
            if (def.DurationScale != null)
            {
                var (scaleBase, scalePerLevel, scaleCalc, scaleCalcTag, scaleByLevel, scaleTail,
                        scaleIncrement) =
                    MagnitudeCompiler.Resolve(def.DurationScale, $"Effect '{def.Name}'", def.MaxLevel, tags, seams, attributes);
                durationScale = new CompiledMagnitude(
                    scaleBase, scalePerLevel, scaleCalc, scaleCalcTag, scaleByLevel, scaleTail,
                    scaleIncrement);
            }

            var drCategory = GameplayTag.None;
            if (!string.IsNullOrEmpty(def.DrCategory) && !tags.TryGet(def.DrCategory!, out drCategory))
            {
                throw new InvalidOperationException(
                    $"Effect '{def.Name}': cannot resolve DR category tag: {def.DrCategory}");
            }

            var drStageMultipliers = def.DrStageMultipliers.Count > 0
                ? def.DrStageMultipliers.ToArray()
                : Array.Empty<BigNum>();

            var raiseEvents = new CompiledRaiseEvent[def.RaiseEvents.Count];
            for (var i = 0; i < def.RaiseEvents.Count; i++)
            {
                var raise = def.RaiseEvents[i];
                if (string.IsNullOrEmpty(raise.Tag))
                    throw new InvalidOperationException($"Effect '{def.Name}': RaiseEvents entry has an empty Tag.");
                if (!attributes.Contains(raise.AttributeId))
                {
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': RaiseEvents references unregistered attribute {raise.AttributeId}.");
                }

                var modified = false;
                for (var m = 0; m < def.Modifiers.Count; m++)
                {
                    if (def.Modifiers[m].AttributeId != raise.AttributeId) continue;
                    modified = true;
                    break;
                }

                if (!modified)
                {
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': RaiseEvents attribute {raise.AttributeId} is not modified by a " +
                        "Modifiers entry on this effect. An Executions-only change to the attribute does not " +
                        "count — add a Modifiers entry targeting it, even a zero-magnitude one, or drop the " +
                        "RaiseEvents entry.");
                }

                raiseEvents[i] = new CompiledRaiseEvent(tags.GetRequired(raise.Tag), raise.AttributeId);
            }

            var stack = new CompiledStackPolicy(
                def.Stack.MaxStack, def.Stack.OnReapply, def.Stack.AddStackCount,
                def.Stack.RefreshDurationOnReapply, def.Stack.ResetPeriodOnReapply, def.Stack.OnExpiration,
                def.Stack.OnOverflow, def.Stack.OverflowEffectName, def.Stack.ClearStacksOnOverflow,
                def.Stack.LevelFromStack, def.Stack.ExtendCapMultiplier);

            return new CompiledEffect(
                def.Name, def.MaxLevel, def.DurationType, def.DurationTicks, def.PeriodTicks, stack,
                modifiers, executions, applicationConditions, ongoingConditions,
                grantedTags, grantedAbilityNames, assetTags, immunityTags, chains, overflowEffectId,
                removeOnApplyTags, chanceToApply, durationPerLevel, durationScale,
                drCategory, def.DrWindowTicks, drStageMultipliers, raiseEvents);
        }

        private static void ValidateDurationTypeFields(EffectDef def)
        {
            switch (def.DurationType)
            {
                case EffectDurationType.Instant:
                    if (def.DurationTicks != 0 || def.PeriodTicks != 0)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{def.Name}': Instant requires DurationTicks/PeriodTicks to be 0.");
                    }

                    if (def.OngoingConditions.Count != 0)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{def.Name}': Instant cannot have OngoingConditions.");
                    }

                    if (def.GrantedTags.Count != 0)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{def.Name}': Instant cannot have GrantedTags.");
                    }

                    if (def.GrantedAbilities.Count != 0)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{def.Name}': Instant cannot have GrantedAbilities.");
                    }

                    if (def.Stack.MaxStack != 0)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{def.Name}': Instant requires Stack.MaxStack to be 0.");
                    }

                    break;

                case EffectDurationType.Duration:
                    // With DurationPerLevel, DurationTicks must stay unused (0) — the mutual-exclusion
                    // rule is validated by ValidateDurationScaleFields.
                    if (def.DurationPerLevel == null && def.DurationTicks <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{def.Name}': Duration requires DurationTicks > 0 (or use DurationPerLevel).");
                    }

                    break;

                case EffectDurationType.Infinite:
                    if (def.DurationTicks != 0)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{def.Name}': Infinite requires DurationTicks to be 0.");
                    }

                    break;
            }
        }

        private static void ValidateExecutionEligibility(EffectDef def)
        {
            if (def.Executions.Count > 0
                && def.DurationType != EffectDurationType.Instant
                && def.PeriodTicks <= 0)
            {
                throw new InvalidOperationException(
                    $"Effect '{def.Name}': Executions are only allowed for Instant or PeriodTicks > 0.");
            }
        }

        // Instant or periodic (PeriodTicks > 0) defs allow Add modifiers only. Multiply/Override carry
        // aggregation semantics (non-periodic Duration/Infinite only) and do not exist on paths that apply
        // immediately without an instance — a percent change is equivalently Add + a self-referencing
        // operand (e.g. Operand.Attribute(id, -0.3) = "reduce current value by 30%").
        private static void ValidateInstantOrPeriodicModifierOps(EffectDef def)
        {
            if (def.DurationType != EffectDurationType.Instant && def.PeriodTicks <= 0) return;

            for (var i = 0; i < def.Modifiers.Count; i++)
            {
                if (def.Modifiers[i].Op != AttributeModifierOp.Add)
                {
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': Instant/periodic modifiers only allow Add — " +
                        "express percent changes as Add + a self-referencing operand (e.g. Operand.Attribute(id, -0.3)).");
                }
            }
        }

        private static void ValidateStackOverflowConsistency(EffectDef def)
        {
            if (def.Stack.MaxStack == 0
                && (def.Stack.OnOverflow == StackOverflow.ApplyEffect
                    || def.Stack.OnReapply == StackReapply.AddStack))
            {
                throw new InvalidOperationException(
                    $"Effect '{def.Name}': ApplyEffect/AddStack policies cannot be used when Stack.MaxStack is 0.");
            }
        }

        private static void ValidateStackVocabConsistency(EffectDef def)
        {
            if (def.Stack.LevelFromStack && def.Stack.MaxStack == 0)
            {
                throw new InvalidOperationException(
                    $"Effect '{def.Name}': LevelFromStack requires Stack.MaxStack > 0.");
            }

            if (def.Stack.OnReapply == StackReapply.ExtendCapped)
            {
                if (def.DurationType != EffectDurationType.Duration)
                {
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': StackReapply.ExtendCapped requires DurationType Duration.");
                }

                if (def.Stack.ExtendCapMultiplier < BigNum.One)
                {
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': Stack.ExtendCapMultiplier must be at least 1.");
                }
            }
        }

        // DurationPerLevel/DurationScale consistency — both are Duration-only; DurationPerLevel is
        // mutually exclusive with DurationTicks (DurationTicks must be 0) and must match MaxLevel in length.
        private static void ValidateDurationScaleFields(EffectDef def)
        {
            if (def.DurationPerLevel != null)
            {
                if (def.DurationType != EffectDurationType.Duration)
                {
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': DurationPerLevel requires DurationType Duration.");
                }

                if (def.DurationTicks != 0)
                {
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': DurationPerLevel cannot be combined with DurationTicks (mutually exclusive).");
                }

                if (def.MaxLevel < 1)
                {
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': DurationPerLevel requires MaxLevel >= 1 to be declared.");
                }

                if (def.DurationPerLevel.Count != def.MaxLevel)
                {
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': DurationPerLevel length ({def.DurationPerLevel.Count}) must "
                        + $"match MaxLevel ({def.MaxLevel}).");
                }
            }

            if (def.DurationScale != null && def.DurationType != EffectDurationType.Duration)
            {
                throw new InvalidOperationException(
                    $"Effect '{def.Name}': DurationScale requires DurationType Duration.");
            }
        }

        // DR field consistency — with DrCategory set: Duration-only, window at least 1 tick, stage
        // multipliers non-empty and all non-negative.
        private static void ValidateDrFields(EffectDef def)
        {
            if (string.IsNullOrEmpty(def.DrCategory)) return;

            if (def.DurationType != EffectDurationType.Duration)
            {
                throw new InvalidOperationException(
                    $"Effect '{def.Name}': DrCategory requires DurationType Duration.");
            }

            if (def.DrWindowTicks < 1)
            {
                throw new InvalidOperationException(
                    $"Effect '{def.Name}': DrCategory requires DrWindowTicks >= 1.");
            }

            if (def.DrStageMultipliers.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Effect '{def.Name}': DrCategory requires non-empty DrStageMultipliers.");
            }

            for (var i = 0; i < def.DrStageMultipliers.Count; i++)
            {
                if (def.DrStageMultipliers[i].Sign < 0)
                {
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': DrStageMultipliers must be non-negative (index {i}).");
                }
            }
        }

        // OngoingConditions are evaluated every tick with no triggering event, so EventMagnitude there
        // could never resolve to anything meaningful — rejected unconditionally, independent of reachability.
        private static void ValidateOngoingConditionsNoEventMagnitude(EffectDef def)
        {
            for (var i = 0; i < def.OngoingConditions.Count; i++)
            {
                if (def.OngoingConditions[i].Left.Kind == OperandKind.EventMagnitude
                    || def.OngoingConditions[i].Right.Kind == OperandKind.EventMagnitude)
                {
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': EventMagnitude cannot be used in OngoingConditions.");
                }
            }
        }

        // A periodic def's Modifiers/Executions run every period via FirePeriodic, which has no
        // triggering event (the event magnitude is a per-application value, not persisted across
        // periods) -- so EventMagnitude there would always resolve to zero. ChanceToApply and
        // ApplicationConditions are evaluated once at application time and do see the real value, so
        // they remain legal even on a periodic def.
        private static void ValidatePeriodicNoEventMagnitudeInModifiersOrExecutions(EffectDef def)
        {
            if (def.PeriodTicks <= 0) return;

            for (var i = 0; i < def.Modifiers.Count; i++)
            {
                if (!MagnitudeUsesEventMagnitude(def.Modifiers[i].Magnitude)) continue;
                throw new InvalidOperationException(
                    $"Effect '{def.Name}': EventMagnitude cannot be used in a periodic effect's Modifiers -- "
                    + "the event magnitude is a per-application value that does not persist across periodic "
                    + "executions. ChanceToApply/ApplicationConditions may still use it.");
            }

            for (var i = 0; i < def.Executions.Count; i++)
            {
                var inputs = def.Executions[i].Inputs;
                for (var k = 0; k < inputs.Count; k++)
                {
                    if (inputs[k].Kind != OperandKind.EventMagnitude) continue;
                    throw new InvalidOperationException(
                        $"Effect '{def.Name}': EventMagnitude cannot be used in a periodic effect's Executions -- "
                        + "the event magnitude is a per-application value that does not persist across periodic "
                        + "executions. ChanceToApply/ApplicationConditions may still use it.");
                }
            }
        }

        // EventMagnitude is only meaningful for an effect an OnEvent chain applies directly (one hop) —
        // a def reached only transitively (via further OnApplication/etc. chains) always sees zero, so
        // using EventMagnitude there is never anything but a silent authoring bug. Reachability is
        // therefore strict: a def that uses EventMagnitude anywhere must be the direct target of at
        // least one OnEvent edge somewhere in the catalog (it may also be reached transitively elsewhere
        // — that adds nothing, since the direct edge alone already makes it legal).
        private static void ValidateEventMagnitudeReachability(List<EffectDef> defs, Dictionary<string, int> nameToId)
        {
            var reachable = new bool[defs.Count];
            for (var i = 0; i < defs.Count; i++)
            {
                var chains = defs[i].Chains;
                for (var c = 0; c < chains.Count; c++)
                {
                    if (chains[c].Trigger != ChainTrigger.OnEvent) continue;
                    if (nameToId.TryGetValue(chains[c].EffectName, out var id)) reachable[id] = true;
                }
            }

            for (var i = 0; i < defs.Count; i++)
            {
                if (reachable[i] || !UsesEventMagnitude(defs[i])) continue;
                throw new InvalidOperationException(
                    $"Effect '{defs[i].Name}': uses EventMagnitude but no OnEvent chain applies it directly.");
            }
        }

        private static bool UsesEventMagnitude(EffectDef def)
        {
            for (var i = 0; i < def.Modifiers.Count; i++)
            {
                if (MagnitudeUsesEventMagnitude(def.Modifiers[i].Magnitude)) return true;
            }

            for (var i = 0; i < def.Executions.Count; i++)
            {
                var inputs = def.Executions[i].Inputs;
                for (var k = 0; k < inputs.Count; k++)
                {
                    if (inputs[k].Kind == OperandKind.EventMagnitude) return true;
                }
            }

            for (var i = 0; i < def.ApplicationConditions.Count; i++)
            {
                if (def.ApplicationConditions[i].Left.Kind == OperandKind.EventMagnitude
                    || def.ApplicationConditions[i].Right.Kind == OperandKind.EventMagnitude)
                {
                    return true;
                }
            }

            if (def.DurationScale != null && MagnitudeUsesEventMagnitude(def.DurationScale)) return true;

            return def.ChanceToApply != null && MagnitudeUsesEventMagnitude(def.ChanceToApply);
        }

        private static bool MagnitudeUsesEventMagnitude(MagnitudeDef def) =>
            (def.Base.HasValue && def.Base.Value.Kind == OperandKind.EventMagnitude)
            || (def.PerLevel.HasValue && def.PerLevel.Value.Kind == OperandKind.EventMagnitude);

        // LevelFromStack combined with a ScaleWithStack=true modifier compounds level curve × stack
        // multiplier — not an error (may be intended content), but accumulated as a Build warning so the
        // author is aware.
        private static void DetectLevelFromStackScaleWithStackWarnings(List<EffectDef> defs, List<string> warnings)
        {
            for (var i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (!def.Stack.LevelFromStack) continue;

                for (var m = 0; m < def.Modifiers.Count; m++)
                {
                    if (!def.Modifiers[m].ScaleWithStack) continue;
                    warnings.Add(
                        $"[warn] Effect '{def.Name}': LevelFromStack and ScaleWithStack=true are both enabled, "
                        + "so level curve × stack multiplier apply compounded.");
                    break;
                }
            }
        }

        // OngoingConditions cannot have SourceAttribute operands.
        private static CompiledCondition[] ResolveConditions(
            List<ConditionDef> defs, string ownerName, AttributeRegistry attributes, bool allowSourceAttribute)
        {
            var result = new CompiledCondition[defs.Count];
            for (var i = 0; i < defs.Count; i++)
            {
                var c = defs[i];
                if (!allowSourceAttribute
                    && (c.Left.Kind == OperandKind.SourceAttribute || c.Right.Kind == OperandKind.SourceAttribute))
                {
                    throw new InvalidOperationException(
                        $"{ownerName}: OngoingConditions cannot have SourceAttribute operands.");
                }

                MagnitudeCompiler.ValidateOperandAttribute(c.Left, ownerName, attributes);
                MagnitudeCompiler.ValidateOperandAttribute(c.Right, ownerName, attributes);
                result[i] = new CompiledCondition(c.Left, c.Op, c.Right);
            }

            return result;
        }

        // Resolves SelectorTag (if any) and EffectName. LevelRule.Fixed also validates that FixedLevel
        // does not exceed the target def's declared MaxLevel.
        private static CompiledChainEdge ResolveChain(
            ChainEdgeDef edge, EffectDef ownerDef, List<EffectDef> allDefs, Dictionary<string, int> nameToId,
            TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            if (string.IsNullOrEmpty(edge.EffectName) || !nameToId.TryGetValue(edge.EffectName, out var effectId))
            {
                throw new InvalidOperationException(
                    $"Effect '{ownerDef.Name}': cannot resolve chain target effect: {edge.EffectName}");
            }

            if (edge.LevelRule == ChainLevelRule.Fixed)
            {
                var targetMaxLevel = allDefs[effectId].MaxLevel;
                if (targetMaxLevel > 0 && edge.FixedLevel > targetMaxLevel)
                {
                    throw new InvalidOperationException(
                        $"Effect '{ownerDef.Name}': FixedLevel ({edge.FixedLevel}) for chain target '{edge.EffectName}' "
                        + $"exceeds MaxLevel ({targetMaxLevel}).");
                }
            }

            ITargetSelector? selector = null;
            var selectorTag = GameplayTag.None;
            if (!string.IsNullOrEmpty(edge.SelectorTag))
            {
                selector = MagnitudeCompiler.ResolveTargetSelector(
                    edge.SelectorTag!, $"Effect '{ownerDef.Name}'", tags, seams, out selectorTag);
                if (edge.Trigger != ChainTrigger.OnEvent
                    && (edge.SelectorTag == BuiltInSelectors.EventSourceTag || edge.SelectorTag == BuiltInSelectors.EventTargetTag))
                {
                    throw new InvalidOperationException(
                        $"Effect '{ownerDef.Name}': selector '{edge.SelectorTag}' is only meaningful on an OnEvent "
                        + $"chain edge, but this edge's trigger is {edge.Trigger}.");
                }
            }

            var eventTag = default(GameplayTag);
            if (edge.Trigger == ChainTrigger.OnEvent)
            {
                if (string.IsNullOrEmpty(edge.EventTag))
                {
                    throw new InvalidOperationException(
                        $"Effect '{ownerDef.Name}': an OnEvent chain requires EventTag.");
                }

                eventTag = tags.GetRequired(edge.EventTag!);
            }
            else if (!string.IsNullOrEmpty(edge.EventTag))
            {
                throw new InvalidOperationException(
                    $"Effect '{ownerDef.Name}': EventTag is only valid on an OnEvent chain.");
            }

            var conditions = ResolveConditions(edge.Conditions, $"Effect '{ownerDef.Name}'", attributes, allowSourceAttribute: true);
            if (edge.Trigger != ChainTrigger.OnEvent)
            {
                for (var i = 0; i < conditions.Length; i++)
                {
                    if (conditions[i].Left.Kind != OperandKind.EventMagnitude
                        && conditions[i].Right.Kind != OperandKind.EventMagnitude)
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Effect '{ownerDef.Name}': EventMagnitude cannot be used in a non-OnEvent chain edge's Conditions.");
                }
            }

            return new CompiledChainEdge(
                edge.Trigger, effectId, selector, selectorTag, edge.SelectorParams.ToArray(),
                conditions, edge.LevelRule, edge.FixedLevel, eventTag, edge.Listen);
        }

        // Cycle detection via DFS gray/black coloring following OnApplication edges only. A cycle passing
        // through any Duration/Period def is "low" severity, otherwise "high".
        private static List<string> DetectChainCycles(CompiledEffect[] compiled)
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
