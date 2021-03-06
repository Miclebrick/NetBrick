﻿using System.Net;
using NetBrick.Core;
using NetBrick.Core.Server;

namespace ConsoleChat.Server
{
    internal class ChatPeerHandler : BasePeerHandler
    {
        public IPEndPoint EndPoint { get; set; }

        public override void OnConnect(IPEndPoint endPoint)
        {
            EndPoint = endPoint;
            ChatServer.Instance.Log(LogLevel.Info, $"User {EndPoint} connected.");
        }

        public override void OnDisconnect(string reason)
        {
            ChatServer.Instance.Log(LogLevel.Info, $"User {EndPoint} has disconnected.");
        }
    }
}