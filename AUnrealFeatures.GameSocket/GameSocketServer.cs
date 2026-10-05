using AUnrealFeatures.Hosting.Configuration;
using AUnrealFeatures.Hosting.Http;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AUnrealFeatures.GameSocket;

public interface IGameSocketServer { }

/// <summary>
/// HTTP control surface for dedicated-server allocation. Connected allocator agents use the
/// companion TCP hub and report capacity independently.
/// </summary>
public sealed class GameSocketServer : AstraHttpServer, IGameSocketServer
{
    private const string DefaultHostname = "localhost";
    private const ushort ControlPort = 9095;
    private const int AgentPort = 9100;

    private static readonly string DeploymentIdArgument = NormalizeArgumentName(
        EnvironmentSettings.Get("GAME_DEPLOYMENT_ID_ARG", "DashboardDeploymentId"));
    private static readonly string ControlKey = EnvironmentSettings.Get("GAME_SOCKET_CONTROL_KEY");

    public static GameSocketServer? Instance { get; private set; }

    // Names supplied by the operator UI are consumed when the launched server registers itself.
    public static readonly ConcurrentDictionary<string, string> PendingNames = new();

    private readonly AgentHub _hub = new(AgentPort);

    public GameSocketServer() : base(DefaultHostname, ControlPort)
    {
        _hub.Start();
        Instance = this;
        Console.WriteLine($"[GameSocket] control HTTP on {ControlPort}, agent TCP on {AgentPort}");
    }

    public SpinUpResult RequestSpinUp(string? box, string? map, string? region, string? args, string? name = null)
    {
        var agent = _hub.Pick(box);
        if (agent == null)
        {
            return new SpinUpResult
            {
                success = false,
                error = string.IsNullOrEmpty(box)
                    ? "no agent with free capacity"
                    : $"agent '{box}' unavailable or full"
            };
        }

        var requestId = Guid.NewGuid().ToString("N");
        var finalArgs = args ?? "";
        var deploymentPattern = $@"-{Regex.Escape(DeploymentIdArgument)}=(\S+)";
        var existing = Regex.Match(finalArgs, deploymentPattern, RegexOptions.IgnoreCase);
        var deploymentId = existing.Success
            ? existing.Groups[1].Value
            : Guid.NewGuid().ToString("N");

        if (!existing.Success)
            finalArgs = (finalArgs + $" -{DeploymentIdArgument}={deploymentId}").Trim();

        if (!string.IsNullOrWhiteSpace(name))
            PendingNames[deploymentId] = name.Trim();

        var payload = JsonSerializer.Serialize(new
        {
            type = "spinup",
            id = requestId,
            map = map ?? "",
            region = region ?? "",
            args = finalArgs
        });

        if (!agent.SendLine(payload))
        {
            PendingNames.TryRemove(deploymentId, out _);
            return new SpinUpResult
            {
                success = false,
                request_id = requestId,
                agent = agent.Box,
                error = "send to agent failed"
            };
        }

        Console.WriteLine($"[GameSocket] spinup {requestId} -> agent '{agent.Box}' (map='{map}', region='{region}')");
        return new SpinUpResult { success = true, request_id = requestId, agent = agent.Box };
    }

    public AgentInfo[] GetAgents() => _hub.Agents.Select(a => new AgentInfo
    {
        box = a.Box,
        capacity = a.Capacity,
        running = a.Running,
        free = a.Free,
        last_seen = a.LastSeen
    }).ToArray();

    [HttpGet("/agents")]
    public Task<IHttpActionResult> ListAgents(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult<IHttpActionResult>(IsAuthorized(request)
            ? Results.Ok(new AgentListResult { agents = GetAgents() })
            : Results.Unauthorized());

    [HttpPost("/spinup")]
    public Task<IHttpActionResult> SpinUp(IHttpRequest request, IHttpResponse response)
    {
        if (!IsAuthorized(request))
            return Task.FromResult<IHttpActionResult>(Results.Unauthorized());

        SpinUpBody? body;
        try
        {
            body = JsonSerializer.Deserialize<SpinUpBody>(request.Body);
        }
        catch (JsonException)
        {
            body = null;
        }

        return Task.FromResult<IHttpActionResult>(
            Results.Ok(RequestSpinUp(body?.box, body?.map, body?.region, body?.args, body?.name)));
    }


    private static bool IsAuthorized(IHttpRequest request)
    {
        if (IPAddress.IsLoopback(request.Remote))
            return true;
        if (string.IsNullOrWhiteSpace(ControlKey) || ControlKey.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            return false;

        var candidate = request.GetHeaderValue("X-GameSocket-Key");
        if (string.IsNullOrEmpty(candidate))
            return false;

        var left = Encoding.UTF8.GetBytes(candidate);
        var right = Encoding.UTF8.GetBytes(ControlKey);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string NormalizeArgumentName(string value)
    {
        var normalized = value.Trim().TrimStart('-');
        return string.IsNullOrWhiteSpace(normalized) ? "DashboardDeploymentId" : normalized;
    }
}

public sealed class SpinUpBody
{
    public string? box { get; set; }
    public string? map { get; set; }
    public string? region { get; set; }
    public string? args { get; set; }
    public string? name { get; set; }
}

public sealed class SpinUpResult
{
    public bool success { get; set; }
    public string request_id { get; set; } = "";
    public string agent { get; set; } = "";
    public string error { get; set; } = "";
}

public sealed class AgentInfo
{
    public string box { get; set; } = "";
    public int capacity { get; set; }
    public int running { get; set; }
    public int free { get; set; }
    public DateTime last_seen { get; set; }
}

public sealed class AgentListResult
{
    public AgentInfo[] agents { get; set; } = Array.Empty<AgentInfo>();
}
