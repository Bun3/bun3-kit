using Cysharp.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Rooms.Ngo.Tests
{
    internal static class NgoTestPeers
    {
        public static NetworkManager Create(string name)
        {
            var root = new GameObject(name);
            var utp = root.AddComponent<UnityTransport>();
            var manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig { NetworkTransport = utp, EnableSceneManagement = false };
            return manager;
        }

        public static UtpRoomTransport Transport(NetworkManager manager) =>
            new UtpRoomTransport(manager.GetComponent<UnityTransport>());

        public static async UniTask DestroyAsync(params NetworkManager[] managers)
        {
            foreach (var manager in managers)
                if (manager != null && manager.IsListening) manager.Shutdown();
            await UniTask.DelayFrame(3);
            foreach (var manager in managers)
                if (manager != null) Object.Destroy(manager.gameObject);
            await UniTask.DelayFrame(1);
        }
    }
}
