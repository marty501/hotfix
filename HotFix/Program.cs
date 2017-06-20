﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using HotFix.Core;
using HotFix.Transport;
using HotFix.Utilities;

namespace HotFix
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var configuration = new Configuration
            {
                Host = "localhost",
                Port = 1234,
                Sender = "DAEV",
                Target = "TARGET",
                InboundSeqNum = 1,
                OutboundSeqNum = 1,
                HeartbeatInterval = 5,
                InboundBufferSize = 65536,
                OutboundBufferSize = 65536
            };

            var transport = new TcpTransport(configuration.Host, configuration.Port);
            var session = new Session(transport, configuration);

            session.Run();

            Console.WriteLine("Session has ended, press any key to exit");
            Console.ReadKey();
        }
    }

    
}