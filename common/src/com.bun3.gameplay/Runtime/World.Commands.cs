// World partial — command queue (the only way to change simulation state from outside Tick).
#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;

namespace Bun3.Gameplay
{
    public sealed partial class World
    {
        private enum CommandKind : byte
        {
            Apply,
            Activate,
        }

        private struct Command
        {
            internal CommandKind Kind;
            internal TargetId Unit;
            internal TargetId Source;
            internal int Id;
            internal int Level;
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

        // Commands queued while processing (e.g. from a selector) wait for the next tick.
        private void ProcessCommands()
        {
            var count = _commands.Count;
            for (var i = 0; i < count; i++)
            {
                var command = _commands[i];
                switch (command.Kind)
                {
                    case CommandKind.Apply:
                        Pipeline.EnqueueApply(command.Id, command.Source, command.Unit, command.Level);
                        break;
                    case CommandKind.Activate:
                        RecordActivation(command.Unit, command.Id, EvaluateActivation(command.Unit, command.Id, commit: true));
                        break;
                }
            }

            _commands.RemoveRange(0, count);
        }

        private void RecordActivation(TargetId unitId, int abilityId, AbilityActivationResult result)
        {
            if (_activationCount == _activations.Length) Array.Resize(ref _activations, _activations.Length * 2);
            _activations[_activationCount++] = new AbilityActivation(unitId, abilityId, result);
        }
    }
}
