using AUnrealFeatures.Hosting.Configuration;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Playfab;
using AUnrealFeatures.Playfab.Models;
using System.Net;

namespace AUnrealFeatures.Virtex;


public sealed partial class VirtexServer
{
    // --- OAuth redirect targets (opened in an in-game WebBrowser widget) ---
    [HttpGet("/twitch_auth")]
    public async Task<IHttpActionResult> TwitchAuth(IHttpRequest request, IHttpResponse response)
        => OAuthStubPage("Twitch");

    [HttpPost("/twitch_token_refresh")]
    public async Task<IHttpActionResult> TwitchTokenRefresh(IHttpRequest request, IHttpResponse response)
        => VirtexOk(new { access_token = "", refresh_token = "", expires_in = 0 });

    [HttpGet("/TwitchUserDeclineAccess")]
    public async Task<IHttpActionResult> TwitchUserDeclineAccess(IHttpRequest request, IHttpResponse response)
        => OAuthStubPage("Twitch (declined)");

    [HttpGet("/youtube_auth")]
    public async Task<IHttpActionResult> YoutubeAuth(IHttpRequest request, IHttpResponse response)
        => OAuthStubPage("Youtube");

    [HttpPost("/youtube_token_refresh")]
    public async Task<IHttpActionResult> YoutubeTokenRefresh(IHttpRequest request, IHttpResponse response)
        => VirtexOk(new { access_token = "", refresh_token = "", expires_in = 0 });

    [HttpGet("/YoutubeUserDeclineAccess")]
    public async Task<IHttpActionResult> YoutubeUserDeclineAccess(IHttpRequest request, IHttpResponse response)
        => OAuthStubPage("Youtube (declined)");

    private static IHttpActionResult OAuthStubPage(string provider) =>
        Results.Configurable(HttpStatusCode.OK, "text/html",
            System.Text.Encoding.UTF8.GetBytes($"<html><body><h3>{provider} OAuth is not implemented on this self-hosted server.</h3></body></html>"));

    private sealed class SuperuserCheckRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("UserId")] public string? UserId { get; set; }
    }

    private sealed class CoordinatorRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("pid")]
        public string? Pid { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("port")]
        public int? Port { get; set; }
    }

    private sealed class LiveServerEntry
    {
        public string Address { get; init; } = "127.0.0.1";
        public int Port { get; init; } = 7777;
        public DateTime LastSeenUtc { get; init; } = DateTime.UtcNow;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, LiveServerEntry>
        LiveServers = new(StringComparer.Ordinal);

    internal static bool TryGetLiveServer(string partnerId, out string address, out int port)
    {
        address = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(partnerId) || !LiveServers.TryGetValue(partnerId, out var entry))
            return false;

        var ttlSeconds = EnvironmentSettings.GetInt32("VIRTEX_LIVE_SERVER_TTL_SECONDS", 0);
        if (ttlSeconds > 0 && DateTime.UtcNow - entry.LastSeenUtc > TimeSpan.FromSeconds(ttlSeconds))
        {
            LiveServers.TryRemove(partnerId, out _);
            return false;
        }

        address = entry.Address;
        port = entry.Port;
        return true;
    }

    // --- Misc single-purpose services ---
    
    [HttpPost("/superuser-check")]
    public async Task<IHttpActionResult> SuperuserCheck(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseBody<SuperuserCheckRequest>(request);
        Logger.Information("Superuser check requested for UserId={UserId}", body?.UserId);
        var groups = new[] { "dev_group", "prod_group", "mod_group", "vip_group" };
        return Results.Configurable(HttpStatusCode.OK, "application/json",
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(groups));
    }
    // Map selection can be overridden per partner with coordinator_map_<id>.
    [HttpPost("/coordinator")]
    public async Task<IHttpActionResult> Coordinator(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseBody<CoordinatorRequest>(request);
        var pid = body?.Pid ?? "";
        Logger.Information("Coordinator config requested for pid={Pid}", pid);

        // Register the calling dedicated server for this partner. The remote address is
        // taken from the connection rather than from client-controlled JSON.
        if (!string.IsNullOrEmpty(pid) && body?.Port is int port and > 0)
        {
            var remote = request.Remote;
            var addr = remote != null && !System.Net.IPAddress.IsLoopback(remote) ? remote.ToString() : "127.0.0.1";
            LiveServers[pid] = new LiveServerEntry { Address = addr, Port = port, LastSeenUtc = DateTime.UtcNow };
            Logger.Information("Registered live dedicated server for pid={Pid} at {Address}:{Port}", pid, addr, port);
        }

        var coll = PlayfabServer.Database.GetCollection<PlayfabTitleDataDbObject>(true)!;

        PlayfabTitleDataDbObject? mapEntry = null;
        if (!string.IsNullOrEmpty(pid))
        {
            var perPidKey = $"coordinator_map_{pid}";
            mapEntry = coll.FindOne(x => x.TitleId == PlayfabServer.TITLE_ID && x.Scope == "Title" && x.Key == perPidKey);
        }
        if (string.IsNullOrEmpty(mapEntry?.Value))
            mapEntry = coll.FindOne(x => x.TitleId == PlayfabServer.TITLE_ID && x.Scope == "Title" && x.Key == "coordinator_map");

        var map = string.IsNullOrEmpty(mapEntry?.Value) ? EnvironmentSettings.Get("GAME_DEFAULT_MAP", "ExampleMap") : mapEntry!.Value;

        return VirtexOk(new
        {
            Ready = true,
            Map = map,
            Image = "",
            PartnerId = pid,
            Capacity = 32,
            CPU = "1",
            MemoryLimit = "2Gi",
            MemoryRequest = "1Gi",
            Port = 7777,
        });
    }

    [HttpGet("/check-stream")]
    public async Task<IHttpActionResult> CheckStream(IHttpRequest request, IHttpResponse response)
    {
        var url = request.GetQueryParameter("url");
        Logger.Information("Stream status check for url={Url}", url);
        return VirtexOk(new { active = true });
    }

    private sealed class StreamsStatusRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("streams")] public List<string>? Streams { get; set; }
    }

    
    [HttpPost("/stream-status")]
    public async Task<IHttpActionResult> StreamStatus(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseBody<StreamsStatusRequest>(request);
        var streams = body?.Streams ?? new List<string>();
        Logger.Information("Stream status batch requested for {Count} streams: {Streams}", streams.Count, streams);
        // No real liveness checking backend — report everything as active.
        var result = streams.ToDictionary(url => url, _ => true);
        return Results.Configurable(HttpStatusCode.OK, "application/json",
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result));
    }
}
