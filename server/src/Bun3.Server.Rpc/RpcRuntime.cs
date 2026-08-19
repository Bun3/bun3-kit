using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Bun3.Server.Rpc.ControlMessages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Rpc
{
    /// <summary>Non-generic surface that RpcSession delegates packet processing to.</summary>
    internal interface IRpcRuntime
    {
        TimeSpan? IdleKickTimeout { get; }
        ILogger Logger { get; }
        ValueTask ProcessPacketAsync(RpcSession session, ReadOnlyMemory<byte> packet);
        ValueTask SendUpdateAsync(Session session, IMessage update);
    }

    /// <summary>Channel branching, request dispatch, and response assembly — the server-side messaging core. All state is built at startup.</summary>
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
            schema.Validate(config);   // startup fail-fast — throws with the full list of violations
            _schema = schema;
            // Snapshot copy — config is a caller-owned object that stays alive after startup;
            // holding the original Dictionary would let later config.OnRequest(...) calls mutate
            // a dictionary read concurrently by session threads (undefined behavior, bypasses Validate).
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
                Violation(session, "Empty packet");
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
                    Violation(session, $"Disallowed channel 0x{channel:X2}");
                    break;
            }
        }

        public ValueTask SendUpdateAsync(Session session, IMessage update)
        {
            var updateCase = _schema.UpdateMap.ByPayloadType(update.GetType())
                ?? throw new ArgumentException($"Type not in Update oneof: {update.GetType().Name}", nameof(update));
            var envelope = new TUpdate();
            updateCase.Set(envelope, update);
            return SendAsync(session, Channels.Update, envelope);
        }

        private async ValueTask HandleControlAsync(RpcSession session, ReadOnlyMemory<byte> body)
        {
            Control control;
            try
            {
                control = Control.Parser.ParseFrom(new ReadOnlySequence<byte>(body));   // zero-copy parse
            }
            catch (InvalidProtocolBufferException ex)
            {
                Violation(session, $"Control parse failure: {ex.Message}");
                return;
            }

            if (control.BodyCase != Control.BodyOneofCase.Ping)
            {
                // Server is strict (unknown Control = violation), client is lenient (warn and ignore) —
                // deliberate asymmetry so rolling upgrades can ship new Control messages server-first.
                Violation(session, $"Control not allowed from client: {control.BodyCase}");
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
                envelope = _schema.RequestParser.ParseFrom(new ReadOnlySequence<byte>(body));   // zero-copy parse
            }
            catch (InvalidProtocolBufferException ex)
            {
                Violation(session, $"Request parse failure: {ex.Message}");
                return;
            }

            var requestId = (long)_schema.RequestIdOfRequest.Accessor.GetValue(envelope);
            var requestCase = _schema.RequestMap.GetActiveCase(envelope);
            if (requestCase == null)
            {
                Violation(session, "Request without body");
                return;
            }

            var gate = session.OnGateRequest(requestCase.PayloadType);
            if (gate != RpcStatus.Ok)
            {
                var gatedResponse = new TResponse();
                _schema.RequestIdOfResponse.Accessor.SetValue(gatedResponse, requestId);
                _schema.StatusOfResponse.Accessor.SetValue(gatedResponse, gate);
                await SendAsync(session, Channels.Response, gatedResponse).ConfigureAwait(false);
                return;
            }

            int status;
            IMessage? responsePayload = null;
            if (!_registrations.TryGetValue(requestCase.PayloadType, out var registration))
            {
                status = RpcStatus.UnregisteredHandler;   // impossible after startup validation — defensive
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
                    status = RpcStatus.HandlerException;
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
            Logger.LogWarning("Session {SessionId}: protocol violation — {Reason}; kicking.", session.Id, reason);
            session.Kick(DisconnectCode.ProtocolViolation);
        }
    }
}
