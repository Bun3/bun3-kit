#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Bun3.Chat;
using Unity.Collections;
using Unity.Netcode;

namespace Bun3.Unity.Chat.Ngo
{
    /// <summary>
    /// Carries <see cref="ChatRelay"/> traffic over Netcode for GameObjects named messages.
    /// The server validates and fans out submissions; peers display only what the server sends back.
    /// Newly connected clients receive the relay's replay before any later live message: the server relays live
    /// messages only to peers whose replay has already been queued.
    /// </summary>
    public sealed class NgoChatScope : IDisposable
    {
        const string SubmitName = "Bun3.Chat.Submit";
        const string MessageName = "Bun3.Chat.Message";
        const string HistoryName = "Bun3.Chat.History";
        const int ChunkBytes = 4096;
        const int ReservedBytes = 512;
        const int FixedBytes = sizeof(ulong) + sizeof(byte) + sizeof(long) + 3 * sizeof(ushort);
        const NetworkDelivery Delivery = NetworkDelivery.ReliableFragmentedSequenced;

        readonly NetworkManager manager;
        readonly ChatRelay relay;
        readonly List<ulong> recipients = new List<ulong>();
        readonly List<ulong> ready = new List<ulong>();
        readonly List<ChatMessage> buffer = new List<ChatMessage>();
        CustomMessagingManager? messaging;
        bool submitRegistered;
        bool disposed;

        /// <summary>Binds <paramref name="relay"/> to the current and every future session of <paramref name="manager"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">One message would not fit a 4 KB history chunk (MaxLength above 896).</exception>
        public NgoChatScope(NetworkManager manager, ChatRelay relay)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            this.manager = manager;
            this.relay = relay ?? throw new ArgumentNullException(nameof(relay));
            if ((long)relay.MaxLength * 4 + ReservedBytes > ChunkBytes)
                throw new ArgumentOutOfRangeException(nameof(relay), "MaxLength must be at most 896 so one message fits a history chunk.");
            manager.OnServerStarted += Register;
            manager.OnClientStarted += Register;
            manager.OnServerStopped += OnServerStopped;
            manager.OnClientStopped += OnClientStopped;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            if (manager.IsListening) Register();
        }

        /// <summary>
        /// Raised for each live message the server relays to this peer. Handlers must not call <see cref="Send"/>
        /// synchronously, because on the host the event is raised inside the relay's fan-out.
        /// </summary>
        public event Action<ChatMessage>? Received;

        /// <summary>Raised for each replayed history message, oldest first, after this peer connects.</summary>
        public event Action<ChatMessage>? HistoryReceived;

        /// <summary>
        /// Sends normalized text to the server. Returns false when disposed, the text is invalid, or this peer is
        /// neither a connected client nor a host (a server without a local client cannot send).
        /// Nothing is shown locally until the server relays the message back.
        /// </summary>
        public bool Send(string text)
        {
            if (disposed || messaging == null || !(manager.IsHost || manager.IsConnectedClient)) return false;
            if (!ChatText.TryNormalize(text, relay.MaxLength, out var normalized)) return false;
            var bytes = Encoding.UTF8.GetBytes(normalized);
            using var writer = new FastBufferWriter(sizeof(ushort) + bytes.Length, Allocator.Temp);
            writer.WriteValueSafe((ushort)bytes.Length);
            writer.WriteBytesSafe(bytes);
            messaging.SendNamedMessage(SubmitName, NetworkManager.ServerClientId, writer, Delivery);
            return true;
        }

        /// <summary>Unsubscribes from the manager and unregisters every handler.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Unregister();
            ready.Clear();
            manager.OnServerStarted -= Register;
            manager.OnClientStarted -= Register;
            manager.OnServerStopped -= OnServerStopped;
            manager.OnClientStopped -= OnClientStopped;
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        void Register()
        {
            var current = manager.CustomMessagingManager;
            if (disposed || current == null) return;
            if (!ReferenceEquals(messaging, current))
            {
                Unregister();
                messaging = current;
                current.RegisterNamedMessageHandler(MessageName, OnMessage);
                current.RegisterNamedMessageHandler(HistoryName, OnHistory);
            }
            if (manager.IsHost) MarkReady(manager.LocalClientId);
            if (manager.IsServer && !submitRegistered)
            {
                current.RegisterNamedMessageHandler(SubmitName, OnSubmit);
                submitRegistered = true;
            }
        }

        void Unregister()
        {
            if (messaging == null) return;
            messaging.UnregisterNamedMessageHandler(MessageName);
            messaging.UnregisterNamedMessageHandler(HistoryName);
            if (submitRegistered) messaging.UnregisterNamedMessageHandler(SubmitName);
            messaging = null;
            submitRegistered = false;
        }

        void MarkReady(ulong clientId)
        {
            if (!ready.Contains(clientId)) ready.Add(clientId);
        }

        void OnServerStopped(bool _)
        {
            ready.Clear();
            relay.Clear();
            Unregister();
        }

        void OnClientStopped(bool _) => Unregister();

        void OnClientDisconnected(ulong clientId)
        {
            if (!manager.IsServer) return;
            ready.Remove(clientId);
            relay.Forget(clientId);
        }

        void OnClientConnected(ulong clientId)
        {
            if (!manager.IsServer || messaging == null) return;
            if (clientId == manager.LocalClientId || relay.HistoryReplayCount == 0)
            {
                MarkReady(clientId);
                return;
            }
            relay.CollectReplay(clientId, buffer);
            // ponytail: the whole replay is queued at once; spread chunks across ticks if join-time spikes matter.
            for (int start = 0; start < buffer.Count;)
            {
                int end = start, size = sizeof(ushort);
                while (end < buffer.Count && (end == start || size + SizeOf(buffer[end]) <= ChunkBytes)) size += SizeOf(buffer[end++]);
                using var writer = new FastBufferWriter(size, Allocator.Temp);
                writer.WriteValueSafe((ushort)(end - start));
                for (int i = start; i < end; i++) Write(writer, buffer[i]);
                messaging.SendNamedMessage(HistoryName, clientId, writer, Delivery);
                start = end;
            }
            buffer.Clear();
            MarkReady(clientId);
        }

        void OnSubmit(ulong senderClientId, FastBufferReader reader)
        {
            if (!manager.IsServer || messaging == null) return;
            string text;
            try
            {
                if (!TryReadUtf8(reader, relay.MaxLength * 4, out text)) return;
            }
            catch (OverflowException) { return; }
            if (relay.Submit(senderClientId, text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), out var message) != ChatRejectReason.None) return;
            relay.CollectRecipients(message, ready, recipients);
            if (recipients.Count == 0) return;
            using var writer = new FastBufferWriter(SizeOf(message), Allocator.Temp);
            Write(writer, message);
            messaging.SendNamedMessage(MessageName, recipients, writer, Delivery);
        }

        void OnMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (senderClientId != NetworkManager.ServerClientId) return;
            ChatMessage message;
            try
            {
                if (!TryRead(reader, relay.MaxLength * 4, out message)) return;
            }
            catch (OverflowException) { return; }
            Received?.Invoke(message);
        }

        void OnHistory(ulong senderClientId, FastBufferReader reader)
        {
            if (senderClientId != NetworkManager.ServerClientId) return;
            buffer.Clear();
            try
            {
                reader.ReadValueSafe(out ushort count);
                for (int i = 0; i < count; i++)
                {
                    if (!TryRead(reader, relay.MaxLength * 4, out var message)) break;
                    buffer.Add(message);
                }
            }
            catch (OverflowException) { /* Keep the messages read before the malformed tail. */ }
            for (int i = 0; i < buffer.Count; i++) HistoryReceived?.Invoke(buffer[i]);
            buffer.Clear();
        }

        static int SizeOf(in ChatMessage message) => FixedBytes +
            Encoding.UTF8.GetByteCount(message.SenderPlayerId) + Encoding.UTF8.GetByteCount(message.SenderName) +
            Encoding.UTF8.GetByteCount(message.Text);

        static void Write(FastBufferWriter writer, in ChatMessage message)
        {
            writer.WriteValueSafe(message.SenderId);
            writer.WriteValueSafe(message.Channel);
            writer.WriteValueSafe(message.TimestampMs);
            WriteUtf8(writer, message.SenderPlayerId);
            WriteUtf8(writer, message.SenderName);
            WriteUtf8(writer, message.Text);
        }

        static bool TryRead(FastBufferReader reader, int maxTextBytes, out ChatMessage message)
        {
            message = default;
            reader.ReadValueSafe(out ulong senderId);
            reader.ReadValueSafe(out byte channel);
            reader.ReadValueSafe(out long timestampMs);
            if (!TryReadUtf8(reader, ChatMessage.MaxIdentityBytes, out var playerId) ||
                !TryReadUtf8(reader, ChatMessage.MaxIdentityBytes - Encoding.UTF8.GetByteCount(playerId), out var name) ||
                !TryReadUtf8(reader, maxTextBytes, out var text)) return false;
            message = new ChatMessage(senderId, playerId, name, text, channel, timestampMs);
            return true;
        }

        static void WriteUtf8(FastBufferWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.WriteValueSafe((ushort)bytes.Length);
            writer.WriteBytesSafe(bytes);
        }

        static bool TryReadUtf8(FastBufferReader reader, int maxBytes, out string value)
        {
            value = string.Empty;
            reader.ReadValueSafe(out ushort length);
            if (length > maxBytes) return false;
            var bytes = new byte[length];
            reader.ReadBytesSafe(ref bytes, length);
            value = Encoding.UTF8.GetString(bytes);
            return true;
        }
    }
}
