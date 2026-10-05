using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Database;
using AUnrealFeatures.Playfab.Models;
using PlayFab.ClientModels;
using System.Linq.Expressions;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed class PlayfabGenericIdDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string UserId { get; set; } = "";
}

public sealed partial class PlayfabServer
{
    /* ─── Linking helpers ─────────────────────────────────────────────────── */

    private IHttpActionResult LinkAccount(IHttpRequest req, Action<PlayfabPlayerDbObject> apply)
    {
        if (!CheckAuth(req, AuthType.LoginSession)) return Unauthorized();
        var pid = PlayFabIdFromRequest(req);
        if (string.IsNullOrEmpty(pid)) return Unauthorized();
        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var p = coll.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound);
        apply(p);
        coll.Update(p);
        return OkEmpty();
    }

    private IHttpActionResult UnlinkAccount(IHttpRequest req, Action<PlayfabPlayerDbObject> clear)
    {
        if (!CheckAuth(req, AuthType.LoginSession)) return Unauthorized();
        var pid = PlayFabIdFromRequest(req);
        if (string.IsNullOrEmpty(pid)) return Unauthorized();
        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var p = coll.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound);
        clear(p);
        coll.Update(p);
        return OkEmpty();
    }

    /* ─── Link / Unlink endpoints ─────────────────────────────────────────── */

    /// <summary>Links an Android device id to the calling player.</summary>
    [HttpPost("/Client/LinkAndroidDeviceID")]
    public Task<IHttpActionResult> Client_LinkAndroidDeviceID(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkAndroidDeviceIDRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.AndroidDeviceId = b?.AndroidDeviceId ?? p.AndroidDeviceId));
    }

    /// <summary>Unlinks the Android device id from the player.</summary>
    [HttpPost("/Client/UnlinkAndroidDeviceID")]
    public Task<IHttpActionResult> Client_UnlinkAndroidDeviceID(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkAndroidDeviceIDRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.AndroidDeviceId = ""));
    }

    /// <summary>Links an iOS device id to the calling player.</summary>
    [HttpPost("/Client/LinkIOSDeviceID")]
    public Task<IHttpActionResult> Client_LinkIOSDeviceID(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkIOSDeviceIDRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.IosDeviceId = b?.DeviceId ?? p.IosDeviceId));
    }

    /// <summary>Unlinks the iOS device id from the player.</summary>
    [HttpPost("/Client/UnlinkIOSDeviceID")]
    public Task<IHttpActionResult> Client_UnlinkIOSDeviceID(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkIOSDeviceIDRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.IosDeviceId = ""));
    }

    /// <summary>Links a custom id to the player.</summary>
    [HttpPost("/Client/LinkCustomID")]
    public Task<IHttpActionResult> Client_LinkCustomID(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkCustomIDRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.CustomId = b?.CustomId ?? p.CustomId));
    }

    /// <summary>Unlinks a custom id from the player.</summary>
    [HttpPost("/Client/UnlinkCustomID")]
    public Task<IHttpActionResult> Client_UnlinkCustomID(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkCustomIDRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.CustomId = ""));
    }

    /// <summary>Links a Steam account.</summary>
    [HttpPost("/Client/LinkSteamAccount")]
    public Task<IHttpActionResult> Client_LinkSteamAccount(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkSteamAccountRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.SteamId = b?.SteamTicket ?? p.SteamId));
    }

    /// <summary>Unlinks a Steam account.</summary>
    [HttpPost("/Client/UnlinkSteamAccount")]
    public Task<IHttpActionResult> Client_UnlinkSteamAccount(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkSteamAccountRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.SteamId = ""));
    }

    /// <summary>Links an Xbox Live account.</summary>
    [HttpPost("/Client/LinkXboxAccount")]
    public Task<IHttpActionResult> Client_LinkXboxAccount(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkXboxAccountRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.XboxId = b?.XboxToken ?? p.XboxId));
    }

    /// <summary>Unlinks an Xbox Live account.</summary>
    [HttpPost("/Client/UnlinkXboxAccount")]
    public Task<IHttpActionResult> Client_UnlinkXboxAccount(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkXboxAccountRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.XboxId = ""));
    }

    /// <summary>Links a Facebook account.</summary>
    [HttpPost("/Client/LinkFacebookAccount")]
    public Task<IHttpActionResult> Client_LinkFacebookAccount(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkFacebookAccountRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.FacebookId = b?.AccessToken ?? p.FacebookId));
    }

    /// <summary>Unlinks a Facebook account.</summary>
    [HttpPost("/Client/UnlinkFacebookAccount")]
    public Task<IHttpActionResult> Client_UnlinkFacebookAccount(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkFacebookAccountRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.FacebookId = ""));
    }

    /// <summary>Links a Facebook Instant Games account.</summary>
    [HttpPost("/Client/LinkFacebookInstantGamesId")]
    public Task<IHttpActionResult> Client_LinkFacebookInstantGamesId(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkFacebookInstantGamesIdRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.FacebookId = b?.FacebookInstantGamesSignature ?? p.FacebookId));
    }

    /// <summary>Unlinks a Facebook Instant Games account.</summary>
    [HttpPost("/Client/UnlinkFacebookInstantGamesId")]
    public Task<IHttpActionResult> Client_UnlinkFacebookInstantGamesId(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkFacebookInstantGamesIdRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.FacebookId = ""));
    }

    /// <summary>Links a Google account.</summary>
    [HttpPost("/Client/LinkGoogleAccount")]
    public Task<IHttpActionResult> Client_LinkGoogleAccount(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkGoogleAccountRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.GoogleId = b?.ServerAuthCode ?? p.GoogleId));
    }

    /// <summary>Unlinks a Google account.</summary>
    [HttpPost("/Client/UnlinkGoogleAccount")]
    public Task<IHttpActionResult> Client_UnlinkGoogleAccount(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkGoogleAccountRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.GoogleId = ""));
    }

    /// <summary>Links a Google Play Games Services account.</summary>
    [HttpPost("/Client/LinkGooglePlayGamesServicesAccount")]
    public Task<IHttpActionResult> Client_LinkGooglePlayGamesServicesAccount(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkGooglePlayGamesServicesAccountRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.GoogleAccountId = b?.ServerAuthCode ?? p.GoogleAccountId));
    }

    /// <summary>Unlinks a Google Play Games Services account.</summary>
    [HttpPost("/Client/UnlinkGooglePlayGamesServicesAccount")]
    public Task<IHttpActionResult> Client_UnlinkGooglePlayGamesServicesAccount(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkGooglePlayGamesServicesAccountRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.GoogleAccountId = ""));
    }

    /// <summary>Links a Game Center account.</summary>
    [HttpPost("/Client/LinkGameCenterAccount")]
    public Task<IHttpActionResult> Client_LinkGameCenterAccount(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkGameCenterAccountRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.GameCenterId = b?.GameCenterId ?? p.GameCenterId));
    }

    /// <summary>Unlinks a Game Center account.</summary>
    [HttpPost("/Client/UnlinkGameCenterAccount")]
    public Task<IHttpActionResult> Client_UnlinkGameCenterAccount(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkGameCenterAccountRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.GameCenterId = ""));
    }

    /// <summary>Links a Kongregate account.</summary>
    [HttpPost("/Client/LinkKongregate")]
    public Task<IHttpActionResult> Client_LinkKongregate(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkKongregateAccountRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.KongregateId = b?.KongregateId ?? p.KongregateId));
    }

    /// <summary>Unlinks a Kongregate account.</summary>
    [HttpPost("/Client/UnlinkKongregate")]
    public Task<IHttpActionResult> Client_UnlinkKongregate(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkKongregateAccountRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.KongregateId = ""));
    }

    /// <summary>Links a Twitch account.</summary>
    [HttpPost("/Client/LinkTwitch")]
    public Task<IHttpActionResult> Client_LinkTwitch(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkTwitchAccountRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.TwitchId = b?.AccessToken ?? p.TwitchId));
    }

    /// <summary>Unlinks a Twitch account.</summary>
    [HttpPost("/Client/UnlinkTwitch")]
    public Task<IHttpActionResult> Client_UnlinkTwitch(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkTwitchAccountRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.TwitchId = ""));
    }

    /// <summary>Links an Apple account.</summary>
    [HttpPost("/Client/LinkApple")]
    public Task<IHttpActionResult> Client_LinkApple(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkAppleRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.OpenIdSubject = b?.IdentityToken ?? p.OpenIdSubject));
    }

    /// <summary>Unlinks an Apple account.</summary>
    [HttpPost("/Client/UnlinkApple")]
    public Task<IHttpActionResult> Client_UnlinkApple(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkAppleRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.OpenIdSubject = ""));
    }

    /// <summary>Links a Battle.net account.</summary>
    [HttpPost("/Client/LinkBattleNetAccount")]
    public Task<IHttpActionResult> Client_LinkBattleNetAccount(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkBattleNetAccountRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.OpenIdSubject = b?.IdentityToken ?? p.OpenIdSubject));
    }

    /// <summary>Unlinks a Battle.net account.</summary>
    [HttpPost("/Client/UnlinkBattleNetAccount")]
    public Task<IHttpActionResult> Client_UnlinkBattleNetAccount(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkBattleNetAccountRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.OpenIdSubject = ""));
    }

    /// <summary>Links a Nintendo Service Account.</summary>
    [HttpPost("/Client/LinkNintendoServiceAccount")]
    public Task<IHttpActionResult> Client_LinkNintendoServiceAccount(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkNintendoServiceAccountRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.NintendoId = b?.IdentityToken ?? p.NintendoId));
    }

    /// <summary>Unlinks a Nintendo Service Account.</summary>
    [HttpPost("/Client/UnlinkNintendoServiceAccount")]
    public Task<IHttpActionResult> Client_UnlinkNintendoServiceAccount(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkNintendoServiceAccountRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.NintendoId = ""));
    }

    /// <summary>Links a Nintendo Switch device id.</summary>
    [HttpPost("/Client/LinkNintendoSwitchDeviceId")]
    public Task<IHttpActionResult> Client_LinkNintendoSwitchDeviceId(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkNintendoSwitchDeviceIdRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.NintendoId = b?.NintendoSwitchDeviceId ?? p.NintendoId));
    }

    /// <summary>Unlinks a Nintendo Switch device id.</summary>
    [HttpPost("/Client/UnlinkNintendoSwitchDeviceId")]
    public Task<IHttpActionResult> Client_UnlinkNintendoSwitchDeviceId(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkNintendoSwitchDeviceIdRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.NintendoId = ""));
    }

    /// <summary>Links an OpenID Connect account.</summary>
    [HttpPost("/Client/LinkOpenIdConnect")]
    public Task<IHttpActionResult> Client_LinkOpenIdConnect(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkOpenIdConnectRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.OpenIdSubject = b?.IdToken ?? p.OpenIdSubject));
    }

    /// <summary>Unlinks an OpenID Connect account.</summary>
    [HttpPost("/Client/UnlinkOpenIdConnect")]
    public Task<IHttpActionResult> Client_UnlinkOpenIdConnect(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkOpenIdConnectRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.OpenIdSubject = ""));
    }

    /// <summary>Links a PSN account.</summary>
    [HttpPost("/Client/LinkPSNAccount")]
    public Task<IHttpActionResult> Client_LinkPSNAccount(IHttpRequest request, IHttpResponse response)
    {
        var b = ParseRequest<LinkPSNAccountRequest>(request);
        return Task.FromResult(LinkAccount(request, p => p.PsnId = b?.AuthCode ?? p.PsnId));
    }

    /// <summary>Unlinks a PSN account.</summary>
    [HttpPost("/Client/UnlinkPSNAccount")]
    public Task<IHttpActionResult> Client_UnlinkPSNAccount(IHttpRequest request, IHttpResponse response)
    {
        ParseRequest<UnlinkPSNAccountRequest>(request);
        return Task.FromResult(UnlinkAccount(request, p => p.PsnId = ""));
    }

    /// <summary>Refreshes a PSN auth token.</summary>
    [HttpPost("/Client/RefreshPSNAuthToken")]
    public Task<IHttpActionResult> Client_RefreshPSNAuthToken(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<RefreshPSNAuthTokenRequest>(request);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /* ─── Add/remove username/password & contact email ─────────────────────── */

    /// <summary>Adds a username/password pair to an existing account.</summary>
    [HttpPost("/Client/AddUsernamePassword")]
    public Task<IHttpActionResult> Client_AddUsernamePassword(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<AddUsernamePasswordRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Username) || string.IsNullOrEmpty(body.Password))
            return Task.FromResult(Err("InvalidParams", 1000, "Username and Password are required"));
        var pid = PlayFabIdFromRequest(request);
        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var p = coll.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound));
        var uname = body.Username;
        var dupe = coll.FindOne(x => x.Username == uname);
        if (dupe != null && dupe.PlayFabId != pid)
            return Task.FromResult(Err("UsernameNotAvailable", 1014, "Username already taken"));
        p.Username     = body.Username;
        p.Email        = body.Email ?? p.Email;
        p.PasswordHash = HashPassword(body.Password);
        coll.Update(p);
        return Task.FromResult(Ok(new AddUsernamePasswordResult { Username = p.Username }));
    }

    /// <summary>Adds or updates the contact email on the player.</summary>
    [HttpPost("/Client/AddOrUpdateContactEmail")]
    public Task<IHttpActionResult> Client_AddOrUpdateContactEmail(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<AddOrUpdateContactEmailRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.EmailAddress))
            return Task.FromResult(Err("InvalidParams", 1000, "EmailAddress required"));
        var pid = PlayFabIdFromRequest(request);
        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var p = coll.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound));
        p.ContactEmail = body.EmailAddress;
        coll.Update(p);
        // also persist on dedicated collection
        var ec = Database.GetCollection<PlayfabContactEmailDbObject>(true)!;
        var existing = ec.FindOne(x => x.PlayFabId == pid);
        if (existing == null) ec.Insert(new PlayfabContactEmailDbObject { PlayFabId = pid, EmailAddress = body.EmailAddress });
        else { existing.EmailAddress = body.EmailAddress; ec.Update(existing); }
        return Task.FromResult(Ok(new AddOrUpdateContactEmailResult()));
    }

    /// <summary>Removes the contact email from the player.</summary>
    [HttpPost("/Client/RemoveContactEmail")]
    public Task<IHttpActionResult> Client_RemoveContactEmail(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<RemoveContactEmailRequest>(request);
        var pid = PlayFabIdFromRequest(request);
        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var p = coll.FindOne(x => x.PlayFabId == pid);
        if (p != null) { p.ContactEmail = ""; coll.Update(p); }
        Database.GetCollection<PlayfabContactEmailDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid);
        return Task.FromResult(Ok(new RemoveContactEmailResult()));
    }

    /// <summary>Sends an account-recovery email (logs to events).</summary>
    [HttpPost("/Client/SendAccountRecoveryEmail")]
    public Task<IHttpActionResult> Client_SendAccountRecoveryEmail(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<SendAccountRecoveryEmailRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Email))
            return Task.FromResult(Err("InvalidParams", 1000, "Email required"));
        Database.GetCollection<PlayfabEventDbObject>(true)!.Insert(new PlayfabEventDbObject
        {
            EventId        = NewId(),
            EventName      = "AccountRecoveryEmail",
            EventNamespace = "title.account",
            PayloadJson    = JsonSerializer.Serialize(new { body.Email }, JsonOpts),
            Timestamp      = NowUnixMs(),
        });
        return Task.FromResult(Ok(new SendAccountRecoveryEmailResult()));
    }

    /// <summary>Stores a player secret used for encrypted requests.</summary>
    [HttpPost("/Client/SetPlayerSecret")]
    public Task<IHttpActionResult> Client_SetPlayerSecret(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SetPlayerSecretRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        return Task.FromResult(Ok(new SetPlayerSecretResult()));
    }

    /* ─── Generic IDs ─────────────────────────────────────────────────────── */

    /// <summary>Adds a generic external service id mapping.</summary>
    [HttpPost("/Client/AddGenericID")]
    public Task<IHttpActionResult> Client_AddGenericID(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<AddGenericIDRequest>(request);
        if (body == null || body.GenericId == null || string.IsNullOrEmpty(body.GenericId.ServiceName))
            return Task.FromResult(Err("InvalidParams", 1000, "GenericId required"));
        var pid = PlayFabIdFromRequest(request);
        var coll = Database.GetCollection<PlayfabGenericIdDbObject>(true)!;
        var svc = body.GenericId.ServiceName;
        var existing = coll.FindOne(x => x.PlayFabId == pid && x.ServiceName == svc);
        if (existing == null)
            coll.Insert(new PlayfabGenericIdDbObject { PlayFabId = pid, ServiceName = body.GenericId.ServiceName, UserId = body.GenericId.UserId ?? "" });
        else { existing.UserId = body.GenericId.UserId ?? ""; coll.Update(existing); }
        return Task.FromResult(Ok(new AddGenericIDResult()));
    }

    /// <summary>Removes a generic external service id mapping.</summary>
    [HttpPost("/Client/RemoveGenericID")]
    public Task<IHttpActionResult> Client_RemoveGenericID(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RemoveGenericIDRequest>(request);
        if (body == null || body.GenericId == null || string.IsNullOrEmpty(body.GenericId.ServiceName))
            return Task.FromResult(Err("InvalidParams", 1000, "GenericId required"));
        var pid = PlayFabIdFromRequest(request);
        var svc = body.GenericId.ServiceName;
        Database.GetCollection<PlayfabGenericIdDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid && x.ServiceName == svc);
        return Task.FromResult(Ok(new RemoveGenericIDResult()));
    }

    /* ─── PlayFab id resolution helpers ───────────────────────────────────── */

    private IHttpActionResult ResolveIds<T>(IHttpRequest req, List<string>? ids, Expression<Func<PlayfabPlayerDbObject, bool>>? extra,
        Func<PlayfabPlayerDbObject, string?> fieldGetter, Func<List<T>, IHttpActionResult> mapper) where T : new()
    {
        if (!CheckAuth(req, AuthType.LoginSession)) return Unauthorized();
        return mapper(new List<T>());
    }

    private static (string PlayFabId, string Id) PlayFabPair(string pf, string id) => (pf, id);

    private List<TPair> ResolveByMatch<TPair>(List<string> ids, Func<string, Expression<Func<PlayfabPlayerDbObject, bool>>> matcher,
        Func<PlayfabPlayerDbObject, string, TPair> map)
    {
        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var result = new List<TPair>();
        if (ids == null) return result;
        foreach (var id in ids)
        {
            var p = coll.FindOne(matcher(id));
            if (p != null) result.Add(map(p, id));
        }
        return result;
    }

    /// <summary>Resolves Battle.net account ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromBattleNetAccountIds")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromBattleNetAccountIds(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromBattleNetAccountIdsRequest>(request);
        var pairs = ResolveByMatch(body?.BattleNetAccountIds ?? new(),
            id => p => p.OpenIdSubject == id,
            (p, id) => new BattleNetAccountPlayFabIdPair { BattleNetAccountId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromBattleNetAccountIdsResult { Data = pairs }));
    }

    /// <summary>Resolves Facebook ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromFacebookIDs")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromFacebookIDs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromFacebookIDsRequest>(request);
        var pairs = ResolveByMatch(body?.FacebookIDs ?? new(),
            id => p => p.FacebookId == id,
            (p, id) => new FacebookPlayFabIdPair { FacebookId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromFacebookIDsResult { Data = pairs }));
    }

    /// <summary>Resolves Facebook Instant Games ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromFacebookInstantGamesIds")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromFacebookInstantGamesIds(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromFacebookInstantGamesIdsRequest>(request);
        var pairs = ResolveByMatch(body?.FacebookInstantGamesIds ?? new(),
            id => p => p.FacebookId == id,
            (p, id) => new FacebookInstantGamesPlayFabIdPair { FacebookInstantGamesId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromFacebookInstantGamesIdsResult { Data = pairs }));
    }

    /// <summary>Resolves Game Center ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromGameCenterIDs")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromGameCenterIDs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromGameCenterIDsRequest>(request);
        var pairs = ResolveByMatch(body?.GameCenterIDs ?? new(),
            id => p => p.GameCenterId == id,
            (p, id) => new GameCenterPlayFabIdPair { GameCenterId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromGameCenterIDsResult { Data = pairs }));
    }

    /// <summary>Resolves generic external service ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromGenericIDs")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromGenericIDs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromGenericIDsRequest>(request);
        var data = new List<GenericPlayFabIdPair>();
        if (body?.GenericIDs != null)
        {
            var coll = Database.GetCollection<PlayfabGenericIdDbObject>(true)!;
            foreach (var g in body.GenericIDs)
            {
                var svc = g.ServiceName;
                var uid = g.UserId;
                var row = coll.FindOne(x => x.ServiceName == svc && x.UserId == uid);
                if (row != null) data.Add(new GenericPlayFabIdPair { GenericId = new GenericServiceId { ServiceName = svc, UserId = uid }, PlayFabId = row.PlayFabId });
            }
        }
        return Task.FromResult(Ok(new GetPlayFabIDsFromGenericIDsResult { Data = data }));
    }

    /// <summary>Resolves Google ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromGoogleIDs")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromGoogleIDs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromGoogleIDsRequest>(request);
        var pairs = ResolveByMatch(body?.GoogleIDs ?? new(),
            id => p => p.GoogleId == id,
            (p, id) => new GooglePlayFabIdPair { GoogleId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromGoogleIDsResult { Data = pairs }));
    }

    /// <summary>Resolves Google Play Games ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromGooglePlayGamesPlayerIDs")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromGooglePlayGamesPlayerIDs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromGooglePlayGamesPlayerIDsRequest>(request);
        var pairs = ResolveByMatch(body?.GooglePlayGamesPlayerIDs ?? new(),
            id => p => p.GoogleAccountId == id,
            (p, id) => new GooglePlayGamesPlayFabIdPair { GooglePlayGamesPlayerId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromGooglePlayGamesPlayerIDsResult { Data = pairs }));
    }

    /// <summary>Resolves Kongregate ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromKongregateIDs")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromKongregateIDs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromKongregateIDsRequest>(request);
        var pairs = ResolveByMatch(body?.KongregateIDs ?? new(),
            id => p => p.KongregateId == id,
            (p, id) => new KongregatePlayFabIdPair { KongregateId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromKongregateIDsResult { Data = pairs }));
    }

    /// <summary>Resolves Nintendo Service Account ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromNintendoServiceAccountIds")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromNintendoServiceAccountIds(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromNintendoServiceAccountIdsRequest>(request);
        var pairs = ResolveByMatch(body?.NintendoAccountIds ?? new(),
            id => p => p.NintendoId == id,
            (p, id) => new NintendoServiceAccountPlayFabIdPair { NintendoServiceAccountId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromNintendoServiceAccountIdsResult { Data = pairs }));
    }

    /// <summary>Resolves Nintendo Switch device ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromNintendoSwitchDeviceIds")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromNintendoSwitchDeviceIds(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromNintendoSwitchDeviceIdsRequest>(request);
        var pairs = ResolveByMatch(body?.NintendoSwitchDeviceIds ?? new(),
            id => p => p.NintendoId == id,
            (p, id) => new NintendoSwitchPlayFabIdPair { NintendoSwitchDeviceId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromNintendoSwitchDeviceIdsResult { Data = pairs }));
    }

    /// <summary>Resolves OpenID Connect subject identifiers to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromOpenIdSubjectIdentifiers")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromOpenIdSubjectIdentifiers(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<GetPlayFabIDsFromOpenIdsRequest>(request);
        return Task.FromResult(Ok(new GetPlayFabIDsFromOpenIdsResult { Data = new List<OpenIdSubjectIdentifierPlayFabIdPair>() }));
    }

    /// <summary>Resolves PSN account ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromPSNAccountIDs")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromPSNAccountIDs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromPSNAccountIDsRequest>(request);
        var pairs = ResolveByMatch(body?.PSNAccountIDs ?? new(),
            id => p => p.PsnId == id,
            (p, id) => new PSNAccountPlayFabIdPair { PSNAccountId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromPSNAccountIDsResult { Data = pairs }));
    }

    /// <summary>Resolves PSN online ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromPSNOnlineIDs")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromPSNOnlineIDs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromPSNOnlineIDsRequest>(request);
        var pairs = ResolveByMatch(body?.PSNOnlineIDs ?? new(),
            id => p => p.PsnId == id,
            (p, id) => new PSNOnlinePlayFabIdPair { PSNOnlineId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromPSNOnlineIDsResult { Data = pairs }));
    }

    /// <summary>Resolves Steam ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromSteamIDs")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromSteamIDs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromSteamIDsRequest>(request);
        var pairs = ResolveByMatch(body?.SteamStringIDs ?? new(),
            id => p => p.SteamId == id,
            (p, id) => new SteamPlayFabIdPair { SteamStringId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromSteamIDsResult { Data = pairs }));
    }

    /// <summary>Resolves Steam display names to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromSteamNames")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromSteamNames(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromSteamNamesRequest>(request);
        var pairs = ResolveByMatch(body?.SteamNames ?? new(),
            name => p => p.DisplayName == name,
            (p, name) => new SteamNamePlayFabIdPair { SteamName = name, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromSteamNamesResult { Data = pairs }));
    }

    /// <summary>Resolves Twitch ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromTwitchIDs")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromTwitchIDs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromTwitchIDsRequest>(request);
        var pairs = ResolveByMatch(body?.TwitchIds ?? new(),
            id => p => p.TwitchId == id,
            (p, id) => new TwitchPlayFabIdPair { TwitchId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromTwitchIDsResult { Data = pairs }));
    }

    /// <summary>Resolves Xbox Live ids to PlayFab ids.</summary>
    [HttpPost("/Client/GetPlayFabIDsFromXboxLiveIDs")]
    public Task<IHttpActionResult> Client_GetPlayFabIDsFromXboxLiveIDs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayFabIDsFromXboxLiveIDsRequest>(request);
        var pairs = ResolveByMatch(body?.XboxLiveAccountIDs ?? new(),
            id => p => p.XboxId == id,
            (p, id) => new XboxLiveAccountPlayFabIdPair { XboxLiveAccountId = id, PlayFabId = p.PlayFabId });
        return Task.FromResult(Ok(new GetPlayFabIDsFromXboxLiveIDsResult { Data = pairs }));
    }

    /* ─── Profile updates ─────────────────────────────────────────────────── */

    /// <summary>Updates the player's avatar URL.</summary>
    [HttpPost("/Client/UpdateAvatarUrl")]
    public Task<IHttpActionResult> Client_UpdateAvatarUrl(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateAvatarUrlRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var pid = PlayFabIdFromRequest(request);
        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var p = coll.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound));
        p.AvatarUrl = body.ImageUrl ?? "";
        coll.Update(p);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Updates the player's display name.</summary>
    [HttpPost("/Client/UpdateUserTitleDisplayName")]
    public Task<IHttpActionResult> Client_UpdateUserTitleDisplayName(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateUserTitleDisplayNameRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.DisplayName))
            return Task.FromResult(Err("InvalidParams", 1000, "DisplayName required"));
        var pid = PlayFabIdFromRequest(request);
        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var p = coll.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound));
        p.DisplayName = body.DisplayName;
        coll.Update(p);
        return Task.FromResult(Ok(new UpdateUserTitleDisplayNameResult { DisplayName = body.DisplayName }));
    }

    /* ─── Push notifications, install attribution, device & player reports ─ */

    /// <summary>Records install attribution data.</summary>
    [HttpPost("/Client/AttributeInstall")]
    public Task<IHttpActionResult> Client_AttributeInstall(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<AttributeInstallRequest>(request);
        return Task.FromResult(Ok(new AttributeInstallResult()));
    }

    /// <summary>Reports device info as an event.</summary>
    [HttpPost("/Client/ReportDeviceInfo")]
    public Task<IHttpActionResult> Client_ReportDeviceInfo(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeviceInfoRequest>(request);
        if (body != null)
        {
            Database.GetCollection<PlayfabEventDbObject>(true)!.Insert(new PlayfabEventDbObject
            {
                EventId        = NewId(),
                EventName      = "DeviceInfo",
                EventNamespace = "title.client",
                EntityId       = PlayFabIdFromRequest(request),
                PayloadJson    = JsonSerializer.Serialize(body.Info ?? new(), JsonOpts),
                Timestamp      = NowUnixMs(),
            });
        }
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Reports another player for misconduct.</summary>
    [HttpPost("/Client/ReportPlayer")]
    public Task<IHttpActionResult> Client_ReportPlayer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ReportPlayerClientRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        Database.GetCollection<PlayfabEventDbObject>(true)!.Insert(new PlayfabEventDbObject
        {
            EventId        = NewId(),
            EventName      = "PlayerReported",
            EventNamespace = "title.moderation",
            EntityId       = body.ReporteeId,
            PayloadJson    = JsonSerializer.Serialize(new { Reporter = PlayFabIdFromRequest(request), body.Comment }, JsonOpts),
            Timestamp      = NowUnixMs(),
        });
        return Task.FromResult(Ok(new ReportPlayerClientResult { SubmissionsRemaining = 5 }));
    }

    /// <summary>Registers an Android push notification token.</summary>
    [HttpPost("/Client/AndroidDevicePushNotificationRegistration")]
    public Task<IHttpActionResult> Client_AndroidDevicePushNotificationRegistration(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<AndroidDevicePushNotificationRegistrationRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.DeviceToken))
            return Task.FromResult(Err("InvalidParams", 1000, "DeviceToken required"));
        var pid = PlayFabIdFromRequest(request);
        var coll = Database.GetCollection<PlayfabPushTokenDbObject>(true)!;
        var existing = coll.FindOne(x => x.PlayFabId == pid && x.Platform == "android");
        if (existing == null)
            coll.Insert(new PlayfabPushTokenDbObject
            {
                PlayFabId = pid, Platform = "android",
                DeviceToken = body.DeviceToken,
                SendPushNotificationConfirmation = body.SendPushNotificationConfirmation ?? false,
                ConfirmationMessage = body.ConfirmationMessage ?? "",
            });
        else { existing.DeviceToken = body.DeviceToken; coll.Update(existing); }
        return Task.FromResult(Ok(new AndroidDevicePushNotificationRegistrationResult()));
    }

    /// <summary>Registers an iOS push notification token.</summary>
    [HttpPost("/Client/RegisterForIOSPushNotification")]
    public Task<IHttpActionResult> Client_RegisterForIOSPushNotification(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RegisterForIOSPushNotificationRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.DeviceToken))
            return Task.FromResult(Err("InvalidParams", 1000, "DeviceToken required"));
        var pid = PlayFabIdFromRequest(request);
        var coll = Database.GetCollection<PlayfabPushTokenDbObject>(true)!;
        var existing = coll.FindOne(x => x.PlayFabId == pid && x.Platform == "ios");
        if (existing == null)
            coll.Insert(new PlayfabPushTokenDbObject
            {
                PlayFabId = pid, Platform = "ios",
                DeviceToken = body.DeviceToken,
                SendPushNotificationConfirmation = body.SendPushNotificationConfirmation ?? false,
                ConfirmationMessage = body.ConfirmationMessage ?? "",
            });
        else { existing.DeviceToken = body.DeviceToken; coll.Update(existing); }
        return Task.FromResult(Ok(new RegisterForIOSPushNotificationResult()));
    }
}
