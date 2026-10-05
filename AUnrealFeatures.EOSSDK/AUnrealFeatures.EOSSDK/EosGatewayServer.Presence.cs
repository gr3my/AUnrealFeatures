using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.EOSSDK;


public sealed partial class EosGatewayServer
{
    [HttpPost("/epic/presence/internal/v1/_/{account_id}/presence/{connection_id}")]
    public async Task<IHttpActionResult> UpdatePresence(IHttpRequest request, IHttpResponse response, string account_id, string connection_id)
    {
        var body = request.Body != null ? System.Text.Encoding.UTF8.GetString(request.Body) : "";
        Logger.Information("Presence update for account={AccountId} conn={ConnectionId}: {Body}", account_id, connection_id, body);
        return Results.Configurable(HttpStatusCode.OK, "application/json", JsonSerializer.SerializeToUtf8Bytes(new { }));
    }
}
