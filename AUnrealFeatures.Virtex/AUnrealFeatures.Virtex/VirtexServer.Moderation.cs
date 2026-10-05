using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using System.Text.Json.Serialization;

namespace AUnrealFeatures.Virtex;


public sealed partial class VirtexServer
{
    private sealed class ModerationServerResult
    {
        [JsonPropertyName("address")] public string Address { get; set; } = "";
        [JsonPropertyName("udp_port")] public int UdpPort { get; set; }
    }

    [HttpPost("/moderation/server")]
    public async Task<IHttpActionResult> ModerationServer(IHttpRequest request, IHttpResponse response)
    {
        var entityToken = request.GetHeaderValue("X-EntityToken");
        Logger.Information("Moderation server address requested (entity token present={HasToken})", !string.IsNullOrEmpty(entityToken));

        // No standalone moderation/chat server implemented — point at the
        // main Virtex host itself so the client at least gets a resolvable
        // address instead of failing outright.
        return VirtexOk(new ModerationServerResult { Address = "127.0.0.1", UdpPort = 43614 });
    }

    [HttpPost("/user-report")]
    public async Task<IHttpActionResult> UserReport(IHttpRequest request, IHttpResponse response)
    {
        var authToken = request.GetHeaderValue("X-authtoken");
        var body = request.Body != null ? System.Text.Encoding.UTF8.GetString(request.Body) : "";
        Logger.Information("User report received (authtoken present={HasToken}): {Body}", !string.IsNullOrEmpty(authToken), body);
        return Results.Ok();
    }
}
