using Serilog;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AUnrealFeatures.Virtex;

/// <summary>
/// Raw TCP transport for the native <c>FVirtexConnection</c> wire protocol
/// (VlcMedia/OnlineSubsystemVirtex plugin), reversed from
/// <c>FVirtexConnection::RunEndian</c> / <c>::Send</c> / <c>::Recv</c>.
///
/// Wire format:
///   [4 bytes big-endian length][that many bytes of ASCII/UTF8 JSON text]
/// A single length-prefixed frame may contain multiple '\n'-joined JSON
/// messages. Every message, both directions, is the envelope:
///   {"Type": &lt;int&gt;, "Payload": {...}}
/// Type 2 is a heartbeat (Payload is null/omitted) sent automatically by the
/// engine after ~50s of silence; this class mirrors that and answers
/// incoming heartbeats without surfacing them through <see cref="OnMessage"/>.
///
/// Both <c>CommunicationsServiceAddress/Port</c> and
/// <c>LiveDataDistributorAddress/Port</c> (which <c>DataStreamPort</c>
/// shares) speak this exact same protocol — this class is instantiated once
/// per port from <see cref="VirtexServer"/>.
///
/// The full catalog of <c>Type</c> values beyond the heartbeat wasn't
/// reversed (that requires tracing UVTXCommunications'/
/// UVirtexContentConnectorSubsystem's message handlers), so anything else
/// received is just forwarded via <see cref="OnMessage"/> for the caller to
/// interpret and reply to.
/// </summary>
public sealed class VirtexSocketServer
{
    public const int HeartbeatType = 2;

    private readonly string _name;
    private readonly ushort _port;
    private readonly ILogger _logger;
    private TcpListener? _listener;
    private readonly ConcurrentDictionary<Guid, VirtexSocketConnection> _clients = new();

    public IReadOnlyCollection<VirtexSocketConnection> Clients => (IReadOnlyCollection<VirtexSocketConnection>)_clients.Values;

    /// <summary>Fired for every non-heartbeat message received from any client.</summary>
    public event Action<VirtexSocketConnection, int, JsonElement>? OnMessage;
    public event Action<VirtexSocketConnection>? OnClientConnected;
    public event Action<VirtexSocketConnection>? OnClientDisconnected;

    public VirtexSocketServer(string name, ushort port, ILogger logger)
    {
        _name = name;
        _port = port;
        _logger = logger;
    }

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _logger.Information("Started Virtex socket service '{Name}' on port {Port}", _name, _port);
        _ = AcceptLoop(_listener);
    }

    public void Stop()
    {
        _listener?.Stop();
        foreach (var client in _clients.Values) client.Close();
        _clients.Clear();
    }

    private async Task AcceptLoop(TcpListener listener)
    {
        while (true)
        {
            TcpClient tcpClient;
            try { tcpClient = await listener.AcceptTcpClientAsync(); }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { return; }

            var connection = new VirtexSocketConnection(tcpClient, _logger, _name);
            _clients[connection.Id] = connection;
            OnClientConnected?.Invoke(connection);
            _logger.Information("[{Name}] Client connected: {Remote}", _name, connection.RemoteEndPoint);

            _ = RunConnection(connection);
        }
    }

    private async Task RunConnection(VirtexSocketConnection connection)
    {
        try
        {
            await connection.RunAsync((conn, type, payload) =>
            {
                if (type == HeartbeatType)
                {
                    // Answer in kind so the client's own 50s-silence heartbeat
                    // timer never has a reason to fire a reconnect/backoff path.
                    _ = conn.SendAsync(HeartbeatType, default);
                    return;
                }
                OnMessage?.Invoke(conn, type, payload);
            });
        }
        catch (Exception ex)
        {
            _logger.Warning("[{Name}] Connection {Remote} errored: {Message}", _name, connection.RemoteEndPoint, ex.Message);
        }
        finally
        {
            _clients.TryRemove(connection.Id, out _);
            OnClientDisconnected?.Invoke(connection);
            connection.Close();
            _logger.Information("[{Name}] Client disconnected: {Remote}", _name, connection.RemoteEndPoint);
        }
    }

    /// <summary>Broadcasts a message (Type + Payload) to every connected client.</summary>
    public async Task BroadcastAsync(int type, JsonElement payload)
    {
        foreach (var client in _clients.Values)
        {
            try { await client.SendAsync(type, payload); }
            catch { /* best-effort broadcast; a dead client will be reaped by RunConnection */ }
        }
    }
}

public sealed class VirtexSocketConnection
{
    public Guid Id { get; } = Guid.NewGuid();
    public EndPoint? RemoteEndPoint { get; }

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly ILogger _logger;
    private readonly string _serverName;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    internal VirtexSocketConnection(TcpClient client, ILogger logger, string serverName)
    {
        _client = client;
        _stream = client.GetStream();
        _logger = logger;
        _serverName = serverName;
        RemoteEndPoint = client.Client.RemoteEndPoint;
    }

    internal async Task RunAsync(Action<VirtexSocketConnection, int, JsonElement> onMessage)
    {
        var lengthPrefix = new byte[4];

        while (_client.Connected)
        {
            if (!await ReadExactAsync(lengthPrefix, 4)) return;
            var frameLength = BinaryPrimitives.ReadInt32BigEndian(lengthPrefix);

            // Mirrors the engine's own sanity check (RunEndian bails if the
            // declared length exceeds 0x8000000 bytes) against malformed/rogue frames.
            if (frameLength < 0 || frameLength > 0x8000000) return;
            if (frameLength == 0) continue;

            var frame = new byte[frameLength];
            if (!await ReadExactAsync(frame, frameLength)) return;

            var text = Encoding.ASCII.GetString(frame);
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); }
                catch (JsonException) { continue; }

                using (doc)
                {
                    if (!doc.RootElement.TryGetProperty("Type", out var typeElement) || !typeElement.TryGetInt32(out var type))
                        continue;

                    var payload = doc.RootElement.TryGetProperty("Payload", out var payloadElement)
                        ? payloadElement.Clone()
                        : default;

                    onMessage(this, type, payload);
                }
            }
        }
    }

    private async Task<bool> ReadExactAsync(byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            int read;
            try { read = await _stream.ReadAsync(buffer.AsMemory(offset, count - offset)); }
            catch (IOException) { return false; }
            catch (ObjectDisposedException) { return false; }

            if (read == 0) return false; // remote closed
            offset += read;
        }
        return true;
    }

    public async Task SendAsync(int type, JsonElement payload)
    {
        object envelope = payload.ValueKind == JsonValueKind.Undefined
            ? new { Type = type }
            : new { Type = type, Payload = payload };

        var json = JsonSerializer.Serialize(envelope);
        var bytes = Encoding.ASCII.GetBytes(json);

        var lengthPrefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, bytes.Length);

        await _writeLock.WaitAsync();
        try
        {
            await _stream.WriteAsync(lengthPrefix);
            await _stream.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            _logger.Warning("[{Name}] Failed sending to {Remote}: {Message}", _serverName, RemoteEndPoint, ex.Message);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Close()
    {
        try { _stream.Close(); } catch { }
        try { _client.Close(); } catch { }
    }
}
