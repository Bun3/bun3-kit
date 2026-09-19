using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using global::Dissonance;
using global::Dissonance.Audio.Capture;
using global::Dissonance.Integrations.Unity_NFGO;
using global::Dissonance.Networking;
using NAudio.Wave;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Dissonance.Netcode.Tests.PlayMode
{
    public sealed class SilentCapture : MonoBehaviour, IMicrophoneCapture
    {
        public bool IsRecording { get; private set; }
        public string Device => null;
        public TimeSpan Latency => TimeSpan.Zero;
        public WaveFormat StartCapture(string name) { IsRecording = true; return new WaveFormat(48000, 1); }
        public void StopCapture() { IsRecording = false; }
        public void Subscribe(IMicrophoneSubscriber listener) { }
        public bool Unsubscribe(IMicrophoneSubscriber listener) => true;
        public bool UpdateSubscribers() => false;
    }

    public sealed class DissonanceNfgoHostTests
    {
        [UnityTest]
        public IEnumerator ActualHostHandshakeTracksDespawnsAndReusesPlayerWithoutHardwareCapture()
        {
            var scopeType = Type.GetType("Bun3.Unity.Audio.Dissonance.Netcode.DissonanceNfgoSessionScope, Bun3.Unity.Audio.Dissonance.Netcode");
            var playerType = Type.GetType("Bun3.Unity.Audio.Dissonance.Netcode.DissonanceNfgoPlayer, Bun3.Unity.Audio.Dissonance.Netcode");
            Assert.That(scopeType, Is.Not.Null, "A session binding is required before the real host starts.");
            Assert.That(playerType, Is.Not.Null);
            var root = new GameObject("Actual NGO voice host"); root.SetActive(false);
            var voice = new GameObject("Actual SDK with silent capture"); voice.SetActive(false);
            var player = new GameObject("Reusable voice avatar"); player.SetActive(false);
            NetworkManager manager = null;
            IDisposable scope = null;
            bool priorBackground = Application.runInBackground;
            try
            {
                Application.runInBackground = true;
                var transport = root.AddComponent<UnityTransport>();
                using (var socket = new UdpClient(0))
                    transport.SetConnectionData("127.0.0.1", (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port, "127.0.0.1");
                manager = root.AddComponent<NetworkManager>();
                manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
                voice.AddComponent<SilentCapture>();
                var comms = voice.AddComponent<DissonanceComms>();
                comms.LocalPlayerName = "bun3-host-fixture";
                var sdk = voice.AddComponent<NfgoCommsNetwork>();
                scope = (IDisposable)Activator.CreateInstance(scopeType, manager, comms, sdk);
                root.SetActive(true); voice.SetActive(true);
                Assert.That(manager.StartHost(), Is.True);
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while ((sdk.Mode != NetworkMode.Host || sdk.Status != ConnectionStatus.Connected) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Assert.That(sdk.Mode, Is.EqualTo(NetworkMode.Host));
                Assert.That(sdk.Status, Is.EqualTo(ConnectionStatus.Connected));
                Assert.That(scopeType.GetProperty("IsReady").GetValue(scope), Is.True);
                Assert.That(voice.GetComponent<BasicMicrophoneCapture>(), Is.Null);
                var networkObject = player.AddComponent<NetworkObject>();
                var tracker = (IDissonancePlayer)player.AddComponent(playerType);
                player.SetActive(true);
                networkObject.SpawnAsPlayerObject(manager.LocalClientId);
                deadline = Time.realtimeSinceStartupAsDouble + 5;
                while (!tracker.IsTracking && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Assert.That(tracker.IsTracking, Is.True);
                Assert.That(tracker.PlayerId, Is.EqualTo(comms.LocalPlayerName));
                Assert.That(tracker.Type, Is.EqualTo(NetworkPlayerType.Local));
                object[] lookup = { manager.LocalClientId, null };
                Assert.That(scopeType.GetMethod("TryGetPlayerId").Invoke(scope, lookup), Is.True);
                Assert.That(lookup[1], Is.EqualTo(comms.LocalPlayerName));
                networkObject.Despawn(false);
                Assert.That(tracker.IsTracking, Is.False);
                Assert.That(scopeType.GetMethod("TryGetPlayerId").Invoke(scope, lookup), Is.False);
                networkObject.SpawnAsPlayerObject(manager.LocalClientId);
                deadline = Time.realtimeSinceStartupAsDouble + 5;
                while (!tracker.IsTracking && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Assert.That(tracker.IsTracking, Is.True);
                // Actual SDK destruction must release the tracking registration on the next owner update.
                scope.Dispose();
                Assert.That(tracker.IsTracking, Is.False);
                Assert.That(manager.IsListening, Is.True);
                scope = (IDisposable)Activator.CreateInstance(scopeType, manager, comms, sdk);
                deadline = Time.realtimeSinceStartupAsDouble + 5;
                while (!tracker.IsTracking && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Assert.That(tracker.IsTracking, Is.True);
                UnityEngine.Object.DestroyImmediate(voice);
                yield return null;
                yield return null;
                Assert.That(tracker.IsTracking, Is.False);
                Assert.That(scopeType.GetProperty("IsReady").GetValue(scope), Is.False);
                scope.Dispose();
                Assert.That(tracker.IsTracking, Is.False);
                Assert.That(manager.IsListening, Is.True);
            }
            finally
            {
                scope?.Dispose();
                if (manager != null && manager.IsListening) manager.Shutdown();
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(voice);
                UnityEngine.Object.DestroyImmediate(root);
                Application.runInBackground = priorBackground;
            }
            yield return null;
        }
    }
}
