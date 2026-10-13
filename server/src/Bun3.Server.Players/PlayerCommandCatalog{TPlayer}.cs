using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Players
{
    /// <summary>Registers input reconstruction factories without executing game logic during discovery.</summary>
    public sealed class PlayerCommandCatalog<TPlayer> where TPlayer : Player
    {
        private readonly Dictionary<(string Kind, int Version), Registration> _handlers = new Dictionary<(string, int), Registration>();
        private bool _frozen;
        /// <summary>Registers a unique command kind and input schema. Factories must only reconstruct the operation.</summary>
        public void Register<TResult>(string kind, int inputSchemaVersion, bool supportsOffline,
            Func<PlayerCommandEnvelope, PlayerOperation<TPlayer, TResult>> factory) where TResult : class
        {
            if (_frozen) throw new InvalidOperationException("The command catalog is frozen.");
            if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("A command kind is required.", nameof(kind));
            if (inputSchemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(inputSchemaVersion));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _handlers.Add((kind, inputSchemaVersion), new Registration(supportsOffline, (registry, envelope, store, ct) =>
            {
                var operation = factory(envelope);
                if (operation == null || !envelope.Matches(operation)) throw new PlayerCommandConflictException(envelope.Id);
                return registry.ExecuteCommandAsync(operation, envelope, store, ct);
            }));
        }
        internal void Freeze() => _frozen = true;
        /// <summary>Returns the registered input types executable by the selected worker role.</summary>
        public IReadOnlyList<(string Kind, int InputSchemaVersion)> GetSupportedInputs(bool offline)
        {
            var inputs = new List<(string Kind, int InputSchemaVersion)>();
            foreach (var handler in _handlers)
                if (!offline || handler.Value.SupportsOffline) inputs.Add(handler.Key);
            return inputs.AsReadOnly();
        }
        internal Registration? Find(PlayerCommandEnvelope command) => _handlers.TryGetValue((command.Kind, command.InputSchemaVersion), out var value) ? value : null;
        internal sealed class Registration
        {
            internal Registration(bool offline, Func<PlayerRegistry<TPlayer>, PlayerCommandEnvelope, PlayerCommandStore, CancellationToken, ValueTask<PlayerCommandRecord?>> execute)
            { SupportsOffline = offline; Execute = execute; }
            internal bool SupportsOffline { get; }
            internal Func<PlayerRegistry<TPlayer>, PlayerCommandEnvelope, PlayerCommandStore, CancellationToken, ValueTask<PlayerCommandRecord?>> Execute { get; }
        }
    }
}
