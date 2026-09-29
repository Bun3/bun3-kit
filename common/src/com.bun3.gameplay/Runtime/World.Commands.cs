// World partial — command queue (the only way to change simulation state from outside Tick).
#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay
{
    public sealed partial class World
    {
        private enum CommandKind : byte
        {
            Apply,
            Activate,
            RemoveByTag,
            RemoveById,
            SetBase,
        }

        private struct Command
        {
            internal CommandKind Kind;
            internal TargetId Unit;
            internal TargetId Source;
            internal int Id;
            internal int Level;
            internal ulong InstanceId;
            internal GameplayTag Tag;
            internal ushort AttributeId;
            internal BigNum Value;
        }

        private readonly List<Command> _commands = new List<Command>();
        private AbilityActivation[] _activations = new AbilityActivation[8];
        private int _activationCount;

        /// <summary>Commands waiting for the next <see cref="Tick"/>.</summary>
        public int PendingCommandCount => _commands.Count;

        /// <summary>Activation results recorded by the last <see cref="Tick"/>, in request order. Cleared
        /// at the start of the next tick.</summary>
        public ReadOnlySpan<AbilityActivation> PendingActivations => _activations.AsSpan(0, _activationCount);

        /// <summary>Queues an effect application. It enters the pipeline's apply queue during the next
        /// <see cref="Tick"/>, in call order relative to every other queued command.</summary>
        /// <param name="effectId">Effect id to apply.</param>
        /// <param name="source">Caster id.</param>
        /// <param name="target">Receiving unit id.</param>
        /// <param name="level">Effect level.</param>
        public void EnqueueApply(int effectId, TargetId source, TargetId target, int level = 1) =>
            _commands.Add(new Command { Kind = CommandKind.Apply, Unit = target, Source = source, Id = effectId, Level = level });

        /// <summary>Queues an ability activation. The result appears in <see cref="PendingActivations"/>
        /// after the next <see cref="Tick"/>.</summary>
        /// <param name="unitId">Activating unit.</param>
        /// <param name="abilityId">Compiled ability id.</param>
        public void EnqueueActivation(TargetId unitId, int abilityId) =>
            _commands.Add(new Command { Kind = CommandKind.Activate, Unit = unitId, Id = abilityId });

        /// <summary>Queues removal of every active instance on the unit whose asset tags have
        /// <paramref name="tag"/> as self-or-ancestor. Runs during the next <see cref="Tick"/>.</summary>
        /// <param name="unitId">Unit to dispel.</param>
        /// <param name="tag">Dispel query tag.</param>
        public void EnqueueRemoveByTag(TargetId unitId, GameplayTag tag) =>
            _commands.Add(new Command { Kind = CommandKind.RemoveByTag, Unit = unitId, Tag = tag });

        /// <summary>Queues removal of one active instance by id. Runs during the next <see cref="Tick"/>;
        /// an id that is gone by then is ignored.</summary>
        /// <param name="unitId">Unit holding the instance.</param>
        /// <param name="instanceId">Instance id.</param>
        public void EnqueueRemoveById(TargetId unitId, ulong instanceId) =>
            _commands.Add(new Command { Kind = CommandKind.RemoveById, Unit = unitId, InstanceId = instanceId });

        /// <summary>Queues a Base write for a registered unit (e.g. a meta-progression stat refresh). Runs
        /// during the next <see cref="Tick"/>; an unknown unit or an attribute the unit does not declare is
        /// ignored. Initialize a unit before <see cref="Spawn"/> with <c>AttributeSet.SetBase</c> directly.</summary>
        /// <param name="unitId">Unit to write.</param>
        /// <param name="attributeId">Declared attribute id.</param>
        /// <param name="value">New Base value (clamped as usual).</param>
        public void EnqueueSetBase(TargetId unitId, ushort attributeId, BigNum value) =>
            _commands.Add(new Command { Kind = CommandKind.SetBase, Unit = unitId, AttributeId = attributeId, Value = value });

        // Commands queued while processing (e.g. from a selector) wait for the next tick. A command that
        // throws is dropped together with the ones before it, so the failure surfaces once and the next
        // tick neither re-runs processed commands nor throws again.
        private void ProcessCommands()
        {
            Span<GameplayTag> query = stackalloc GameplayTag[1];
            var count = _commands.Count;
            var processed = 0;
            try
            {
                for (; processed < count; processed++)
                {
                    var command = _commands[processed];
                    switch (command.Kind)
                    {
                        case CommandKind.Apply:
                            Pipeline.EnqueueApply(command.Id, command.Source, command.Unit, command.Level);
                            break;
                        case CommandKind.Activate:
                            RecordActivation(command.Unit, command.Id, EvaluateActivation(command.Unit, command.Id, commit: true));
                            break;
                        case CommandKind.RemoveByTag:
                            query[0] = command.Tag;
                            Pipeline.RemoveByTagSpan(command.Unit, query);
                            break;
                        case CommandKind.RemoveById:
                            Pipeline.RemoveById(command.Unit, command.InstanceId);
                            break;
                        case CommandKind.SetBase:
                            if (TryGetUnit(command.Unit, out var unit) && unit != null && unit.Attributes.Has(command.AttributeId))
                                unit.Attributes.SetBase(command.AttributeId, command.Value);
                            break;
                    }
                }
            }
            finally
            {
                _commands.RemoveRange(0, processed < count ? processed + 1 : count);
            }
        }

        private void RecordActivation(TargetId unitId, int abilityId, AbilityActivationResult result)
        {
            if (_activationCount == _activations.Length) Array.Resize(ref _activations, _activations.Length * 2);
            _activations[_activationCount++] = new AbilityActivation(unitId, abilityId, result);
        }
    }
}
