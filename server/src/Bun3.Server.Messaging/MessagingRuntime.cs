using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Bun3.Server.Messaging.ControlMessages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Messaging
{
    /// <summary>MessagingSession이 패킷 처리를 위임하는 비제네릭 창구.</summary>
    internal interface IMessagingRuntime
    {
        TimeSpan? IdleKickTimeout { get; }
        ILogger Logger { get; }
        ValueTask ProcessPacketAsync(MessagingSession session, ReadOnlyMemory<byte> packet);
        ValueTask SendUpdateAsync(Session session, IMessage update);
    }

    /// <summary>채널 분기·요청 디스패치·응답 조립 — 서버 측 메시징의 두뇌. 상태는 전부 기동 시 구축.</summary>
    internal sealed class MessagingRuntime<TSession, TRequest, TResponse, TUpdate> : IMessagingRuntime
        where TSession : MessagingSession
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        private readonly MessagingSchema<TRequest, TResponse, TUpdate> _schema;
        private readonly Dictionary<Type, MessagingConfig<TSession>.Registration> _registrations;

        public MessagingRuntime(
            MessagingSchema<TRequest, TResponse, TUpdate> schema,
            MessagingConfig<TSession> config,
            MessagingServerOptions options,
            ILogger logger)
        {
            schema.Validate(config);   // 기동 fail-fast — 위반 전체 목록과 함께 throw
            _schema = schema;
            _registrations = config.Registrations;
            IdleKickTimeout = options.IdleKickTimeout;
            Logger = logger;
        }

        public TimeSpan? IdleKickTimeout { get; }

        public ILogger Logger { get; }

        public async ValueTask ProcessPacketAsync(MessagingSession session, ReadOnlyMemory<byte> packet)
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

        private async ValueTask HandleControlAsync(MessagingSession session, ReadOnlyMemory<byte> body)
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
                Violation(session, $"클라이언트가 보낼 수 없는 Control: {control.BodyCase}");
                return;
            }

            var pong = new Control { Pong = new Pong { ClientTimeUnixMs = control.Ping.ClientTimeUnixMs } };
            await SendAsync(session, Channels.Control, pong).ConfigureAwait(false);
        }

        private async ValueTask HandleRequestAsync(MessagingSession session, ReadOnlyMemory<byte> body)
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

        private static ValueTask SendAsync(Session session, byte channel, IMessage message)
        {
            var body = message.ToByteArray();
            var packet = new byte[1 + body.Length];
            packet[0] = channel;
            body.CopyTo(packet, 1);
            return session.SendAsync(packet);
        }

        private void Violation(MessagingSession session, string reason)
        {
            Logger.LogWarning("Session {SessionId}: 프로토콜 위반 — {Reason}; kicking.", session.Id, reason);
            session.Kick();
        }
    }
}
