using System.Collections.Generic;
using System.Linq;
using Bun3.Server.Core;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Bun3.Server.Rpc
{
    /// <summary>
    /// Schema map built once at startup from the descriptors of the three game-owned roots (Request/Response/Update).
    /// Contract: all three roots have oneof "body"; TRequest/TResponse have int64 request_id; TResponse has int32 status.
    /// </summary>
    public sealed class RpcSchema<TRequest, TResponse, TUpdate>
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        internal OneofMap RequestMap { get; }
        internal OneofMap ResponseMap { get; }
        internal OneofMap UpdateMap { get; }
        internal FieldDescriptor RequestIdOfRequest { get; }
        internal FieldDescriptor RequestIdOfResponse { get; }
        internal FieldDescriptor StatusOfResponse { get; }
        internal MessageParser<TRequest> RequestParser { get; } = new MessageParser<TRequest>(() => new TRequest());
        internal MessageParser<TResponse> ResponseParser { get; } = new MessageParser<TResponse>(() => new TResponse());
        internal MessageParser<TUpdate> UpdateParser { get; } = new MessageParser<TUpdate>(() => new TUpdate());

        private RpcSchema(
            OneofMap requestMap,
            OneofMap responseMap,
            OneofMap updateMap,
            FieldDescriptor requestIdOfRequest,
            FieldDescriptor requestIdOfResponse,
            FieldDescriptor statusOfResponse)
        {
            RequestMap = requestMap;
            ResponseMap = responseMap;
            UpdateMap = updateMap;
            RequestIdOfRequest = requestIdOfRequest;
            RequestIdOfResponse = requestIdOfResponse;
            StatusOfResponse = statusOfResponse;
        }

        /// <summary>Throws RpcValidationException with the full list on root contract violations.</summary>
        public static RpcSchema<TRequest, TResponse, TUpdate> Create()
        {
            var errors = new List<string>();
            var requestDescriptor = new TRequest().Descriptor;
            var responseDescriptor = new TResponse().Descriptor;
            var updateDescriptor = new TUpdate().Descriptor;

            var requestMap = OneofMap.TryBuild(requestDescriptor, "Request", errors);
            var responseMap = OneofMap.TryBuild(responseDescriptor, "Response", errors);
            var updateMap = OneofMap.TryBuild(updateDescriptor, "Update", errors);
            var requestId = RequireField(requestDescriptor, "Request", "request_id", FieldType.Int64, errors);
            var responseRequestId = RequireField(responseDescriptor, "Response", "request_id", FieldType.Int64, errors);
            var status = RequireField(responseDescriptor, "Response", "status", FieldType.Int32, errors);

            AddDuplicatePayloadErrors(requestMap, "Request", requestDescriptor.Name, errors);
            AddDuplicatePayloadErrors(updateMap, "Update", updateDescriptor.Name, errors);

            if (errors.Count > 0)
            {
                throw new RpcValidationException(errors);
            }

            return new RpcSchema<TRequest, TResponse, TUpdate>(
                requestMap!, responseMap!, updateMap!, requestId!, responseRequestId!, status!);
        }

        private static FieldDescriptor? RequireField(
            MessageDescriptor message, string rootLabel, string fieldName, FieldType fieldType, List<string> errors)
        {
            var field = message.FindFieldByName(fieldName);
            if (field == null || field.FieldType != fieldType || field.IsRepeated || field.ContainingOneof != null)
            {
                errors.Add($"{rootLabel}({message.Name}): requires a singular (non-repeated, outside oneof) {fieldType} field {fieldName}");
                return null;
            }

            return field;
        }

        private static void AddDuplicatePayloadErrors(OneofMap? map, string rootLabel, string messageName, List<string> errors)
        {
            if (map == null)
            {
                return;
            }

            foreach (var group in map.DuplicatePayloadTypeGroups())
            {
                var caseNames = string.Join(", ", group.Select(c => c.Name));
                errors.Add($"{rootLabel}({messageName}): cases {caseNames} share payload type {group.Key.Name} — type-based dispatch impossible");
            }
        }

        /// <summary>Fully validates the registration table against the schema. Throws with the full list of violations.</summary>
        public void Validate<TSession>(RpcConfig<TSession> config) where TSession : Session
        {
            var errors = new List<string>();

            foreach (var requestCase in RequestMap.Cases)
            {
                if (!config.Registrations.ContainsKey(requestCase.PayloadType))
                {
                    errors.Add($"Handler not registered: {requestCase.Name} ({requestCase.PayloadType.Name})");
                }
            }

            foreach (var pair in config.Registrations)
            {
                var registration = pair.Value;
                var requestCase = RequestMap.ByPayloadType(registration.RequestType);
                if (requestCase == null)
                {
                    errors.Add($"Registered type not in Request oneof: {registration.RequestType.Name}");
                    continue;
                }

                var responseCase = ResponseMap.ByFieldNumber(requestCase.FieldNumber);
                if (responseCase == null || responseCase.Name != requestCase.Name)
                {
                    errors.Add(
                        $"Response case mismatch: {requestCase.Name}(#{requestCase.FieldNumber}) — " +
                        "Response.body requires a case with the same name and number");
                }
                else if (responseCase.PayloadType != registration.ResponseType)
                {
                    errors.Add(
                        $"Response type mismatch: {requestCase.Name} — registered {registration.ResponseType.Name}, " +
                        $"schema {responseCase.PayloadType.Name}");
                }
            }

            if (errors.Count > 0)
            {
                throw new RpcValidationException(errors);
            }
        }
    }
}
