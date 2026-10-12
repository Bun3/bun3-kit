using System;
using System.Globalization;
using Bun3.Unity.Rooms.Ngo;
using Bun3.Unity.Steam.Ngo;
using Unity.Netcode;

namespace Bun3.Unity.Rooms.Steam.Ngo
{
    /// <summary>
    /// Room transport over <see cref="SteamSocketsTransport"/>. The host endpoint is the host's SteamID and the
    /// host accepts only SteamIDs that are current room members.
    /// </summary>
    public sealed class SteamRoomTransport : INgoRoomTransport
    {
        private readonly SteamSocketsTransport transport;

        /// <summary>Wraps the manager's Steam transport component.</summary>
        public SteamRoomTransport(SteamSocketsTransport transport) =>
            this.transport = transport != null ? transport : throw new ArgumentNullException(nameof(transport));

        /// <summary>Admits only members of <paramref name="room"/> and returns the local SteamID as the endpoint.</summary>
        public string PrepareHost(NetworkManager manager, IRoom room)
        {
            transport.AcceptFilter = steamId => IsMember(room, steamId);
            manager.NetworkConfig.NetworkTransport = transport;
            return room.LocalMemberId;
        }

        /// <summary>Targets the host SteamID in <paramref name="endpoint"/>.</summary>
        /// <exception cref="FormatException"><paramref name="endpoint"/> is not a non-zero decimal SteamID.</exception>
        public void PrepareClient(NetworkManager manager, IRoom room, string endpoint)
        {
            transport.TargetSteamId = ParseEndpoint(endpoint);
            manager.NetworkConfig.NetworkTransport = transport;
        }

        internal static bool IsMember(IRoom room, ulong steamId)
        {
            var members = room.Members;
            for (int i = 0; i < members.Count; i++)
                if (ulong.TryParse(members[i].Id, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && id == steamId)
                    return true;
            return false;
        }

        internal static ulong ParseEndpoint(string endpoint) =>
            ulong.TryParse(endpoint, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && id != 0
                ? id
                : throw new FormatException("The host endpoint is not a SteamID.");
    }
}
