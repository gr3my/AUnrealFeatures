using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Playfab.Models;
using PlayFab.AddonModels;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    /* ─── Helpers ─────────────────────────────────────────────────────────── */

    private (PlayfabAddonConfigDbObject row, bool created) AddonUpsert(string addon, object payload, bool? errorIfExists)
    {
        var coll = Database.GetCollection<PlayfabAddonConfigDbObject>(true)!;
        var existing = coll.FindOne(x => x.TitleId == TITLE_ID && x.AddonName == addon);
        if (existing != null && errorIfExists == true)
            throw new PlayfabFault("AddonAlreadyExists", 1690, $"{addon} addon already configured");
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        if (existing == null)
        {
            existing = new PlayfabAddonConfigDbObject
            {
                TitleId     = TITLE_ID,
                AddonName   = addon,
                ConfigJson  = json,
                LastUpdated = NowUnix(),
            };
            coll.Insert(existing);
            return (existing, true);
        }
        existing.ConfigJson  = json;
        existing.LastUpdated = NowUnix();
        coll.Update(existing);
        return (existing, false);
    }

    private bool AddonDelete(string addon)
    {
        var coll = Database.GetCollection<PlayfabAddonConfigDbObject>(true)!;
        return coll.DeleteMany(x => x.TitleId == TITLE_ID && x.AddonName == addon) > 0;
    }

    private T? AddonGet<T>(string addon) where T : class
    {
        var existing = Database.GetCollection<PlayfabAddonConfigDbObject>(true)!
                               .FindOne(x => x.TitleId == TITLE_ID && x.AddonName == addon);
        if (existing == null || string.IsNullOrEmpty(existing.ConfigJson)) return null;
        try { return JsonSerializer.Deserialize<T>(existing.ConfigJson, JsonOpts); }
        catch { return null; }
    }

    private bool AddonExists(string addon) =>
        Database.GetCollection<PlayfabAddonConfigDbObject>(true)!
                .FindOne(x => x.TitleId == TITLE_ID && x.AddonName == addon) != null;

    /* ─── Apple ───────────────────────────────────────────────────────────── */

    /// <summary>Creates or updates the Apple addon configuration.</summary>
    [HttpPost("/Addon/CreateOrUpdateApple")]
    public Task<IHttpActionResult> Addon_CreateOrUpdateApple(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateOrUpdateAppleRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        try { AddonUpsert("Apple", body, body.ErrorIfExists); return Task.FromResult(Ok(new CreateOrUpdateAppleResponse())); }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
    }

    /// <summary>Returns the Apple addon configuration.</summary>
    [HttpPost("/Addon/GetApple")]
    public Task<IHttpActionResult> Addon_GetApple(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var cfg = AddonGet<CreateOrUpdateAppleRequest>("Apple");
        return Task.FromResult(Ok(new GetAppleResponse
        {
            Created                     = cfg != null,
            AppBundleId                 = cfg?.AppBundleId ?? "",
            IgnoreExpirationDate        = cfg?.IgnoreExpirationDate,
            RequireSecureAuthentication = cfg?.RequireSecureAuthentication,
        }));
    }

    /// <summary>Deletes the Apple addon configuration.</summary>
    [HttpPost("/Addon/DeleteApple")]
    public Task<IHttpActionResult> Addon_DeleteApple(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        AddonDelete("Apple");
        return Task.FromResult(Ok(new DeleteAppleResponse()));
    }

    /* ─── Facebook ────────────────────────────────────────────────────────── */

    /// <summary>Creates or updates the Facebook addon configuration.</summary>
    [HttpPost("/Addon/CreateOrUpdateFacebook")]
    public Task<IHttpActionResult> Addon_CreateOrUpdateFacebook(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateOrUpdateFacebookRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        try { AddonUpsert("Facebook", body, body.ErrorIfExists); return Task.FromResult(Ok(new CreateOrUpdateFacebookResponse())); }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
    }

    /// <summary>Returns the Facebook addon configuration.</summary>
    [HttpPost("/Addon/GetFacebook")]
    public Task<IHttpActionResult> Addon_GetFacebook(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var cfg = AddonGet<CreateOrUpdateFacebookRequest>("Facebook");
        return Task.FromResult(Ok(new GetFacebookResponse
        {
            Created           = cfg != null,
            AppID             = cfg?.AppID ?? "",
            NotificationEmail = cfg?.NotificationEmail ?? "",
        }));
    }

    /// <summary>Deletes the Facebook addon configuration.</summary>
    [HttpPost("/Addon/DeleteFacebook")]
    public Task<IHttpActionResult> Addon_DeleteFacebook(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        AddonDelete("Facebook");
        return Task.FromResult(Ok(new DeleteFacebookResponse()));
    }

    /* ─── Facebook Instant Games ─────────────────────────────────────────── */

    /// <summary>Creates or updates the Facebook Instant Games addon configuration.</summary>
    [HttpPost("/Addon/CreateOrUpdateFacebookInstantGames")]
    public Task<IHttpActionResult> Addon_CreateOrUpdateFacebookInstantGames(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateOrUpdateFacebookInstantGamesRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        try { AddonUpsert("FacebookInstantGames", body, body.ErrorIfExists); return Task.FromResult(Ok(new CreateOrUpdateFacebookInstantGamesResponse())); }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
    }

    /// <summary>Returns the Facebook Instant Games addon configuration.</summary>
    [HttpPost("/Addon/GetFacebookInstantGames")]
    public Task<IHttpActionResult> Addon_GetFacebookInstantGames(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var cfg = AddonGet<CreateOrUpdateFacebookInstantGamesRequest>("FacebookInstantGames");
        return Task.FromResult(Ok(new GetFacebookInstantGamesResponse
        {
            Created = cfg != null,
            AppID   = cfg?.AppID ?? "",
        }));
    }

    /// <summary>Deletes the Facebook Instant Games addon configuration.</summary>
    [HttpPost("/Addon/DeleteFacebookInstantGames")]
    public Task<IHttpActionResult> Addon_DeleteFacebookInstantGames(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        AddonDelete("FacebookInstantGames");
        return Task.FromResult(Ok(new DeleteFacebookInstantGamesResponse()));
    }

    /* ─── Google ──────────────────────────────────────────────────────────── */

    /// <summary>Creates or updates the Google addon configuration.</summary>
    [HttpPost("/Addon/CreateOrUpdateGoogle")]
    public Task<IHttpActionResult> Addon_CreateOrUpdateGoogle(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateOrUpdateGoogleRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        try { AddonUpsert("Google", body, body.ErrorIfExists); return Task.FromResult(Ok(new CreateOrUpdateGoogleResponse())); }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
    }

    /// <summary>Returns the Google addon configuration.</summary>
    [HttpPost("/Addon/GetGoogle")]
    public Task<IHttpActionResult> Addon_GetGoogle(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var cfg = AddonGet<CreateOrUpdateGoogleRequest>("Google");
        return Task.FromResult(Ok(new GetGoogleResponse
        {
            Created                = cfg != null,
            AppPackageID           = cfg?.AppPackageID ?? "",
            OAuthClientID          = cfg?.OAuthClientID ?? "",
            OauthCustomRedirectUri = cfg?.OAuthCustomRedirectUri ?? "",
        }));
    }

    /// <summary>Deletes the Google addon configuration.</summary>
    [HttpPost("/Addon/DeleteGoogle")]
    public Task<IHttpActionResult> Addon_DeleteGoogle(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        AddonDelete("Google");
        return Task.FromResult(Ok(new DeleteGoogleResponse()));
    }

    /* ─── Steam ───────────────────────────────────────────────────────────── */

    /// <summary>Creates or updates the Steam addon configuration.</summary>
    [HttpPost("/Addon/CreateOrUpdateSteam")]
    public Task<IHttpActionResult> Addon_CreateOrUpdateSteam(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateOrUpdateSteamRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        try { AddonUpsert("Steam", body, body.ErrorIfExists); return Task.FromResult(Ok(new CreateOrUpdateSteamResponse())); }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
    }

    /// <summary>Returns the Steam addon configuration.</summary>
    [HttpPost("/Addon/GetSteam")]
    public Task<IHttpActionResult> Addon_GetSteam(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var cfg = AddonGet<CreateOrUpdateSteamRequest>("Steam");
        return Task.FromResult(Ok(new GetSteamResponse
        {
            Created                       = cfg != null,
            ApplicationId                 = cfg?.ApplicationId ?? "",
            EnforceServiceSpecificTickets = cfg?.EnforceServiceSpecificTickets,
            UseSandbox                    = cfg?.UseSandbox,
        }));
    }

    /// <summary>Deletes the Steam addon configuration.</summary>
    [HttpPost("/Addon/DeleteSteam")]
    public Task<IHttpActionResult> Addon_DeleteSteam(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        AddonDelete("Steam");
        return Task.FromResult(Ok(new DeleteSteamResponse()));
    }

    /* ─── Nintendo ────────────────────────────────────────────────────────── */

    /// <summary>Creates or updates the Nintendo addon configuration.</summary>
    [HttpPost("/Addon/CreateOrUpdateNintendo")]
    public Task<IHttpActionResult> Addon_CreateOrUpdateNintendo(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateOrUpdateNintendoRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        try { AddonUpsert("Nintendo", body, body.ErrorIfExists); return Task.FromResult(Ok(new CreateOrUpdateNintendoResponse())); }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
    }

    /// <summary>Returns the Nintendo addon configuration.</summary>
    [HttpPost("/Addon/GetNintendo")]
    public Task<IHttpActionResult> Addon_GetNintendo(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var cfg = AddonGet<CreateOrUpdateNintendoRequest>("Nintendo");
        return Task.FromResult(Ok(new GetNintendoResponse
        {
            Created       = cfg != null,
            ApplicationID = cfg?.ApplicationID ?? "",
            Environments  = cfg?.Environments ?? new List<NintendoEnvironment>(),
        }));
    }

    /// <summary>Deletes the Nintendo addon configuration.</summary>
    [HttpPost("/Addon/DeleteNintendo")]
    public Task<IHttpActionResult> Addon_DeleteNintendo(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        AddonDelete("Nintendo");
        return Task.FromResult(Ok(new DeleteNintendoResponse()));
    }

    /* ─── PSN ─────────────────────────────────────────────────────────────── */

    /// <summary>Creates or updates the PSN addon configuration.</summary>
    [HttpPost("/Addon/CreateOrUpdatePSN")]
    public Task<IHttpActionResult> Addon_CreateOrUpdatePSN(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateOrUpdatePSNRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        try { AddonUpsert("PSN", body, body.ErrorIfExists); return Task.FromResult(Ok(new CreateOrUpdatePSNResponse())); }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
    }

    /// <summary>Returns the PSN addon configuration.</summary>
    [HttpPost("/Addon/GetPSN")]
    public Task<IHttpActionResult> Addon_GetPSN(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var cfg = AddonGet<CreateOrUpdatePSNRequest>("PSN");
        return Task.FromResult(Ok(new GetPSNResponse
        {
            Created         = cfg != null,
            ClientID        = cfg?.ClientID ?? "",
            NextGenClientID = cfg?.NextGenClientID ?? "",
        }));
    }

    /// <summary>Deletes the PSN addon configuration.</summary>
    [HttpPost("/Addon/DeletePSN")]
    public Task<IHttpActionResult> Addon_DeletePSN(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        AddonDelete("PSN");
        return Task.FromResult(Ok(new DeletePSNResponse()));
    }

    /* ─── Twitch ──────────────────────────────────────────────────────────── */

    /// <summary>Creates or updates the Twitch addon configuration.</summary>
    [HttpPost("/Addon/CreateOrUpdateTwitch")]
    public Task<IHttpActionResult> Addon_CreateOrUpdateTwitch(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateOrUpdateTwitchRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        try { AddonUpsert("Twitch", body, body.ErrorIfExists); return Task.FromResult(Ok(new CreateOrUpdateTwitchResponse())); }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
    }

    /// <summary>Returns the Twitch addon configuration.</summary>
    [HttpPost("/Addon/GetTwitch")]
    public Task<IHttpActionResult> Addon_GetTwitch(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var cfg = AddonGet<CreateOrUpdateTwitchRequest>("Twitch");
        return Task.FromResult(Ok(new GetTwitchResponse
        {
            Created  = cfg != null,
            ClientID = cfg?.ClientID ?? "",
        }));
    }

    /// <summary>Deletes the Twitch addon configuration.</summary>
    [HttpPost("/Addon/DeleteTwitch")]
    public Task<IHttpActionResult> Addon_DeleteTwitch(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        AddonDelete("Twitch");
        return Task.FromResult(Ok(new DeleteTwitchResponse()));
    }

    /* ─── Kongregate ──────────────────────────────────────────────────────── */

    /// <summary>Creates or updates the Kongregate addon configuration.</summary>
    [HttpPost("/Addon/CreateOrUpdateKongregate")]
    public Task<IHttpActionResult> Addon_CreateOrUpdateKongregate(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateOrUpdateKongregateRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        try { AddonUpsert("Kongregate", body, body.ErrorIfExists); return Task.FromResult(Ok(new CreateOrUpdateKongregateResponse())); }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
    }

    /// <summary>Returns the Kongregate addon configuration.</summary>
    [HttpPost("/Addon/GetKongregate")]
    public Task<IHttpActionResult> Addon_GetKongregate(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        return Task.FromResult(Ok(new GetKongregateResponse { Created = AddonExists("Kongregate") }));
    }

    /// <summary>Deletes the Kongregate addon configuration.</summary>
    [HttpPost("/Addon/DeleteKongregate")]
    public Task<IHttpActionResult> Addon_DeleteKongregate(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        AddonDelete("Kongregate");
        return Task.FromResult(Ok(new DeleteKongregateResponse()));
    }

    /* ─── ToxMod ──────────────────────────────────────────────────────────── */

    /// <summary>Creates or updates the ToxMod addon configuration.</summary>
    [HttpPost("/Addon/CreateOrUpdateToxMod")]
    public Task<IHttpActionResult> Addon_CreateOrUpdateToxMod(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateOrUpdateToxModRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        try { AddonUpsert("ToxMod", body, body.ErrorIfExists); return Task.FromResult(Ok(new CreateOrUpdateToxModResponse())); }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
    }

    /// <summary>Returns the ToxMod addon configuration.</summary>
    [HttpPost("/Addon/GetToxMod")]
    public Task<IHttpActionResult> Addon_GetToxMod(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var cfg = AddonGet<CreateOrUpdateToxModRequest>("ToxMod");
        return Task.FromResult(Ok(new GetToxModResponse
        {
            Created    = cfg != null,
            Enabled    = cfg?.Enabled ?? false,
            AccountId  = cfg?.AccountId ?? "",
            AccountKey = cfg?.AccountKey ?? "",
        }));
    }

    /// <summary>Deletes the ToxMod addon configuration.</summary>
    [HttpPost("/Addon/DeleteToxMod")]
    public Task<IHttpActionResult> Addon_DeleteToxMod(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        AddonDelete("ToxMod");
        return Task.FromResult(Ok(new DeleteToxModResponse()));
    }
}
