using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VTX.Format;

namespace AUnrealFeatures.Virtex;

/// <summary>
/// LiveDataCommunications router — the backend hub of the per-game live pipeline.
///
///   injected native reader (per source game)  ──raw VTX stream──▶  INGEST (:9200)
///                                                                        │
///                                          route by gameId → stadium(s)  │  (VtxRouteTable)
///                                                                        ▼
///   Unity stadium client  ◀──raw VTX stream──  DISTRIBUTE (:8997)  ◀─ fan-out
///
/// Both legs speak the raw, self-delimiting VTX byte stream (magic + schema
/// header + length-prefixed frame messages) — no JSON/base64 re-wrapping, so the
/// bytes a producer writes are the bytes a consumer reads. The router never
/// decodes frame bodies (<see cref="VtxStreamRelay"/> only parses the header for
/// routing); it forwards raw message slices, and replays the cached stream
/// preamble to any consumer that joins mid-match.
///
/// A consumer subscribes by sending one frame: [4-byte big-endian length][UTF8
/// stadium name]. One active source stream per stadium is assumed (one match
/// being cast at a time).
/// </summary>
public sealed class VtxLiveRouter : IDisposable
{
    public const int DefaultIngestPort = 9200;
    public const int DefaultDistributePort = 8997; // matches discovery LiveDataDistributorPort

    private readonly int _ingestPort;
    private readonly int _distributePort;
    private readonly VtxGameRegistry _registry;
    private readonly VtxRouteTable _routes;
    private readonly Action<string> _log;

    private TcpListener _ingestListener;
    private TcpListener _distributeListener;
    private readonly CancellationTokenSource _cts = new();

    // stadium -> connected consumers
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Consumer>> _subscribers = new();
    // stadium -> the current source's cached preamble (for late joiners)
    private readonly ConcurrentDictionary<string, byte[]> _preambleByStadium = new();

    public VtxLiveRouter(VtxGameRegistry registry = null, VtxRouteTable routes = null,
        int ingestPort = DefaultIngestPort, int distributePort = DefaultDistributePort, Action<string> log = null)
    {
        _registry = registry ?? VtxGameRegistry.CreateDefault();
        _routes = routes ?? VtxRouteTable.IdentityFrom(_registry);
        _ingestPort = ingestPort;
        _distributePort = distributePort;
        _log = log ?? (_ => { });
    }

    public void Start()
    {
        _ingestListener = new TcpListener(IPAddress.Any, _ingestPort);
        _ingestListener.Start();
        _distributeListener = new TcpListener(IPAddress.Any, _distributePort);
        _distributeListener.Start();
        _log($"VtxLiveRouter started (ingest :{_ingestPort}, distribute :{_distributePort})");
        _ = AcceptLoop(_ingestListener, HandleProducer);
        _ = AcceptLoop(_distributeListener, HandleConsumer);
    }

    private async Task AcceptLoop(TcpListener listener, Func<TcpClient, Task> handler)
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(); }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { return; }
            client.NoDelay = true;
            _ = handler(client);
        }
    }

    // ── Producer (injected reader) → ingest ──────────────────────────────────
    private async Task HandleProducer(TcpClient client)
    {
        var remote = client.Client.RemoteEndPoint;
        _log($"Producer connected: {remote}");
        var targets = Array.Empty<string>();
        var relay = new VtxStreamRelay();

        relay.HeaderReady += (schema, gameId, stadium, preamble) =>
        {
            targets = ResolveTargets(gameId, stadium);
            _log($"Producer {remote} streaming game '{gameId}' -> [{string.Join(", ", targets)}]");
            foreach (var s in targets)
            {
                _preambleByStadium[s] = preamble;
                Broadcast(s, preamble); // bring current subscribers onto the new source
            }
        };
        relay.MessageReady += (type, raw) =>
        {
            for (int i = 0; i < targets.Length; i++)
                Broadcast(targets[i], raw);
        };

        try
        {
            var buf = new byte[16 * 1024];
            var stream = client.GetStream();
            while (!_cts.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buf, 0, buf.Length, _cts.Token);
                if (n == 0) break;
                relay.Feed(buf, 0, n);
            }
        }
        catch (Exception ex) { _log($"Producer {remote} error: {ex.Message}"); }
        finally
        {
            foreach (var s in targets)
                _preambleByStadium.TryRemove(s, out _);
            try { client.Close(); } catch { }
            _log($"Producer disconnected: {remote}");
        }
    }

    private string[] ResolveTargets(string gameId, string headerStadium)
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (var s in _routes.ResolveTargets(gameId)) list.Add(s);
        if (list.Count == 0 && !string.IsNullOrEmpty(headerStadium)) list.Add(headerStadium);
        if (list.Count == 0)
        {
            var g = _registry.Get(gameId);
            if (g != null && !string.IsNullOrEmpty(g.StadiumMap)) list.Add(g.StadiumMap);
        }
        return list.ToArray();
    }

    // ── Consumer (Unity stadium) → distribute ────────────────────────────────
    private async Task HandleConsumer(TcpClient client)
    {
        var remote = client.Client.RemoteEndPoint;
        var consumer = new Consumer(client);
        string stadium = null;
        try
        {
            var stream = client.GetStream();
            stadium = await ReadSubscribeAsync(stream, _cts.Token);
            if (string.IsNullOrEmpty(stadium)) { client.Close(); return; }

            var set = _subscribers.GetOrAdd(stadium, _ => new ConcurrentDictionary<Guid, Consumer>());
            set[consumer.Id] = consumer;
            _log($"Consumer {remote} subscribed to '{stadium}' ({set.Count} total)");

            if (_preambleByStadium.TryGetValue(stadium, out var preamble))
                await consumer.SendAsync(preamble); // catch a late joiner up to the current source

            // Hold the connection open; detect disconnect by reading (consumers don't send more).
            var scratch = new byte[256];
            while (!_cts.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(scratch, 0, scratch.Length, _cts.Token);
                if (n == 0) break;
            }
        }
        catch (Exception ex) { _log($"Consumer {remote} error: {ex.Message}"); }
        finally
        {
            if (stadium != null && _subscribers.TryGetValue(stadium, out var set))
                set.TryRemove(consumer.Id, out _);
            consumer.Dispose();
            _log($"Consumer disconnected: {remote}");
        }
    }

    private static async Task<string> ReadSubscribeAsync(NetworkStream stream, CancellationToken ct)
    {
        var lenBuf = new byte[4];
        if (!await ReadExactAsync(stream, lenBuf, 4, ct)) return null;
        int len = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
        if (len <= 0 || len > 4096) return null;
        var strBuf = new byte[len];
        if (!await ReadExactAsync(stream, strBuf, len, ct)) return null;
        return Encoding.UTF8.GetString(strBuf).Trim();
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer, offset, count - offset, ct);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    private void Broadcast(string stadium, byte[] bytes)
    {
        if (!_subscribers.TryGetValue(stadium, out var set)) return;
        foreach (var kv in set)
        {
            var consumer = kv.Value;
            _ = consumer.SendAsync(bytes).ContinueWith(t =>
            {
                if (t.IsFaulted) { set.TryRemove(kv.Key, out _); consumer.Dispose(); }
            }, TaskScheduler.Default);
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _ingestListener?.Stop(); } catch { }
        try { _distributeListener?.Stop(); } catch { }
        foreach (var set in _subscribers.Values)
            foreach (var c in set.Values)
                c.Dispose();
        _subscribers.Clear();
    }

    private sealed class Consumer : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public Consumer(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        public async Task SendAsync(byte[] bytes)
        {
            await _writeLock.WaitAsync();
            try { await _stream.WriteAsync(bytes, 0, bytes.Length); }
            finally { _writeLock.Release(); }
        }

        public void Dispose()
        {
            try { _stream.Dispose(); } catch { }
            try { _client.Close(); } catch { }
        }
    }
}
