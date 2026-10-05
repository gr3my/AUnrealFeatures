using AUnrealFeatures.Hosting.Database.Interfaces;
using AUnrealFeatures.Hosting.Configuration;
using AUnrealFeatures.Hosting.Http;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Binding.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Ares.Models;
using AUnrealFeatures.GameSocket;
using AUnrealFeatures.EOSSDK;
using AUnrealFeatures.EOSSDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FuzzySharp;

namespace AUnrealFeatures.Ares.Servers
{
    public interface IStationDbServer { }
    public sealed class StationDbServer : AstraHttpServer, IStationDbServer
    {
        const string HOSTNAME = "localhost";
        const ushort PORT = 78;

        private static readonly string ApiSigningSecret = LoadApiSigningSecret();
        private static readonly string BoardUrl = EnvironmentSettings.Get("ARES_DEFAULT_BOARD_URL", "https://example.invalid/your-board.png");
        private static readonly string ServerMasterKey = EnvironmentSettings.Get("ARES_SERVER_MASTER_KEY");
        private static readonly string BuildVersion = EnvironmentSettings.Get("GAME_BUILD_VERSION", "1");
        private static readonly int BuildIdNum = EnvironmentSettings.GetInt32("GAME_BUILD_ID", 1);
        private static readonly bool NonceVerify = EnvironmentSettings.GetBoolean("META_NONCE_VERIFY", false);
        private static readonly bool NonceEnforce = EnvironmentSettings.GetBoolean("META_NONCE_ENFORCE", false);
        private static readonly bool NonceFailOpen = EnvironmentSettings.GetBoolean("META_NONCE_FAIL_OPEN", false);
        private static readonly string QuestAppId = EnvironmentSettings.Get("META_QUEST_APP_ID");
        private static readonly string QuestAppSecret = EnvironmentSettings.Get("META_QUEST_APP_SECRET");
        private static readonly string RiftAppId = EnvironmentSettings.Get("META_RIFT_APP_ID");
        private static readonly string RiftAppSecret = EnvironmentSettings.Get("META_RIFT_APP_SECRET");
        private static readonly string MetaNonceValidationUrl = EnvironmentSettings.Get("META_NONCE_VALIDATION_URL");

        public StationDbServer() : base(HOSTNAME, PORT)
        {
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static bool IsConfiguredSecret(string value)
            => !string.IsNullOrWhiteSpace(value)
               && !value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
               && !value.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);

        private static string LoadApiSigningSecret()
        {
            var configured = EnvironmentSettings.Get("ARES_API_SIGNING_SECRET");
            return IsConfiguredSecret(configured)
                ? configured
                : Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        private static bool SecureEquals(string? candidate, string configured)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !IsConfiguredSecret(configured)) return false;
            var left = Encoding.UTF8.GetBytes(candidate);
            var right = Encoding.UTF8.GetBytes(configured);
            return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
        }

        private static bool IsServerMasterKey(string? candidate) => SecureEquals(candidate, ServerMasterKey);

        // Registration can remain open for local development when no master key is configured.
        // Configure ARES_SERVER_MASTER_KEY in any shared or internet-facing deployment.
        private static bool IsOptionalServerAuthValid(IHttpRequest request)
            => !IsConfiguredSecret(ServerMasterKey) || IsServerMasterKey(request.GetHeaderValue("x-api-key"));

        private static bool IsMasterOrUserKey(IHttpRequest request, string userId)
        {
            var apiKey = request.GetHeaderValue("x-api-key", "");
            if (IsServerMasterKey(apiKey)) return true;
            if (string.IsNullOrWhiteSpace(apiKey)) return false;
            var keys = Program.Database.GetCollection<UserApiKeyDbObject>(true);
            return keys?.FindAll().Any(k => k.ApiKey == apiKey && k.UserId == userId) == true;
        }

        private static bool IsMasterOrDeploymentKey(IHttpRequest request, DeploymentDbObject deployment)
        {
            var apiKey = request.GetHeaderValue("x-api-key", "");
            return IsServerMasterKey(apiKey) || (deployment.ApiKeys?.Contains(apiKey) == true);
        }

        private static string GenerateApiKey(string ownerId, string keyType = "user")
        {
            var keyId = Guid.NewGuid().ToString();
            var createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
            var payload = new { key_id = keyId, key_type = keyType, owner_id = ownerId, created_at = createdAt };
            var header = Base64UrlEncode(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
            var body = Base64UrlEncode(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload));
            var signing = $"{header}.{body}";
            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(ApiSigningSecret));
            return $"{signing}.{Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(signing)))}";
        }

        private static string Base64UrlEncode(byte[] data)
            => Convert.ToBase64String(data).Replace("+", "-").Replace("/", "_").TrimEnd('=');

        private static bool GetBooleanQuery(IHttpRequest request, string name, bool fallback)
        {
            var value = request.GetQueryParameter(name);
            return bool.TryParse(value, out var parsed) ? parsed : fallback;
        }

        private static string GenerateId()
            => Guid.NewGuid().ToString("N")[..16];

        private static ServerDeploymentResponse DeploymentToResponse(DeploymentDbObject d)
            => new ServerDeploymentResponse
            {
                DeploymentId = d.DeploymentId,
                StationId = d.StationId,
                DeploymentName = d.DeploymentName,
                Region = d.Region,
                IpAddress = d.IpAddress,
                Version = d.Version,
                CreatedAt = d.CreatedAt,
                Online = d.Online,
                LastEventAt = d.LastEvent ?? DateTime.MinValue,
                PlayerCount = d.PlayerCount,
                Config = d.Config
            };

        private static StationResponse StationToResponse(StationDbObject s,
            IEnumerable<DeploymentDbObject>? deps, bool includeDeployments, bool includeConfig)
            => new StationResponse
            {
                StationId = s.StationId,
                StationName = s.StationName,
                CreatedAt = s.CreatedAt,
                Deployments = includeDeployments
                    ? deps?.Select(DeploymentToResponse).ToList()
                    : null,
                Config = includeConfig ? s.Config : null
            };

        private List<RoleResponse> ExpandRoles(IEnumerable<string> roleIds)
        {
            var roleCollection = Program.Database.GetCollection<RoleResponse>(true);
            return roleIds
                .Select(rid => roleCollection?.FindOne(r => r.RoleId == rid))
                .Where(r => r != null)
                .Select(r => r!)
                .ToList();
        }

        private static readonly string[] AllPermissions =
        {
            "admin",
            "station:create", "station:delete", "station:write", "station:read",
            "station_config:write", "station_config:read",
            "role:read", "role:write",
            "global:fleet:create", "global:fleet:delete", "fleet:read", "fleet:join", "fleet:write",
            "fleet_config:read", "fleet_config:write", "config_netvars:write", "custom_config:write",
            "record_entry:write",
            "server_login", "server_event:read", "server_event:write",
            "user:create", "user:delete", "global:user_data", "user_data:read", "user_login",
            "user_permissions:read", "user_roles:read", "user_roles:write",
            "user_ban:write", "user_ban_short:write", "user_ban:update", "user_ban:revoke",
            "user_mute", "user_kick", "user_warn",
            "global_voip", "dev_inputs",
            "key_holder", "key_holder_zone_a", "key_holder_zone_b", "key_holder_zone_c", "key_holder_zone_d",
            "color:red", "color:yellow", "color:green",
            "anonymous_mode",
            "fleet_report:read", "fleet_report:write", "self:fleet_report:read", "self:fleet_report:write"
        };

        private List<UserPermission> GetPermissionsForUser(UserDataResponse user)
        {
            var roleCollection = Program.Database.GetCollection<RoleResponse>(true);
            var perms = new List<UserPermission>();
            foreach (var roleId in user.Roles ?? new List<string>())
            {
                var role = roleCollection?.FindOne(r => r.RoleId == roleId);
                if (role?.Permissions == null) continue;
                if (role.Permissions.Contains("global:admin"))
                {
                    foreach (var perm in AllPermissions)
                        perms.Add(new UserPermission { StationId = "global", Permission = perm });
                    return perms;
                }
                foreach (var perm in role.Permissions)
                    perms.Add(new UserPermission { StationId = role.StationId, Permission = perm });
            }
            return perms;
        }

        // Default station roles. Keep these names stable if your client expects them.
        private static readonly (string Name, string[] Perms)[] DefaultStationRoles =
        {
            // Full explicit permission set (not the "global:admin" sentinel), so both the
            // login path and the /roles route return real, game-recognized tokens.
            ("Admin",         AllPermissions),
            ("Event Manager", new[]
            {
                "server_event:read", "server_event:write",
                "station:read", "station:write",
                "station_config:read", "station_config:write",
                "config_netvars:write", "custom_config:write",
                "user_kick", "user_mute", "user_warn", "user_roles:read"
            }),
            ("Helper",        new[] { "station:read", "user_kick", "user_mute", "user_warn" }),
        };
        // Default station config. Override these values through the environment or station config API.

        private static readonly Dictionary<string, string> DefaultStationConfig = new()
        {
            ["StationAnnouncement"]      = EnvironmentSettings.Get("GAME_STATION_ANNOUNCEMENT", "Welcome to Your Game"),
            ["BoardTextureUrl1"]         = BoardUrl,
            ["BoardTextureUrl2"]         = BoardUrl,
            ["BoardTextureUrl3"]         = BoardUrl,
            ["BoardTextureUrl4"]         = BoardUrl,
            ["BoardTextureUrl5"]         = BoardUrl,
            ["BoardTextureUrl6"]         = BoardUrl,
            ["SignPrimaryUrl"]           = BoardUrl,
            ["SignSecondaryUrl"]         = BoardUrl,
            ["StatusBoardMainUrl"]       = BoardUrl,
            ["StatusBoardSecondaryUrl"]  = BoardUrl,
            ["TeamLogosUrl"]             = BoardUrl,
        };
        // Keep provider calls bounded so authentication does not occupy request workers indefinitely.
        private static readonly System.Net.Http.HttpClient NonceHttp =
            new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        // Cache successful nonce validation for a limited period to reduce provider traffic.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _nonceOkCache = new();
        private const long NonceCacheTtlSec = 12 * 3600;

        // Returns 1 for valid, 0 for invalid, and -1 when the identity provider is unavailable.
        private static async Task<int> VerifyNonceWith(string accessToken, string userId, string nonce)
        {
            if (string.IsNullOrWhiteSpace(MetaNonceValidationUrl)) return -1;
            try
            {
                var form = new System.Net.Http.FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["access_token"] = accessToken,
                    ["nonce"]        = nonce,
                    ["user_id"]      = userId,
                });
                var resp = await NonceHttp.PostAsync(MetaNonceValidationUrl, form);
                var text = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(text);
                bool ok = doc.RootElement.TryGetProperty("is_valid", out var v) &&
                          (v.ValueKind == JsonValueKind.True ||
                           (v.ValueKind == JsonValueKind.String && v.GetString()?.ToLowerInvariant() == "true"));
                return ok ? 1 : 0;
            }
            catch { return -1; }   // unreachable / timeout / parse failure -> no definitive answer
        }

        // Try configured Quest credentials first, then Rift. Successful validation is cached.
        private static async Task<bool> VerifyNonceAny(string userId, string nonce)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(nonce)) return false;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_nonceOkCache.TryGetValue(userId, out var exp) && exp > now) return true;   // cache hit
            if (exp != 0 && exp <= now) _nonceOkCache.TryRemove(userId, out _);              // evict stale

            // Check Quest credentials first.
            var q = await VerifyNonceWith($"OC|{QuestAppId}|{QuestAppSecret}", userId, nonce);
            if (q == 1) { _nonceOkCache[userId] = now + NonceCacheTtlSec; return true; }
            if (q == -1)
            {
                Console.WriteLine($"[StationDb][LOGIN] nonce provider unavailable (quest) for '{userId}'");
                return NonceFailOpen;
            }

            // Quest said definitively "not valid" (graph is up) -> could be a Rift user; check Rift.
            var r = await VerifyNonceWith($"OC|{RiftAppId}|{RiftAppSecret}", userId, nonce);
            if (r == 1) { _nonceOkCache[userId] = now + NonceCacheTtlSec; return true; }
            if (r == -1)
            {
                Console.WriteLine($"[StationDb][LOGIN] nonce provider unavailable (rift) for '{userId}'");
                return NonceFailOpen;
            }
            return false;      // both apps definitively not-valid -> forged/absent nonce
        }

        // Idempotently ensures Admin / Event Manager / Helper exist for a station.
        private void EnsureStationRoles(string stationId)
        {
            var roleCollection = Program.Database.GetCollection<RoleResponse>(true);
            if (roleCollection == null) return;

            foreach (var (name, perms) in DefaultStationRoles)
            {
                if (roleCollection.FindOne(r => r.StationId == stationId && r.RoleName == name) != null)
                    continue;

                roleCollection.Insert(new RoleResponse
                {
                    RoleId = GenerateId(),
                    StationId = stationId,
                    RoleName = name,
                    RoleDescription = "",
                    Permissions = perms.ToList()
                });
            }
        }

        // ─── DEDICATED SERVER REGISTRATION ───────────────────────────────────────

        // POST /register_server — a dedicated server self-registers on spin-up.
        // Creates a unique station + online deployment + roles, and registers the
        // EOS matchmaking session so the game's server search finds it. Returns the
        // station_id the server should then use (for the /roles path, etc.).
        [HttpPost("/register_server")]
        public async Task<IHttpActionResult> RegisterServer(IHttpRequest request, IHttpResponse response)
        {
            if (!IsOptionalServerAuthValid(request))
                return Results.Ok(new RegisterServerResponse { Success = false });

            RegisterServerRequest? body;
            try { body = JsonSerializer.Deserialize<RegisterServerRequest>(request.Body); }
            catch { body = null; }
            if (body == null)
                return Results.Ok(new RegisterServerResponse { Success = false });

            var port       = string.IsNullOrWhiteSpace(body.Port) ? "7777" : body.Port.Trim();
            var maxPlayers = body.MaxPlayers > 0 ? body.MaxPlayers : 10;

            // Prefer the server-reported address, but if it is missing (or loopback while the
            // registration request itself came from a remote host) advertise the request origin.
            // This prevents remote clients being handed 127.0.0.1 for an otherwise healthy server.
            var ip = string.IsNullOrWhiteSpace(body.Ip) ? request.Remote.ToString() : body.Ip.Trim();
            if (IPAddress.TryParse(ip, out var parsedIp) && IPAddress.IsLoopback(parsedIp) &&
                !IPAddress.IsLoopback(request.Remote))
                ip = request.Remote.ToString();
            if (string.IsNullOrWhiteSpace(ip)) ip = "127.0.0.1";

            var buildVersion = string.IsNullOrWhiteSpace(body.Version) ? BuildVersion : body.Version.Trim();
            var buildId = body.BuildId.GetValueOrDefault();
            if (buildId <= 0)
            {
                var suffix = buildVersion.Split('_').LastOrDefault();
                if (!int.TryParse(suffix, out buildId) || buildId <= 0)
                    buildId = BuildIdNum;
            }

            // If the caller supplied a deployment id (allocator decided it pre-launch and passed it to
            // the game as -DashboardDeploymentId), register under THAT id and reuse its station on a
            // re-register (upsert) so we don't spawn a new station per boot. Otherwise mint fresh.
            var depCol = Program.Database.GetCollection<DeploymentDbObject>(true);
            var existingDep = !string.IsNullOrWhiteSpace(body.DeploymentId)
                ? depCol?.FindAll().FirstOrDefault(d =>
                    string.Equals(d.DeploymentId, body.DeploymentId, StringComparison.OrdinalIgnoreCase))
                : null;

            var deploymentId = !string.IsNullOrWhiteSpace(body.DeploymentId)
                ? body.DeploymentId!
                : Guid.NewGuid().ToString("N");
            var stationId    = existingDep?.StationId ?? GenerateId();
            var serverName   = string.IsNullOrWhiteSpace(body.ServerName) ? $"Server_{stationId[..6]}" : body.ServerName.Trim();

            // A name typed in the dashboard "Spin Up" modal wins over the DLL's default. GameSocket
            // stashed it against this deployment id at spin-up time. It stays sticky across re-registers
            // via the existing deployment row's name once the pending entry is consumed.
            if (GameSocketServer.PendingNames.TryRemove(deploymentId, out var dashName) && !string.IsNullOrWhiteSpace(dashName))
                serverName = dashName;
            else if (existingDep != null && !string.IsNullOrWhiteSpace(existingDep.DeploymentName))
                serverName = existingDep.DeploymentName!;

            var now = DateTime.Now;
            var stationCol0 = Program.Database.GetCollection<StationDbObject>(true);
            var stationRow = stationCol0?.FindAll().FirstOrDefault(s =>
                string.Equals(s.StationId, stationId, StringComparison.OrdinalIgnoreCase));
            if (stationRow == null)
            {
                stationCol0?.Insert(new StationDbObject
                {
                    StationId   = stationId,
                    StationName = serverName,
                    CreatedAt   = now,
                    Online      = true,
                    LastOnline  = now,
                    Config      = new Dictionary<string, string>()
                });
            }
            else
            {
                stationRow.StationName = serverName;
                stationRow.Online = true;
                stationRow.LastOnline = now;
                stationCol0?.Update(stationRow);
            }

            DeploymentDbObject depRow;
            if (existingDep == null)
            {
                depRow = new DeploymentDbObject
                {
                    DeploymentId   = deploymentId,
                    StationId      = stationId,
                    DeploymentName = serverName,
                    IpAddress      = $"{ip}:{port}",
                    Region         = string.IsNullOrWhiteSpace(body.Region) ? "NAE" : body.Region,
                    Version        = buildVersion,
                    CreatedAt      = now,
                    LastEvent      = now,
                    Online         = true,
                    PlayerCount    = 0,
                    Config         = new Dictionary<string, string>()
                };
                depCol?.Insert(depRow);
            }
            else
            {
                // IMPORTANT: mutate the existing LiteDB object instead of constructing a replacement.
                // AstraDbObject.Id is the LiteDB key; replacing the object gives it a new ObjectId and
                // Update() silently misses the existing row.
                depRow = existingDep;
                depRow.StationId      = stationId;
                depRow.DeploymentName = serverName;
                depRow.IpAddress      = $"{ip}:{port}";
                depRow.Region         = string.IsNullOrWhiteSpace(body.Region) ? (depRow.Region ?? "NAE") : body.Region;
                depRow.Version        = buildVersion;
                depRow.Online         = true;
                depRow.LastEvent      = now;
                depCol?.Update(depRow);
            }

            EnsureStationRoles(stationId);

            var eosSessionId = EosGatewayServer.GetSessionIdForDeployment(deploymentId) ?? Guid.NewGuid().ToString();
            EosGatewayServer.UpsertSession(new EosSessionInfo
            {
                Id            = eosSessionId,
                ServerName    = serverName,
                IpAddress     = ip,
                Port          = port,
                MaxPlayers    = maxPlayers,
                Bucket        = buildVersion,
                BuildId       = buildId,
                ImguiPort     = body.ImguiPort ?? "",
                StationId     = stationId,
                DeploymentId  = deploymentId,
                PublicPlayers = new List<string>()
            });

            Logger.Warning($"Dedicated server registered | station={stationId} deployment={deploymentId} {ip}:{port}");
            return Results.Ok(new RegisterServerResponse
            {
                Success      = true,
                StationId    = stationId,
                DeploymentId = deploymentId
            });
        }

        // Compatibility alias used by newer server injectors/clients. Keep the original route too.
        [HttpPost("/v1/server/register_server")]
        public Task<IHttpActionResult> RegisterServerV1(IHttpRequest request, IHttpResponse response)
            => RegisterServer(request, response);

        // POST /update_player_count — periodic heartbeat from a dedicated server reporting its REAL
        // connected-player count (netdriver ClientConnections). The displayed count comes from the EOS
        // session's PublicPlayers, which only clients mutate, so hard disconnects (no leave DELETE)
        // leave ghosts and the count stays stuck high. Reconcile the session down to the reported count
        // and mirror it onto the DB deployment row.
        [HttpPost("/update_player_count")]
        public async Task<IHttpActionResult> UpdatePlayerCount(IHttpRequest request, IHttpResponse response)
        {
            if (!IsOptionalServerAuthValid(request))
                return Results.Ok(new { success = false, error = "unauthorized" });

            UpdatePlayerCountRequest? body;
            try { body = JsonSerializer.Deserialize<UpdatePlayerCountRequest>(request.Body); }
            catch { body = null; }
            if (body == null || string.IsNullOrWhiteSpace(body.DeploymentId))
                return Results.Ok(new { success = false, error = "deployment_id required" });

            var count   = body.PlayerCount < 0 ? 0 : body.PlayerCount;
            var matched = EosGatewayServer.SetSessionPlayerCount(body.DeploymentId!, count);

            var depCol = Program.Database.GetCollection<DeploymentDbObject>(true);
            var dep    = depCol?.FindAll().FirstOrDefault(d => d.DeploymentId == body.DeploymentId);
            if (dep != null)
            {
                dep.PlayerCount = count;
                dep.LastEvent   = DateTime.Now;
                depCol!.Update(dep);
            }

            return Results.Ok(new { success = true, deployment_id = body.DeploymentId, player_count = count, session_matched = matched });
        }

        [HttpPost("/v1/server/update_player_count")]
        public Task<IHttpActionResult> UpdatePlayerCountV1(IHttpRequest request, IHttpResponse response)
            => UpdatePlayerCount(request, response);

        // POST /v1/server/admin/purge_stations — dev cleanup: wipe ALL stations + deployments +
        // roles (they accumulated one-per-boot before deployment ids were made stable). Gated by
        // the server master key (x-api-key). Destructive; returns the counts removed.
        [HttpPost("/v1/server/admin/purge_stations")]
        public Task<IHttpActionResult> PurgeStations(IHttpRequest request, IHttpResponse response)
        {
            if (!IsServerMasterKey(request.GetHeaderValue("x-api-key")))
                return Task.FromResult<IHttpActionResult>(Results.Ok(new SuccessBoolean { Success = false }));

            // DeleteAll() rather than DeleteMany(_ => true) — LiteDB's expression visitor NREs on a
            // constant-true predicate. Return a plain typed ack (anonymous types tripped the serializer).
            int st = Program.Database.GetCollection<StationDbObject>(true)?.DeleteAll()    ?? 0;
            int dp = Program.Database.GetCollection<DeploymentDbObject>(true)?.DeleteAll() ?? 0;
            int rl = Program.Database.GetCollection<RoleResponse>(true)?.DeleteAll()       ?? 0;
            // The DB rows above aren't what the GAME lists — that's driven by the in-memory EOS
            // matchmaking sessions. Clear those too or purged stations keep showing in-game.
            int es = EosGatewayServer.ClearSessions();
            Logger.Warning($"PURGE stations={st} deployments={dp} roles={rl} eos_sessions={es}");
            return Task.FromResult<IHttpActionResult>(Results.Ok(new SuccessBoolean { Success = true }));
        }

        // ─── USERS ───────────────────────────────────────────────────────────────

        // GET /users (deprecated v0) — returns raw array of users filtered by last_login
        [HttpGet("/users")]
        public async Task<IHttpActionResult> ListAllUsersV0(IHttpRequest request, IHttpResponse response)
        {
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            DateTime lastLoginTimeEpsilon = request.Queries.ContainsKey("last_login")
                ? DateTime.Parse(request.GetQueryParameter("last_login", ""))
                : DateTime.MinValue;

            return Results.Ok(userCollection?.FindAll()
                .Where(x => x.LastLogin >= lastLoginTimeEpsilon)
                .ToArray() ?? Array.Empty<UserDataResponse>());
        }

        // POST /users — create or update user
        [HttpPost("/users")]
        public async Task<IHttpActionResult> CreateOrUpdateUser(
            IHttpRequest request,
            IHttpResponse response,
            [FromBody] UserRequest serverRequest)
        {
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            if (userCollection == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            Logger.Warning(JsonSerializer.Serialize(serverRequest));
            var selectedUser = userCollection.FindOne(x => x.UserId == serverRequest.UserId);
            if (selectedUser == null)
            {
                userCollection.Insert(new UserDataResponse
                {
                    UserId = serverRequest.UserId,
                    Username = "User",
                    DiscordId = serverRequest.DiscordId,
                    Platform = serverRequest.Platform,
                    CreatedAt = DateTime.Now,
                    LastLogin = DateTime.MinValue,
                    Roles = new List<string>(),
                    Bans = new List<BanRequest>()
                });
                return Results.Ok(new SuccessBoolean { Success = true });
            }

            selectedUser.Username = serverRequest.Username;
            selectedUser.DiscordId = serverRequest.DiscordId;
            selectedUser.Platform = serverRequest.Platform;
            userCollection.Update(selectedUser);
            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // GET /v1/users — paginated, fuzzy search
        [HttpGet("v1/users")]
        public async Task<IHttpActionResult> ListAllUsersV1(IHttpRequest request, IHttpResponse response)
        {
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            int pageSize = int.Parse(request.GetQueryParameter("page_size", "100") ?? "100");
            int page = int.Parse(request.GetQueryParameter("page", "1") ?? "1");
            string searchString = request.GetQueryParameter("search_string", "") ?? "";
            string lastLogin = request.GetQueryParameter("last_login", "") ?? "";

            var users = userCollection?.FindAll().ToList() ?? new List<UserDataResponse>();

            if (!string.IsNullOrEmpty(lastLogin))
            {
                var loginThreshold = DateTime.Parse(lastLogin);
                users = users.Where(u => u.LastLogin >= loginThreshold).ToList();
            }

            List<UserDataResponse> filtered;
            if (!string.IsNullOrEmpty(searchString))
            {
                var matched = Process.ExtractTop(searchString, users.Select(x => x.Username))
                    .Select(x => x.Value)
                    .ToHashSet();
                filtered = users.Where(u => matched.Contains(u.Username)).ToList();
            }
            else
            {
                filtered = users;
            }

            return Results.Ok(
                PagedResponse<UserDataResponse>.Create<UsersResponsePage>(filtered, pageSize, page));
        }

        // DELETE /users/{user_id}
        [HttpDelete("/users/{user_id}")]
        public async Task<IHttpActionResult> DeleteUser(
            IHttpRequest request, IHttpResponse response, string user_id)
        {
            if (!IsMasterOrUserKey(request, user_id))
                return Results.Ok(new SuccessBoolean { Success = false });

            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindOne(u => u.UserId == user_id);
            if (user == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            userCollection!.Delete(user.Id);

            // remove api keys
            var keyCollection = Program.Database.GetCollection<UserApiKeyDbObject>(true);
            foreach (var key in keyCollection?.Find(k => k.UserId == user_id).ToList() ?? new())
                keyCollection!.Delete(key.Id);

            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // GET /users/{user_id} (deprecated v0) — returns list of StationPerms
        [HttpGet("/users/{user_id}")]
        public async Task<IHttpActionResult> GetUserV0(
            IHttpRequest request, IHttpResponse response, string user_id)
        {
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindOne(u => u.UserId == user_id);
            if (user == null)
                return Results.Ok(Array.Empty<StationPerm>());

            return Results.Ok(GetPermissionsForUser(user).ToArray());
        }

        // GET /v1/users/{user_id} — full user data with optional role/permission expansion
        [HttpGet("v1/users/{user_id}")]
        public async Task<IHttpActionResult> GetUserV1(
            IHttpRequest request, IHttpResponse response, string user_id)
        {
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindOne(u => u.UserId == user_id);
            if (user == null)
                return Results.Ok((object?)null);

            bool includeRoles = GetBooleanQuery(request, "include_roles", false);
            bool includePerms = GetBooleanQuery(request, "include_permissions", false);
            bool includeBans = GetBooleanQuery(request, "include_bans", false);

            List<RoleResponse>? roles = null;
            if (includeRoles || includePerms)
                roles = ExpandRoles(user.Roles ?? new List<string>());

            return Results.Ok(UserDataV1Response.FromStorage(user, roles, includeBans));
        }

        // POST /users/log_in_server — server-side login (requires root key)
        [HttpPost("/users/log_in_server")]
        public async Task<IHttpActionResult> UserServerLogin(IHttpRequest request, IHttpResponse response)
        {
            if (!IsServerMasterKey(request.GetHeaderValue("x-api-key")))
                return Results.Ok(new LogInServerResponse { ApiKey = "", User = null!, Permissions = new() });

            LogInServerRequest? body;
            try { body = JsonSerializer.Deserialize<LogInServerRequest>(request.Body); }
            catch { body = null; }

            if (body == null || string.IsNullOrEmpty(body.UserId))
                return Results.Ok(new LogInServerResponse { ApiKey = "", User = null!, Permissions = new() });

            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindOne(u => u.UserId == body.UserId);
            if (user == null)
            {
                user = new UserDataResponse
                {
                    UserId = body.UserId,
                    Username = body.Username,
                    Platform = body.Platform,
                    CreatedAt = DateTime.Now,
                    LastLogin = DateTime.Now,
                    Roles = new List<string>(),
                    Bans = new List<BanRequest>()
                };
                userCollection?.Insert(user);
            }
            else
            {
                user.Username = body.Username;
                user.Platform = body.Platform;
                user.LastLogin = DateTime.Now;
                userCollection?.Update(user);
            }

            var apiKey = GenerateApiKey(user.UserId, "server");
            var keyCollection = Program.Database.GetCollection<UserApiKeyDbObject>(true);
            keyCollection?.Insert(new UserApiKeyDbObject { UserId = user.UserId, ApiKey = apiKey });

            return Results.Ok(new LogInServerResponse
            {
                ApiKey = apiKey,
                User = user,
                Permissions = GetPermissionsForUser(user)
            });
        }

        // POST /users/log_in_with_key — login using existing api key from header
        [HttpPost("/users/log_in_with_key")]
        public async Task<IHttpActionResult> LogInWithKey(IHttpRequest request, IHttpResponse response)
        {
            var apiKey = request.GetHeaderValue("x-api-key", "");
            if (string.IsNullOrEmpty(apiKey))
                return Results.Ok((object?)null);

            // The dedicated server authenticates with the fixed launch-arg key (-DashboardApiKey=).
            // Accept it directly (no per-user key needed) so the deployment/station-config fetch runs.
            if (IsServerMasterKey(apiKey))
                return Results.Ok(new LogInServerResponse
                {
                    ApiKey = apiKey,
                    User = new UserDataResponse
                    {
                        UserId = "dedicated-server", Username = "DedicatedServer",
                        Roles = new List<string>(), Bans = new List<BanRequest>()
                    },
                    Permissions = AllPermissions
                        .Select(p => new UserPermission { StationId = "global", Permission = p })
                        .ToList()
                });

            var keyCollection = Program.Database.GetCollection<UserApiKeyDbObject>(true);
            var keyEntry = keyCollection?.FindOne(k => k.ApiKey == apiKey);
            if (keyEntry == null)
                return Results.Ok((object?)null);

            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindOne(u => u.UserId == keyEntry.UserId);
            if (user == null)
                return Results.Ok((object?)null);

            return Results.Ok(new LogInServerResponse
            {
                ApiKey = apiKey,
                User = user,
                Permissions = GetPermissionsForUser(user)
            });
        }

        // POST /users/log_in — player login with nonce
        [HttpPost("/users/log_in")]
        public async Task<IHttpActionResult> UserPlayerLogin(IHttpRequest request, IHttpResponse response)
        {
            ClientLoginRequest? body;
            try { body = JsonSerializer.Deserialize<ClientLoginRequest>(request.Body); }
            catch { body = null; }

            if (body == null)
                return Results.Ok(new ClientLoginResponse
                {
                    Success = false, ApiKey = "", OrgScopedId = "", ServerDeployments = new()
                });

            // Both Quest and Rift players hit this endpoint with a UserProof nonce. When NonceVerify is
            // on, verify it owns the claimed platform id (against both app cred sets) — a client that
            // never authenticated with Meta can't produce a valid nonce. Skipped entirely when off so
            // the live login path is untouched (no graph call).
            if (NonceVerify)
            {
                var nonceOk = await VerifyNonceAny(body.PlatformId, body.Nonce);
                Console.WriteLine($"[StationDb][LOGIN] platformId='{body.PlatformId}' nonceLen={(body.Nonce ?? "").Length} nonceOk={nonceOk} enforce={NonceEnforce}");
                if (NonceEnforce && !nonceOk)
                    return Results.Ok(new ClientLoginResponse
                    {
                        Success = false, ApiKey = "", OrgScopedId = "", ServerDeployments = new()
                    });
            }

            var userId = body.PlatformId;
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindOne(u => u.UserId == userId);

            if (user == null)
            {
                user = new UserDataResponse
                {
                    UserId = userId,
                    Username = body.Username,
                    Platform = body.Platform,
                    CreatedAt = DateTime.Now,
                    LastLogin = DateTime.Now,
                    Roles = new List<string>(),
                    Bans = new List<BanRequest>()
                };
                userCollection?.Insert(user);
            }
            else
            {
                user.Username = body.Username;
                user.Platform = body.Platform;
                user.LastLogin = DateTime.Now;
                userCollection?.Update(user);
            }

            var apiKey = GenerateApiKey(userId);
            var keyCollection = Program.Database.GetCollection<UserApiKeyDbObject>(true);
            keyCollection?.Insert(new UserApiKeyDbObject { UserId = userId, ApiKey = apiKey });

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
            var onlineDeployments = deploymentCollection?.FindAll()
                .Where(d => d.Online)
                .Select(d =>
                {
                    var station = stationCollection?.FindOne(s => s.StationId == d.StationId);
                    return new ClientLoginDeployment
                    {
                        DeploymentId = d.DeploymentId,
                        StationId = d.StationId,
                        StationName = station?.StationName ?? d.StationId,
                        DeploymentName = d.DeploymentName,
                        IpAddress = d.IpAddress,
                        IsWhitelist = false,
                        IsPublic = true,
                        IsJoinable = d.Online
                    };
                }).ToList() ?? new List<ClientLoginDeployment>();

            // merge in EOS matchmaking sessions as joinable deployments
            var eosIds = new HashSet<string>(
                onlineDeployments.Select(d => d.DeploymentId),
                StringComparer.OrdinalIgnoreCase);
            foreach (var s in GetEosSessions())
            {
                var canonicalDeploymentId = CanonicalDeploymentId(s);
                if (eosIds.Contains(canonicalDeploymentId)) continue;
                var station = stationCollection?.FindOne(st => st.StationId == (s.StationId ?? s.Bucket));
                onlineDeployments.Add(new ClientLoginDeployment
                {
                    DeploymentId   = canonicalDeploymentId,
                    StationId      = s.StationId ?? s.Bucket,
                    StationName    = station?.StationName ?? s.ServerName,
                    DeploymentName = s.ServerName,
                    IpAddress      = string.IsNullOrEmpty(s.Port) ? s.IpAddress : $"{s.IpAddress}:{s.Port}",
                    IsWhitelist    = false,
                    IsPublic       = true,
                    IsJoinable     = true
                });
            }

            return Results.Ok(new ClientLoginResponse
            {
                Success = true,
                ApiKey = apiKey,
                OrgScopedId = userId,
                ServerDeployments = onlineDeployments
            });
        }

        // POST /users/{user_id}/api_key — generate a new api key for user
        [HttpPost("/users/{user_id}/api_key")]
        public async Task<IHttpActionResult> CreateUserApiKey(
            IHttpRequest request, IHttpResponse response, string user_id)
        {
            if (!IsMasterOrUserKey(request, user_id))
                return Results.Ok(new ApiKeyResponse { ApiKey = "" });

            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindOne(u => u.UserId == user_id);
            if (user == null)
                return Results.Ok(new ApiKeyResponse { ApiKey = "" });

            var apiKey = GenerateApiKey(user_id);
            var keyCollection = Program.Database.GetCollection<UserApiKeyDbObject>(true);
            keyCollection?.Insert(new UserApiKeyDbObject { UserId = user_id, ApiKey = apiKey });

            return Results.Ok(new ApiKeyResponse { ApiKey = apiKey });
        }

        // ─── STATIONS ─────────────────────────────────────────────────────────────

        // POST /stations/create
        [HttpPost("/stations/create")]
        public async Task<IHttpActionResult> CreateStation(IHttpRequest request, IHttpResponse response)
        {
            CreateStationRequest? body;
            try { body = JsonSerializer.Deserialize<CreateStationRequest>(request.Body); }
            catch { body = null; }

            if (body == null || string.IsNullOrEmpty(body.StationName))
                return Results.Ok(new CreateStationResponse { StationId = "" });

            var stationId = string.IsNullOrEmpty(body.StationId) ? GenerateId() : body.StationId;
            var stationCollection = Program.Database.GetCollection<StationDbObject>(true);

            var existing = stationCollection?.FindOne(s => s.StationId == stationId);
            if (existing != null)
                return Results.Ok(new CreateStationResponse { StationId = stationId });

            stationCollection?.Insert(new StationDbObject
            {
                StationId = stationId,
                StationName = body.StationName,
                CreatedAt = DateTime.Now,
                Online = false,
                Config = new Dictionary<string, string>()
            });

            EnsureStationRoles(stationId);

            return Results.Ok(new CreateStationResponse { StationId = stationId });
        }

        // GET /stations (deprecated v0) — returns array of StationResponse
        [HttpGet("/stations")]
        public async Task<IHttpActionResult> FetchAllStationsV0(IHttpRequest request, IHttpResponse response)
        {
            bool includeConfig = GetBooleanQuery(request, "include_config", false);
            bool includeDeployments = GetBooleanQuery(request, "include_deployments", false);
            bool includeOffline = GetBooleanQuery(request, "include_offline_stations", true);

            var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);

            var stations = stationCollection?.FindAll().ToList() ?? new List<StationDbObject>();
            if (!includeOffline)
                stations = stations.Where(s => s.Online).ToList();

            var result = stations.Select(s =>
            {
                IEnumerable<DeploymentDbObject>? deps = includeDeployments
                    ? deploymentCollection?.Find(d => d.StationId == s.StationId)
                    : null;
                return StationToResponse(s, deps, includeDeployments, includeConfig);
            }).ToArray();

            return Results.Ok(result);
        }

        // GET /v1/stations — paginated stations
        [HttpGet("v1/stations")]
        public async Task<IHttpActionResult> FetchAllStationsV1(IHttpRequest request, IHttpResponse response)
        {
            bool includeConfig = GetBooleanQuery(request, "include_config", false);
            bool includeDeployments = GetBooleanQuery(request, "include_deployments", false);
            bool includeOffline = GetBooleanQuery(request, "include_offline_stations", true);
            int pageSize = int.Parse(request.GetQueryParameter("page_size", "100") ?? "100");
            int page = int.Parse(request.GetQueryParameter("page", "1") ?? "1");

            var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);

            var stations = stationCollection?.FindAll().ToList() ?? new List<StationDbObject>();
            if (!includeOffline)
                stations = stations.Where(s => s.Online).ToList();

            var responses = stations.Select(s =>
            {
                IEnumerable<DeploymentDbObject>? deps = includeDeployments
                    ? deploymentCollection?.Find(d => d.StationId == s.StationId)
                    : null;
                return StationToResponse(s, deps, includeDeployments, includeConfig);
            }).ToList();

            return Results.Ok(PagedResponse<StationResponse>.Create<StationResponsePage>(responses, pageSize, page));
        }

        // GET /stations/{station_id} — also handles /stations/player_count
        [HttpGet("/stations/{station_id}")]
        public async Task<IHttpActionResult> GetStation(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            // handle player_count route in case it lands here
            if (station_id == "player_count")
            {
                var depsCol = Program.Database.GetCollection<DeploymentDbObject>(true);
                int total = depsCol?.FindAll().Where(d => d.Online).Sum(d => d.PlayerCount) ?? 0;
                return Results.Ok(total);
            }

            bool includeConfig = GetBooleanQuery(request, "include_config", true);
            bool includeDeployments = GetBooleanQuery(request, "include_deployments", true);

            var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
            var station = stationCollection?.FindOne(s => s.StationId == station_id);
            if (station == null)
                return Results.Ok((object?)null);

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            IEnumerable<DeploymentDbObject>? deps = includeDeployments
                ? deploymentCollection?.Find(d => d.StationId == station_id)
                : null;

            return Results.Ok(StationToResponse(station, deps, includeDeployments, includeConfig));
        }

        // GET /stations/player_count — sum player counts of online deployments
        [HttpGet("/stations/player_count")]
        public async Task<IHttpActionResult> GetPlayerCount(IHttpRequest request, IHttpResponse response)
        {
            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            int total = deploymentCollection?.FindAll().Where(d => d.Online).Sum(d => d.PlayerCount) ?? 0;
            return Results.Ok(total);
        }

        // PATCH /stations/{station_id} — update station name
        [HttpPatch("/stations/{station_id}")]
        public async Task<IHttpActionResult> UpdateStation(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            UpdateStationRequest? body;
            try { body = JsonSerializer.Deserialize<UpdateStationRequest>(request.Body); }
            catch { body = null; }

            var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
            var station = stationCollection?.FindOne(s => s.StationId == station_id);
            if (station == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            if (body != null && !string.IsNullOrEmpty(body.StationName))
                station.StationName = body.StationName;

            stationCollection?.Update(station);
            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // DELETE /stations/{station_id}
        [HttpDelete("/stations/{station_id}")]
        public async Task<IHttpActionResult> DeleteStation(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
            var station = stationCollection?.FindOne(s => s.StationId == station_id);
            if (station == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            stationCollection!.Delete(station.Id);

            // delete all associated deployments and their EOS matchmaking entries
            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            foreach (var dep in deploymentCollection?.Find(d => d.StationId == station_id).ToList() ?? new())
            {
                EosGatewayServer.RemoveSessionByDeploymentId(dep.DeploymentId);
                deploymentCollection!.Delete(dep.Id);
            }
            // Also remove any manually-created/seeded EOS session linked directly to this station.
            foreach (var eos in GetEosSessions().Where(s => string.Equals(s.StationId, station_id, StringComparison.OrdinalIgnoreCase)))
                EosGatewayServer.RemoveSession(eos.Id);

            // delete all roles for station
            var roleCollection = Program.Database.GetCollection<RoleResponse>(true);
            foreach (var role in roleCollection?.Find(r => r.StationId == station_id).ToList() ?? new())
                roleCollection!.Delete(role.Id);

            // delete all events for station
            var eventCollection = Program.Database.GetCollection<StationEventDbObject>(true);
            foreach (var ev in eventCollection?.Find(e => e.StationId == station_id).ToList() ?? new())
                eventCollection!.Delete(ev.Id);

            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // GET /stations/{station_id}/config
        [HttpGet("/stations/{station_id}/config")]
        public async Task<IHttpActionResult> GetStationConfig(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
            var station = stationCollection?.FindOne(s => s.StationId == station_id);
            return Results.Ok(station?.Config ?? new Dictionary<string, string>());
        }

        // POST /stations/{station_id}/config — merge new keys into config
        [HttpPost("/stations/{station_id}/config")]
        public async Task<IHttpActionResult> SetStationConfig(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            Dictionary<string, string>? data;
            try { data = JsonSerializer.Deserialize<Dictionary<string, string>>(request.Body); }
            catch { data = null; }

            var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
            var station = stationCollection?.FindOne(s => s.StationId == station_id);
            if (station == null)
                return Results.Ok(new Dictionary<string, string>());

            if (data != null)
                foreach (var kv in data)
                    station.Config[kv.Key] = kv.Value;

            stationCollection?.Update(station);
            return Results.Ok(station.Config);
        }

        // DELETE /stations/{station_id}/config — remove keys from config
        [HttpDelete("/stations/{station_id}/config")]
        public async Task<IHttpActionResult> DeleteStationConfig(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            List<string>? keys;
            try { keys = JsonSerializer.Deserialize<List<string>>(request.Body); }
            catch { keys = null; }

            var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
            var station = stationCollection?.FindOne(s => s.StationId == station_id);
            if (station == null)
                return Results.Ok(new Dictionary<string, string>());

            if (keys != null)
                foreach (var k in keys)
                    station.Config.Remove(k);

            stationCollection?.Update(station);
            return Results.Ok(station.Config);
        }

        // GET /stations/{station_id}/online
        [HttpGet("/stations/{station_id}/online")]
        public async Task<IHttpActionResult> GetStationOnline(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            bool isOnline = deploymentCollection?.FindAll()
                .Any(d => d.StationId == station_id && d.Online) ?? false;
            return Results.Ok(isOnline);
        }

        // GET /stations/{station_id}/users (deprecated v0)
        [HttpGet("/stations/{station_id}/users")]
        public async Task<IHttpActionResult> GetAllUsersInStationV0(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            var roleCollection = Program.Database.GetCollection<RoleResponse>(true);
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);

            var stationRoleIds = roleCollection?.Find(r => r.StationId == station_id)
                .Select(r => r.RoleId).ToHashSet() ?? new HashSet<string>();

            var usersInStation = userCollection?.FindAll()
                .Where(u => (u.Roles ?? new List<string>()).Any(rid => stationRoleIds.Contains(rid)))
                .ToArray() ?? Array.Empty<UserDataResponse>();

            return Results.Ok(usersInStation);
        }

        // GET /v1/stations/{station_id}/users
        [HttpGet("v1/stations/{station_id}/users")]
        public async Task<IHttpActionResult> GetAllUsersInStationV1(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            int pageSize = int.Parse(request.GetQueryParameter("page_size", "100") ?? "100");
            int page = int.Parse(request.GetQueryParameter("page", "1") ?? "1");
            string searchString = request.GetQueryParameter("search_string", "") ?? "";

            var roleCollection = Program.Database.GetCollection<RoleResponse>(true);
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);

            var stationRoleIds = roleCollection?.Find(r => r.StationId == station_id)
                .Select(r => r.RoleId).ToHashSet() ?? new HashSet<string>();

            var users = userCollection?.FindAll()
                .Where(u => (u.Roles ?? new List<string>()).Any(rid => stationRoleIds.Contains(rid)))
                .ToList() ?? new List<UserDataResponse>();

            if (!string.IsNullOrEmpty(searchString))
            {
                var matched = Process.ExtractTop(searchString, users.Select(x => x.Username))
                    .Select(x => x.Value).ToHashSet();
                users = users.Where(u => matched.Contains(u.Username)).ToList();
            }

            return Results.Ok(PagedResponse<UserDataResponse>.Create<UsersResponsePage>(users, pageSize, page));
        }

        // Optional account IDs that should receive the Admin role on every station.
        private static readonly HashSet<string> GlobalAdmins = EnvironmentSettings.Get("ARES_GLOBAL_ADMIN_IDS")
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // GET /stations/{station_id}/users/{user_id}/roles
        [HttpGet("/stations/{station_id}/users/{user_id}/roles")]
        public async Task<IHttpActionResult> GetStationUserRoles(
            IHttpRequest request, IHttpResponse response, string station_id, string user_id)
        {
            EnsureStationRoles(station_id);

            var roleCollection = Program.Database.GetCollection<RoleResponse>(true);

            // Global admins get the station's Admin role automatically on any station.
            if (GlobalAdmins.Contains(user_id))
            {
                var adminRole = roleCollection?.FindAll()
                    .FirstOrDefault(r => r.StationId == station_id && r.RoleName == "Admin");
                return Results.Ok(new RolesResponse
                {
                    Roles = adminRole != null ? new List<RoleResponse> { adminRole } : new List<RoleResponse>()
                });
            }

            // FindAll().FirstOrDefault: captured-variable FindOne hangs this DB.
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindAll().FirstOrDefault(u => u.UserId == user_id);
            if (user == null)
                return Results.Ok(new RolesResponse { Roles = new List<RoleResponse>() });

            var roleList = roleCollection?.FindAll().ToList() ?? new List<RoleResponse>();
            var roles = (user.Roles ?? new List<string>())
                .Select(rid => roleList.FirstOrDefault(r => r.RoleId == rid && r.StationId == station_id))
                .Where(r => r != null)
                .Select(r => r!)
                .ToList();

            return Results.Ok(new RolesResponse { Roles = roles });
        }

        // POST /stations/{station_id}/users/{user_id}/roles/{role_id}
        [HttpPost("/stations/{station_id}/users/{user_id}/roles/{role_id}")]
        public async Task<IHttpActionResult> AddStationUserRole(
            IHttpRequest request, IHttpResponse response,
            string station_id, string user_id, string role_id)
        {
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindOne(u => u.UserId == user_id);
            if (user == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            user.Roles ??= new List<string>();
            if (!user.Roles.Contains(role_id))
            {
                user.Roles.Add(role_id);
                userCollection?.Update(user);
            }
            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // POST /stations/{station_id}/users/{user_id}/grant/{role_name}
        // Convenience: assign a role by NAME (Admin / Event Manager / Helper) — no id lookup.
        [HttpPost("/stations/{station_id}/users/{user_id}/grant/{role_name}")]
        public async Task<IHttpActionResult> GrantStationUserRoleByName(
            IHttpRequest request, IHttpResponse response,
            string station_id, string user_id, string role_name)
        {
            EnsureStationRoles(station_id);

            var roleCollection = Program.Database.GetCollection<RoleResponse>(true);
            // FindOne with captured-variable predicates hangs on this DB's query
            // translator; FindAll().FirstOrDefault evaluates in-memory and is safe.
            var role = roleCollection?.FindAll().FirstOrDefault(r => r.StationId == station_id && r.RoleName == role_name);
            if (role == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindAll().FirstOrDefault(u => u.UserId == user_id);
            if (user == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            user.Roles ??= new List<string>();
            if (!user.Roles.Contains(role.RoleId))
            {
                user.Roles.Add(role.RoleId);
                userCollection?.Update(user);
            }
            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // DELETE /stations/{station_id}/users/{user_id}/role/{role_id}
        [HttpDelete("/stations/{station_id}/users/{user_id}/role/{role_id}")]
        public async Task<IHttpActionResult> DeleteStationUserRole(
            IHttpRequest request, IHttpResponse response,
            string station_id, string user_id, string role_id)
        {
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindOne(u => u.UserId == user_id);
            if (user == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            bool removed = user.Roles?.Remove(role_id) ?? false;
            if (removed)
                userCollection?.Update(user);

            return Results.Ok(new SuccessBoolean { Success = removed });
        }

        // POST /v1/stations/{station_id}/user_roles — add role by username
        [HttpPost("v1/stations/{station_id}/user_roles")]
        public async Task<IHttpActionResult> AddStationUserRoleByName(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            UserRoleByNameRequest? body;
            try { body = JsonSerializer.Deserialize<UserRoleByNameRequest>(request.Body); }
            catch { body = null; }

            if (body == null)
                return Results.Ok(new UserRoleByNameResponse { Success = false, UserExists = false });

            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindOne(u => u.Username == body.Username);
            if (user == null)
                return Results.Ok(new UserRoleByNameResponse { Success = false, UserExists = false });

            user.Roles ??= new List<string>();
            if (!user.Roles.Contains(body.RoleId))
            {
                user.Roles.Add(body.RoleId);
                userCollection?.Update(user);
            }
            return Results.Ok(new UserRoleByNameResponse { Success = true, UserExists = true });
        }

        // GET /stations/{station_id}/roles
        [HttpGet("/stations/{station_id}/roles")]
        public async Task<IHttpActionResult> GetStationRoles(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            EnsureStationRoles(station_id);

            var roleCollection = Program.Database.GetCollection<RoleResponse>(true);
            var roles = roleCollection?.Find(r => r.StationId == station_id).ToList()
                ?? new List<RoleResponse>();
            return Results.Ok(new RolesResponse { Roles = roles });
        }

        // POST /stations/{station_id}/roles — create role
        [HttpPost("/stations/{station_id}/roles")]
        public async Task<IHttpActionResult> CreateStationRole(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            CreateRoleRequest? body;
            try { body = JsonSerializer.Deserialize<CreateRoleRequest>(request.Body); }
            catch { body = null; }

            if (body == null || string.IsNullOrWhiteSpace(body.RoleName))
                return Results.Ok((object?)null);

            var roleId = GenerateId();
            var role = new RoleResponse
            {
                RoleId = roleId,
                StationId = station_id,
                RoleName = body.RoleName,
                RoleDescription = body.RoleDescription ?? "",
                Permissions = new List<string>()
            };

            var roleCollection = Program.Database.GetCollection<RoleResponse>(true);
            roleCollection?.Insert(role);

            return Results.Ok(new RoleData
            {
                RoleId = roleId,
                StationId = station_id,
                RoleName = role.RoleName,
                RoleDescription = role.RoleDescription
            });
        }

        // DELETE /stations/{station_id}/roles/{role_id}
        [HttpDelete("/stations/{station_id}/roles/{role_id}")]
        public async Task<IHttpActionResult> DeleteStationRole(
            IHttpRequest request, IHttpResponse response, string station_id, string role_id)
        {
            var roleCollection = Program.Database.GetCollection<RoleResponse>(true);
            var role = roleCollection?.FindOne(r => r.RoleId == role_id && r.StationId == station_id);
            if (role == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            roleCollection!.Delete(role.Id);

            // remove role from all users who have it
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            foreach (var user in userCollection?.FindAll().Where(u => u.Roles != null && u.Roles.Contains(role_id)).ToList() ?? new())
            {
                user.Roles!.Remove(role_id);
                userCollection!.Update(user);
            }

            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // PATCH /stations/{station_id}/roles/{role_id} — update role name/description
        [HttpPatch("/stations/{station_id}/roles/{role_id}")]
        public async Task<IHttpActionResult> UpdateStationRole(
            IHttpRequest request, IHttpResponse response, string station_id, string role_id)
        {
            CreateRoleRequest? body;
            try { body = JsonSerializer.Deserialize<CreateRoleRequest>(request.Body); }
            catch { body = null; }

            var roleCollection = Program.Database.GetCollection<RoleResponse>(true);
            var role = roleCollection?.FindOne(r => r.RoleId == role_id && r.StationId == station_id);
            if (role == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            if (body != null)
            {
                if (!string.IsNullOrWhiteSpace(body.RoleName)) role.RoleName = body.RoleName;
                if (body.RoleDescription != null) role.RoleDescription = body.RoleDescription;
            }
            roleCollection?.Update(role);
            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // PATCH /stations/{station_id}/roles/{role_id}/permissions
        [HttpPatch("/stations/{station_id}/roles/{role_id}/permissions")]
        public async Task<IHttpActionResult> AssignPerms(
            IHttpRequest request, IHttpResponse response, string station_id, string role_id)
        {
            AssignPermsBody? body;
            try { body = JsonSerializer.Deserialize<AssignPermsBody>(request.Body); }
            catch { body = null; }

            var roleCollection = Program.Database.GetCollection<RoleResponse>(true);
            var role = roleCollection?.FindOne(r => r.RoleId == role_id && r.StationId == station_id);
            if (role == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            role.Permissions = body?.Permissions ?? role.Permissions;
            roleCollection?.Update(role);
            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // GET /v1/stations/{station_id}/roles/{role_id}/users
        [HttpGet("v1/stations/{station_id}/roles/{role_id}/users")]
        public async Task<IHttpActionResult> GetRoleUsers(
            IHttpRequest request, IHttpResponse response, string station_id, string role_id)
        {
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var users = userCollection?.FindAll()
                .Where(u => u.Roles != null && u.Roles.Contains(role_id))
                .ToList() ?? new List<UserDataResponse>();

            return Results.Ok(new UsersResponse { Users = users });
        }

        // GET /stations/{station_id}/server_events (deprecated v0)
        [HttpGet("/stations/{station_id}/server_events")]
        public async Task<IHttpActionResult> GetServerEventsV0(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            int pageSize = int.Parse(request.GetQueryParameter("page_size", "500") ?? "500");

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var eventCollection = Program.Database.GetCollection<ServerEventDbObject>(true);

            var depIds = deploymentCollection?.Find(d => d.StationId == station_id)
                .Select(d => d.DeploymentId).ToHashSet() ?? new HashSet<string>();

            var events = eventCollection?.FindAll()
                .Where(e => depIds.Contains(e.DeploymentId))
                .OrderByDescending(e => e.Timestamp)
                .Take(pageSize)
                .Select((e, i) => new ServerEventResponse
                {
                    Index = i + 1,
                    EventType = e.EventType,
                    DeploymentId = e.DeploymentId,
                    EventData = e.EventData,
                    Timestamp = e.Timestamp
                }).ToArray() ?? Array.Empty<ServerEventResponse>();

            return Results.Ok(events);
        }

        // GET /v1/stations/{station_id}/server_events
        [HttpGet("v1/stations/{station_id}/server_events")]
        public async Task<IHttpActionResult> GetServerEventsV1(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            int pageSize = int.Parse(request.GetQueryParameter("page_size", "500") ?? "500");
            int page = int.Parse(request.GetQueryParameter("page", "1") ?? "1");
            string? eventType = request.GetQueryParameter("event_type");

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var eventCollection = Program.Database.GetCollection<ServerEventDbObject>(true);

            var depIds = deploymentCollection?.Find(d => d.StationId == station_id)
                .Select(d => d.DeploymentId).ToHashSet() ?? new HashSet<string>();

            var query = eventCollection?.FindAll()
                .Where(e => depIds.Contains(e.DeploymentId)) ?? Enumerable.Empty<ServerEventDbObject>();

            if (!string.IsNullOrEmpty(eventType))
                query = query.Where(e => e.EventType == eventType);

            var allEvents = query.OrderByDescending(e => e.Timestamp).ToList();
            var mapped = allEvents.Select((e, i) => new ServerEventResponse
            {
                Index = i + 1,
                EventType = e.EventType,
                DeploymentId = e.DeploymentId,
                EventData = e.EventData,
                Timestamp = e.Timestamp
            }).ToList();

            return Results.Ok(PagedResponse<ServerEventResponse>.Create<ServerEventResponsePage>(mapped, pageSize, page));
        }

        // POST /v1/stations/{station_id}/users/{user_id}/ban
        [HttpPost("v1/stations/{station_id}/users/{user_id}/ban")]
        public async Task<IHttpActionResult> BanStationUser(
            IHttpRequest request, IHttpResponse response, string station_id, string user_id)
        {
            int duration = int.Parse(request.GetQueryParameter("duration", "0") ?? "0");

            BanRequest? body;
            try { body = JsonSerializer.Deserialize<BanRequest>(request.Body); }
            catch { body = null; }

            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindOne(u => u.UserId == user_id);
            if (user == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            user.Bans ??= new List<BanRequest>();
            user.Bans.Add(new BanRequest
            {
                UserId = user_id,
                StationId = station_id,
                Expiration = duration > 0 ? DateTime.Now.AddSeconds(duration) : DateTime.MaxValue,
                Reason = body?.Reason ?? "",
                Revoked = false
            });
            userCollection?.Update(user);
            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // PATCH /v1/stations/{station_id}/users/{user_id}/unban
        [HttpPatch("v1/stations/{station_id}/users/{user_id}/unban")]
        public async Task<IHttpActionResult> UnbanStationUser(
            IHttpRequest request, IHttpResponse response, string station_id, string user_id)
        {
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindOne(u => u.UserId == user_id);
            if (user == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            bool updated = false;
            foreach (var ban in user.Bans ?? new List<BanRequest>())
            {
                if (ban.StationId == station_id && !ban.Revoked)
                {
                    ban.Revoked = true;
                    updated = true;
                }
            }
            if (updated) userCollection?.Update(user);
            return Results.Ok(new SuccessBoolean { Success = updated });
        }

        // GET /v1/stations/{station_id}/bans
        [HttpGet("v1/stations/{station_id}/bans")]
        public async Task<IHttpActionResult> GetStationBans(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            bool includeRevoked = GetBooleanQuery(request, "include_revoked", false);
            bool includeExpired = GetBooleanQuery(request, "include_expired", false);

            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var now = DateTime.Now;

            var allBans = userCollection?.FindAll()
                .SelectMany(u => (u.Bans ?? new List<BanRequest>())
                    .Where(b => b.StationId == station_id)
                    .Where(b => includeRevoked || !b.Revoked)
                    .Where(b => includeExpired || b.Expiration > now))
                .ToList() ?? new List<BanRequest>();

            return Results.Ok(new BanResponse { Bans = allBans });
        }

        // POST /v1/stations/{station_id}/event — create station event
        [HttpPost("v1/stations/{station_id}/event")]
        public async Task<IHttpActionResult> CreateStationEvent(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            string title = request.GetQueryParameter("title", "") ?? "";
            string description = request.GetQueryParameter("description", "") ?? "";
            int duration = int.Parse(request.GetQueryParameter("duration", "0") ?? "0");
            string? deploymentId = request.GetQueryParameter("deployment_id");
            string startTimeStr = request.GetQueryParameter("start_time", DateTime.Now.ToString("o")) ?? DateTime.Now.ToString("o");
            bool isPublic = GetBooleanQuery(request, "public", true);
            bool signupsOpen = GetBooleanQuery(request, "signups_open", true);

            Dictionary<string, string>? config;
            try { config = JsonSerializer.Deserialize<Dictionary<string, string>>(request.Body); }
            catch { config = new Dictionary<string, string>(); }

            var eventId = GenerateId();
            var startTime = DateTime.Parse(startTimeStr);
            var ev = new StationEventDbObject
            {
                EventId = eventId,
                StationId = station_id,
                DeploymentId = string.IsNullOrEmpty(deploymentId) ? null : deploymentId,
                Title = title,
                Description = description,
                StartTime = startTime,
                Duration = duration,
                Public = isPublic,
                SignupsOpen = signupsOpen,
                Config = config ?? new Dictionary<string, string>()
            };

            var eventCollection = Program.Database.GetCollection<StationEventDbObject>(true);
            eventCollection?.Insert(ev);

            return Results.Ok(new StationEvent
            {
                EventId = ev.EventId,
                StationId = ev.StationId,
                DeploymentId = ev.DeploymentId ?? "",
                Title = ev.Title,
                Description = ev.Description,
                StartTime = ev.StartTime,
                Duration = ev.Duration,
                Public = ev.Public,
                SignupsOpen = ev.SignupsOpen
            });
        }

        // GET /v1/stations/{station_id}/events
        [HttpGet("v1/stations/{station_id}/events")]
        public async Task<IHttpActionResult> GetStationEvents(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            bool getPast = GetBooleanQuery(request, "get_past_events", false);
            var now = DateTime.Now;

            var eventCollection = Program.Database.GetCollection<StationEventDbObject>(true);
            var events = eventCollection?.Find(e => e.StationId == station_id).ToList()
                ?? new List<StationEventDbObject>();

            if (!getPast)
                events = events.Where(e => e.StartTime.AddSeconds(e.Duration) >= now).ToList();

            return Results.Ok(new StationEvents
            {
                Events = events.Select(e => new StationEvent
                {
                    EventId = e.EventId,
                    StationId = e.StationId,
                    DeploymentId = e.DeploymentId ?? "",
                    Title = e.Title,
                    Description = e.Description,
                    StartTime = e.StartTime,
                    Duration = e.Duration,
                    Public = e.Public,
                    SignupsOpen = e.SignupsOpen
                }).ToList()
            });
        }

        // GET /v1/events/{event_id}
        [HttpGet("v1/events/{event_id}")]
        public async Task<IHttpActionResult> GetStationEvent(
            IHttpRequest request, IHttpResponse response, string event_id)
        {
            var eventCollection = Program.Database.GetCollection<StationEventDbObject>(true);
            var ev = eventCollection?.FindOne(e => e.EventId == event_id);
            if (ev == null)
                return Results.Ok((object?)null);

            return Results.Ok(new StationEvent
            {
                EventId = ev.EventId,
                StationId = ev.StationId,
                DeploymentId = ev.DeploymentId ?? "",
                Title = ev.Title,
                Description = ev.Description,
                StartTime = ev.StartTime,
                Duration = ev.Duration,
                Public = ev.Public,
                SignupsOpen = ev.SignupsOpen
            });
        }

        // DELETE /v1/stations/{station_id}/event/{event_id}
        [HttpDelete("v1/stations/{station_id}/event/{event_id}")]
        public async Task<IHttpActionResult> DeleteStationEvent(
            IHttpRequest request, IHttpResponse response, string station_id, string event_id)
        {
            var eventCollection = Program.Database.GetCollection<StationEventDbObject>(true);
            var ev = eventCollection?.FindOne(e => e.EventId == event_id && e.StationId == station_id);
            if (ev == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            eventCollection!.Delete(ev.Id);

            // delete signups
            var signupCollection = Program.Database.GetCollection<EventSignupDbObject>(true);
            foreach (var s in signupCollection?.Find(s => s.EventId == event_id).ToList() ?? new())
                signupCollection!.Delete(s.Id);

            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // PATCH /v1/stations/{station_id}/event/{event_id}
        [HttpPatch("v1/stations/{station_id}/event/{event_id}")]
        public async Task<IHttpActionResult> UpdateStationEvent(
            IHttpRequest request, IHttpResponse response, string station_id, string event_id)
        {
            StationEvent? body;
            try { body = JsonSerializer.Deserialize<StationEvent>(request.Body); }
            catch { body = null; }

            var eventCollection = Program.Database.GetCollection<StationEventDbObject>(true);
            var ev = eventCollection?.FindOne(e => e.EventId == event_id && e.StationId == station_id);
            if (ev == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            if (body != null)
            {
                if (!string.IsNullOrEmpty(body.Title)) ev.Title = body.Title;
                if (!string.IsNullOrEmpty(body.Description)) ev.Description = body.Description;
                if (body.StartTime != default) ev.StartTime = body.StartTime;
                if (body.Duration > 0) ev.Duration = body.Duration;
                ev.Public = body.Public;
                ev.SignupsOpen = body.SignupsOpen;
                if (!string.IsNullOrEmpty(body.DeploymentId)) ev.DeploymentId = body.DeploymentId;
            }
            eventCollection?.Update(ev);
            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // POST /v1/stations/{station_id}/event/{event_id}/users/{user_id}/signup
        [HttpPost("v1/stations/{station_id}/event/{event_id}/users/{user_id}/signup")]
        public async Task<IHttpActionResult> SignupStationEvent(
            IHttpRequest request, IHttpResponse response,
            string station_id, string event_id, string user_id)
        {
            string? data = null;
            try
            {
                if (request.Body.Length > 0)
                    data = JsonSerializer.Deserialize<string>(request.Body);
            }
            catch { data = null; }

            var eventCollection = Program.Database.GetCollection<StationEventDbObject>(true);
            var ev = eventCollection?.FindOne(e => e.EventId == event_id);
            if (ev == null || !ev.SignupsOpen)
                return Results.Ok(new EventSignupResponse { UserSignedUp = false, SignupId = "" });

            var signupId = GenerateId();
            var signupCollection = Program.Database.GetCollection<EventSignupDbObject>(true);

            // check if already signed up
            var existing = signupCollection?.FindOne(s => s.EventId == event_id && s.UserId == user_id);
            if (existing != null)
                return Results.Ok(new EventSignupResponse { UserSignedUp = true, SignupId = existing.SignupId });

            signupCollection?.Insert(new EventSignupDbObject
            {
                SignupId = signupId,
                EventId = event_id,
                UserId = user_id,
                Timestamp = DateTime.Now,
                Data = data
            });

            return Results.Ok(new EventSignupResponse { UserSignedUp = true, SignupId = signupId });
        }

        // GET /v1/stations/{station_id}/event/{event_id}/signups
        [HttpGet("v1/stations/{station_id}/event/{event_id}/signups")]
        public async Task<IHttpActionResult> GetStationEventSignups(
            IHttpRequest request, IHttpResponse response, string station_id, string event_id)
        {
            var signupCollection = Program.Database.GetCollection<EventSignupDbObject>(true);
            var signups = signupCollection?.Find(s => s.EventId == event_id)
                .Select(s => new StationEventSignupInfo
                {
                    SignupId = s.SignupId,
                    UserId = s.UserId,
                    Timestamp = s.Timestamp,
                    Data = s.Data
                }).ToList() ?? new List<StationEventSignupInfo>();

            return Results.Ok(new StationEventSignups { Signups = signups });
        }

        // DELETE /v1/stations/{station_id}/event/{event_id}/signup
        [HttpDelete("v1/stations/{station_id}/event/{event_id}/signup")]
        public async Task<IHttpActionResult> DeleteStationEventSignup(
            IHttpRequest request, IHttpResponse response, string station_id, string event_id)
        {
            string signupId = request.GetQueryParameter("signup_id", "") ?? "";
            var signupCollection = Program.Database.GetCollection<EventSignupDbObject>(true);
            var signup = signupCollection?.FindOne(s => s.SignupId == signupId && s.EventId == event_id);
            if (signup == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            signupCollection!.Delete(signup.Id);
            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // GET /v1/stations/{station_id}/event/{event_id}/config
        [HttpGet("v1/stations/{station_id}/event/{event_id}/config")]
        public async Task<IHttpActionResult> GetStationEventConfig(
            IHttpRequest request, IHttpResponse response, string station_id, string event_id)
        {
            var eventCollection = Program.Database.GetCollection<StationEventDbObject>(true);
            var ev = eventCollection?.FindOne(e => e.EventId == event_id && e.StationId == station_id);
            return Results.Ok(ev?.Config ?? new Dictionary<string, string>());
        }

        // POST /v1/stations/{station_id}/event/{event_id}/config — merge keys
        [HttpPost("v1/stations/{station_id}/event/{event_id}/config")]
        public async Task<IHttpActionResult> SetStationEventConfig(
            IHttpRequest request, IHttpResponse response, string station_id, string event_id)
        {
            Dictionary<string, string>? data;
            try { data = JsonSerializer.Deserialize<Dictionary<string, string>>(request.Body); }
            catch { data = null; }

            var eventCollection = Program.Database.GetCollection<StationEventDbObject>(true);
            var ev = eventCollection?.FindOne(e => e.EventId == event_id && e.StationId == station_id);
            if (ev == null)
                return Results.Ok(new Dictionary<string, string>());

            if (data != null)
                foreach (var kv in data)
                    ev.Config[kv.Key] = kv.Value;

            eventCollection?.Update(ev);
            return Results.Ok(ev.Config);
        }

        // DELETE /v1/stations/{station_id}/event/{event_id}/config — remove keys
        [HttpDelete("v1/stations/{station_id}/event/{event_id}/config")]
        public async Task<IHttpActionResult> DeleteStationEventConfig(
            IHttpRequest request, IHttpResponse response, string station_id, string event_id)
        {
            List<string>? keys;
            try { keys = JsonSerializer.Deserialize<List<string>>(request.Body); }
            catch { keys = null; }

            var eventCollection = Program.Database.GetCollection<StationEventDbObject>(true);
            var ev = eventCollection?.FindOne(e => e.EventId == event_id && e.StationId == station_id);
            if (ev == null)
                return Results.Ok(new Dictionary<string, string>());

            if (keys != null)
                foreach (var k in keys)
                    ev.Config.Remove(k);

            eventCollection?.Update(ev);
            return Results.Ok(ev.Config);
        }

        // ─── FLEETS ───────────────────────────────────────────────────────────────

        private static Dictionary<string, object>? ToTypedConfig(Dictionary<string, string>? config)
        {
            if (config == null || config.Count == 0) return null;
            var result = new Dictionary<string, object>();
            foreach (var kv in config)
            {
                if (bool.TryParse(kv.Value, out var b))
                    result[kv.Key] = b;
                else if (double.TryParse(kv.Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                    result[kv.Key] = d;
                else
                    result[kv.Key] = kv.Value;
            }
            return result;
        }

        private static List<EosSessionInfo> GetEosSessions()
            => EosGatewayServer.GetSessionsSnapshot();

        private static string CanonicalDeploymentId(EosSessionInfo s)
            => string.IsNullOrWhiteSpace(s.DeploymentId) ? s.Id : s.DeploymentId!;

        private static DeploymentDbObject EosSessionToDeployment(EosSessionInfo s)
            => new DeploymentDbObject
            {
                DeploymentId   = CanonicalDeploymentId(s),
                StationId      = s.StationId ?? s.Bucket,
                DeploymentName = s.ServerName,
                IpAddress      = string.IsNullOrEmpty(s.Port) ? s.IpAddress : $"{s.IpAddress}:{s.Port}",
                Version        = s.BuildId.ToString(),
                CreatedAt      = DateTime.UtcNow,
                Online         = true,
                PlayerCount    = s.PublicPlayers.Count,
                Config         = new Dictionary<string, string>
                {
                    ["port"]        = s.Port,
                    ["imgui_port"]  = s.ImguiPort,
                    ["bucket"]      = s.Bucket,
                    ["max_players"] = s.MaxPlayers.ToString()
                }
            };

        private static FleetStationResponse DeploymentToFleetStation(DeploymentDbObject d)
            => new FleetStationResponse
            {
                StationId   = d.DeploymentId,
                FleetId     = d.StationId,
                SessionId   = d.DeploymentId,
                StationName = d.DeploymentName,
                Region      = d.Region,
                Ip          = d.IpAddress,
                Version     = d.Version,
                DeploymentCl = d.Version,
                Created     = d.CreatedAt,
                Online      = d.Online,
                LastEvent   = d.LastEvent,
                PlayerCount = d.PlayerCount,
                Disabled    = false,
                Config      = d.Config?.Count > 0 ? ToTypedConfig(d.Config) : null,
                DistrictPopulations = null
            };

        private static FleetResponse StationToFleet(StationDbObject s, IEnumerable<DeploymentDbObject>? allDeps, bool includeStations, bool includeConfig)
        {
            var depsList = allDeps?.ToList();
            return new FleetResponse
            {
                FleetId   = s.StationId,
                FleetName = s.StationName,
                Created   = s.CreatedAt,
                Online    = depsList?.Any(d => d.Online) ?? s.Online,
                Stations  = includeStations ? depsList?.Select(DeploymentToFleetStation).ToList() : null,
                Config    = includeConfig && s.Config?.Count > 0 ? ToTypedConfig(s.Config) : null
            };
        }

        // GET /v2/fleets — paginated fleets
        [HttpGet("v2/fleets")]
        public async Task<IHttpActionResult> FetchAllFleetsV2(IHttpRequest request, IHttpResponse response)
        {
            bool includeConfig   = bool.Parse(request.GetQueryParameter("include_config",   "false") ?? "false");
            bool includeStations = GetBooleanQuery(request, "include_stations", false);
            bool includeOffline  = GetBooleanQuery(request, "include_offline_fleets", true);
            int pageSize = int.Parse(request.GetQueryParameter("page_size", "100") ?? "100");
            int page     = int.Parse(request.GetQueryParameter("page", "1") ?? "1");

            var stationCollection    = Program.Database.GetCollection<StationDbObject>(true);
            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);

            var fleets = stationCollection?.FindAll().ToList() ?? new List<StationDbObject>();
            if (!includeOffline)
                fleets = fleets.Where(s => s.Online).ToList();

            var eosByStation = GetEosSessions()
                .ToLookup(s => s.StationId ?? s.Bucket, s => EosSessionToDeployment(s));

            var responses = fleets.Select(s =>
            {
                IEnumerable<DeploymentDbObject> deps = includeStations
                    ? (deploymentCollection?.Find(d => d.StationId == s.StationId) ?? Enumerable.Empty<DeploymentDbObject>())
                        .Concat(eosByStation[s.StationId])
                    : Enumerable.Empty<DeploymentDbObject>();
                return StationToFleet(s, deps, includeStations, includeConfig);
            }).ToList();

            return Results.Ok(PagedResponse<FleetResponse>.Create<FleetResponsePage>(responses, pageSize, page));
        }

        // GET /v2/fleets/{fleet_id} — single fleet
        [HttpGet("v2/fleets/{fleet_id}")]
        public async Task<IHttpActionResult> GetFleetV2(IHttpRequest request, IHttpResponse response, string fleet_id)
        {
            bool includeConfig   = bool.Parse(request.GetQueryParameter("include_config",   "true") ?? "true");
            bool includeStations = GetBooleanQuery(request, "include_stations", true);

            var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
            var fleet = stationCollection?.FindOne(s => s.StationId == fleet_id);
            if (fleet == null)
                return Results.Ok((object?)null);

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            IEnumerable<DeploymentDbObject> deps = includeStations
                ? (deploymentCollection?.Find(d => d.StationId == fleet_id) ?? Enumerable.Empty<DeploymentDbObject>())
                    .Concat(GetEosSessions()
                        .Where(s => (s.StationId ?? s.Bucket) == fleet_id)
                        .Select(EosSessionToDeployment))
                : Enumerable.Empty<DeploymentDbObject>();

            return Results.Ok(StationToFleet(fleet, deps, includeStations, includeConfig));
        }

        // V2 client API
        // The v2 client contract uses (host api.example.com, overridable via
        // -DashboardApiUrl). These map our existing station/deployment DB onto the new shapes.
        // Model note: our StationDbObject == v2 "fleet", our DeploymentDbObject == v2 "station".

        // GET /v2/stations?include_offline=&version=&page_size=&page= — the browse list. Returns every
        // deployment (DB + live EOS session) as an FStationResponse item. We stamp BuildVersion on each
        // row (see DeploymentToFleetStation via register), so we do NOT hard-filter on the version param
        // (avoids ever hiding our own server); include_offline is honored.
        [HttpGet("v2/stations")]
        public async Task<IHttpActionResult> FetchAllStationsV2(IHttpRequest request, IHttpResponse response)
        {
            bool includeOffline = GetBooleanQuery(request, "include_offline", true);

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var deps = (deploymentCollection?.FindAll() ?? Enumerable.Empty<DeploymentDbObject>())
                .Concat(GetEosSessions().Select(EosSessionToDeployment))
                .ToList();
            if (!includeOffline)
                deps = deps.Where(d => d.Online).ToList();

            // Dedupe by deployment id (a registered server also shows as an EOS session).
            var seen = new HashSet<string>();
            var items = new List<FleetStationResponse>();
            foreach (var d in deps)
                if (!string.IsNullOrEmpty(d.DeploymentId) && seen.Add(d.DeploymentId))
                    items.Add(DeploymentToFleetStation(d));

            return Results.Ok(new StationsPageV2 { FleetId = "", Items = items });
        }

        // GET /v1/region — the client geo-picks its region from this on boot. Self-host => fixed NA/US.
        [HttpGet("/v1/region")]
        public async Task<IHttpActionResult> GetRegion(IHttpRequest request, IHttpResponse response)
            => Results.Ok(new RegionResponse { ContinentCode = "NA", CountryCode = "US" });

        // POST /v2/users/log_in — player login (v2). Same user-upsert + key-mint + nonce handling as the
        // v0 path, but returns the SLIM v2 response (no embedded server list; client uses /v2/stations).
        [HttpPost("/v2/users/log_in")]
        public async Task<IHttpActionResult> UserPlayerLoginV2(IHttpRequest request, IHttpResponse response)
        {
            ClientLoginRequest? body;
            try { body = JsonSerializer.Deserialize<ClientLoginRequest>(request.Body); }
            catch { body = null; }

            if (body == null || string.IsNullOrEmpty(body.PlatformId))
                return Results.Ok(new ClientLoginV2Response { Success = false });

            if (NonceVerify)
            {
                var nonceOk = await VerifyNonceAny(body.PlatformId, body.Nonce);
                Console.WriteLine($"[StationDB][LOGIN v2] platformId='{body.PlatformId}' nonceOk={nonceOk} enforce={NonceEnforce}");
                if (NonceEnforce && !nonceOk)
                    return Results.Ok(new ClientLoginV2Response { Success = false });
            }

            var userId = body.PlatformId;
            var userCollection = Program.Database.GetCollection<UserDataResponse>(true);
            var user = userCollection?.FindOne(u => u.UserId == userId);
            if (user == null)
            {
                user = new UserDataResponse
                {
                    UserId = userId, Username = body.Username, Platform = body.Platform,
                    CreatedAt = DateTime.Now, LastLogin = DateTime.Now,
                    Roles = new List<string>(), Bans = new List<BanRequest>()
                };
                userCollection?.Insert(user);
            }
            else
            {
                user.Username = body.Username; user.Platform = body.Platform; user.LastLogin = DateTime.Now;
                userCollection?.Update(user);
            }

            var apiKey = GenerateApiKey(userId);
            var keyCollection = Program.Database.GetCollection<UserApiKeyDbObject>(true);
            keyCollection?.Insert(new UserApiKeyDbObject { UserId = userId, ApiKey = apiKey });

            return Results.Ok(new ClientLoginV2Response { Success = true, ApiKey = apiKey, OrgScopedId = userId });
        }

        // POST /v1/users/log_in_with_key — the dedicated server authenticates here (master key). Same as the
        // v0 route; alias so both builds' clients/servers resolve it.
        [HttpPost("/v1/users/log_in_with_key")]
        public Task<IHttpActionResult> LogInWithKeyV1(IHttpRequest request, IHttpResponse response)
            => LogInWithKey(request, response);

        // ─── DEPLOYMENTS ──────────────────────────────────────────────────────────

        // POST /stations/{station_id}/deployments — create deployment
        [HttpPost("/stations/{station_id}/deployments")]
        public async Task<IHttpActionResult> CreateDeployment(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            DeploymentRequest? body;
            try { body = JsonSerializer.Deserialize<DeploymentRequest>(request.Body); }
            catch { body = null; }

            var deploymentId = GenerateId();
            var apiKey = GenerateApiKey(deploymentId, "service");

            var deployment = new DeploymentDbObject
            {
                DeploymentId = deploymentId,
                StationId = station_id,
                DeploymentName = body?.DeploymentName,
                IpAddress = body?.IpAddress,
                Region = body?.Region,
                Version = body?.Version,
                CreatedAt = DateTime.Now,
                Online = false,
                PlayerCount = 0,
                Config = new Dictionary<string, string>(),
                ApiKeys = new List<string> { apiKey }
            };

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            deploymentCollection?.Insert(deployment);

            return Results.Ok(new CreateDeploymentResponse { ApiKey = apiKey, DeploymentId = deploymentId });
        }

        // GET /stations/{station_id}/deployments (deprecated v0)
        [HttpGet("/stations/{station_id}/deployments")]
        public async Task<IHttpActionResult> GetStationDeploymentsV0(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var deployments = (deploymentCollection?.Find(d => d.StationId == station_id) ?? Enumerable.Empty<DeploymentDbObject>())
                .Concat(GetEosSessions()
                    .Where(s => (s.StationId ?? s.Bucket) == station_id)
                    .Select(EosSessionToDeployment))
                .Select(DeploymentToResponse).ToList();

            return Results.Ok(new StationDeploymentResponse
            {
                StationId = station_id,
                Deployments = deployments.Select(d => new DeploymentExpanded
                {
                    StationId = d.StationId,
                    DeploymentName = d.DeploymentName,
                    IpAddress = d.IpAddress,
                    Region = d.Region,
                    Version = d.Version,
                    CreatedAt = d.CreatedAt,
                    Online = d.Online,
                    LastEvent = d.LastEventAt,
                    PlayerCount = d.PlayerCount,
                    Config = d.Config
                }).ToList()
            });
        }

        // GET /v1/stations/{station_id}/deployments
        [HttpGet("v1/stations/{station_id}/deployments")]
        public async Task<IHttpActionResult> GetStationDeploymentsV1(
            IHttpRequest request, IHttpResponse response, string station_id)
        {
            int pageSize = int.Parse(request.GetQueryParameter("page_size", "500") ?? "500");
            int page = int.Parse(request.GetQueryParameter("page", "1") ?? "1");
            bool includeOffline = GetBooleanQuery(request, "include_offline", false);

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var deployments = (deploymentCollection?.Find(d => d.StationId == station_id) ?? Enumerable.Empty<DeploymentDbObject>())
                .Concat(GetEosSessions()
                    .Where(s => (s.StationId ?? s.Bucket) == station_id)
                    .Select(EosSessionToDeployment))
                .ToList();

            if (!includeOffline)
                deployments = deployments.Where(d => d.Online).ToList();

            var items = deployments.Select(DeploymentToResponse).ToList();
            var paged = PagedResponse<ServerDeploymentResponse>.Create<ServerDeploymentResponsePage>(items, pageSize, page);

            return Results.Ok(new DeploymentsResponsePage
            {
                StationId = station_id,
                Page = paged.Page,
                Items = paged.Items.Select(d => new DeploymentExpanded
                {
                    StationId = d.StationId,
                    DeploymentName = d.DeploymentName,
                    IpAddress = d.IpAddress,
                    Region = d.Region,
                    Version = d.Version,
                    CreatedAt = d.CreatedAt,
                    Online = d.Online,
                    LastEvent = d.LastEventAt,
                    PlayerCount = d.PlayerCount,
                    Config = d.Config
                }).ToList()
            });
        }

        // POST /deployments/{deployment_id}/api_key
        [HttpPost("/deployments/{deployment_id}/api_key")]
        public async Task<IHttpActionResult> CreateDeploymentApiKey(
            IHttpRequest request, IHttpResponse response, string deployment_id)
        {
            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var deployment = deploymentCollection?.FindOne(d => d.DeploymentId == deployment_id);
            if (deployment == null || !IsMasterOrDeploymentKey(request, deployment))
                return Results.Ok(new ApiKeyResponse { ApiKey = "" });

            var newKey = GenerateApiKey(deployment_id, "service");
            deployment.ApiKeys ??= new List<string>();
            deployment.ApiKeys.Add(newKey);
            deploymentCollection?.Update(deployment);

            return Results.Ok(new ApiKeyResponse { ApiKey = newKey });
        }

        // POST /deployments/{deployment_id}/server_events — add server event
        [HttpPost("/deployments/{deployment_id}/server_events")]
        public async Task<IHttpActionResult> AddServerEvent(
            IHttpRequest request, IHttpResponse response, string deployment_id)
        {
            ServerEventRequest? body;
            try { body = JsonSerializer.Deserialize<ServerEventRequest>(request.Body); }
            catch { body = null; }

            if (body == null || string.IsNullOrEmpty(body.EventType))
                return Results.Ok(new SuccessBoolean { Success = false });

            // Only persist small events. The game fires "netvars" (~1.6 MB) and "state" (~40 KB)
            // every few seconds; storing those would bloat production.db fast, and we don't read
            // them back. Keep tiny events for monitoring, skip the big spammy ones.
            const int MaxStoredEventBytes = 8192;
            if (string.IsNullOrEmpty(body.EventData) || body.EventData.Length <= MaxStoredEventBytes)
            {
                var eventCollection = Program.Database.GetCollection<ServerEventDbObject>(true);
                eventCollection?.Insert(new ServerEventDbObject
                {
                    DeploymentId = deployment_id,
                    EventType = body.EventType,
                    EventData = body.EventData,
                    Timestamp = DateTime.Now
                });
            }

            // Update online status if this deployment has a real DB row. The game server uses a
            // synthetic deployment id ("default") with no row — that's fine, we still ACK success so
            // the game's reporting delegate keeps ticking (returning false made it look rejected).
            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var deployment = deploymentCollection?.FindOne(d => d.DeploymentId == deployment_id);
            if (deployment != null)
            {
                deployment.Online = true;
                deployment.LastEvent = DateTime.Now;
                deploymentCollection?.Update(deployment);

                var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
                var station = stationCollection?.FindOne(s => s.StationId == deployment.StationId);
                if (station != null)
                {
                    station.Online = true;
                    station.LastOnline = DateTime.Now;
                    stationCollection?.Update(station);
                }
            }

            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // PATCH /deployments/{deployment_id} — update deployment info
        [HttpPatch("/deployments/{deployment_id}")]
        public async Task<IHttpActionResult> UpdateDeployment(
            IHttpRequest request, IHttpResponse response, string deployment_id)
        {
            DeploymentRequest? body;
            try { body = JsonSerializer.Deserialize<DeploymentRequest>(request.Body); }
            catch { body = null; }

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var deployment = deploymentCollection?.FindOne(d => d.DeploymentId == deployment_id);
            if (deployment == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            if (body != null)
            {
                if (body.DeploymentName != null) deployment.DeploymentName = body.DeploymentName;
                if (body.IpAddress != null) deployment.IpAddress = body.IpAddress;
                if (body.Region != null) deployment.Region = body.Region;
                if (body.Version != null) deployment.Version = body.Version;
            }
            deploymentCollection?.Update(deployment);
            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // DELETE /deployments/{deployment_id}
        [HttpDelete("/deployments/{deployment_id}")]
        public async Task<IHttpActionResult> DeleteDeployment(
            IHttpRequest request, IHttpResponse response, string deployment_id)
        {
            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var deployment = deploymentCollection?.FindOne(d => d.DeploymentId == deployment_id);
            if (deployment == null)
                return Results.Ok(new SuccessBoolean { Success = false });

            deploymentCollection!.Delete(deployment.Id);

            // Keep the EOS browser/session registry in sync with the deployment database.
            EosGatewayServer.RemoveSessionByDeploymentId(deployment_id);

            // delete all server events for this deployment
            var eventCollection = Program.Database.GetCollection<ServerEventDbObject>(true);
            foreach (var ev in eventCollection?.Find(e => e.DeploymentId == deployment_id).ToList() ?? new())
                eventCollection!.Delete(ev.Id);

            return Results.Ok(new SuccessBoolean { Success = true });
        }

        // GET /deployments/{deployment_id}/config (deprecated v0)
        [HttpGet("/deployments/{deployment_id}/config")]
        public async Task<IHttpActionResult> GetDeploymentConfigV0(
            IHttpRequest request, IHttpResponse response, string deployment_id)
        {
            bool includeStation = GetBooleanQuery(request, "include_station_config", true);

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var deployment = deploymentCollection?.FindOne(d => d.DeploymentId == deployment_id)
                ?? GetEosSessions().Select(EosSessionToDeployment).FirstOrDefault(d => d.DeploymentId == deployment_id);

            var config = deployment != null
                ? new Dictionary<string, string>(deployment.Config)
                : new Dictionary<string, string>();

            if (includeStation)
            {
                if (deployment != null)
                {
                    var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
                    var station = stationCollection?.FindOne(s => s.StationId == deployment.StationId);
                    if (station != null)
                        foreach (var kv in station.Config)
                            config.TryAdd(kv.Key, kv.Value);
                }
                // default board/sign URLs (served even for an unknown/fixed deployment id).
                foreach (var kv in DefaultStationConfig)
                    config.TryAdd(kv.Key, kv.Value);
            }

            return Results.Ok(config);
        }

        // GET /v1/deployments/{deployment_id}
        [HttpGet("v1/deployments/{deployment_id}")]
        public async Task<IHttpActionResult> GetDeploymentV1(
            IHttpRequest request, IHttpResponse response, string deployment_id)
        {
            bool includeDeploymentConfig = GetBooleanQuery(request, "include_deployment_config", true);
            bool includeStationConfig = GetBooleanQuery(request, "include_station_config", true);

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var deployment = deploymentCollection?.FindOne(d => d.DeploymentId == deployment_id);
            if (deployment == null)
            {
                var eos = GetEosSessions().FirstOrDefault(s =>
                    string.Equals(CanonicalDeploymentId(s), deployment_id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Id, deployment_id, StringComparison.OrdinalIgnoreCase));
                if (eos != null)
                    deployment = EosSessionToDeployment(eos);
                else
                {
                    // Synthetic deployment so a FIXED launch arg (-DashboardDeploymentId=default)
                    // still resolves — the server mints its real deployment id at runtime, but the
                    // cmdline id is read at startup, so they won't match. CRITICAL: carry a REAL
                    // station id, else the game logs "Station ID not set" and never fetches roles
                    // (and our join-gate never triggers). Resolve the station from the most-recent
                    // online deployment register_server actually created. (FindAll().Where — never
                    // FindOne(captured) — per the LiteDB translator-hang gotcha.)
                    var latest = deploymentCollection?.FindAll()
                        .Where(d => d.Online && !string.IsNullOrEmpty(d.StationId))
                        .OrderByDescending(d => d.CreatedAt)
                        .FirstOrDefault();
                    deployment = new DeploymentDbObject
                    {
                        DeploymentId = deployment_id,
                        StationId = latest?.StationId ?? "",
                        DeploymentName = latest?.DeploymentName ?? "Game Server",
                        Region = latest?.Region ?? "NAE", IpAddress = latest?.IpAddress ?? "",
                        Version = BuildVersion, CreatedAt = DateTime.Now,
                        Online = true, PlayerCount = 0, Config = new Dictionary<string, string>()
                    };
                }
            }

            var resp = DeploymentToResponse(deployment);
            // Build config from flags. Station config (incl. the default board values) is served
            // whenever include_station_config=true, independent of include_deployment_config.
            resp.Config ??= new Dictionary<string, string>();
            if (!includeDeploymentConfig)
                resp.Config = new Dictionary<string, string>();
            if (includeStationConfig)
            {
                var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
                var station = stationCollection?.FindOne(s => s.StationId == deployment.StationId);
                if (station != null)
                    foreach (var kv in station.Config)
                        resp.Config.TryAdd(kv.Key, kv.Value);
                // default board/sign URLs (a real station config, if present, overrides these).
                foreach (var kv in DefaultStationConfig)
                    resp.Config.TryAdd(kv.Key, kv.Value);
            }

            return Results.Ok(resp);
        }

        // GET /v1/deployments/{deployment_id}/config
        [HttpGet("v1/deployments/{deployment_id}/config")]
        public async Task<IHttpActionResult> GetDeploymentConfigV1(
            IHttpRequest request, IHttpResponse response, string deployment_id)
        {
            bool includeStation = GetBooleanQuery(request, "include_station_config", true);
            bool includeEvent = GetBooleanQuery(request, "include_event_config", true);

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var deployment = deploymentCollection?.FindOne(d => d.DeploymentId == deployment_id);

            var config = deployment != null
                ? new Dictionary<string, string>(deployment.Config)
                : new Dictionary<string, string>();

            if (includeStation)
            {
                if (deployment != null)
                {
                    var stationCollection = Program.Database.GetCollection<StationDbObject>(true);
                    var station = stationCollection?.FindOne(s => s.StationId == deployment.StationId);
                    if (station != null)
                        foreach (var kv in station.Config)
                            config.TryAdd(kv.Key, kv.Value);
                }
                // default board/sign URLs (served even for an unknown/fixed deployment id).
                foreach (var kv in DefaultStationConfig)
                    config.TryAdd(kv.Key, kv.Value);
            }

            if (includeEvent)
            {
                var eventCollection = Program.Database.GetCollection<StationEventDbObject>(true);
                var now = DateTime.Now;
                var activeEvent = eventCollection?.FindAll()
                    .Where(e => deployment != null && e.StationId == deployment.StationId)
                    .Where(e => e.DeploymentId == deployment_id || string.IsNullOrEmpty(e.DeploymentId))
                    .Where(e => e.StartTime <= now && e.StartTime.AddSeconds(e.Duration) >= now)
                    .FirstOrDefault();
                if (activeEvent != null)
                    foreach (var kv in activeEvent.Config)
                        config.TryAdd(kv.Key, kv.Value);
            }

            return Results.Ok(config);
        }

        // POST /v1/deployments/{deployment_id}/config — merge keys
        [HttpPost("v1/deployments/{deployment_id}/config")]
        public async Task<IHttpActionResult> SetDeploymentConfigV1(
            IHttpRequest request, IHttpResponse response, string deployment_id)
        {
            Dictionary<string, string>? data;
            try { data = JsonSerializer.Deserialize<Dictionary<string, string>>(request.Body); }
            catch { data = null; }

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var deployment = deploymentCollection?.FindOne(d => d.DeploymentId == deployment_id);
            if (deployment == null)
                return Results.Ok(new Dictionary<string, string>());

            if (data != null)
                foreach (var kv in data)
                    deployment.Config[kv.Key] = kv.Value;

            deploymentCollection?.Update(deployment);
            return Results.Ok(deployment.Config);
        }

        // DELETE /v1/deployments/{deployment_id}/config — remove keys
        [HttpDelete("v1/deployments/{deployment_id}/config")]
        public async Task<IHttpActionResult> DeleteDeploymentConfigV1(
            IHttpRequest request, IHttpResponse response, string deployment_id)
        {
            List<string>? keys;
            try { keys = JsonSerializer.Deserialize<List<string>>(request.Body); }
            catch { keys = null; }

            var deploymentCollection = Program.Database.GetCollection<DeploymentDbObject>(true);
            var deployment = deploymentCollection?.FindOne(d => d.DeploymentId == deployment_id);
            if (deployment == null)
                return Results.Ok(new Dictionary<string, string>());

            if (keys != null)
                foreach (var k in keys)
                    deployment.Config.Remove(k);

            deploymentCollection?.Update(deployment);
            return Results.Ok(deployment.Config);
        }

        // GET /v1/deployments/{deployment_id}/server_events
        [HttpGet("v1/deployments/{deployment_id}/server_events")]
        public async Task<IHttpActionResult> GetDeploymentServerEvents(
            IHttpRequest request, IHttpResponse response, string deployment_id)
        {
            int pageSize = int.Parse(request.GetQueryParameter("page_size", "500") ?? "500");
            int page = int.Parse(request.GetQueryParameter("page", "1") ?? "1");
            string? eventType = request.GetQueryParameter("event_type");

            var eventCollection = Program.Database.GetCollection<ServerEventDbObject>(true);
            var query = eventCollection?.FindAll()
                .Where(e => e.DeploymentId == deployment_id) ?? Enumerable.Empty<ServerEventDbObject>();

            if (!string.IsNullOrEmpty(eventType))
                query = query.Where(e => e.EventType == eventType);

            var allEvents = query.OrderByDescending(e => e.Timestamp).ToList();
            var mapped = allEvents.Select((e, i) => new ServerEventResponse
            {
                Index = i + 1,
                EventType = e.EventType,
                DeploymentId = e.DeploymentId,
                EventData = e.EventData,
                Timestamp = e.Timestamp
            }).ToList();

            return Results.Ok(PagedResponse<ServerEventResponse>.Create<ServerEventResponsePage>(mapped, pageSize, page));
        }
    }
}
