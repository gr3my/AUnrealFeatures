using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Playfab.Models;
using PlayFab.AuthenticationModels;
using PlayFab.ClientModels;
using System.Linq.Expressions;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    /* ─── PlayFab ID resolution helpers ──────────────────────────────────── */

    private PlayfabPlayerDbObject? FindPlayer(Expression<Func<PlayfabPlayerDbObject, bool>> predicate) =>
        Database.GetCollection<PlayfabPlayerDbObject>(true)!.FindOne(predicate);

    private PlayfabPlayerDbObject ResolveOrCreatePlayer(
        Expression<Func<PlayfabPlayerDbObject, bool>> match,
        Action<PlayfabPlayerDbObject> seed,
        bool? createAccount,
        out bool newlyCreated)
    {
        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var existing = coll.FindOne(match);
        if (existing != null) { newlyCreated = false; return existing; }
        if (createAccount == false)
            throw new PlayfabFault("AccountNotFound", 1001, "User not found", HttpStatusCode.NotFound);

        var p = new PlayfabPlayerDbObject
        {
            PlayFabId = NewPlayFabId(),
            EntityId  = NewId(),
            TitleId   = TITLE_ID,
            Created   = NowUnix(),
            LastLogin = NowUnix()
        };
        seed(p);
        coll.Insert(p);
        newlyCreated = true;
        return p;
    }

    private LoginResult IssueLoginResult(PlayfabPlayerDbObject player, bool newlyCreated)
    {
        var sessionTicket = IssueSessionTicket(player.PlayFabId);
        var entityToken   = IssueEntityToken(player.EntityId);
        var exp           = DateTimeOffset.UtcNow.AddDays(1);

        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        player.SessionTicket     = sessionTicket;
        player.SessionExpiration = exp.ToUnixTimeSeconds();
        player.LastLogin         = NowUnix();
        coll.Update(player);

        // Ensure configured default inventory items are present on every login.
        // This also backfills accounts created before the defaults were configured.
        EnsureDefaultInventoryItems(player.EntityId);

        return new LoginResult
        {
            PlayFabId     = player.PlayFabId,
            SessionTicket = sessionTicket,
            NewlyCreated  = newlyCreated,
            LastLoginTime = newlyCreated ? null : DateTime.UtcNow,
            EntityToken   = new PlayFab.ClientModels.EntityTokenResponse
            {
                Entity          = new PlayFab.ClientModels.EntityKey { Id = player.EntityId, Type = "title_player_account" },
                EntityToken     = entityToken,
                TokenExpiration = exp.UtcDateTime
            }
        };
    }

    private const int PasswordHashIterations = 100_000;

    private static string HashPassword(string password)
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordHashIterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${PasswordHashIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
            return false;

        if (storedHash.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal))
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations < 10_000)
                return false;

            try
            {
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // Backward compatibility for databases created by older builds.
        var legacy = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        byte[] expectedLegacy;
        try
        {
            expectedLegacy = Convert.FromHexString(storedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return legacy.Length == expectedLegacy.Length &&
               CryptographicOperations.FixedTimeEquals(legacy, expectedLegacy);
    }

    /* ─── Login endpoints ─────────────────────────────────────────────────── */

    /// <summary>Anonymous login by client-generated CustomId.</summary>
    [HttpPost("/Client/LoginWithCustomID")]
    public Task<IHttpActionResult> Client_LoginWithCustomID(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithCustomIDRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.CustomId))
            return Task.FromResult(Err("InvalidParams", 1000, "CustomId is required"));
        var customId = body.CustomId;
        var player = ResolveOrCreatePlayer(
            p => p.CustomId == customId,
            p => { p.CustomId = customId; p.OriginPlatform = "CustomID"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Sign in with email + password.</summary>
    [HttpPost("/Client/LoginWithEmailAddress")]
    public Task<IHttpActionResult> Client_LoginWithEmailAddress(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithEmailAddressRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Email) || string.IsNullOrEmpty(body.Password))
            return Task.FromResult(Err("InvalidParams", 1000, "Email and Password are required"));
        var email = body.Email;
        var player = FindPlayer(p => p.Email == email);
        if (player == null || !VerifyPassword(body.Password, player.PasswordHash))
            return Task.FromResult(Err("AccountNotFound", 1001, "Invalid email or password", HttpStatusCode.Unauthorized));
        return Task.FromResult(Ok(IssueLoginResult(player, false)));
    }

    /// <summary>Sign in with PlayFab username + password.</summary>
    [HttpPost("/Client/LoginWithPlayFab")]
    public Task<IHttpActionResult> Client_LoginWithPlayFab(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithPlayFabRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Username) || string.IsNullOrEmpty(body.Password))
            return Task.FromResult(Err("InvalidParams", 1000, "Username and Password are required"));
        var uname = body.Username;
        var player = FindPlayer(p => p.Username == uname);
        if (player == null || !VerifyPassword(body.Password, player.PasswordHash))
            return Task.FromResult(Err("AccountNotFound", 1001, "Invalid username or password", HttpStatusCode.Unauthorized));
        return Task.FromResult(Ok(IssueLoginResult(player, false)));
    }

    /// <summary>Login with Android device id.</summary>
    [HttpPost("/Client/LoginWithAndroidDeviceID")]
    public Task<IHttpActionResult> Client_LoginWithAndroid(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithAndroidDeviceIDRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.AndroidDeviceId))
            return Task.FromResult(Err("InvalidParams", 1000, "AndroidDeviceId is required"));
        var devId = body.AndroidDeviceId;
        var player = ResolveOrCreatePlayer(
            p => p.AndroidDeviceId == devId,
            p => { p.AndroidDeviceId = devId; p.OriginPlatform = "Android"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with iOS device id.</summary>
    [HttpPost("/Client/LoginWithIOSDeviceID")]
    public Task<IHttpActionResult> Client_LoginWithIOS(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithIOSDeviceIDRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.DeviceId))
            return Task.FromResult(Err("InvalidParams", 1000, "DeviceId is required"));
        var devId = body.DeviceId;
        var player = ResolveOrCreatePlayer(
            p => p.IosDeviceId == devId,
            p => { p.IosDeviceId = devId; p.OriginPlatform = "iOS"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with Steam ticket.</summary>
    [HttpPost("/Client/LoginWithSteam")]
    public Task<IHttpActionResult> Client_LoginWithSteam(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithSteamRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.SteamTicket))
            return Task.FromResult(Err("InvalidParams", 1000, "SteamTicket is required"));
        var sid = body.SteamTicket;
        var player = ResolveOrCreatePlayer(
            p => p.SteamId == sid,
            p => { p.SteamId = sid; p.OriginPlatform = "Steam"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with Xbox Live token.</summary>
    [HttpPost("/Client/LoginWithXbox")]
    public Task<IHttpActionResult> Client_LoginWithXbox(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithXboxRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.XboxToken))
            return Task.FromResult(Err("InvalidParams", 1000, "XboxToken is required"));
        var xbid = body.XboxToken;
        var player = ResolveOrCreatePlayer(
            p => p.XboxId == xbid,
            p => { p.XboxId = xbid; p.OriginPlatform = "Xbox"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with Facebook access token.</summary>
    [HttpPost("/Client/LoginWithFacebook")]
    public Task<IHttpActionResult> Client_LoginWithFacebook(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithFacebookRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.AccessToken))
            return Task.FromResult(Err("InvalidParams", 1000, "AccessToken is required"));
        var fbid = body.AccessToken;
        var player = ResolveOrCreatePlayer(
            p => p.FacebookId == fbid,
            p => { p.FacebookId = fbid; p.OriginPlatform = "Facebook"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with Google account auth code.</summary>
    [HttpPost("/Client/LoginWithGoogleAccount")]
    public Task<IHttpActionResult> Client_LoginWithGoogle(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithGoogleAccountRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ServerAuthCode))
            return Task.FromResult(Err("InvalidParams", 1000, "ServerAuthCode is required"));
        var gid = body.ServerAuthCode;
        var player = ResolveOrCreatePlayer(
            p => p.GoogleId == gid,
            p => { p.GoogleId = gid; p.OriginPlatform = "Google"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with Google Play Games auth code.</summary>
    [HttpPost("/Client/LoginWithGooglePlayGamesServices")]
    public Task<IHttpActionResult> Client_LoginWithGooglePlay(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithGooglePlayGamesServicesRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ServerAuthCode))
            return Task.FromResult(Err("InvalidParams", 1000, "ServerAuthCode is required"));
        var gid = body.ServerAuthCode;
        var player = ResolveOrCreatePlayer(
            p => p.GoogleAccountId == gid,
            p => { p.GoogleAccountId = gid; p.OriginPlatform = "GooglePlay"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with Game Center player id.</summary>
    [HttpPost("/Client/LoginWithGameCenter")]
    public Task<IHttpActionResult> Client_LoginWithGameCenter(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithGameCenterRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.PlayerId))
            return Task.FromResult(Err("InvalidParams", 1000, "PlayerId is required"));
        var pid = body.PlayerId;
        var player = ResolveOrCreatePlayer(
            p => p.GameCenterId == pid,
            p => { p.GameCenterId = pid; p.OriginPlatform = "GameCenter"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with Kongregate user id.</summary>
    [HttpPost("/Client/LoginWithKongregate")]
    public Task<IHttpActionResult> Client_LoginWithKongregate(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithKongregateRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.KongregateId))
            return Task.FromResult(Err("InvalidParams", 1000, "KongregateId is required"));
        var kid = body.KongregateId;
        var player = ResolveOrCreatePlayer(
            p => p.KongregateId == kid,
            p => { p.KongregateId = kid; p.OriginPlatform = "Kongregate"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with Twitch access token.</summary>
    [HttpPost("/Client/LoginWithTwitch")]
    public Task<IHttpActionResult> Client_LoginWithTwitch(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithTwitchRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.AccessToken))
            return Task.FromResult(Err("InvalidParams", 1000, "AccessToken is required"));
        var tid = body.AccessToken;
        var player = ResolveOrCreatePlayer(
            p => p.TwitchId == tid,
            p => { p.TwitchId = tid; p.OriginPlatform = "Twitch"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with Apple identity token.</summary>
    [HttpPost("/Client/LoginWithApple")]
    public Task<IHttpActionResult> Client_LoginWithApple(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithAppleRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.IdentityToken))
            return Task.FromResult(Err("InvalidParams", 1000, "IdentityToken is required"));
        var sub = body.IdentityToken;
        var player = ResolveOrCreatePlayer(
            p => p.OpenIdSubject == sub,
            p => { p.OpenIdSubject = sub; p.OriginPlatform = "Apple"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with Facebook Instant Games signature.</summary>
    [HttpPost("/Client/LoginWithFacebookInstantGamesId")]
    public Task<IHttpActionResult> Client_LoginWithFBInstant(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithFacebookInstantGamesIdRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FacebookInstantGamesSignature))
            return Task.FromResult(Err("InvalidParams", 1000, "FacebookInstantGamesSignature is required"));
        var sig = body.FacebookInstantGamesSignature;
        var player = ResolveOrCreatePlayer(
            p => p.FacebookId == sig,
            p => { p.FacebookId = sig; p.OriginPlatform = "FacebookInstant"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with Nintendo service account id token.</summary>
    [HttpPost("/Client/LoginWithNintendoServiceAccount")]
    public Task<IHttpActionResult> Client_LoginWithNintendoSA(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithNintendoServiceAccountRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.IdentityToken))
            return Task.FromResult(Err("InvalidParams", 1000, "IdentityToken is required"));
        var nid = body.IdentityToken;
        var player = ResolveOrCreatePlayer(
            p => p.NintendoId == nid,
            p => { p.NintendoId = nid; p.OriginPlatform = "Nintendo"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with Nintendo Switch device id.</summary>
    [HttpPost("/Client/LoginWithNintendoSwitchDeviceId")]
    public Task<IHttpActionResult> Client_LoginWithNintendoSwitch(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithNintendoSwitchDeviceIdRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.NintendoSwitchDeviceId))
            return Task.FromResult(Err("InvalidParams", 1000, "NintendoSwitchDeviceId is required"));
        var swid = body.NintendoSwitchDeviceId;
        var player = ResolveOrCreatePlayer(
            p => p.NintendoId == swid,
            p => { p.NintendoId = swid; p.OriginPlatform = "NintendoSwitch"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with OpenID Connect id token.</summary>
    [HttpPost("/Client/LoginWithOpenIdConnect")]
    public Task<IHttpActionResult> Client_LoginWithOIDC(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithOpenIdConnectRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.IdToken))
            return Task.FromResult(Err("InvalidParams", 1000, "IdToken is required"));
        var sub = body.IdToken;
        var player = ResolveOrCreatePlayer(
            p => p.OpenIdSubject == sub,
            p => { p.OpenIdSubject = sub; p.OriginPlatform = "OpenID"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with PSN auth code.</summary>
    [HttpPost("/Client/LoginWithPSN")]
    public Task<IHttpActionResult> Client_LoginWithPSN(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithPSNRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.AuthCode))
            return Task.FromResult(Err("InvalidParams", 1000, "AuthCode is required"));
        var psnid = body.AuthCode;
        var player = ResolveOrCreatePlayer(
            p => p.PsnId == psnid,
            p => { p.PsnId = psnid; p.OriginPlatform = "PSN"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Login with Battle.net identity token.</summary>
    [HttpPost("/Client/LoginWithBattleNet")]
    public Task<IHttpActionResult> Client_LoginWithBattleNet(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<LoginWithBattleNetRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.IdentityToken))
            return Task.FromResult(Err("InvalidParams", 1000, "IdentityToken is required"));
        var sub = body.IdentityToken;
        var player = ResolveOrCreatePlayer(
            p => p.OpenIdSubject == sub,
            p => { p.OpenIdSubject = sub; p.OriginPlatform = "BattleNet"; },
            body.CreateAccount, out var nc);
        return Task.FromResult(Ok(IssueLoginResult(player, nc)));
    }

    /// <summary>Register a new PlayFab user with email/username/password.</summary>
    [HttpPost("/Client/RegisterPlayFabUser")]
    public Task<IHttpActionResult> Client_RegisterPlayFabUser(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<RegisterPlayFabUserRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Password))
            return Task.FromResult(Err("InvalidParams", 1000, "Password is required"));
        if (string.IsNullOrEmpty(body.Username) && string.IsNullOrEmpty(body.Email))
            return Task.FromResult(Err("InvalidParams", 1000, "Username or Email is required"));

        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var uname = body.Username ?? "";
        var email = body.Email ?? "";
        var dupe = coll.FindOne(p => p.Username == uname || p.Email == email);
        if (dupe != null)
            return Task.FromResult(Err("UsernameNotAvailable", 1014, "Username or email already exists"));

        var p = new PlayfabPlayerDbObject
        {
            PlayFabId    = NewPlayFabId(),
            EntityId     = NewId(),
            TitleId      = TITLE_ID,
            Username     = uname,
            Email        = email,
            DisplayName  = body.DisplayName ?? uname,
            PasswordHash = HashPassword(body.Password),
            Created      = NowUnix(),
            LastLogin    = NowUnix()
        };
        coll.Insert(p);

        var sessionTicket = IssueSessionTicket(p.PlayFabId);
        var entityToken   = IssueEntityToken(p.EntityId);
        p.SessionTicket = sessionTicket;
        p.SessionExpiration = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
        coll.Update(p);
        EnsureDefaultInventoryItems(p.EntityId);

        return Task.FromResult(Ok(new RegisterPlayFabUserResult
        {
            PlayFabId     = p.PlayFabId,
            SessionTicket = sessionTicket,
            Username      = p.Username,
            EntityToken   = new PlayFab.ClientModels.EntityTokenResponse
            {
                Entity          = new PlayFab.ClientModels.EntityKey { Id = p.EntityId, Type = "title_player_account" },
                EntityToken     = entityToken,
                TokenExpiration = DateTimeOffset.UtcNow.AddDays(1).UtcDateTime
            }
        }));
    }

    /// <summary>Returns the title's RSA public key (stub — not used in self-hosted mode).</summary>
    [HttpPost("/Client/GetTitlePublicKey")]
    public Task<IHttpActionResult> Client_GetTitlePublicKey(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult(Ok(new GetTitlePublicKeyResult { RSAPublicKey = "" }));

    /* ─── Authentication API (entity tokens) ───────────────────────────────── */

    // Newer PlayFabAllSDK builds no longer expose Entity on GetEntityTokenRequest, while some
    // older clients still send it. Parse that compatibility shape locally so both forms work.
    private sealed class GetEntityTokenCompatRequest
    {
        public Dictionary<string, string>? CustomTags { get; set; }
        public PlayFab.AuthenticationModels.EntityKey? Entity { get; set; }
    }

    /// <summary>Issue an entity token for the calling principal.</summary>
    [HttpPost("/Authentication/GetEntityToken")]
    public Task<IHttpActionResult> Authentication_GetEntityToken(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<GetEntityTokenCompatRequest>(request) ?? new GetEntityTokenCompatRequest();
        var entity = body.Entity;

        // Preserve the legacy optional Entity request field when supplied.  Otherwise
        // derive the principal from the authentication headers, matching PlayFab's
        // current GetEntityToken behaviour.
        string entityId;
        string entityType;

        if (!string.IsNullOrEmpty(entity?.Id))
        {
            entityId = entity!.Id;
            entityType = !string.IsNullOrEmpty(entity.Type) ? entity.Type : "title_player_account";
        }
        else
        {
            var existingEntityToken = Header(request, "X-EntityToken");
            var sessionTicket = Header(request, "X-Authorization");
            var secretKey = Header(request, "X-SecretKey");

            var parsed = ParseEntityToken(existingEntityToken ?? "");
            if (parsed.ok)
            {
                entityId = parsed.entityId;
                entityType = parsed.entityType;
            }
            else if (!string.IsNullOrEmpty(sessionTicket))
            {
                var playFabId = PlayFabIdFromSessionTicket(sessionTicket);
                var player = Database.GetCollection<PlayfabPlayerDbObject>(true)!
                    .FindOne(p => p.PlayFabId == playFabId);

                if (player == null)
                    return Task.FromResult(Unauthorized());

                entityId = player.EntityId;
                entityType = "title_player_account";
            }
            else if (IsConfiguredSecret(DEV_SECRET) &&
                     (FixedTimeEquals(secretKey ?? string.Empty, DEV_SECRET) ||
                      FixedTimeEquals(request.GetQueryParameter("DevSecretKey") ?? string.Empty, DEV_SECRET)))
            {
                entityId = TITLE_ID;
                entityType = "title";
            }
            else
            {
                return Task.FromResult(Unauthorized());
            }
        }

        var token = IssueEntityToken(entityId, entityType);
        var exp = DateTimeOffset.UtcNow.AddDays(1).UtcDateTime;
        return Task.FromResult(Ok(new GetEntityTokenResponse
        {
            EntityToken     = token,
            TokenExpiration = exp,
            Entity          = new PlayFab.AuthenticationModels.EntityKey { Id = entityId, Type = entityType }
        }));
    }

    /// <summary>Validate an entity token and return its claims.</summary>
    [HttpPost("/Authentication/ValidateEntityToken")]
    public Task<IHttpActionResult> Authentication_ValidateEntityToken(IHttpRequest request, IHttpResponse response)
    {
        var headerToken = Header(request, "X-EntityToken");
        if (string.IsNullOrEmpty(headerToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ValidateEntityTokenRequest>(request) ?? new ValidateEntityTokenRequest();
        var token = !string.IsNullOrEmpty(body.EntityToken) ? body.EntityToken : headerToken;
        var (entityId, entityType, ok) = ParseEntityToken(token);
        if (!ok) return Task.FromResult(Err("InvalidEntityToken", 1235, "Token is invalid or expired", HttpStatusCode.Unauthorized));
        return Task.FromResult(Ok(new ValidateEntityTokenResponse
        {
            Entity = new PlayFab.AuthenticationModels.EntityKey { Id = entityId, Type = entityType }
        }));
    }

    /// <summary>Game-server custom-id authentication (issues a server entity token).</summary>
    [HttpPost("/GameServerIdentity/AuthenticateGameServerWithCustomId")]
    public Task<IHttpActionResult> Authentication_AuthGameServer(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<AuthenticateCustomIdRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.CustomId))
            return Task.FromResult(Err("InvalidParams", 1000, "CustomId is required"));

        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var customId = body.CustomId;
        var existing = coll.FindOne(p => p.CustomId == customId && p.OriginPlatform == "GameServer");
        bool newlyCreated = false;
        if (existing == null)
        {
            existing = new PlayfabPlayerDbObject
            {
                PlayFabId      = NewPlayFabId(),
                EntityId       = NewId(),
                CustomId       = customId,
                OriginPlatform = "GameServer",
                TitleId        = TITLE_ID,
                Created        = NowUnix(),
                LastLogin      = NowUnix(),
            };
            coll.Insert(existing);
            newlyCreated = true;
        }
        var token = IssueEntityToken(existing.EntityId, "game_server");
        var exp = DateTimeOffset.UtcNow.AddDays(1).UtcDateTime;
        return Task.FromResult(Ok(new AuthenticateCustomIdResult
        {
            EntityToken = new PlayFab.AuthenticationModels.EntityTokenResponse
            {
                EntityToken     = token,
                TokenExpiration = exp,
                Entity          = new PlayFab.AuthenticationModels.EntityKey { Id = existing.EntityId, Type = "game_server" }
            },
            NewlyCreated = newlyCreated
        }));
    }

    /* ─── Token verification ──────────────────────────────────────────────── */

    private static (string entityId, string entityType, bool ok) ParseEntityToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return ("", "", false);
        var parts = token.Split('.');
        if (parts.Length != 3) return ("", "", false);
        var signing = $"{parts[0]}.{parts[1]}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ENTITY_KEY));
        var sig = Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(signing)));
        if (!FixedTimeEquals(sig, parts[2])) return ("", "", false);
        try
        {
            var pad = parts[1].Replace('-', '+').Replace('_', '/');
            pad += new string('=', (4 - pad.Length % 4) % 4);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(pad));
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var sub  = doc.RootElement.TryGetProperty("sub",  out var s) ? s.GetString() ?? "" : "";
            var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() ?? "title_player_account" : "title_player_account";
            var exp  = doc.RootElement.TryGetProperty("exp",  out var e) ? e.GetInt64() : 0;
            if (exp != 0 && exp < NowUnix()) return ("", "", false);
            return (sub, type, true);
        }
        catch { return ("", "", false); }
    }

    private static string PlayFabIdFromSessionTicket(string ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return "";
        var dash = ticket.IndexOf('-');
        return dash > 0 ? ticket.Substring(0, dash) : ticket;
    }

    internal static string PlayFabIdFromRequest(IHttpRequest req)
    {
        var ticket = Header(req, "X-Authorization");
        if (!string.IsNullOrEmpty(ticket)) return PlayFabIdFromSessionTicket(ticket);
        var entityToken = Header(req, "X-EntityToken");
        if (!string.IsNullOrEmpty(entityToken))
        {
            var (id, _, ok) = ParseEntityToken(entityToken);
            if (ok)
            {
                var p = Database.GetCollection<PlayfabPlayerDbObject>(true)!.FindOne(x => x.EntityId == id);
                if (p != null) return p.PlayFabId;
            }
        }
        return "";
    }
}
