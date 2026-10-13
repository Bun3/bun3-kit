// Freezes participant packets and observes delivery after releasing execution ownership.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bun3.Server.Rpc;

namespace Bun3.Server.Players
{
    internal sealed class PlayerOperationNotifications
    {
        private readonly List<(Player Player, RpcSession Session, byte[] Packet)> _packets = new List<(Player, RpcSession, byte[])>();
        private readonly List<(string Key, Task Send)> _sends = new List<(string, Task)>();
        internal Exception? Error;
        internal void Add(Player player, RpcSession session, byte[] packet) => _packets.Add((player, session, packet));
        internal void Start()
        {
            foreach (var packet in _packets)
            {
                if (!packet.Player.IsValid || !ReferenceEquals(packet.Player.CurrentSession, packet.Session) || !packet.Session.Connection.IsOpen) continue;
                try { _sends.Add((packet.Player.AccountKey, packet.Session.SendAsync(packet.Packet).AsTask())); }
                catch (Exception ex) { _sends.Add((packet.Player.AccountKey, Task.FromException(ex))); }
            }
            _packets.Clear();
        }
        internal async ValueTask ObserveAsync(PlayerOperationDiagnostics diagnostics)
        {
            foreach (var send in _sends)
            {
                try { await send.Send.ConfigureAwait(false); }
                catch (Exception ex) { diagnostics.Record("DeliveryFailed", send.Key, error: ex); }
            }
            _sends.Clear();
            _packets.Clear();
        }
    }
}
