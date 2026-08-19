using System;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Rpc.ControlMessages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Rpc
{
    /// <summary>
    /// Session base of the messaging layer. Raw packet handling (OnPacketAsync) is framework-owned;
    /// games participate only through the OnSessionOpenedAsync/OnSessionClosedAsync hooks and registered handlers.
    /// </summary>
    public abstract class RpcSession : Session
    {
        private IRpcRuntime? _runtime;
        private CancellationTokenSource? _watchdogCts;
        private long _lastReceivedTicksUtc;
        private int _disconnectSent;

        /// <summary>Creates a messaging session bound to the given connection.</summary>
        protected RpcSession(IConnection connection) : base(connection) { }

        /// <summary>Messaging default: a handler exception replies status=2 and keeps the session. Games may override.</summary>
        protected override ErrorDecision OnHandlerError(Exception ex) => ErrorDecision.Continue;

        /// <summary>Connection-opened hook (replaces v0 OnConnectedAsync). An exception here always kicks the session (regardless of OnHandlerError).</summary>
        protected virtual ValueTask OnSessionOpenedAsync() => default;

        /// <summary>Session-closed hook (replaces v0 OnDisconnectedAsync). error is null on normal close.</summary>
        protected virtual ValueTask OnSessionClosedAsync(Exception? error) => default;

        /// <summary>
        /// Gate called just before request dispatch. RpcStatus.Ok (0) proceeds;
        /// non-zero replies immediately with that status code and never reaches the handler.
        /// The Control channel (Ping) is not gated.
        /// </summary>
        protected internal virtual int OnGateRequest(Type requestType) => RpcStatus.Ok;

        /// <summary>Server push. update must be a case type of the game's Update oneof.</summary>
        public ValueTask SendUpdateAsync(IMessage update) =>
            RequireRuntime().SendUpdateAsync(this, update);

        /// <summary>Kicks with a reason code — best-effort sends Disconnect{code} (1-second cap,
        /// exceptions ignored, first call only), then closes the connection. Idempotent.</summary>
        public override void Kick(int reasonCode)
        {
            if (reasonCode == DisconnectCode.None)
            {
                Kick();   // 0 never goes on the wire — treated as a reasonless kick (does not consume the one-shot guard)
                return;
            }

            if (Interlocked.Exchange(ref _disconnectSent, 1) != 0)
            {
                // A reason is already being sent — that task's finally guarantees the close (1-second cap).
                // Calling raw Kick() here could cut off a send still being flushed.
                return;
            }

            _ = SendDisconnectThenCloseAsync(reasonCode);
        }

        private async Task SendDisconnectThenCloseAsync(int reasonCode)
        {
            try
            {
                var control = new Control { Disconnect = new Disconnect { Code = reasonCode } };
                var send = SendAsync(PacketWriter.Wrap(Channels.Control, control)).AsTask();
                await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
            }
            catch
            {
                // best-effort — closing anyway
            }
            finally
            {
                Kick();
            }
        }

        /// <summary>v0 connect hook. Sealed because the messaging layer owns it — games override OnSessionOpenedAsync.</summary>
        protected sealed override async ValueTask OnConnectedAsync()
        {
            Volatile.Write(ref _lastReceivedTicksUtc, DateTime.UtcNow.Ticks);
            StartIdleWatchdog();
            try
            {
                await OnSessionOpenedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // open hook failure = half-initialized state — always kick, regardless of the error policy (which applies to request handlers only)
                RequireRuntime().Logger.LogError(ex, "Session {SessionId}: OnSessionOpenedAsync threw; kicking.", Id);
                Kick();
            }
        }

        /// <summary>v0 packet hook. Sealed because the messaging layer owns it — raw packets always go to the runtime.</summary>
        protected sealed override ValueTask OnPacketAsync(ReadOnlyMemory<byte> packet)
        {
            Volatile.Write(ref _lastReceivedTicksUtc, DateTime.UtcNow.Ticks);
            return RequireRuntime().ProcessPacketAsync(this, packet);
        }

        /// <summary>v0 disconnect hook. Sealed because the messaging layer owns it — games override OnSessionClosedAsync.</summary>
        protected sealed override ValueTask OnDisconnectedAsync(Exception? error)
        {
            _watchdogCts?.Cancel();
            return OnSessionClosedAsync(error);
        }

        internal void AttachRuntime(IRpcRuntime runtime) => _runtime = runtime;

        internal ErrorDecision RaiseHandlerError(Exception ex) => OnHandlerError(ex);

        private IRpcRuntime RequireRuntime() =>
            _runtime ?? throw new InvalidOperationException(
                "Runtime not attached — RpcSession must be created through RpcServer.");

        private void StartIdleWatchdog()
        {
            var timeout = RequireRuntime().IdleKickTimeout;
            if (timeout == null)
            {
                return;
            }

            _watchdogCts = new CancellationTokenSource();
            _ = RunWatchdogAsync(timeout.Value, _watchdogCts.Token);
        }

        private async Task RunWatchdogAsync(TimeSpan timeout, CancellationToken ct)
        {
            var interval = TimeSpan.FromTicks(Math.Max(timeout.Ticks / 2, TimeSpan.TicksPerMillisecond * 50));
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    var last = new DateTime(Volatile.Read(ref _lastReceivedTicksUtc), DateTimeKind.Utc);
                    if (DateTime.UtcNow - last > timeout)
                    {
                        RequireRuntime().Logger.LogInformation(
                            "Session {SessionId}: idle for {Timeout}; kicking.", Id, timeout);
                        Kick(DisconnectCode.IdleKick);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // normal cancellation from session close
            }
        }
    }
}
