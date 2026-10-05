using AUnrealFeatures.Hosting.Configuration;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AUnrealFeatures.GameSocket;

// Represents one connected allocator agent. Messages are newline-delimited JSON.
public sealed class AgentConnection
{
    public TcpClient     Client { get; init; } = null!;
    public NetworkStream Stream { get; init; } = null!;
    public string   Box      { get; set; } = "?";
    public int      Capacity { get; set; }
    public int      Running  { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    private readonly object _writeLock = new();

    public int Free => Math.Max(0, Capacity - Running);

    public bool SendLine(string json)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            lock (_writeLock) { Stream.Write(bytes, 0, bytes.Length); Stream.Flush(); }
            return true;
        }
        catch { return false; }
    }
}

// TCP hub for allocator agents using newline-delimited JSON messages.
public sealed class AgentHub
{
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<Guid, AgentConnection> _agents = new();

    public AgentHub(int port)
    {
        var configuredAddress = EnvironmentSettings.Get("GAME_AGENT_BIND", "127.0.0.1");
        if (!IPAddress.TryParse(configuredAddress, out var bindAddress))
            throw new InvalidOperationException($"GAME_AGENT_BIND must be an IP address, got '{configuredAddress}'.");
        _listener = new TcpListener(bindAddress, port);
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoop);
        _ = Task.Run(ReapLoop);
        Console.WriteLine($"[GameSocket] agent hub listening on TCP {((IPEndPoint)_listener.LocalEndpoint).Port}");
    }

    private async Task AcceptLoop()
    {
        while (true)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch { break; }
            var conn = new AgentConnection { Client = client, Stream = client.GetStream() };
            var id = Guid.NewGuid();
            _agents[id] = conn;
            _ = Task.Run(() => ClientLoop(id, conn));
        }
    }

    private async Task ClientLoop(Guid id, AgentConnection conn)
    {
        var buf = new byte[4096];
        var acc = new StringBuilder();
        try
        {
            while (true)
            {
                int n = await conn.Stream.ReadAsync(buf, 0, buf.Length);
                if (n <= 0) break;
                acc.Append(Encoding.UTF8.GetString(buf, 0, n));
                var s = acc.ToString();
                int nl;
                while ((nl = s.IndexOf('\n')) >= 0)
                {
                    var line = s.Substring(0, nl).Trim();
                    s = s.Substring(nl + 1);
                    if (line.Length > 0) HandleAgentMessage(conn, line);
                }
                acc.Clear();
                acc.Append(s);
            }
        }
        catch { /* fallthrough to disconnect */ }

        _agents.TryRemove(id, out _);
        try { conn.Client.Close(); } catch { }
        Console.WriteLine($"[GameSocket] agent '{conn.Box}' disconnected");
    }

    private static string GetStr(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? v.ToString() : "";
    private static int    GetInt(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.TryGetInt32(out var i) ? i : 0;

    private void HandleAgentMessage(AgentConnection conn, string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var type = GetStr(root, "type");
            switch (type)
            {
                case "hello":
                    conn.Box      = GetStr(root, "box");
                    conn.Capacity = GetInt(root, "capacity");
                    conn.Running  = GetInt(root, "running");
                    conn.LastSeen = DateTime.UtcNow;
                    Console.WriteLine($"[GameSocket] agent '{conn.Box}' online (cap={conn.Capacity}, running={conn.Running})");
                    break;
                case "heartbeat":
                    conn.Running  = GetInt(root, "running");
                    conn.LastSeen = DateTime.UtcNow;
                    break;
                case "spunup":
                    Console.WriteLine($"[GameSocket] spunup id={GetStr(root, "id")} pid={GetStr(root, "pid")} status={GetStr(root, "status")} msg={GetStr(root, "msg")}");
                    break;
                case "exited":
                    conn.Running = GetInt(root, "running");
                    Console.WriteLine($"[GameSocket] instance exited pid={GetStr(root, "pid")} code={GetStr(root, "code")}");
                    break;
            }
        }
        catch { /* ignore malformed */ }
    }

    // Remove agents that have not sent a heartbeat within the configured stale window.
    private async Task ReapLoop()
    {
        while (true)
        {
            await Task.Delay(15000);
            var cutoff = DateTime.UtcNow.AddSeconds(-90);
            foreach (var kv in _agents)
            {
                if (kv.Value.LastSeen >= cutoff) continue;
                _agents.TryRemove(kv.Key, out _);
                try { kv.Value.Client.Close(); } catch { }
                Console.WriteLine($"[GameSocket] reaped stale agent '{kv.Value.Box}'");
            }
        }
    }

    public IReadOnlyList<AgentConnection> Agents => _agents.Values.ToList();

    // Select the requested agent when available; otherwise prefer the agent with the most free capacity.
    public AgentConnection? Pick(string? box)
    {
        var list = _agents.Values.ToList();
        if (!string.IsNullOrEmpty(box))
            return list.FirstOrDefault(a => a.Box == box && a.Free > 0);
        return list.Where(a => a.Free > 0).OrderByDescending(a => a.Free).FirstOrDefault();
    }
}
