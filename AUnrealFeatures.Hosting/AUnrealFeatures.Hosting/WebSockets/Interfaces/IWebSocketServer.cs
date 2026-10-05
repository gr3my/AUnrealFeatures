using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AUnrealFeatures.Hosting.WebSockets.Interfaces
{
    public interface IWebSocketServer : IStartStopObject
    {
        new void Start();
        new void Stop();

        string Hostname { get; }
        ushort Port { get; }
    }
}
