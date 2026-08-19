using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Bun3.Server.Rpc
{
    /// <summary>One case of the oneof "body". Accessor delegates are cached once at build time.</summary>
    internal sealed class OneofCase
    {
        public int FieldNumber { get; }
        public string Name { get; }
        public Type PayloadType { get; }

        /// <summary>Extracts the payload from the envelope. Non-null only when this case is active (GetActiveCase result).</summary>
        public Func<IMessage, IMessage?> Get { get; }

        public Action<IMessage, IMessage> Set { get; }

        public OneofCase(FieldDescriptor field)
        {
            FieldNumber = field.FieldNumber;
            Name = field.Name;
            PayloadType = field.MessageType.ClrType;
            var accessor = field.Accessor;
            // On IL2CPP these accessor delegates may run interpreted.
            Get = message => (IMessage?)accessor.GetValue(message);
            Set = (message, payload) => accessor.SetValue(message, payload);
        }
    }

    /// <summary>Case map built by enumerating the root message's oneof "body" once at startup.</summary>
    internal sealed class OneofMap
    {
        private readonly OneofDescriptor _oneof;
        private readonly Dictionary<int, OneofCase> _byNumber = new Dictionary<int, OneofCase>();
        private readonly Dictionary<Type, OneofCase> _byType = new Dictionary<Type, OneofCase>();

        private OneofMap(OneofDescriptor oneof)
        {
            _oneof = oneof;
            foreach (var field in oneof.Fields)
            {
                var oneofCase = new OneofCase(field);
                _byNumber.Add(oneofCase.FieldNumber, oneofCase);
                if (!_byType.ContainsKey(oneofCase.PayloadType))
                {
                    _byType.Add(oneofCase.PayloadType, oneofCase);
                }
            }
        }

        public IReadOnlyCollection<OneofCase> Cases => _byNumber.Values;

        /// <summary>Groups of cases sharing the same payload type (shapes where type-based dispatch is impossible).</summary>
        public IEnumerable<IGrouping<Type, OneofCase>> DuplicatePayloadTypeGroups() =>
            _byNumber.Values.GroupBy(c => c.PayloadType).Where(g => g.Count() > 1);

        /// <summary>Returns null and appends to errors when oneof "body" is missing or a case is not a message type.</summary>
        public static OneofMap? TryBuild(MessageDescriptor message, string rootLabel, List<string> errors)
        {
            var oneof = message.Oneofs.FirstOrDefault(o => o.Name == "body");
            if (oneof == null)
            {
                errors.Add($"{rootLabel}({message.Name}): oneof \"body\" is missing");
                return null;
            }

            foreach (var field in oneof.Fields)
            {
                if (field.FieldType != FieldType.Message)
                {
                    errors.Add($"{rootLabel}({message.Name}): body case {field.Name} must be a message type");
                    return null;
                }
            }

            return new OneofMap(oneof);
        }

        public OneofCase? ByFieldNumber(int fieldNumber) =>
            _byNumber.GetValueOrDefault(fieldNumber);

        public OneofCase? ByPayloadType(Type payloadType) =>
            _byType.GetValueOrDefault(payloadType);

        /// <summary>The case actually set on the envelope; null when empty.</summary>
        public OneofCase? GetActiveCase(IMessage envelope)
        {
            var field = _oneof.Accessor.GetCaseFieldDescriptor(envelope);
            return field == null ? null : ByFieldNumber(field.FieldNumber);
        }
    }
}
