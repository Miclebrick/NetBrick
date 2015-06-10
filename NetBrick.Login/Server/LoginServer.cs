﻿using NetBrick.Core.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetBrick.Login.Server
{
    public abstract class LoginServer : BrickServer
    {
        public abstract string MasterAddress { get; }
        public abstract int MasterPort { get; }

        protected LoginServer(string appIdentifier, int port, int maxConnections = 10, string address = "127.0.0.1")
            : base(appIdentifier, port, maxConnections, address)
        {
            ConnectToServer(MasterAddress, MasterPort);
        }
    }
}