using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using System.Text.Json.Serialization;

namespace AUnrealFeatures.Virtex;

/* ─── RichPresenceServiceAddress + SetAvatarLambda ───────────────────────────
 * MakeRichPresenceRequest POSTs to "{Address}:{WebPort}/RichPresence/{command}"
 * with an already-serialized JSON string body built by the caller. Confirmed
 * from real captured traffic that the client actually hits the literal
 * ".../richpresence:8001/RichPresence/{command}" path (RichPresenceServiceAddress
 * already has "/richpresence" baked in as a bare-host value, and the
 * "%s:%s/RichPresence/%s" format string appends ":WebPort" mid-path rather
 * than as an actual port) — so that's the real route, not a bug to fix. */
public sealed partial class VirtexServer
{
    private sealed class GetUserOnlineStatusRequest
    {
        [JsonPropertyName("EntityID")] public string? EntityID { get; set; }
    }

    private sealed class UserOnlineStatusResult
    {
        [JsonPropertyName("EntityID")] public string EntityID { get; set; } = "";
        [JsonPropertyName("Online")] public bool Online { get; set; } = true;
    }

    [HttpPost("/richpresence:8001/RichPresence/{command}")]
    public async Task<IHttpActionResult> RichPresenceLiteral(IHttpRequest request, IHttpResponse response, string command) =>
        await RichPresence(request, response, command);

    [HttpPost("/richpresence/{command}")]
    public async Task<IHttpActionResult> RichPresence(IHttpRequest request, IHttpResponse response, string command)
    {
        var body = request.Body != null ? System.Text.Encoding.UTF8.GetString(request.Body) : "";
        Logger.Information("RichPresence command '{Command}' requested: {Body}", command, body);

        if (command == "GetUserOnlineStatus")
        {
            var req = ParseBody<GetUserOnlineStatusRequest>(request);
            // Default everyone to online — this is a self-hosted server, there's
            // no real presence backend to check against.
            return VirtexOk(new UserOnlineStatusResult { EntityID = req?.EntityID ?? "", Online = true });
        }

        return VirtexOk(new Dictionary<string, object>());
    }

    [HttpGet("/set-avatar/SetAvatar")]
    public async Task<IHttpActionResult> SetAvatar(IHttpRequest request, IHttpResponse response)
    {
        var id = request.GetQueryParameter("id");
        var icon = request.GetQueryParameter("icon");
        Logger.Information("Avatar customization link opened for id={Id} icon={Icon}", id, icon);

        // GetRPMCustomizationLink builds "{SetAvatarLambda}?id={netId}&icon={iconImageID}"
        // and this is opened in a browser widget for Ready Player Me-style
        // avatar picking. Without an actual RPM integration, serve a minimal
        // stub page so the flow doesn't hard-fail in the browser widget.
        var html =
            "<html><body><h3>Avatar customization is not implemented on this self-hosted server.</h3>" +
            $"<p>id={System.Net.WebUtility.HtmlEncode(id)} icon={System.Net.WebUtility.HtmlEncode(icon)}</p></body></html>";
        return Results.Configurable(System.Net.HttpStatusCode.OK, "text/html", System.Text.Encoding.UTF8.GetBytes(html));
    }
}
