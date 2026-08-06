using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Bun3.Server.Rpc.ControlMessages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Rpc
{
    /// <summary>RpcSession이 패킷 처리를 위임하는 비제네릭 창구.</summary>
    internal interface IRpcRuntime
    {
        TimeSpan? IdleKickTimeout { get; }
        ILogger Logger { get; }
        ValueTask ProcessPacketAsync(RpcSession session, ReadOnlyMemory<byte> packet);
        ValueTask SendUpdateAsync(Session session, IMessage update);
    }

    /// <summary>채널 분기·요청 디스패치·응답 조립 — 서버 측 메시징의 두뇌. 상태는 전부 기동 시 구축.</summary>
    internal sealed class RpcRuntime<TSession, TRequest, TResponse, TUpdate> : IRpcRuntime
        where TSession : RpcSession
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        private readonly RpcSchema<TRequest, TResponse, TUpdate> _schema;
        private readonly Dictionary<Type, RpcConfig<TSession>.Registration> _registrations;

        public RpcRuntime(
            RpcSchema<TRequest, TResponse, TUpdate> schema,
            RpcConfig<TSession> config,
            RpcServerOptions options,
            ILogger logger)
        {
            schema.Validate(config);   // 기동 fail-fast — 위반 전체 목록과 함께 throw
            _schema = schema;
            // 스냅샷 복사 — config는 기동 후에도 살아있는 호출자 소유 객체이므로,
            // 원본 Dictionary를 그대로 들고 있으면 이후 config.OnRequest(...) 호출이
            // 세션 스레드가 동시에 읽는 딕셔너리를 변경해 미정의 동작을 유발하고 Validate도 우회한다.
            _registrations = new Dictionary<Type, RpcConfig<TSession>.Registration>(config.Registrations);
            IdleKickTimeout = options.IdleKickTimeout;
            Logger = logger;
        }

        public TimeSpan? IdleKickTimeout { get; }

        public ILogger Logger { get; }

        public async ValueTask ProcessPacketAsync(RpcSession session, ReadOnlyMemory<byte> packet)
        {
            if (packet.Length < 1)
            {
                Violation(session, "빈 패킷");
                return;
            }

            var channel = packet.Span[0];
            var body = packet.Slice(1);
            switch (channel)
            {
                case Channels.Control:
                    await HandleControlAsync(session, body).ConfigureAwait(false);
                    break;
                case Channels.Request:
                    await HandleRequestAsync(session, body).ConfigureAwait(false);
                    break;
                default:
                    Violation(session, $"허용되지 않은 채널 0x{channel:X2}");
                    break;
            }
        }

        public ValueTask SendUpdateAsync(Session session, IMessage update)
        {
            var updateCase = _schema.UpdateMap.ByPayloadType(update.GetType())
                ?? throw new ArgumentException($"Update oneof에 없는 타입: {update.GetType().Name}", nameof(update));
            var envelope = new TUpdate();
            updateCase.Set(envelope, update);
            return SendAsync(session, Channels.Update, envelope);
        }

        private async ValueTask HandleControlAsync(RpcSession session, ReadOnlyMemory<byte> body)
        {
            Control control;
            try
            {
                control = Control.Parser.ParseFrom(body.ToArray());
            }
            catch (InvalidProtocolBufferException ex)
            {
                Violation(session, $"Control 파싱 실패: {ex.Message}");
                return;
            }

            if (control.BodyCase != Control.BodyOneofCase.Ping)
            {
                // 서버는 엄격(미지 Control = 위반), 클라는 관대(경고 무시) — 서버가 새 Control을 먼저 배포하는 롤링 업그레이드를 위한 의도적 비대칭
                Violation(session, $"클라이언트가 보낼 수 없는 Control: {control.BodyCase}");
                return;
            }

            var pong = new Control { Pong = new Pong { ClientTimeUnixMs = control.Ping.ClientTimeUnixMs } };
            await SendAsync(session, Channels.Control, pong).ConfigureAwait(false);
        }

        private async ValueTask HandleRequestAsync(RpcSession session, ReadOnlyMemory<byte> body)
        {
            TRequest envelope;
            try
            {
                envelope = _schema.RequestParser.ParseFrom(body.ToArray());
            }
            catch (InvalidProtocolBufferException ex)
            {
                Violation(session, $"Request 파싱 실패: {ex.Message}");
                return;
            }

            var requestId = (long)_schema.RequestIdOfRequest.Accessor.GetValue(envelope);
            var requestCase = _schema.RequestMap.GetActiveCase(envelope);
            if (requestCase == null)
            {
                Violation(session, "body 없는 Request");
                return;
            }

            int status;
            IMessage? responsePayload = null;
            if (!_registrations.TryGetValue(requestCase.PayloadType, out var registration))
            {
                status = 1;   // 기동 검증상 불가 — 방어
            }
            else
            {
                try
                {
                    (status, responsePayload) = await registration
                        .Invoke((TSession)session, requestCase.Get(envelope)!)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (session.RaiseHandlerError(ex) == ErrorDecision.CloseSession)
                    {
                        Logger.LogError(ex,
                            "Session {SessionId}: handler exception on {Case}; closing per OnHandlerError.",
                            session.Id, requestCase.Name);
                        session.Kick();
                        return;
                    }

                    Logger.LogError(ex,
                        "Session {SessionId}: handler exception on {Case}; replying status 2.",
                        session.Id, requestCase.Name);
                    status = 2;
                }
            }

            var response = new TResponse();
            _schema.RequestIdOfResponse.Accessor.SetValue(response, requestId);
            _schema.StatusOfResponse.Accessor.SetValue(response, status);
            if (status == 0 && responsePayload != null)
            {
                _schema.ResponseMap.ByFieldNumber(requestCase.FieldNumber)!.Set(response, responsePayload);
            }

            await SendAsync(session, Channels.Response, response).ConfigureAwait(false);
        }

        private static ValueTask SendAsync(Session session, byte channel, IMessage message) =>
            session.SendAsync(PacketWriter.Wrap(channel, message));

        private void Violation(RpcSession session, string reason)
        {
            Logger.LogWarning("Session {SessionId}: 프로토콜 위반 — {Reason}; kicking.", session.Id, reason);
            session.Kick();
        }
    }
}
