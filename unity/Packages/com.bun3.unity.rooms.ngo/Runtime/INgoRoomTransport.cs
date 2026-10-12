using Unity.Netcode;

namespace Bun3.Unity.Rooms.Ngo
{
    /// <summary>Points a NetworkManager at the transport and peer for a room.</summary>
    public interface INgoRoomTransport
    {
        /// <summary>
        /// Makes this transport the manager's transport, prepares it to host <paramref name="room"/>,
        /// and returns the endpoint clients connect to.
        /// </summary>
        string PrepareHost(NetworkManager manager, IRoom room);

        /// <summary>Makes this transport the manager's transport and targets the host at <paramref name="endpoint"/>.</summary>
        void PrepareClient(NetworkManager manager, IRoom room, string endpoint);
    }
}
