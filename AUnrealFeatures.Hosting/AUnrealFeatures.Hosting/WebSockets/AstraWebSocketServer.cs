using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http;
using AUnrealFeatures.Hosting.WebSockets;
using AUnrealFeatures.Hosting.WebSockets.Attributes;
using AUnrealFeatures.Hosting.WebSockets.Interfaces;

using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AUnrealFeatures.Hosting.Http.Actions;
using System.Security.Cryptography;
using System.Buffers;
using Serilog.Core;

namespace AUnrealFeatures.Hosting.WebSockets
{
    public abstract class AstraWebSocketServer : IWebSocketServer, IStartStopObject
    {
        private bool _initialized;
        private HttpListener _httpListener;
        private readonly List<AstraWebSocketRoute> _routes;
        private Task _listenTask = null!;

        private readonly ILogger _logger;
        public ILogger Logger => _logger;

        public AstraWebSocketServer(string hostname, ushort port)
        {
            _logger = ModuleInitialization.InitializeLogger(GetType().Name);

            Hostname = hostname;
            Port = port;
            _routes = new List<AstraWebSocketRoute>();

            _httpListener = new HttpListener();
            var bindHost = Environment.GetEnvironmentVariable("ASTRA_HTTP_BIND");
            if (string.IsNullOrWhiteSpace(bindHost)) bindHost = "127.0.0.1";
            _httpListener.Prefixes.Add($"http://{bindHost}:{Port}/");

            Start();
        }

        public void Start()
        {
            if (!_initialized)
            {
                var routeMethods = GetType().GetAllMethodsWithAttribute<WebSocketRouteAttribute>();
                foreach (var routeMethodInfo in routeMethods)
                {
                    var socketEndpointAttr = routeMethodInfo.GetCustomAttribute<WebSocketRouteAttribute>()
                        ?? throw new InvalidOperationException();

                    _routes.Add(new AstraWebSocketRoute
                    {
                        EndpointName = routeMethodInfo.Name,
                        RouteUri = socketEndpointAttr.Uri,
                        Processors = routeMethodInfo.GetCustomAttributes<WebSocketRouteProcessorAttribute>().ToList(),
                        MethodInfo = routeMethodInfo
                    });
                }

                _initialized = true;
            }

            if (!_httpListener.IsListening)
            {
                _httpListener.Start();
                _listenTask = Task.Run(ListenImpl);
                _logger.Information("Started socket server on '{Host}:{Port}'", Hostname, Port);
            }
            else _logger.Warning("Cannot start socket server when it is already listening!");
        }

        private async Task ListenImpl()
        {
            while (_httpListener.IsListening)
            {
                try
                {
                    var rawHttpContext = await _httpListener.GetContextAsync();

                    if (rawHttpContext.Request.IsWebSocketRequest)
                    {
                        var route = FindMatchingRoute(rawHttpContext.Request.Url!.AbsolutePath);
                        if (route == null)
                        {
                            _logger.Warning("Rejected WebSocket connection for unknown route {Path}", rawHttpContext.Request.Url.AbsolutePath);
                            rawHttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                            rawHttpContext.Response.Close();
                            continue;
                        }

                        var rawWebSocketContext = await rawHttpContext.AcceptWebSocketAsync(null);
                        if (rawWebSocketContext == null)
                        {
                            _logger.Error("The WebSocket context was not found.");
                            rawHttpContext.Response.StatusCode = (int)HttpStatusCode.FailedDependency;
                            rawHttpContext.Response.Close();
                            continue;
                        }

                        var result = route.MethodInfo.Invoke(this, Array.Empty<object>());
                        if (result == null || result is not IWebSocketStateMachine)
                        {
                            _logger.Error("The method '{MethodName}' did not return a '{TypeName}'.", route.EndpointName, nameof(IWebSocketStateMachine));
                            rawHttpContext.Response.StatusCode = (int)HttpStatusCode.Conflict;
                            rawHttpContext.Response.Close();
                            continue;
                        }

                        var webSocketClient = AstraWebSocketClient.New(rawHttpContext, rawWebSocketContext);
                        var rejected = false;
                        foreach (var processor in route.Processors)
                        {
                            if (await processor.Validate(webSocketClient))
                                continue;

                            _logger.Warning("WebSocket processor rejected route {Route}", route.EndpointName);
                            await rawWebSocketContext.WebSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Request rejected", CancellationToken.None);
                            rejected = true;
                            break;
                        }

                        if (rejected)
                            continue;

                        _ = Task.Run(() => SocketImpl(webSocketClient, (AstraWebSocketStateMachine)result));
                    }
                }
                catch (HttpListenerException) when (!_httpListener.IsListening)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error processing WebSocket request");
                }

                await Task.Delay(1);
            }
        }

        private AstraWebSocketRoute? FindMatchingRoute(string route)
        {
            return _routes.FirstOrDefault(e => e!.RouteUri == route, null);
        }

        private async Task SocketImpl(IWebSocketClient webSocketClient, AstraWebSocketStateMachine webSocketStateMachine)
        {
            try
            {
                const int MAX_BUFFER = 1024 * 8 * 4 * 2;
                webSocketStateMachine.InvokeOnOpen(webSocketClient);

                while (webSocketClient.IsConnected)
                {
                    byte[]? rawMessageBuffer = null;
                    try
                    {
                        rawMessageBuffer = ArrayPool<byte>.Shared.Rent(MAX_BUFFER);
                        int totalBytesReceived = 0;
                        bool endOfMessage = false;

                        while (!endOfMessage && webSocketClient.IsConnected)
                        {
                            var receiveResult = await webSocketClient.ReadMessageAsync(rawMessageBuffer.AsMemory(totalBytesReceived, MAX_BUFFER - totalBytesReceived));
                            totalBytesReceived += receiveResult.Count;
                            endOfMessage = receiveResult.EndOfMessage;

                            if (totalBytesReceived >= MAX_BUFFER)
                            {
                                _logger.Warning("Message exceeds maximum buffer size and may be truncated.");
                                break;
                            }
                        }

                        if (totalBytesReceived > 0)
                        {
                            byte[] message = rawMessageBuffer[..totalBytesReceived];
                            webSocketStateMachine.InvokeOnMessage(webSocketClient, message);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("Failed to invoke state / read buffer: {Message}", ex.Message);
                    }
                    finally
                    {
                        if (rawMessageBuffer != null) ArrayPool<byte>.Shared.Return(rawMessageBuffer);
                    }
                }

                webSocketStateMachine.InvokeOnClose(webSocketClient);
            }
            catch (Exception ex)
            {
                Log.Error("Error while handling socket: {Message}", ex.Message);
            }
        }

        public void Stop()
        {
            if (!_initialized)
                return;

            if (_httpListener.IsListening)
            {
                _httpListener.Stop();
                _listenTask = null!;
                _logger.Information("Stopped socket server");
            }
            else _logger.Warning("Cannot stop socket server when it is not listening!");
        }


        public string Hostname { get; } = "localhost";
        public ushort Port { get; } = 80;
    }
}
