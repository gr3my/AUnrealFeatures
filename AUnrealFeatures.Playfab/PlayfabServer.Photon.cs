using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Database;
using AUnrealFeatures.Hosting.Configuration;
using AUnrealFeatures.Playfab.Models;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

/* ─── DB models ────────────────────────────────────────────────────────────── */

/// <summary>Persisted Photon room state for IsPersistent rooms (PathCreate Type=Load / PathClose Type=Save).</summary>
public sealed class PlayfabPhotonRoomStateDbObject : AstraDbObject
{
    public string AppId { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string Region { get; set; } = "";
    public string GameId { get; set; } = "";
    public string StateJson { get; set; } = "";
    public int ActorCount { get; set; }
    public long Saved { get; set; }
}

/// <summary>Photon webhook event log (every PathX call appended for debugging/replay).</summary>
public sealed class PlayfabPhotonWebhookLogDbObject : AstraDbObject
{
    public string AppId { get; set; } = "";
    public string GameId { get; set; } = "";
    public string Region { get; set; } = "";
    public string Handler { get; set; } = "";  // RoomCreated, RoomClosed, etc.
    public string Type { get; set; } = "";     // Create, Load, Save, Close, Join, Leave, Event, Game, Actor, BeforeJoin
    public string PlayFabId { get; set; } = "";
    public int ActorNr { get; set; }
    public string PayloadJson { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public bool Success { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>Issued Photon auth ticket — username + token tracked here for /photon/authenticate to verify.</summary>
public sealed class PlayfabPhotonTicketDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string Token { get; set; } = "";
    public string PhotonAppId { get; set; } = "";
    public long Issued { get; set; }
    public long Expires { get; set; }
    public bool Used { get; set; }
}

public sealed partial class PlayfabServer
{
    // Photon applications are opt-in. Public builds intentionally ship without IDs or secrets.
    internal sealed record PhotonApp(string Label, string AppId, string Secret);

    private static readonly PhotonApp[] PhotonApps = LoadPhotonApps();

    private static PhotonApp[] LoadPhotonApps()
    {
        var apps = new List<PhotonApp>(3);
        AddConfiguredPhotonApp(apps, "Realtime", "PHOTON_REALTIME_APP_ID", "PHOTON_REALTIME_SECRET");
        AddConfiguredPhotonApp(apps, "Chat", "PHOTON_CHAT_APP_ID", "PHOTON_CHAT_SECRET");
        AddConfiguredPhotonApp(apps, "Voice", "PHOTON_VOICE_APP_ID", "PHOTON_VOICE_SECRET");
        return apps.ToArray();
    }

    private static void AddConfiguredPhotonApp(List<PhotonApp> apps, string label, string appIdVariable, string secretVariable)
    {
        var appId = EnvironmentSettings.Get(appIdVariable);
        var secret = EnvironmentSettings.Get(secretVariable);

        if (IsPlaceholder(appId) || IsPlaceholder(secret))
            return;

        apps.Add(new PhotonApp(label, appId, secret));
    }

    private static bool IsPlaceholder(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownPhotonSecret(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
            return false;

        var candidate = Encoding.UTF8.GetBytes(secret);
        foreach (var app in PhotonApps)
        {
            var configured = Encoding.UTF8.GetBytes(app.Secret);
            if (candidate.Length == configured.Length && CryptographicOperations.FixedTimeEquals(candidate, configured))
                return true;
        }

        return false;
    }

    private static bool IsKnownPhotonAppId(string? appId) =>
        !string.IsNullOrWhiteSpace(appId) &&
        PhotonApps.Any(app => string.Equals(app.AppId, appId, StringComparison.OrdinalIgnoreCase));

    internal static string PHOTON_APP_ID => PhotonApps.FirstOrDefault()?.AppId ?? string.Empty;

    /* ─── /photon/authenticate ─────────────────────────────────────────────── */

    /// <summary>Photon Custom Authentication callback. Invoked server-to-server when a client connects with
    /// AuthValues containing username=PlayFabId and token=PhotonAuthToken. Validates the token, returns
    /// a PlayFab-compatible response with ResultCode 1 on success and an AuthCookie that subsequent
    /// webhooks will receive.</summary>
    [HttpPost("/")]
    public Task<IHttpActionResult> Photon_Authenticate(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult(PhotonAuthenticateImpl(request));

    /// <summary>GET form of /photon/authenticate — Photon often sends auth params as query string.</summary>
    [HttpGet("/")]
    public Task<IHttpActionResult> Photon_AuthenticateGet(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult(PhotonAuthenticateImpl(request));

    private IHttpActionResult PhotonAuthenticateImpl(IHttpRequest request)
    {
        var username = request.GetQueryParameter("username");
        var token = request.GetQueryParameter("token");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(token))
        {
            try
            {
                var form = request.FormBody;
                if (string.IsNullOrEmpty(username)) form.TryGetValue("username", out username!);
                if (string.IsNullOrEmpty(token)) form.TryGetValue("token", out token!);
            }
            catch { }
        }
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(token))
        {
            try
            {
                using var doc = JsonDocument.Parse(request.Body);
                if (string.IsNullOrEmpty(username) && doc.RootElement.TryGetProperty("username", out var u))
                    username = u.GetString();
                if (string.IsNullOrEmpty(token) && doc.RootElement.TryGetProperty("token", out var t))
                    token = t.GetString();
            }
            catch { }
        }

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(token))
            return PhotonAuthResponse(3, "Missing username or token");

        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;

        // Try JWT — sub is EntityId, look up player by EntityId then match PlayFabId
        var (subject, _, ok) = ParseEntityToken(token);
        if (ok)
        {
            var player = coll.FindOne(p => p.EntityId == subject);
            if (player != null && player.PlayFabId == username)
                return PhotonAuthResponse(1, "Success",
                    new { PlayFabId = player.PlayFabId, EntityId = player.EntityId, TitleId = TITLE_ID, Issued = NowUnix() },
                    player.DisplayName, player.PlayFabId);
        }

        // Fall back to one-shot ticket lookup
        var u2 = username;
        var t2 = token;
        var ticket = Database.GetCollection<PlayfabPhotonTicketDbObject>(true)!
                             .FindOne(x => x.PlayFabId == u2 && x.Token == t2);
        if (ticket == null || ticket.Expires < NowUnix())
            return PhotonAuthResponse(2, "Invalid or expired token");

        ticket.Used = true;
        Database.GetCollection<PlayfabPhotonTicketDbObject>(true)!.Update(ticket);

        var p2 = coll.FindOne(p => p.PlayFabId == u2);
        if (p2 == null) return PhotonAuthResponse(2, "Player not found");

        return PhotonAuthResponse(1, "Success",
            new { PlayFabId = p2.PlayFabId, EntityId = p2.EntityId, TitleId = TITLE_ID, Issued = NowUnix() },
            p2.DisplayName, p2.PlayFabId);
    }
    private IHttpActionResult PhotonAuthResponse(int resultCode, string message, object? authCookie = null, string? nickname = null, string? userId = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["ResultCode"] = resultCode,
            ["Message"]    = message,
        };
        if (authCookie != null) body["AuthCookie"] = authCookie;
        if (!string.IsNullOrEmpty(nickname)) body["Nickname"] = nickname;
        if (!string.IsNullOrEmpty(userId))   body["UserId"]   = userId;
        return Results.Configurable(HttpStatusCode.OK, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(body, JsonOpts));
    }

    /* ─── Photon webhook router ────────────────────────────────────────────── */

    /// <summary>CloudScript-style Photon webhook router. Photon's BaseUrl is configured to
    /// {host}/webhook/1/{revision}/{photonSecretKey} and the per-path webhook (PathCreate/PathClose/etc.)
    /// is appended as the {handler} segment, e.g. /webhook/1/prod/SECRET/RoomCreated.</summary>
    [HttpPost("/webhook/1/{revision}/{secret}/{handler}")]
    public Task<IHttpActionResult> Photon_CloudScriptWebhook(
        IHttpRequest request, IHttpResponse response,
        string revision, string secret, string handler)
        => Task.FromResult(DispatchPhotonWebhook(request, revision, secret, handler));

    /// <summary>Map a handler segment from the URL onto the canonical PlayFab handler name. Photon
    /// dashboards commonly have typos like "RoomEventRasied" or use legacy "PathX" naming — this
    /// keeps webhook events from being dropped over a config typo. Returns null if no match.</summary>
    private static string? CanonicalPhotonHandler(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        switch (raw)
        {
            case "RoomCreated":
            case "RoomClosed":
            case "RoomJoined":
            case "RoomLeft":
            case "RoomEventRaised":
            case "RoomPropertyUpdated":
            case "RoomBeforeJoin":
                return raw;
            // Common typos seen in the wild
            case "RoomEventRasied":      return "RoomEventRaised";
            case "RoomPropertiesUpdated":
            case "RoomPropertyChanged":  return "RoomPropertyUpdated";
            case "RoomCreate":           return "RoomCreated";
            case "RoomClose":            return "RoomClosed";
            case "RoomJoin":             return "RoomJoined";
            case "RoomBeforeJoined":     return "RoomBeforeJoin";
            // Legacy Photon BaseUrl shape (PathCreate/PathClose/...)
            case "PathCreate":           return "RoomCreated";
            case "PathClose":            return "RoomClosed";
            case "PathJoin":             return "RoomJoined";
            case "PathLeave":            return "RoomLeft";
            case "PathEvent":            return "RoomEventRaised";
            case "PathGameProperties":   return "RoomPropertyUpdated";
            default:
                foreach (var canonical in new[] { "RoomCreated", "RoomClosed", "RoomJoined", "RoomLeft", "RoomEventRaised", "RoomPropertyUpdated", "RoomBeforeJoin" })
                    if (string.Equals(canonical, raw, StringComparison.OrdinalIgnoreCase))
                        return canonical;
                return null;
        }
    }

    private IHttpActionResult DispatchPhotonWebhook(IHttpRequest request, string revision, string secret, string handler)
    {
        // Authenticate by Photon Secret Key
        if (!IsKnownPhotonSecret(secret))
            return PhotonWebhookResponse(handler, request, 1, "Not authorized", null);

        if (revision != "prod" && revision != "test")
            return PhotonWebhookResponse(handler, request, 1, $"Unknown revision '{revision}'", null);

        // Map dashboard-configured name (which may be a typo or legacy "PathX") onto the
        // canonical PlayFab handler name. Falls through to original if nothing matches so the
        // existing "Unknown handler" diagnostic still fires for genuinely-unknown names.
        handler = CanonicalPhotonHandler(handler) ?? handler;

        // Parse body once for all handlers
        Dictionary<string, JsonElement> body;
        try
        {
            body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Body, JsonOpts) ?? new();
        }
        catch
        {
            return PhotonWebhookResponse(handler, request, 1, "Malformed JSON body", null);
        }

        // For everything except RoomClosed, validate that UserId resolves to a real player
        if (handler != "RoomClosed")
        {
            if (!body.TryGetValue("UserId", out var uidEl) || uidEl.ValueKind != JsonValueKind.String)
                return PhotonWebhookResponse(handler, request, 1, "Missing Webhook Argument: UserId.", null);
            var uid = uidEl.GetString()!;
            var player = Database.GetCollection<PlayfabPlayerDbObject>(true)!
                                 .FindOne(p => p.PlayFabId == uid);
            if (player == null)
                return PhotonWebhookResponse(handler, request, 1, $"Unknown PlayFabId '{uid}'", null);
        }

        // First, try a CloudScript JS handler with this name. If found, its return value is used
        // verbatim as the webhook response (PlayFab integration uses CloudScript handlers for all
        // Photon hooks — RoomCreated, RoomJoined, RoomLeft, RoomEventRaised, RoomPropertyUpdated,
        // RoomClosed, plus any custom override the title set on the Photon dashboard).
        // Photon webhooks always run against the LATEST uploaded revision (not just the
        // published one) — that way scripts being iterated on take effect immediately for room
        // events without needing an explicit publish step. The prod/test path segment is kept
        // as a routing convention but doesn't gate revision selection.
        var pid = body.TryGetValue("UserId", out var uEl) && uEl.ValueKind == JsonValueKind.String ? uEl.GetString() ?? "" : "";
        var inv = InvokeCloudScript(handler, body, pid,
            specificRevision: null,
            useTestRevision: true);

        if (!inv.HandlerNotFound)
        {
            LogPhotonWebhook(handler, body, inv.ExecutionError == null ? "ok" : "fail");

            // Mirror the execution into the CloudScriptExecutionLog table so webhook-triggered
            // runs show up alongside ExecuteCloudScript / ExecuteFunction calls in the dashboard.
            try
            {
                Database.GetCollection<PlayfabCloudScriptExecutionLogDbObject>(true)!.Insert(
                    new PlayfabCloudScriptExecutionLogDbObject
                    {
                        ExecutionId          = NewId(),
                        PlayFabId            = pid,
                        FunctionName         = handler,
                        ResultJson           = inv.FunctionResult != null ? JsonSerializer.Serialize(inv.FunctionResult, JsonOpts) : "",
                        LogsJson             = JsonSerializer.Serialize(inv.Logs, JsonOpts),
                        ExecutionTimeSeconds = (long)inv.ExecutionTimeSeconds,
                        Timestamp            = NowUnix(),
                        Error                = inv.ExecutionError != null,
                    });
            }
            catch { /* never let log persistence break a webhook */ }

            // The handler is expected to return { ResultCode, Message?, ... }
            if (inv.FunctionResult != null)
                return Results.Configurable(HttpStatusCode.OK, "application/json",
                    JsonSerializer.SerializeToUtf8Bytes(inv.FunctionResult, JsonOpts));
            // Handler returned undefined/null — treat as success
            return PhotonWebhookSuccess(new Dictionary<string, object?> { ["ResultCode"] = 0 });
        }

        // No JS handler — fall back to the built-in C# handlers below
        IHttpActionResult result = handler switch
        {
            "RoomCreated"         => HandleRoomCreated(body),
            "RoomClosed"          => HandleRoomClosed(body),
            "RoomJoined"          => HandleRoomJoined(body),
            "RoomLeft"            => HandleRoomLeft(body),
            "RoomEventRaised"     => HandleRoomEventRaised(body),
            "RoomPropertyUpdated" => HandleRoomPropertyUpdated(body),
            _                     => PhotonWebhookResponse(handler, request, 1, $"Unknown handler '{handler}'", null),
        };

        LogPhotonWebhook(handler, body, result is { } r ? "ok" : "fail");
        return result;
    }

    private IHttpActionResult PhotonWebhookResponse(string handler, IHttpRequest req, int code, string msg, object? extra)
    {
        var body = new Dictionary<string, object?>
        {
            ["ResultCode"] = code,
            ["Message"]    = msg,
        };
        if (extra != null) body["Data"] = extra;
        return Results.Configurable(HttpStatusCode.OK, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(body, JsonOpts));
    }

    private void LogPhotonWebhook(string handler, Dictionary<string, JsonElement> body, string status)
    {
        try
        {
            string Get(string k) => body.TryGetValue(k, out var e) && e.ValueKind == JsonValueKind.String ? (e.GetString() ?? "") : "";
            int GetInt(string k)  => body.TryGetValue(k, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0;
            Database.GetCollection<PlayfabPhotonWebhookLogDbObject>(true)!.Insert(new PlayfabPhotonWebhookLogDbObject
            {
                AppId        = Get("AppId"),
                GameId       = Get("GameId"),
                Region       = Get("Region"),
                Handler      = handler,
                Type         = Get("Type"),
                PlayFabId    = Get("UserId"),
                ActorNr      = GetInt("ActorNr"),
                PayloadJson  = JsonSerializer.Serialize(body, JsonOpts),
                ResponseJson = status,
                Success      = status == "ok",
                Timestamp    = NowUnixMs(),
            });
        }
        catch { /* never let logging break a webhook */ }
    }

    /* ─── PathCreate handler (RoomCreated) ─────────────────────────────────── */

    private IHttpActionResult HandleRoomCreated(Dictionary<string, JsonElement> body)
    {
        var type   = body.GetValueOrDefault("Type").ValueKind == JsonValueKind.String ? body["Type"].GetString() : "";
        var appId  = body.GetValueOrDefault("AppId").ValueKind  == JsonValueKind.String ? body["AppId"].GetString()  ?? "" : "";
        var ver    = body.GetValueOrDefault("AppVersion").ValueKind == JsonValueKind.String ? body["AppVersion"].GetString() ?? "" : "";
        var region = body.GetValueOrDefault("Region").ValueKind == JsonValueKind.String ? body["Region"].GetString() ?? "" : "";
        var gameId = body.GetValueOrDefault("GameId").ValueKind == JsonValueKind.String ? body["GameId"].GetString() ?? "" : "";
        if (string.IsNullOrEmpty(gameId)) return PhotonWebhookResponse("RoomCreated", null!, 1, "Missing Webhook Argument: GameId.", null);

        var coll = Database.GetCollection<PlayfabPhotonRoomStateDbObject>(true)!;

        if (type == "Load")
        {
            // Async-join / rejoin / join-or-create needs us to load persisted state
            var existing = coll.FindOne(s => s.AppId == appId && s.Region == region && s.GameId == gameId);
            if (existing != null && !string.IsNullOrEmpty(existing.StateJson))
            {
                JsonElement state;
                try { state = JsonSerializer.Deserialize<JsonElement>(existing.StateJson); }
                catch { state = default; }
                return PhotonWebhookSuccess(new Dictionary<string, object?>
                {
                    ["ResultCode"] = 0,
                    ["Message"]    = "Room State successfully loaded",
                    ["State"]      = state.ValueKind == JsonValueKind.Undefined ? null : (object)state,
                });
            }

            // No state — allow creation if requested
            var createIfNotExists = body.GetValueOrDefault("CreateIfNotExists").ValueKind == JsonValueKind.True;
            if (createIfNotExists)
            {
                return PhotonWebhookSuccess(new Dictionary<string, object?>
                {
                    ["ResultCode"] = 0,
                    ["State"]      = "",
                });
            }
            return PhotonWebhookResponse("RoomCreated", null!, 3, $"Could not load the State, Reason=NotFound for GameId={gameId}", null);
        }

        // Type == "Create" (or absent): just acknowledge and seed an empty persistence row
        if (coll.FindOne(s => s.AppId == appId && s.Region == region && s.GameId == gameId) == null)
        {
            coll.Insert(new PlayfabPhotonRoomStateDbObject
            {
                AppId      = appId,
                AppVersion = ver,
                Region     = region,
                GameId     = gameId,
                Saved      = NowUnix(),
                ActorCount = 1,
            });
        }
        return PhotonWebhookSuccess(new Dictionary<string, object?> { ["ResultCode"] = 0 });
    }

    /* ─── PathClose handler (RoomClosed) ───────────────────────────────────── */

    private IHttpActionResult HandleRoomClosed(Dictionary<string, JsonElement> body)
    {
        var type   = body.GetValueOrDefault("Type").ValueKind   == JsonValueKind.String ? body["Type"].GetString()   ?? "" : "";
        var appId  = body.GetValueOrDefault("AppId").ValueKind  == JsonValueKind.String ? body["AppId"].GetString()  ?? "" : "";
        var region = body.GetValueOrDefault("Region").ValueKind == JsonValueKind.String ? body["Region"].GetString() ?? "" : "";
        var gameId = body.GetValueOrDefault("GameId").ValueKind == JsonValueKind.String ? body["GameId"].GetString() ?? "" : "";
        var actorCount = body.GetValueOrDefault("ActorCount").ValueKind == JsonValueKind.Number ? body["ActorCount"].GetInt32() : 0;

        var coll = Database.GetCollection<PlayfabPhotonRoomStateDbObject>(true)!;

        if (type == "Save" && body.TryGetValue("State", out var stateEl))
        {
            // Persist the state for later Load
            var json = JsonSerializer.Serialize(stateEl, JsonOpts);
            var existing = coll.FindOne(s => s.AppId == appId && s.Region == region && s.GameId == gameId);
            if (existing == null)
            {
                coll.Insert(new PlayfabPhotonRoomStateDbObject
                {
                    AppId      = appId,
                    Region     = region,
                    GameId     = gameId,
                    StateJson  = json,
                    ActorCount = actorCount,
                    Saved      = NowUnix(),
                });
            }
            else
            {
                existing.StateJson  = json;
                existing.ActorCount = actorCount;
                existing.Saved      = NowUnix();
                coll.Update(existing);
            }
        }
        else
        {
            // Type == "Close": room is gone forever — drop persisted state
            coll.DeleteMany(s => s.AppId == appId && s.Region == region && s.GameId == gameId);
        }

        return PhotonWebhookSuccess(new Dictionary<string, object?> { ["ResultCode"] = 0 });
    }

    /* ─── PathJoin / PathLeave / PathEvent / PathGameProperties (no state mutation by default) ─── */

    private IHttpActionResult HandleRoomJoined(Dictionary<string, JsonElement> body) =>
        PhotonWebhookSuccess(new Dictionary<string, object?> { ["ResultCode"] = 0 });

    private IHttpActionResult HandleRoomLeft(Dictionary<string, JsonElement> body) =>
        PhotonWebhookSuccess(new Dictionary<string, object?> { ["ResultCode"] = 0 });

    private IHttpActionResult HandleRoomEventRaised(Dictionary<string, JsonElement> body)
    {
        // Persist as a normal analytics event so the title can query it later
        try
        {
            var pid = body.TryGetValue("UserId", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? "" : "";
            var gid = body.TryGetValue("GameId", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() ?? "" : "";
            var evCode = body.TryGetValue("EvCode", out var ec) && ec.ValueKind == JsonValueKind.Number ? ec.GetInt32() : 0;
            Database.GetCollection<PlayfabEventDbObject>(true)!.Insert(new PlayfabEventDbObject
            {
                EventId        = NewId(),
                EventName      = $"Photon_RoomEventRaised_{evCode}",
                EventNamespace = "title.photon",
                EntityId       = pid,
                EntityType     = "title_player_account",
                PayloadJson    = JsonSerializer.Serialize(body, JsonOpts),
                Timestamp      = NowUnixMs(),
            });
        }
        catch { /* don't break the webhook */ }
        return PhotonWebhookSuccess(new Dictionary<string, object?> { ["ResultCode"] = 0 });
    }

    private IHttpActionResult HandleRoomPropertyUpdated(Dictionary<string, JsonElement> body) =>
        PhotonWebhookSuccess(new Dictionary<string, object?> { ["ResultCode"] = 0 });

    private IHttpActionResult PhotonWebhookSuccess(Dictionary<string, object?> body) =>
        Results.Configurable(HttpStatusCode.OK, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(body, JsonOpts));

    /* ─── PathBeforeJoin (PlayFab integration calls it RoomBeforeJoin if configured) ─── */

    /// <summary>Optional PathBeforeJoin handler — fires before an actor is created so the title can
    /// reject the join based on UserId/AuthCookie. Mapped to handler name "RoomBeforeJoin". Runs the
    /// CloudScript `handlers.RoomBeforeJoin` if defined; otherwise falls back to a built-in ban check.</summary>
    [HttpPost("/webhook/1/{revision}/{secret}/RoomBeforeJoin")]
    public Task<IHttpActionResult> Photon_RoomBeforeJoin(
        IHttpRequest request, IHttpResponse response,
        string revision, string secret)
    {
        if (!IsKnownPhotonSecret(secret))
            return Task.FromResult(PhotonWebhookResponse("RoomBeforeJoin", request, 1, "Not authorized", null));

        Dictionary<string, JsonElement> body;
        try { body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Body, JsonOpts) ?? new(); }
        catch { return Task.FromResult(PhotonWebhookResponse("RoomBeforeJoin", request, 1, "Malformed JSON body", null)); }

        // Validate the user
        var uid = body.TryGetValue("UserId", out var uidEl) && uidEl.ValueKind == JsonValueKind.String ? uidEl.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(uid)) return Task.FromResult(PhotonWebhookResponse("RoomBeforeJoin", request, 1, "Missing Webhook Argument: UserId.", null));

        var player = Database.GetCollection<PlayfabPlayerDbObject>(true)!
                             .FindOne(p => p.PlayFabId == uid);
        if (player == null) return Task.FromResult(PhotonWebhookResponse("RoomBeforeJoin", request, 2, $"Unknown PlayFabId '{uid}'", null));

        // Run the CloudScript handler first — its return value (e.g. { ResultCode, Message }) becomes
        // the webhook response, letting titles override the join decision from JS.
        var inv = InvokeCloudScript("RoomBeforeJoin", body, uid, specificRevision: null, useTestRevision: true);
        if (!inv.HandlerNotFound)
        {
            LogPhotonWebhook("RoomBeforeJoin", body, inv.ExecutionError == null ? "ok" : "fail");
            try
            {
                Database.GetCollection<PlayfabCloudScriptExecutionLogDbObject>(true)!.Insert(
                    new PlayfabCloudScriptExecutionLogDbObject
                    {
                        ExecutionId          = NewId(),
                        PlayFabId            = uid,
                        FunctionName         = "RoomBeforeJoin",
                        ResultJson           = inv.FunctionResult != null ? JsonSerializer.Serialize(inv.FunctionResult, JsonOpts) : "",
                        LogsJson             = JsonSerializer.Serialize(inv.Logs, JsonOpts),
                        ExecutionTimeSeconds = (long)inv.ExecutionTimeSeconds,
                        Timestamp            = NowUnix(),
                        Error                = inv.ExecutionError != null,
                    });
            }
            catch { }
            if (inv.FunctionResult != null)
                return Task.FromResult<IHttpActionResult>(Results.Configurable(HttpStatusCode.OK, "application/json",
                    JsonSerializer.SerializeToUtf8Bytes(inv.FunctionResult, JsonOpts)));
            return Task.FromResult(PhotonWebhookSuccess(new Dictionary<string, object?> { ["ResultCode"] = 0 }));
        }

        // No JS handler — fall back to the built-in ban check.
        if (player.IsBanned) return Task.FromResult(PhotonWebhookResponse("RoomBeforeJoin", request, 2, "Player is banned", null));

        LogPhotonWebhook("RoomBeforeJoin", body, "ok");
        return Task.FromResult(PhotonWebhookSuccess(new Dictionary<string, object?> { ["ResultCode"] = 0 }));
    }

    /* ─── WebRPC catch-all (CloudScript handlers invoked from OpWebRpc) ────── */

    /// <summary>Generic WebRPC dispatcher. Photon clients call OpWebRpc("FunctionName", ...) which
    /// causes Photon to POST to {BaseUrl}/{FunctionName}. We expose a CloudScript-style URL with the
    /// secret-key prefix to match the PlayFab integration: /webrpc/1/{revision}/{secret}/{functionName}.</summary>
    [HttpPost("/webrpc/1/{revision}/{secret}/{functionName}")]
    public Task<IHttpActionResult> Photon_WebRpc(
        IHttpRequest request, IHttpResponse response,
        string revision, string secret, string functionName)
    {
        if (!IsKnownPhotonSecret(secret))
            return Task.FromResult(PhotonWebhookResponse("WebRPC", request, 1, "Not authorized", null));

        Dictionary<string, JsonElement> body;
        try { body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Body, JsonOpts) ?? new(); }
        catch { return Task.FromResult(PhotonWebhookResponse("WebRPC", request, 1, "Malformed JSON body", null)); }

        // Look up a registered CloudScript function with this name and forward the call
        var name = functionName;
        var fn = Database.GetCollection<PlayfabCloudScriptFunctionDbObject>(true)!
                         .FindOne(f => f.TitleId == TITLE_ID && f.FunctionName == name);

        if (fn != null && fn.TriggerType == "HTTP" && !string.IsNullOrEmpty(fn.FunctionUrl))
        {
            // Forward synchronously
            try
            {
                using var http    = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var payload       = JsonSerializer.Serialize(new { FunctionArgument = body, FunctionName = name }, JsonOpts);
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var resp    = http.PostAsync(fn.FunctionUrl, content).GetAwaiter().GetResult();
                var text          = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return Task.FromResult(Results.Configurable(HttpStatusCode.OK, "application/json", Encoding.UTF8.GetBytes(text)));
            }
            catch (Exception e)
            {
                return Task.FromResult(PhotonWebhookResponse("WebRPC", request, 1, $"WebRPC handler '{name}' failed: {e.Message}", null));
            }
        }

        // No HTTP-triggered function registered — fall back to a CloudScript JS handler with
        // the same name. WebRPC calls like OpWebRpc("ReturnCurrentVersion", ...) will then
        // execute `handlers.ReturnCurrentVersion` from the uploaded source.
        var pid = body.TryGetValue("UserId", out var uEl) && uEl.ValueKind == JsonValueKind.String ? uEl.GetString() ?? "" : "";
        var inv = InvokeCloudScript(name, body, pid, specificRevision: null, useTestRevision: true);
        if (!inv.HandlerNotFound)
        {
            LogPhotonWebhook($"WebRPC.{name}", body, inv.ExecutionError == null ? "ok" : "fail");
            try
            {
                Database.GetCollection<PlayfabCloudScriptExecutionLogDbObject>(true)!.Insert(
                    new PlayfabCloudScriptExecutionLogDbObject
                    {
                        ExecutionId          = NewId(),
                        PlayFabId            = pid,
                        FunctionName         = name,
                        ResultJson           = inv.FunctionResult != null ? JsonSerializer.Serialize(inv.FunctionResult, JsonOpts) : "",
                        LogsJson             = JsonSerializer.Serialize(inv.Logs, JsonOpts),
                        ExecutionTimeSeconds = (long)inv.ExecutionTimeSeconds,
                        Timestamp            = NowUnix(),
                        Error                = inv.ExecutionError != null,
                    });
            }
            catch { }
            if (inv.FunctionResult != null)
                return Task.FromResult<IHttpActionResult>(Results.Configurable(HttpStatusCode.OK, "application/json",
                    JsonSerializer.SerializeToUtf8Bytes(inv.FunctionResult, JsonOpts)));
            return Task.FromResult(PhotonWebhookSuccess(new Dictionary<string, object?> { ["ResultCode"] = 0 }));
        }

        // No registered handler and no JS handler — log and ack with empty payload
        LogPhotonWebhook($"WebRPC.{name}", body, "ok");
        return Task.FromResult(PhotonWebhookSuccess(new Dictionary<string, object?>
        {
            ["ResultCode"] = 0,
            ["Message"]    = $"WebRPC '{name}' acknowledged (no handler registered)",
        }));
    }
}
