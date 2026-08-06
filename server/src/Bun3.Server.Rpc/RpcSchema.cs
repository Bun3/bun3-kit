using System.Collections.Generic;
using System.Linq;
using Bun3.Server.Core;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Bun3.Server.Rpc
{
    /// <summary>
    /// 게임 소유 루트 3형(Request/Response/Update)의 디스크립터에서 기동 1회 구축되는 스키마 맵.
    /// 규약: 세 루트 모두 oneof "body"; TRequest/TResponse에 int64 request_id; TResponse에 int32 status.
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

        /// <summary>루트 규약 위반 시 전체 목록과 함께 RpcValidationException.</summary>
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
                errors.Add($"{rootLabel}({message.Name}): 단일(non-repeated, oneof 밖) {fieldType} {fieldName} 필드 필요");
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
                errors.Add($"{rootLabel}({messageName}): 케이스 {caseNames}가 같은 payload 타입 {group.Key.Name} — 타입 기반 디스패치 불가");
            }
        }

        /// <summary>등록표를 스키마에 대해 전수 검증한다. 위반 전체 목록과 함께 throw.</summary>
        public void Validate<TSession>(RpcConfig<TSession> config) where TSession : Session
        {
            var errors = new List<string>();

            foreach (var requestCase in RequestMap.Cases)
            {
                if (!config.Registrations.ContainsKey(requestCase.PayloadType))
                {
                    errors.Add($"핸들러 미등록: {requestCase.Name} ({requestCase.PayloadType.Name})");
                }
            }

            foreach (var pair in config.Registrations)
            {
                var registration = pair.Value;
                var requestCase = RequestMap.ByPayloadType(registration.RequestType);
                if (requestCase == null)
                {
                    errors.Add($"Request oneof에 없는 타입 등록: {registration.RequestType.Name}");
                    continue;
                }

                var responseCase = ResponseMap.ByFieldNumber(requestCase.FieldNumber);
                if (responseCase == null || responseCase.Name != requestCase.Name)
                {
                    errors.Add(
                        $"응답 케이스 불일치: {requestCase.Name}(#{requestCase.FieldNumber}) — " +
                        "Response.body에 같은 이름·번호의 케이스 필요");
                }
                else if (responseCase.PayloadType != registration.ResponseType)
                {
                    errors.Add(
                        $"응답 타입 불일치: {requestCase.Name} — 등록 {registration.ResponseType.Name}, " +
                        $"스키마 {responseCase.PayloadType.Name}");
                }
            }

            if (errors.Count > 0)
            {
                throw new RpcValidationException(errors);
            }
        }
    }
}
