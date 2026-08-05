using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Bun3.Server.Messaging
{
    /// <summary>oneof "body"의 케이스 하나. 접근자 델리게이트는 구축 시 1회 캐시된다.</summary>
    internal sealed class OneofCase
    {
        public int FieldNumber { get; }
        public string Name { get; }
        public Type PayloadType { get; }
        public Func<IMessage, IMessage> Get { get; }
        public Action<IMessage, IMessage> Set { get; }

        public OneofCase(FieldDescriptor field)
        {
            FieldNumber = field.FieldNumber;
            Name = field.Name;
            PayloadType = field.MessageType.ClrType;
            var accessor = field.Accessor;
            Get = message => (IMessage)accessor.GetValue(message);
            Set = (message, payload) => accessor.SetValue(message, payload);
        }
    }

    /// <summary>루트 메시지의 oneof "body"를 기동 1회 열거해 만든 케이스 맵.</summary>
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
                _byType.Add(oneofCase.PayloadType, oneofCase);
            }
        }

        public IReadOnlyCollection<OneofCase> Cases => _byNumber.Values;

        /// <summary>oneof "body"가 없거나 메시지 아닌 케이스가 있으면 errors에 추가하고 null.</summary>
        public static OneofMap? TryBuild(MessageDescriptor message, string rootLabel, List<string> errors)
        {
            var oneof = message.Oneofs.FirstOrDefault(o => o.Name == "body");
            if (oneof == null)
            {
                errors.Add($"{rootLabel}({message.Name}): oneof \"body\" 없음");
                return null;
            }

            foreach (var field in oneof.Fields)
            {
                if (field.FieldType != FieldType.Message)
                {
                    errors.Add($"{rootLabel}({message.Name}): body 케이스 {field.Name}은 message 타입이어야 함");
                    return null;
                }
            }

            return new OneofMap(oneof);
        }

        public OneofCase? ByFieldNumber(int fieldNumber) =>
            _byNumber.TryGetValue(fieldNumber, out var found) ? found : null;

        public OneofCase? ByPayloadType(Type payloadType) =>
            _byType.TryGetValue(payloadType, out var found) ? found : null;

        /// <summary>envelope에 실제 설정된 케이스. 비어 있으면 null.</summary>
        public OneofCase? GetActiveCase(IMessage envelope)
        {
            var field = _oneof.Accessor.GetCaseFieldDescriptor(envelope);
            return field == null ? null : ByFieldNumber(field.FieldNumber);
        }
    }
}
