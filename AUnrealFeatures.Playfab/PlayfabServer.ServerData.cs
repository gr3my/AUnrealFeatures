using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Playfab.Models;
using PlayFab.ClientModels;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    /* ─── Server API: Title Data ──────────────────────────────────────────
     * Separate from /Client/GetTitleData — hit by UGetTitleStorageValueServerTask
     * (the server-authoritative path in BP_StadiumController/BP_Screen's
     * stream resolution). Real captured traffic sends no X-SecretKey at all,
     * so this doesn't enforce DevSecretKey auth like the real PlayFab Server
     * API would — tighten this up if you want that enforced. */
    [HttpPost("/Server/GetTitleData")]
    public Task<IHttpActionResult> Server_GetTitleData(IHttpRequest request, IHttpResponse response)
    {
        var body  = ParseRequest<GetTitleDataRequest>(request) ?? new GetTitleDataRequest();
        var coll  = Database.GetCollection<PlayfabTitleDataDbObject>(true)!;
        var query = coll.Find(t => t.TitleId == TITLE_ID && t.Scope == "Title").ToList();

        Dictionary<string, string> data;
        if (body.Keys != null && body.Keys.Count > 0)
        {
            var requested = new HashSet<string>(body.Keys);
            data = query.Where(t => requested.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value);
        }
        else
        {
            data = query.ToDictionary(t => t.Key, t => t.Value);
        }
        return Task.FromResult(Ok(new GetTitleDataResult { Data = data }));
    }
}
