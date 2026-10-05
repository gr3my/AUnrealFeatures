using AUnrealFeatures.Hosting.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AUnrealFeatures.EOSSDK;

internal static class RtcHelper
{
    private static readonly string ApiKey = EnvironmentSettings.Get("RTC_API_KEY", "CHANGE_ME_RTC_API_KEY");
    private static readonly string ApiSecret = EnvironmentSettings.Get("RTC_API_SECRET", "CHANGE_ME_RTC_API_SECRET");
    internal static readonly string ServerUrl = EnvironmentSettings.Get("RTC_WS_URL", "ws://127.0.0.1:7880");

    internal static string RoomName(string lobbyId) => $"lobby-{lobbyId}";

    internal static string MakeToken(string roomName, string puid)
    {
        var header = Base64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var exp = DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds();
        var nbf = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeSeconds();
        var payload = Base64Url(JsonSerializer.Serialize(new
        {
            iss = ApiKey,
            sub = puid,
            jti = puid,
            exp,
            nbf,
            video = new { room = roomName, roomJoin = true, canPublish = true, canSubscribe = true }
        }));
        var sig = Base64Url(HmacSha256Bytes($"{header}.{payload}", ApiSecret));
        return $"{header}.{payload}.{sig}";
    }

    private static string Base64Url(string value) => Base64Url(Encoding.UTF8.GetBytes(value));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] HmacSha256Bytes(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }
}
