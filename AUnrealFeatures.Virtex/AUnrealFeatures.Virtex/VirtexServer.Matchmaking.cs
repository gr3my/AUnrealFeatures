using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Playfab;
using AUnrealFeatures.Playfab.Models;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace AUnrealFeatures.Virtex;


public sealed partial class VirtexServer
{
    // Prefer a server that registered through /coordinator. Title data provides
    // per-partner and global fallbacks for deployments managed externally.
    private static (string Address, int Port) ResolveMatchmakingServer(string partnerId)
    {
        if (TryGetLiveServer(partnerId, out var liveAddress, out var livePort))
            return (liveAddress, livePort);

        var coll = PlayfabServer.Database.GetCollection<PlayfabTitleDataDbObject>(true)!;

        PlayfabTitleDataDbObject? entry = null;
        if (!string.IsNullOrEmpty(partnerId))
        {
            var perPidKey = $"matchmaking_server_{partnerId}";
            entry = coll.FindOne(x => x.TitleId == PlayfabServer.TITLE_ID && x.Scope == "Title" && x.Key == perPidKey);
        }
        if (string.IsNullOrEmpty(entry?.Value))
            entry = coll.FindOne(x => x.TitleId == PlayfabServer.TITLE_ID && x.Scope == "Title" && x.Key == "matchmaking_server");

        var raw = string.IsNullOrEmpty(entry?.Value) ? "127.0.0.1:7777" : entry!.Value;
        var parts = raw.Split(':', 2);
        var address = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 7777;
        return (address, port);
    }

    private sealed class GameServerAllocation
    {
        public string GameServerId { get; init; } = "";
        public string Address { get; init; } = "127.0.0.1";
        public int UdpPort { get; init; } = 7777;
    }

    // Allocations are kept in memory so /server-translate can resolve the server
    // identifier returned by /matchmaking/allocate-player.
    private static readonly ConcurrentDictionary<string, GameServerAllocation> _allocations = new();

    private sealed class MatchRequest
    {
        [JsonPropertyName("partner_id")] public string? PartnerId { get; set; }
        [JsonPropertyName("environment")] public string? Environment { get; set; }
        [JsonPropertyName("player_id")] public string? PlayerId { get; set; }
    }

    private sealed class SessionRequest
    {
        [JsonPropertyName("game_server_id")] public string? GameServerId { get; set; }
    }

    private sealed class MatchResult
    {
        [JsonPropertyName("game_server_id")] public string GameServerId { get; set; } = "";
        [JsonPropertyName("address")] public string Address { get; set; } = "";
        [JsonPropertyName("udp_port")] public int UdpPort { get; set; }
    }

    private sealed class SessionResult
    {
        [JsonPropertyName("address")] public string Address { get; set; } = "";
        [JsonPropertyName("udp_port")] public int UdpPort { get; set; }
    }

    [HttpPost("/matchmaking/allocate-player")]
    public async Task<IHttpActionResult> AllocatePlayer(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseBody<MatchRequest>(request);
        var partnerId = body?.PartnerId ?? "";
        Logger.Information("Matchmake request: partner_id={PartnerId} player_id={PlayerId} environment={Environment}",
            partnerId, body?.PlayerId, body?.Environment);

        var (address, port) = ResolveMatchmakingServer(partnerId);
        var allocation = new GameServerAllocation
        {
            GameServerId = Guid.NewGuid().ToString("N"),
            Address = address,
            UdpPort = port,
        };
        _allocations[allocation.GameServerId] = allocation;

        return VirtexOk(new MatchResult
        {
            GameServerId = allocation.GameServerId,
            Address = allocation.Address,
            UdpPort = allocation.UdpPort,
        });
    }

    [HttpPost("/server-translate")]
    public async Task<IHttpActionResult> ServerTranslate(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseBody<SessionRequest>(request);
        Logger.Information("Server address translation requested for game_server_id={GameServerId}", body?.GameServerId);

        if (body?.GameServerId != null && _allocations.TryGetValue(body.GameServerId, out var allocation))
        {
            return VirtexOk(new SessionResult { Address = allocation.Address, UdpPort = allocation.UdpPort });
        }

        // Development fallback when no allocation is known.
        return VirtexOk(new SessionResult { Address = "127.0.0.1", UdpPort = 7777 });
    }
}
