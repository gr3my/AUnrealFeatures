using AUnrealFeatures.Hosting.WebSockets;
using AUnrealFeatures.Hosting.WebSockets.Attributes;
using AUnrealFeatures.Hosting.WebSockets.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AUnrealFeatures.Hosting.Test
{
    public sealed class SocketServer : AstraWebSocketServer
    {
        private static readonly IWebSocketStateMachine IndexStateMachine = new AstraWebSocketStateMachine()
            .OnOpen(socket => { Log.Information("Socket connected '{ClientId}'", socket.ClientId); })
            .OnMessage(async (socket, message) => 
            {
                await socket.SendMessageAsync("hello!");
            })
            .OnClose(socket => { });

        public SocketServer() : base("localhost", 81)
        {
        }

        [WebSocketRoute] public IWebSocketStateMachine Index() => IndexStateMachine;
    }
}
