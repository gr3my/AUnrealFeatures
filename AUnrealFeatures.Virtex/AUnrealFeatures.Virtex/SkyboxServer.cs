using AUnrealFeatures.Hosting.Http;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Virtex;


public sealed partial class SkyboxServer : AstraHttpServer, ISkyboxServer
{
    public SkyboxServer() : base("localhost", 9093) { }

    // real MatchStatusFromString enum values, used by both the login check
    // (status code only, body ignored) and the map-list refresh (full body
    // parsed). game_info is only read client-side when rounds_saved > 3 OR
    // status == "Live", so a rich test map plus a bare "just connected" map
    // exercises both paths.
    private static readonly (string MapId, string Status, int RoundsSaved, object? GameInfo)[] MockMaps =
    {
        ("example_map_1", "Live", 7, new
        {
            map = "example_map",
            alpha_clan = "Team A",
            bravo_clan = "Team B",
            alpha_score = 4,
            bravo_score = 3,
        }),
        ("example_map_2", "Connected", 0, null),
    };

    [HttpGet("/api/status")]
    public async Task<IHttpActionResult> Status(IHttpRequest request, IHttpResponse response)
    {
        Logger.Information("Skybox /api/status requested");
        var mapInfos = MockMaps.Select(m => (object)new
        {
            map_id = m.MapId,
            status = m.Status,
            rounds_saved = m.RoundsSaved,
            start_time = DateTimeOffset.UtcNow.ToString("o"),
            game_info = m.GameInfo,
        }).ToArray();
        return Results.Configurable(HttpStatusCode.OK, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new { uptime = 12345, map_infos = mapInfos }));
    }

    [HttpGet("/api/map/{id}/info")]
    public async Task<IHttpActionResult> MapInfo(IHttpRequest request, IHttpResponse response, string id)
    {
        Logger.Information("Skybox /api/map/{Id}/info requested", id);
        var map = MockMaps.FirstOrDefault(m => m.MapId == id);
        return Results.Configurable(HttpStatusCode.OK, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                status = map.MapId != null ? map.Status : "Connecting",
                rounds_saved = map.RoundsSaved,
            }));
    }

    // the native replay controller: this response is NOT JSON -- it must
    // be a valid FlatBuffers-encoded Cs::FramesChunk binary buffer
    // (flatbuffers::Verifier::VerifyBufferFromStart<Cs::FramesChunk>).
    // Hand-encoded via CsFramesChunkBuilder (SkyboxServer.FlatBuffers.cs)
    // file's header for the full field-by-field citation. A single frame
    // with 10 alive players (5 per side) moving in a loose grid around
    // spawn is enough to pass VerifyBufferFromStart and exercise the
    // client round-replay rendering path end to end.
    [HttpGet("/api/map/{mapId}/rounds/{roundId}/data")]
    public async Task<IHttpActionResult> RoundData(IHttpRequest request, IHttpResponse response, string mapId, string roundId)
    {
        Logger.Information("Skybox round data requested for map={MapId} round={RoundId}", mapId, roundId);

        var alivePlayers = CsFramesChunkBuilder.BuildMockAlivePlayers();
        var buffer = CsFramesChunkBuilder.BuildMockFramesChunk("example_map", alivePlayers);
        return Results.Configurable(HttpStatusCode.OK, "application/octet-stream", buffer);
    }
}

public interface ISkyboxServer { }
