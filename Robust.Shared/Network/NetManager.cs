﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Lidgren.Network;
using Robust.Shared.Configuration;
using Robust.Shared.Interfaces.Configuration;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Utility;

namespace Robust.Shared.Network
{
    /// <summary>
    ///     Callback for registered NetMessages.
    /// </summary>
    /// <param name="message">The message received.</param>
    public delegate void ProcessMessage(NetMessage message);

    /// <summary>
    ///     Callback for registered NetMessages.
    /// </summary>
    /// <param name="message">The message received.</param>
    public delegate void ProcessMessage<in T>(T message) where T : NetMessage;

    /// <summary>
    ///     Manages all network connections and packet IO.
    /// </summary>
    public partial class NetManager : IClientNetManager, IServerNetManager, IDisposable
    {
        private readonly Dictionary<Type, ProcessMessage> _callbacks = new Dictionary<Type, ProcessMessage>();

        /// <summary>
        ///     Holds the synced lookup table of NetConnection -> NetChannel
        /// </summary>
        private readonly Dictionary<NetConnection, NetChannel> _channels = new Dictionary<NetConnection, NetChannel>();

        private readonly Dictionary<NetConnection, NetSessionId> _assignedSessions =
            new Dictionary<NetConnection, NetSessionId>();

#pragma warning disable 649
        [Dependency] private readonly IConfigurationManager _config;
#pragma warning restore 649

        /// <summary>
        ///     Holds lookup table for NetMessage.Id -> NetMessage.Type
        /// </summary>
        private readonly Dictionary<string, Type> _messages = new Dictionary<string, Type>();

        /// <summary>
        /// The StringTable for transforming packet Ids to Packet name.
        /// </summary>
        private readonly StringTable _strings = new StringTable();

        /// <summary>
        ///     The list of network peers we are listening on.
        /// </summary>
        private readonly List<NetPeer> _netPeers = new List<NetPeer>();
        private readonly List<NetPeer> _toCleanNetPeers = new List<NetPeer>();

        /// <inheritdoc />
        public int Port => _config.GetCVar<int>("net.port");

        /// <inheritdoc />
        public bool IsServer { get; private set; }

        /// <inheritdoc />
        public bool IsClient => !IsServer;

        /// <inheritdoc />
        public bool IsConnected => _netPeers.Any(p => p.ConnectionsCount > 0);

        public bool IsRunning => _netPeers.Count != 0;

        private ClientConnectionState _clientConnectionState;

        public NetworkStats Statistics
        {
            get
            {
                var sentPackets = 0;
                var sentBytes = 0;
                var recvPackets = 0;
                var recvBytes = 0;

                foreach (var peer in _netPeers)
                {
                    var netPeerStatistics = peer.Statistics;
                    sentPackets += netPeerStatistics.SentPackets;
                    sentBytes += netPeerStatistics.SentBytes;
                    recvPackets += netPeerStatistics.ReceivedPackets;
                    recvBytes += netPeerStatistics.ReceivedBytes;
                }

                return new NetworkStats(sentBytes, recvBytes, sentPackets, recvPackets);
            }
        }

        /// <inheritdoc />
        public IEnumerable<INetChannel> Channels => _channels.Values;

        /// <inheritdoc />
        public int ChannelCount => _channels.Count;

        public IReadOnlyDictionary<Type, ProcessMessage> CallbackAudit => _callbacks;

        /// <inheritdoc />
        public INetChannel ServerChannel
        {
            get
            {
                DebugTools.Assert(IsClient);

                if (_netPeers.Count == 0)
                {
                    return null;
                }

                var peer = _netPeers[0];
                if (peer.ConnectionsCount == 0)
                {
                    return null;
                }

                return GetChannel(peer.Connections[0]);
            }
        }

        private bool _initialized;

        /// <inheritdoc />
        public void Initialize(bool isServer)
        {
            if (_initialized)
            {
                throw new InvalidOperationException("NetManager has already been initialized.");
            }

            IsServer = isServer;

            _config.RegisterCVar("net.port", 1212, CVar.ARCHIVE);

            if (!isServer)
            {
                _config.RegisterCVar("net.server", "127.0.0.1", CVar.ARCHIVE);
                _config.RegisterCVar("net.updaterate", 20, CVar.ARCHIVE);
                _config.RegisterCVar("net.cmdrate", 30, CVar.ARCHIVE);
                _config.RegisterCVar("net.rate", 10240, CVar.REPLICATED | CVar.ARCHIVE);
            }
            else
            {
                // That's comma-separated, btw.
                _config.RegisterCVar("net.bindto", "0.0.0.0,::", CVar.ARCHIVE);
            }

#if DEBUG
            _config.RegisterCVar("net.fakeloss", 0.0f, CVar.CHEAT, _fakeLossChanged);
            _config.RegisterCVar("net.fakelagmin", 0.0f, CVar.CHEAT, _fakeLagMinChanged);
            _config.RegisterCVar("net.fakelagrand", 0.0f, CVar.CHEAT, _fakeLagRandomChanged);
#endif

            _strings.Initialize(this, () => { OnConnected(ServerChannel); });

            _initialized = true;
        }

        public void StartServer()
        {
            DebugTools.Assert(IsServer);
            DebugTools.Assert(!IsRunning);

            var binds = _config.GetCVar<string>("net.bindto").Split(',');

            foreach (var bindAddress in binds)
            {
                if (!IPAddress.TryParse(bindAddress.Trim(), out var address))
                {
                    throw new InvalidOperationException("Not a valid IPv4 or IPv6 address");
                }

                var config = _getBaseNetPeerConfig();
                config.LocalAddress = address;
                config.Port = Port;
                config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

                var peer = new NetPeer(config);
                peer.Start();
                _netPeers.Add(peer);
            }

            if (_netPeers.Count == 0)
            {
                Logger.WarningS("net",
                    "Exactly 0 addresses have been bound to, nothing will be able to connect to the server.");
            }
        }

        public void Dispose()
        {
            Shutdown("Network manager getting disposed.");
        }

        /// <inheritdoc />
        public void Shutdown(string reason)
        {
            foreach (var kvChannel in _channels)
                DisconnectChannel(kvChannel.Value, reason);

            // request shutdown of the netPeer
            _netPeers.ForEach(p => p.Shutdown(reason));
            _netPeers.Clear();

            // wait for the network thread to finish its work (like flushing packets and gracefully disconnecting)
            // Lidgren does not expose the thread, so we can't join or or anything
            // pretty much have to poll every so often and wait for it to finish before continuing
            // when the network thread is finished, it will change status from ShutdownRequested to NotRunning
            while (_netPeers.Any(p => p.Status == NetPeerStatus.ShutdownRequested))
            {
                // sleep the thread for an arbitrary length so it isn't spinning in the while loop as much
                Thread.Sleep(50);
            }

            _strings.Reset();

            _cancelConnectTokenSource?.Cancel();
            _clientConnectionState = ClientConnectionState.NotConnecting;
        }

        public void ProcessPackets()
        {
            foreach (var peer in _netPeers)
            {
                NetIncomingMessage msg;
                var recycle = true;
                while ((msg = peer.ReadMessage()) != null)
                {
                    switch (msg.MessageType)
                    {
                        case NetIncomingMessageType.VerboseDebugMessage:
                            Logger.DebugS("net", "{0}: {1}", peer.Configuration.LocalAddress, msg.ReadString());
                            break;

                        case NetIncomingMessageType.DebugMessage:
                            Logger.InfoS("net", "{0}: {1}", peer.Configuration.LocalAddress, msg.ReadString());
                            break;

                        case NetIncomingMessageType.WarningMessage:
                            Logger.WarningS("net", "{0}: {1}", peer.Configuration.LocalAddress, msg.ReadString());
                            break;

                        case NetIncomingMessageType.ErrorMessage:
                            Logger.ErrorS("net", "{0}: {1}", peer.Configuration.LocalAddress, msg.ReadString());
                            break;

                        case NetIncomingMessageType.ConnectionApproval:
                            HandleApproval(msg);
                            break;

                        case NetIncomingMessageType.Data:
                            recycle = DispatchNetMessage(msg);
                            break;

                        case NetIncomingMessageType.StatusChanged:
                            HandleStatusChanged(msg);
                            break;

                        default:
                            Logger.WarningS("net",
                                "{0}: Unhandled incoming packet type from {1}: {2}",
                                peer.Configuration.LocalAddress,
                                msg.SenderConnection.RemoteEndPoint,
                                msg.MessageType);
                            break;
                    }

                    if (recycle)
                    {
                        peer.Recycle(msg);
                    }
                }
            }

            if (_toCleanNetPeers.Count != 0)
            {
                foreach (var peer in _toCleanNetPeers)
                {
                    _netPeers.Remove(peer);
                }
            }
        }

        /// <inheritdoc />
        public void ClientDisconnect(string reason)
        {
            DebugTools.Assert(IsClient, "Should never be called on the server.");
            Disconnect?.Invoke(this, new NetChannelArgs(ServerChannel));
            Shutdown(reason);
        }

        private NetPeerConfiguration _getBaseNetPeerConfig()
        {
            var netConfig = new NetPeerConfiguration("SS14_NetTag");

#if DEBUG
            //Simulate Latency
            netConfig.SimulatedLoss = _config.GetCVar<float>("net.fakeloss");
            netConfig.SimulatedMinimumLatency = _config.GetCVar<float>("net.fakelagmin");
            netConfig.SimulatedRandomLatency = _config.GetCVar<float>("net.fakelagrand");

            netConfig.ConnectionTimeout = 30000f;
#endif
            return netConfig;
        }

#if DEBUG
        private void _fakeLossChanged(float newValue)
        {
            foreach (var peer in _netPeers)
            {
                peer.Configuration.SimulatedLoss = newValue;
            }
        }

        private void _fakeLagMinChanged(float newValue)
        {
            foreach (var peer in _netPeers)
            {
                peer.Configuration.SimulatedMinimumLatency = newValue;
            }
        }

        private void _fakeLagRandomChanged(float newValue)
        {
            foreach (var peer in _netPeers)
            {
                peer.Configuration.SimulatedRandomLatency = newValue;
            }
        }
#endif

        /// <summary>
        ///     Gets the NetChannel of a peer NetConnection.
        /// </summary>
        /// <param name="connection">The raw connection of the peer.</param>
        /// <returns>The NetChannel of the peer.</returns>
        private INetChannel GetChannel(NetConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            if (_channels.TryGetValue(connection, out NetChannel channel))
                return channel;

            throw new NetManagerException("There is no NetChannel for this NetConnection.");
        }

        private void HandleStatusChanged(NetIncomingMessage msg)
        {
            var sender = msg.SenderConnection;
            msg.ReadByte();
            var reason = msg.ReadString();
            Logger.DebugS("net", $"{sender.RemoteEndPoint}: Status changed to {sender.Status}");

            if (_awaitingStatusChange.TryGetValue(sender, out var resume))
            {
                resume.Item1.Dispose();
                resume.Item2.SetResult(reason);
                _awaitingStatusChange.Remove(sender);
                return;
            }

            switch (sender.Status)
            {
                case NetConnectionStatus.Connected:
                    if (IsServer)
                    {
                        HandleHandshake(sender);
                    }

                    break;

                case NetConnectionStatus.Disconnected:
                    if (_awaitingData.TryGetValue(sender, out var awaitInfo))
                    {
                        awaitInfo.Item1.Dispose();
                        awaitInfo.Item2.TrySetException(
                            new ClientDisconnectedException($"Disconnected: {reason}"));
                        _awaitingData.Remove(sender);
                    }

                    if (_channels.ContainsKey(sender))
                    {
                        HandleDisconnect(sender, reason);
                    }

                    break;
            }
        }

        private void HandleApproval(NetIncomingMessage message)
        {
            // TODO: Maybe preemptively refuse connections here in some cases?
            if (message.SenderConnection.Status != NetConnectionStatus.RespondedAwaitingApproval)
            {
                // This can happen if the approval message comes in after the state changes to disconnected.
                // In that case just ignore it.
                return;
            }
            message.SenderConnection.Approve();
        }

        private async void HandleHandshake(NetConnection connection)
        {
            string requestedUsername;
            try
            {
                var userNamePacket = await AwaitData(connection);
                requestedUsername = userNamePacket.ReadString();
            }
            catch (ClientDisconnectedException)
            {
                return;
            }

            if (!UsernameHelpers.IsNameValid(requestedUsername))
            {
                connection.Disconnect("Username is invalid (contains illegal characters/too long).");
                return;
            }

            var endPoint = connection.RemoteEndPoint;
            var name = requestedUsername;
            var origName = name;
            var iterations = 1;

            while (_assignedSessions.Values.Any(u => u.Username == name))
            {
                // This is shit but I don't care.
                name = $"{origName}_{++iterations}";
            }

            var session = new NetSessionId(name);

            if (OnConnecting(endPoint, session))
            {
                _assignedSessions.Add(connection, session);
                var msg = connection.Peer.CreateMessage();
                msg.Write(name);
                connection.Peer.SendMessage(msg, connection, NetDeliveryMethod.ReliableOrdered);
            }
            else
            {
                connection.Disconnect("Sorry, denied. Why? Couldn't tell you, I didn't implement a deny reason.");
                return;
            }

            NetIncomingMessage okMsg;
            try
            {
                okMsg = await AwaitData(connection);
            }
            catch (ClientDisconnectedException)
            {
                return;
            }

            if (okMsg.ReadString() != "ok")
            {
                connection.Disconnect("You should say ok.");
                return;
            }

            // Handshake complete!
            HandleInitialHandshakeComplete(connection);
        }

        private void HandleInitialHandshakeComplete(NetConnection sender)
        {
            var session = _assignedSessions[sender];

            var channel = new NetChannel(this, sender, session);
            _channels.Add(sender, channel);

            _strings.SendFullTable(channel);

            Logger.InfoS("net", $"{channel.RemoteEndPoint}: Connected");

            OnConnected(channel);
        }

        private void HandleDisconnect(NetConnection connection, string reason)
        {
            var channel = _channels[connection];

            Logger.InfoS("net", $"{channel.RemoteEndPoint}: Disconnected ({reason})");
            _assignedSessions.Remove(connection);

            OnDisconnected(channel);
            _channels.Remove(connection);

            if (IsClient)
                _strings.Reset();
        }

        /// <inheritdoc />
        public void DisconnectChannel(INetChannel channel, string reason)
        {
            channel.Disconnect(reason);
        }

        private bool DispatchNetMessage(NetIncomingMessage msg)
        {
            var peer = msg.SenderConnection.Peer;
            if (peer.Status == NetPeerStatus.ShutdownRequested)
                return true;

            if (peer.Status == NetPeerStatus.NotRunning)
                return true;

            if (!IsConnected)
                return true;

            if (_awaitingData.TryGetValue(msg.SenderConnection, out var info))
            {
                var (cancel, tcs) = info;
                _awaitingData.Remove(msg.SenderConnection);
                cancel.Dispose();
                tcs.TrySetResult(msg);
                return false;
            }

            if (msg.LengthBytes < 1)
            {
                Logger.WarningS("net", $"{msg.SenderConnection.RemoteEndPoint}: Received empty packet.");
                return true;
            }

            var id = msg.ReadByte();

            if (!_strings.TryGetString(id, out string name))
            {
                Logger.WarningS("net", $"{msg.SenderConnection.RemoteEndPoint}:  No string in table with ID {id}.");
                return true;
            }

            if (!_messages.TryGetValue(name, out Type packetType))
            {
                Logger.WarningS("net", $"{msg.SenderConnection.RemoteEndPoint}: No message with Name {name}.");
                return true;
            }

            var channel = GetChannel(msg.SenderConnection);
            var instance = (NetMessage) Activator.CreateInstance(packetType, channel);
            instance.MsgChannel = channel;

            try
            {
                instance.ReadFromBuffer(msg);
            }
            catch (Exception e) // yes, we want to catch ALL exeptions for security
            {
                Logger.WarningS("net",
                    $"{msg.SenderConnection.RemoteEndPoint}: Failed to deserialize {packetType.Name} packet: {e.Message}");
            }

            if (!_callbacks.TryGetValue(packetType, out ProcessMessage callback))
            {
                Logger.WarningS("net",
                    $"{msg.SenderConnection.RemoteEndPoint}: Received packet {id}:{name}, but callback was not registered.");
                return true;
            }

            callback?.Invoke(instance);
            return true;
        }

        #region NetMessages

        /// <inheritdoc />
        public void RegisterNetMessage<T>(string name, ProcessMessage<T> rxCallback = null)
            where T : NetMessage
        {
            _strings.AddString(name);

            _messages.Add(name, typeof(T));

            if (rxCallback != null)
                _callbacks.Add(typeof(T), msg => rxCallback((T) msg));
        }

        /// <inheritdoc />
        public T CreateNetMessage<T>()
            where T : NetMessage
        {
            return (T) Activator.CreateInstance(typeof(T), (INetChannel) null);
        }

        private NetOutgoingMessage BuildMessage(NetMessage message, NetPeer peer)
        {
            var packet = peer.CreateMessage(4);

            if (!_strings.TryFindStringId(message.MsgName, out int msgId))
                throw new NetManagerException(
                    $"[NET] No string in table with name {message.MsgName}. Was it registered?");

            packet.Write((byte) msgId);
            message.WriteToBuffer(packet);
            return packet;
        }

        /// <inheritdoc />
        public void ServerSendToAll(NetMessage message)
        {
            DebugTools.Assert(IsServer);

            if (!IsConnected)
                return;

            foreach (var peer in _netPeers)
            {
                var packet = BuildMessage(message, peer);
                var method = GetMethod(message.MsgGroup);
                if (peer.ConnectionsCount == 0)
                {
                    continue;
                }

                peer.SendMessage(packet, peer.Connections, method, 0);
            }
        }

        /// <inheritdoc />
        public void ServerSendMessage(NetMessage message, INetChannel recipient)
        {
            DebugTools.Assert(IsServer);
            if (!(recipient is NetChannel channel))
                throw new ArgumentException($"Not of type {typeof(NetChannel).FullName}", nameof(recipient));

            var peer = channel.Connection.Peer;
            var packet = BuildMessage(message, peer);
            peer.SendMessage(packet, channel.Connection, NetDeliveryMethod.ReliableOrdered);
        }

        /// <inheritdoc />
        public void ServerSendToMany(NetMessage message, List<INetChannel> recipients)
        {
            DebugTools.Assert(IsServer);
            if (!IsConnected)
                return;

            foreach (var channel in recipients)
            {
                ServerSendMessage(message, channel);
            }
        }

        /// <inheritdoc />
        public void ClientSendMessage(NetMessage message)
        {
            DebugTools.Assert(IsClient);

            // not connected to a server, so a message cannot be sent to it.
            if (!IsConnected)
                return;

            DebugTools.Assert(_netPeers.Count == 1);
            DebugTools.Assert(_netPeers[0].ConnectionsCount == 1);

            var peer = _netPeers[0];
            var packet = BuildMessage(message, peer);
            var method = GetMethod(message.MsgGroup);
            peer.SendMessage(packet, peer.Connections[0], method);
        }

        #endregion NetMessages

        #region Events

        protected virtual bool OnConnecting(IPEndPoint ip, NetSessionId sessionId)
        {
            var args = new NetConnectingArgs(sessionId, ip);
            Connecting?.Invoke(this, args);
            return !args.Deny;
        }

        protected virtual void OnConnectFailed(string reason)
        {
            var args = new NetConnectFailArgs(reason);
            ConnectFailed?.Invoke(this, args);
        }

        protected virtual void OnConnected(INetChannel channel)
        {
            Connected?.Invoke(this, new NetChannelArgs(channel));
        }

        protected virtual void OnDisconnected(INetChannel channel)
        {
            Disconnect?.Invoke(this, new NetChannelArgs(channel));
        }

        /// <inheritdoc />
        public event EventHandler<NetConnectingArgs> Connecting;

        /// <inheritdoc />
        public event EventHandler<NetConnectFailArgs> ConnectFailed;

        /// <inheritdoc />
        public event EventHandler<NetChannelArgs> Connected;

        /// <inheritdoc />
        public event EventHandler<NetChannelArgs> Disconnect;

        #endregion Events

        private static NetDeliveryMethod GetMethod(MsgGroups group)
        {
            switch (group)
            {
                case MsgGroups.Entity:
                    return NetDeliveryMethod.Unreliable;
                case MsgGroups.Core:
                case MsgGroups.String:
                case MsgGroups.Command:
                case MsgGroups.EntityEvent:
                    return NetDeliveryMethod.ReliableUnordered;
                default:
                    throw new ArgumentOutOfRangeException(nameof(group), group, null);
            }
        }

        private enum ClientConnectionState
        {
            /// <summary>
            ///     We are not connected and not trying to get connected either. Quite lonely huh.
            /// </summary>
            NotConnecting,

            /// <summary>
            ///     Resolving the DNS query for the address of the server.
            /// </summary>
            ResolvingHost,

            /// <summary>
            ///     Attempting to establish a connection to the server.
            /// </summary>
            EstablishingConnection,

            /// <summary>
            ///     Connection established, going through regular handshake business.
            /// </summary>
            Handshake,

            /// <summary>
            ///     Connection is solid and handshake is done go wild.
            /// </summary>
            Connected
        }

        [Serializable]
        public class ClientDisconnectedException : Exception
        {
            public ClientDisconnectedException()
            {
            }

            public ClientDisconnectedException(string message) : base(message)
            {
            }

            public ClientDisconnectedException(string message, Exception inner) : base(message, inner)
            {
            }

            protected ClientDisconnectedException(
                SerializationInfo info,
                StreamingContext context) : base(info, context)
            {
            }
        }
    }

    /// <summary>
    ///     Generic exception thrown by the NetManager class.
    /// </summary>
    public class NetManagerException : Exception
    {
        public NetManagerException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    ///     Traffic statistics for a NetChannel.
    /// </summary>
    public struct NetworkStats
    {
        /// <summary>
        ///     Total sent bytes.
        /// </summary>
        public readonly int SentBytes;

        /// <summary>
        ///     Total received bytes.
        /// </summary>
        public readonly int ReceivedBytes;

        /// <summary>
        ///     Total sent packets.
        /// </summary>
        public readonly int SentPackets;

        /// <summary>
        ///     Total received packets.
        /// </summary>
        public readonly int ReceivedPackets;

        public NetworkStats(int sentBytes, int receivedBytes, int sentPackets, int receivedPackets)
        {
            SentBytes = sentBytes;
            ReceivedBytes = receivedBytes;
            SentPackets = sentPackets;
            ReceivedPackets = receivedPackets;
        }

        /// <summary>
        ///     Creates an instance of this object.
        /// </summary>
        public NetworkStats(NetPeerStatistics statistics)
        {
            SentBytes = statistics.SentBytes;
            ReceivedBytes = statistics.ReceivedBytes;
            SentPackets = statistics.SentPackets;
            ReceivedPackets = statistics.ReceivedPackets;
        }
    }
}
