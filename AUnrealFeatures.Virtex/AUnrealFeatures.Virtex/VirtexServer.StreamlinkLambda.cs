using AUnrealFeatures.Hosting.Configuration;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AUnrealFeatures.Virtex;


public sealed partial class VirtexServer
{
    private sealed class StreamlinkRequest
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
    }

    private sealed class StreamlinkResponse
    {
        [JsonPropertyName("message")] public string Message { get; set; } = "";
    }

    
    private sealed class GetStreamIDsRequest
    {
        [JsonPropertyName("stream")] public string? Stream { get; set; }
    }

    private sealed class StreamKeyDataDto
    {
        [JsonPropertyName("StreamUrl")] public string StreamUrl { get; set; } = "";
        [JsonPropertyName("StreamKey")] public string StreamKey { get; set; } = "";
        [JsonPropertyName("StreamTitle")] public string StreamTitle { get; set; } = "";
        [JsonPropertyName("RepServerReady")] public bool RepServerReady { get; set; }
        [JsonPropertyName("VideoActive")] public bool VideoActive { get; set; }
    }

    private Task<IHttpActionResult> GetStreamIDsResolve(GetStreamIDsRequest body)
    {
        Logger.Information("GetStreamIDs requested for stream={Stream}", body.Stream);

        var coll = AUnrealFeatures.Playfab.PlayfabServer.Database.GetCollection<AUnrealFeatures.Playfab.Models.PlayfabTitleDataDbObject>(true)!;
        var entry = coll.FindOne(x => x.TitleId == AUnrealFeatures.Playfab.PlayfabServer.TITLE_ID && x.Scope == "Title" && x.Key == "config_stream_replication");
        var streamUrl = string.IsNullOrEmpty(entry?.Value) ? EnvironmentSettings.Get("GAME_DEFAULT_STREAM_URL", "https://example.invalid/stream") : entry!.Value;

        var streams = new List<StreamKeyDataDto>
        {
            new()
            {
                StreamUrl = streamUrl,
                StreamKey = "main",
                StreamTitle = "Main Broadcast",
                RepServerReady = false,
                VideoActive = true,
            },
        };
        return Task.FromResult(Results.Configurable(HttpStatusCode.OK, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(streams, JsonOpts)));
    }

    private static readonly TimeSpan StreamlinkTimeout = TimeSpan.FromSeconds(20);

    // `streamlink.exe` (from `pip install streamlink`) usually lands in a
    // user Scripts dir that isn't on PATH, so bare "streamlink" often isn't
    // directly runnable even when installed. Probe a few invocation styles
    // once and cache whichever actually works.
    private static readonly (string File, string[] PrefixArgs)[] StreamlinkCandidates =
    {
        ("streamlink", Array.Empty<string>()),
        ("python", new[] { "-m", "streamlink" }),
        ("python3", new[] { "-m", "streamlink" }),
        ("py", new[] { "-m", "streamlink" }),
    };

    private static (string File, string[] PrefixArgs)? _resolvedStreamlinkCommand;
    private static bool _streamlinkProbed;

    private async Task<(string File, string[] PrefixArgs)?> GetStreamlinkCommand()
    {
        if (_streamlinkProbed) return _resolvedStreamlinkCommand;

        foreach (var candidate in StreamlinkCandidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate.File,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                foreach (var a in candidate.PrefixArgs) psi.ArgumentList.Add(a);
                psi.ArgumentList.Add("--version");

                using var proc = Process.Start(psi);
                if (proc == null) continue;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await proc.WaitForExitAsync(cts.Token); }
                catch (OperationCanceledException) { try { proc.Kill(true); } catch { } continue; }

                if (proc.ExitCode == 0)
                {
                    _resolvedStreamlinkCommand = candidate;
                    _streamlinkProbed = true;
                    Logger.Information("Resolved streamlink command: {File} {Args}", candidate.File, string.Join(' ', candidate.PrefixArgs));
                    return candidate;
                }
            }
            catch
            {
                // candidate not runnable on this machine — try the next one
            }
        }

        _resolvedStreamlinkCommand = null;
        _streamlinkProbed = true;
        Logger.Warning("No working streamlink command found (tried: {Candidates}). Falling back to passthrough for all resolves.",
            string.Join(", ", StreamlinkCandidates.Select(c => c.File)));
        return null;
    }

    // Some clients use the same callback route for stream-id lookup and URL resolution.
    // Inspect the request shape before dispatching to the appropriate handler.
    [HttpPost("/")]
    public Task<IHttpActionResult> StreamlinkOrGetStreamIDs(IHttpRequest request, IHttpResponse response)
    {
        try
        {
            if (request.Body is { Length: > 0 })
            {
                using var doc = JsonDocument.Parse(request.Body);
                if (doc.RootElement.TryGetProperty("stream", out var streamProp))
                {
                    return GetStreamIDsResolve(new GetStreamIDsRequest { Stream = streamProp.GetString() });
                }
            }
        }
        catch (JsonException) { /* The request is not a GetStreamIDs payload. */ }

        return StreamlinkResolve(request, response);
    }

    public async Task<IHttpActionResult> StreamlinkResolve(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseBody<StreamlinkRequest>(request);
        var url = body?.Url ?? "";
        Logger.Information("Streamlink resolve requested for url={Url}", url);

        if (string.IsNullOrEmpty(url))
        {
            return Results.Configurable(HttpStatusCode.OK, "application/json",
                JsonSerializer.SerializeToUtf8Bytes(new StreamlinkResponse { Message = "qualities: |||| urls:" }, JsonOpts));
        }

        var (qualities, urls) = await ResolveWithStreamlink(url);
        if (qualities.Count == 0)
        {
            var fallbackUrl = EnvironmentSettings.Get("GAME_FALLBACK_STREAM_URL", "");
            if (!string.IsNullOrWhiteSpace(fallbackUrl))
            {
                Logger.Warning("Stream resolution failed for {Url}; using the configured fallback stream.", url);
                foreach (var quality in new[] { "480p", "720p", "720p60", "best", "worst", "source" })
                {
                    qualities.Add(quality);
                    urls.Add(fallbackUrl);
                }
            }
        }

        var message = FormatStreamlinkMessage(qualities, urls);
        return Results.Configurable(HttpStatusCode.OK, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new StreamlinkResponse { Message = message }, JsonOpts));
    }

    private static string FormatStreamlinkMessage(List<string> qualities, List<string> urls)
    {
        var quaStr = string.Concat(qualities.Select(q => q + "|||"));
        var urlStr = string.Concat(urls.Select(u => u + "|||"));
        return "qualities: " + quaStr + "|||| urls:" + urlStr;
    }

    private async Task<(List<string> qualities, List<string> urls)> ResolveWithStreamlink(string url)
    {
        var qualities = new List<string>();
        var urls = new List<string>();

        var command = await GetStreamlinkCommand();
        if (command == null) return (qualities, urls);

        ProcessStartInfo psi;
        try
        {
            psi = new ProcessStartInfo
            {
                FileName = command.Value.File,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in command.Value.PrefixArgs) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add(url);
            psi.ArgumentList.Add("--json");
            psi.ArgumentList.Add("--stream-types");
            psi.ArgumentList.Add("hls,http");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to build streamlink process for url={Url}", url);
            return (qualities, urls);
        }

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return (qualities, urls);

            using var cts = new CancellationTokenSource(StreamlinkTimeout);
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Logger.Warning("streamlink timed out resolving url={Url}", url);
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return (qualities, urls);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                Logger.Warning("streamlink exited {Code} for url={Url}: {Err}", proc.ExitCode, url, stderr.Trim());
                return (qualities, urls);
            }

            using var doc = JsonDocument.Parse(stdout);
            if (!doc.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Object)
                return (qualities, urls);

            foreach (var stream in streams.EnumerateObject())
            {
                var streamUrl = ExtractStreamUrl(stream.Value);
                if (streamUrl == null) continue;
                qualities.Add(stream.Name);
                urls.Add(streamUrl);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "streamlink resolution threw for url={Url}", url);
        }

        return (qualities, urls);
    }

    // Plain HLS/HTTP streams expose a top-level "url"; muxed streams (separate
    // video+audio, e.g. some YouTube DASH variants) nest per-substream "url"s
    // under a "substreams"/"urls" map instead — take the first one found.
    private static string? ExtractStreamUrl(JsonElement streamEntry)
    {
        if (streamEntry.TryGetProperty("url", out var direct) && direct.ValueKind == JsonValueKind.String)
            return direct.GetString();

        if (streamEntry.TryGetProperty("urls", out var urlsObj) && urlsObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in urlsObj.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.String)
                    return prop.Value.GetString();
        }

        if (streamEntry.TryGetProperty("substreams", out var subs) && subs.ValueKind == JsonValueKind.Array)
        {
            foreach (var sub in subs.EnumerateArray())
                if (sub.TryGetProperty("url", out var subUrl) && subUrl.ValueKind == JsonValueKind.String)
                    return subUrl.GetString();
        }

        return null;
    }
}
