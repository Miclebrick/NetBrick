﻿using Lidgren.Network;
using System.Net;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using NetBrick.Core.Server.Handlers;

namespace NetBrick.Core.Server
{
    public abstract class BrickServer
    {
        private NetServer _server;

        private List<PacketHandler> _handlers;

        private List<PacketHandler> _serverHandlers;

        public Dictionary<IPEndPoint, BrickPeer> Peers { get; set; }

        public Dictionary<IPEndPoint, BrickPeer> Clients
        {
            get
            {
                return (Dictionary<IPEndPoint, BrickPeer>)Peers.Where(p => !p.Value.PeerHandler.IsServer());
            }
        }

        public Dictionary<IPEndPoint, BrickPeer> Servers
        {
            get
            {
                return (Dictionary<IPEndPoint, BrickPeer>)Peers.Where(p => p.Value.PeerHandler.IsServer());
            }
        }

        protected BrickServer(string appIdentifier, int port, int maxConnections = 10, string address = "127.0.0.1")
        {
            var config = new NetPeerConfiguration(appIdentifier);

            config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
            config.EnableMessageType(NetIncomingMessageType.Data);
            config.EnableMessageType(NetIncomingMessageType.StatusChanged);

            config.Port = port;
            config.LocalAddress = IPAddress.Parse(address);
            config.MaximumConnections = maxConnections;

            _server = new NetServer(config);

            new Thread(() => Listen()).Start();

            _handlers = new List<PacketHandler>();
            _serverHandlers = new List<PacketHandler>();

            _server.Start();
        }

        private void Listen()
        {
            while (!Environment.HasShutdownStarted)
            {
                var message = _server.ReadMessage();

                if (message == null) continue;

                switch (message.MessageType)
                {
                    case NetIncomingMessageType.Data:
                        {
                            BrickPeer peer;
                            Peers.TryGetValue(message.SenderEndPoint, out peer);

                            if (peer == null) throw new Exception("Nonexistant peer sent message!");

                            var packet = new Packet(message);
                            var handlers = from h in (peer.PeerHandler.IsServer() ? _serverHandlers : _handlers) where h.Code == packet.PacketCode && h.Type == packet.PacketType select h;

                            foreach (var handler in handlers)
                            {
                                handler.Handle(packet, peer);
                            }
                        }
                        break;
                    case NetIncomingMessageType.ConnectionApproval:
                        message.SenderConnection.Approve();
                        break;
                    case NetIncomingMessageType.StatusChanged:
                        var status = (NetConnectionStatus)message.ReadByte();
                        Log(LogLevel.Info, "Status Changed for {0}. New Status: {1}", message.SenderEndPoint, status);
                        switch (status)
                        {
                            case NetConnectionStatus.Connected:
                                {
                                    var peer = new BrickPeer();
                                    var handler = CreateHandler();
                                    peer.Connection = message.SenderConnection;
                                    peer.PeerHandler = handler;
                                    handler.Peer = peer;

                                    Peers.Add(peer.Connection.RemoteEndPoint, peer);
                                    handler.OnConnect(message.SenderEndPoint);
                                }
                                break;
                            case NetConnectionStatus.Disconnected:
                                {
                                    BrickPeer peer;
                                    Peers.TryGetValue(message.SenderEndPoint, out peer);

                                    if (peer == null) throw new Exception("Nonexistant peer disconnected!");

                                    peer.PeerHandler.OnDisconnect(message.ReadString());
                                }
                                break;
                        }
                        break;
                    case NetIncomingMessageType.VerboseDebugMessage:
                    case NetIncomingMessageType.DebugMessage:
                        Log(LogLevel.Info, message.ReadString());
                        break;
                    case NetIncomingMessageType.ErrorMessage:
                        Log(LogLevel.Error, message.ReadString());
                        break;
                    case NetIncomingMessageType.WarningMessage:
                        Log(LogLevel.Warn, message.ReadString());
                        break;
                    default:
                        Log(LogLevel.Warn, "Unhandled lidgren message \"{0}\" received.", message.MessageType);
                        break;
                }
            }
        }

        public void Send(Packet packet, BrickPeer recipient, NetDeliveryMethod method = NetDeliveryMethod.ReliableOrdered, int sequenceChannel = 0)
        {
            var message = _server.CreateMessage();
            message.Write(packet.ToMessage());
            _server.SendMessage(message, recipient.Connection, method, sequenceChannel);
        }

        public void Send(Packet packet, IEnumerable<BrickPeer> recipients, NetDeliveryMethod method = NetDeliveryMethod.ReliableOrdered, int sequenceChannel = 0)
        {
            var message = _server.CreateMessage();
            message.Write(packet.ToMessage());

            var connections = new List<NetConnection>();

            foreach (var peer in recipients)
            {
                connections.Add(peer.Connection);
            }

            _server.SendMessage(message, connections, method, sequenceChannel);
        }

        public void SendToAll(Packet packet, NetDeliveryMethod method = NetDeliveryMethod.ReliableOrdered)
        {
            var message = _server.CreateMessage();
            message.Write(packet.ToMessage());
            _server.SendToAll(message, method);
        }

        public void AddHandler(PacketHandler handler)
        {
            _handlers.Add(handler);
        }

        public void RemoveHandler(PacketHandler handler)
        {
            _handlers.Remove(handler);
        }

        public void AddServerHandler(PacketHandler handler)
        {
            _serverHandlers.Add(handler);
        }

        public void RemoveServerHandler(PacketHandler handler)
        {
            _serverHandlers.Remove(handler);
        }

        public void ConnectToServer(string address, int port)
        {
            _server.Connect(address, port);
        }

        public abstract BasePeerHandler CreateHandler();
        public abstract void Log(LogLevel warn, string v, params object[] args);
    }
}
