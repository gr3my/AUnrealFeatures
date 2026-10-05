using AUnrealFeatures.Hosting.Configuration;
using AUnrealFeatures.Hosting.Database.Interfaces;
using AUnrealFeatures.Hosting.Http;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Playfab.Models;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AUnrealFeatures.Playfab;

public interface IPlayfabServer { }

public sealed partial class PlayfabServer : AstraHttpServer, IPlayfabServer
{
    private const string Hostname = "localhost";
    private const ushort Port = 9091;

    public static readonly string TITLE_ID = EnvironmentSettings.Get("PLAYFAB_TITLE_ID", "YOUR_TITLE_ID");
    private static readonly string DEV_SECRET = EnvironmentSettings.Get("PLAYFAB_DEV_SECRET", "CHANGE_ME_PLAYFAB_DEV_SECRET");
    private static readonly string ENTITY_KEY = LoadEntitySigningKey();

    public static IDatabase Database { get; set; } = null!;

    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public PlayfabServer() : base(Hostname, Port)
    {
        SeedDefaultTitleData();
        SeedDefaultCatalogItems();
    }

    /* ─── Auth helpers ─────────────────────────────────────────────────── */

    internal enum AuthType { None, PreLoginSession, LoginSession, DevSecretKey, EntityToken, TelemetryKey }

    private static string? Header(IHttpRequest req, string name) => req.GetHeaderValue(name);

    private static bool CheckAuth(IHttpRequest req, AuthType auth)
    {
        switch (auth)
        {
            case AuthType.None:
            case AuthType.PreLoginSession:
                return true;
            case AuthType.LoginSession:
                return ValidateSessionTicket(Header(req, "X-Authorization"));
            case AuthType.EntityToken:
                return ParseEntityToken(Header(req, "X-EntityToken") ?? string.Empty).ok;
            case AuthType.DevSecretKey:
                // Accept the dev secret via the standard X-SecretKey header OR via a
                // ?DevSecretKey= query string (fallback for proxies that strip custom headers).
                return IsConfiguredSecret(DEV_SECRET) &&
                       (FixedTimeEquals(Header(req, "X-SecretKey") ?? string.Empty, DEV_SECRET) ||
                        FixedTimeEquals(req.GetQueryParameter("DevSecretKey") ?? string.Empty, DEV_SECRET));
            case AuthType.TelemetryKey:
                return ValidateTelemetryKey(Header(req, "X-TelemetryKey"));
            default:
                return false;
        }
    }


    private static bool IsConfiguredSecret(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase) &&
        !value.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);

    private static string LoadEntitySigningKey()
    {
        var configured = EnvironmentSettings.Get("PLAYFAB_ENTITY_SECRET");
        if (IsConfiguredSecret(configured))
            return configured;

        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        Console.Error.WriteLine("[PlayFab] PLAYFAB_ENTITY_SECRET is not configured; using an ephemeral signing key for this process.");
        return Convert.ToBase64String(key);
    }

    private static bool ValidateSessionTicket(string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket) || Database == null)
            return false;

        var playFabId = PlayFabIdFromSessionTicket(ticket);
        if (string.IsNullOrEmpty(playFabId))
            return false;

        var player = Database.GetCollection<PlayfabPlayerDbObject>(true)!.FindOne(p => p.PlayFabId == playFabId);
        if (player == null || player.SessionExpiration < NowUnix())
            return false;

        return FixedTimeEquals(ticket, player.SessionTicket);
    }

    private static bool ValidateTelemetryKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || Database == null)
            return false;

        var configured = Database.GetCollection<PlayfabTelemetryKeyDbObject>(true)!
            .FindOne(k => k.TitleId == TITLE_ID && k.IsActive && k.KeyValue == key);
        return configured != null;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        if (left == null || right == null)
            return false;

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    /* ─── PlayFab response envelope ────────────────────────────────────── */

    private sealed class PfResponse<T>
    {
        public int code { get; set; }
        public string status { get; set; } = "OK";
        public T? data { get; set; }
    }

    private sealed class PfError
    {
        public int code { get; set; }
        public string status { get; set; } = "BadRequest";
        public string error { get; set; } = "";
        public int errorCode { get; set; }
        public string errorMessage { get; set; } = "";
        public Dictionary<string, List<string>>? errorDetails { get; set; }
    }

    private static IHttpActionResult Ok<T>(T data, HttpStatusCode code = HttpStatusCode.OK) =>
        Results.Configurable(code, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new PfResponse<T> { code = (int)code, status = "OK", data = data }, JsonOpts));

    private static IHttpActionResult OkEmpty() =>
        Results.Configurable(HttpStatusCode.OK, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new PfResponse<object> { code = 200, status = "OK", data = new { } }, JsonOpts));

    private static IHttpActionResult Err(string error, int errorCode, string message, HttpStatusCode httpStatus = HttpStatusCode.BadRequest) =>
        Results.Configurable(httpStatus, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new PfError
            {
                code = (int)httpStatus,
                status = httpStatus.ToString(),
                error = error,
                errorCode = errorCode,
                errorMessage = message
            }, JsonOpts));

    private static IHttpActionResult Unauthorized() =>
        Err("NotAuthenticated", 1074, "This API method does not allow anonymous access", HttpStatusCode.Unauthorized);

    /* ─── Body parsing ─────────────────────────────────────────────────── */

    private static T? ParseRequest<T>(IHttpRequest req) where T : class
    {
        try
        {
            var bytes = req.Body;
            if (bytes == null || bytes.Length == 0) return Activator.CreateInstance<T>();
            return JsonSerializer.Deserialize<T>(bytes, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    /* ─── Generic endpoint handler ─────────────────────────────────────── */

    /// <summary>
    /// Default handler for any endpoint without a custom implementation.
    /// Validates auth, deserializes the request, and returns an empty response of the expected type.
    /// Specific endpoints override this via partial methods or named handlers.
    /// </summary>
    private Task<IHttpActionResult> Handle<TReq, TRes>(IHttpRequest req, AuthType auth, Func<TReq, TRes>? logic = null)
        where TReq : class
        where TRes : class, new()
    {
        if (!CheckAuth(req, auth))
            return Task.FromResult(Unauthorized());

        var body = ParseRequest<TReq>(req) ?? Activator.CreateInstance<TReq>();
        TRes result;
        try { result = logic != null ? logic(body) : new TRes(); }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
        catch (Exception e)    { return Task.FromResult(Err("InternalServerError", 1110, e.Message, HttpStatusCode.InternalServerError)); }
        return Task.FromResult(Ok(result));
    }

    /* ─── Token / id helpers ───────────────────────────────────────────── */

    internal static string NewId() => Guid.NewGuid().ToString("N");
    internal static string NewPlayFabId() => NewId().Substring(0, 16).ToUpperInvariant();
    internal static long   NowUnix()  => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    internal static long   NowUnixMs()=> DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string Base64Url(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string GenerateJwt(object payload, string secret)
    {
        var header  = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var body    = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts));
        var signing = $"{header}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return $"{signing}.{Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(signing)))}";
    }

    internal static string IssueSessionTicket(string playFabId)
    {
        // Format: {playFabId}-{base64Random}-{titleId}
        var rand = new byte[16];
        RandomNumberGenerator.Fill(rand);
        return $"{playFabId}-{Base64Url(rand)}-{TITLE_ID}";
    }

    internal static string IssueEntityToken(string entityId, string entityType = "title_player_account") =>
        GenerateJwt(new
        {
            sub  = entityId,
            type = entityType,
            tid  = TITLE_ID,
            exp  = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
            iat  = NowUnix()
        }, ENTITY_KEY);
}

internal sealed class PlayfabFault : Exception
{
    public string Error;
    public int ErrorCode;
    public HttpStatusCode HttpStatus;
    public PlayfabFault(string error, int errorCode, string message, HttpStatusCode status = HttpStatusCode.BadRequest)
        : base(message) { Error = error; ErrorCode = errorCode; HttpStatus = status; }
}
