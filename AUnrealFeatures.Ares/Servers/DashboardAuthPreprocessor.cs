using AUnrealFeatures.Hosting.Configuration;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Preprocessors;
using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AUnrealFeatures.Ares.Servers
{
    /// <summary>Validates optional dashboard API authentication.</summary>
    public sealed class DashboardAuthPreprocessor : IHttpRequestPreprocessor
    {
        public static readonly bool Enforce = EnvironmentSettings.GetBoolean("DASHBOARD_AUTH_ENFORCE", false);

        // These must match what the /auth/meta callback uses to MINT the session cookie.
        public const string SessionCookieName = "aunreal_dashboard_session";
        static readonly string SessionSecret = EnvironmentSettings.Get("DASHBOARD_SESSION_SECRET", "CHANGE_ME_DASHBOARD_SESSION_SECRET");
        static readonly string DashboardAdminKey = EnvironmentSettings.Get("DASHBOARD_ADMIN_KEY", "CHANGE_ME_DASHBOARD_ADMIN_KEY");

        public Task<HttpPreprocessorContainer> TryPreprocessRequest(IHttpRequest request, IHttpResponse response)
        {
            var path = request.Uri ?? "";
            bool guarded = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
            if (!guarded || request.Method.Method == "OPTIONS")
                return Ok();   // login page, SSO callback, HTML, CORS preflight -> always allow

            if (HasValidAdminKey(request) || HasValidSession(request))
                return Ok();

            if (!Enforce)
            {
                Console.WriteLine($"[DASH-AUTH] would-401 (log-only) {request.Method.Method} {path} from {request.Remote} — no session/key");
                return Ok();
            }
            Console.WriteLine($"[DASH-AUTH] 401 {request.Method.Method} {path} from {request.Remote} — no session/key");
            return Task.FromResult(new HttpPreprocessorContainer
            {
                result = HttpPreprocessorResult.FAIL | HttpPreprocessorResult.STOP_AFTER,
                actionResult = Results.Configurable(HttpStatusCode.Unauthorized, "application/json", "{\"error\":\"unauthorized\"}")
            });
        }

        static Task<HttpPreprocessorContainer> Ok()
            => Task.FromResult(new HttpPreprocessorContainer { result = HttpPreprocessorResult.OK });

        static bool HasValidAdminKey(IHttpRequest req)
        {
            var k = req.GetHeaderValue("x-dashboard-key") ?? req.GetHeaderValue("x-api-key") ?? "";
            return IsConfigured(DashboardAdminKey) && !string.IsNullOrEmpty(k) && CtEquals(k, DashboardAdminKey);
        }

        static bool HasValidSession(IHttpRequest req)
        {
            if (!IsConfigured(SessionSecret)) return false;
            var cookieHdr = req.GetHeaderValue("Cookie") ?? "";
            foreach (var part in cookieHdr.Split(';'))
            {
                var p = part.Trim();
                if (p.StartsWith(SessionCookieName + "=", StringComparison.Ordinal))
                    return ValidateJwt(p.Substring(SessionCookieName.Length + 1));
            }
            return false;
        }

        // session cookie = base64url(header).base64url(payload{sub,exp,...}).base64url(HMACSHA256(header.payload))
        static bool ValidateJwt(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return false;
                var signing = parts[0] + "." + parts[1];
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SessionSecret));
                var sig = B64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(signing)));
                if (!CtEquals(sig, parts[2])) return false;   // signature must verify
                var payloadJson = Encoding.UTF8.GetString(B64UrlDecode(parts[1]));
                using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("exp", out var e) && e.TryGetInt64(out var exp))
                    return exp > DateTimeOffset.UtcNow.ToUnixTimeSeconds();   // not expired
                return true;
            }
            catch { return false; }
        }

        static string B64Url(byte[] b) => Convert.ToBase64String(b).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        static byte[] B64UrlDecode(string s)
        {
            s = s.Replace("-", "+").Replace("_", "/");
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }

        static bool IsConfigured(string value) =>
            !string.IsNullOrWhiteSpace(value) &&
            !value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase);

        static bool CtEquals(string a, string b)
            => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }
}
