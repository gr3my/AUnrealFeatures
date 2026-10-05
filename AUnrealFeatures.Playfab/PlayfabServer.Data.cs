using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Playfab.Models;
using PlayFab.ClientModels;
using System.Net;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    /* ─── Title Data ──────────────────────────────────────────────────────── */

    /// <summary>Returns title data key/value pairs.</summary>
    [HttpPost("/Client/GetTitleData")]
    public Task<IHttpActionResult> Client_GetTitleData(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());

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

    /* ─── User Data ───────────────────────────────────────────────────────── */

    private IHttpActionResult ReadUserData(IHttpRequest request, string scope, bool readOnly)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Unauthorized();

        var body = ParseRequest<GetUserDataRequest>(request) ?? new GetUserDataRequest();
        var pid = string.IsNullOrEmpty(body.PlayFabId) ? PlayFabIdFromRequest(request) : body.PlayFabId;
        if (string.IsNullOrEmpty(pid)) return Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound);

        var coll = Database.GetCollection<PlayfabUserDataDbObject>(true)!;
        var rows = coll.Find(d => d.PlayFabId == pid && d.Scope == scope).ToList();

        // ReadOnly access type maps to internal/read-only data — our schema uses Permission to gate writes,
        // but for read endpoints we simply return everything in the requested scope.
        var keys = body.Keys != null && body.Keys.Count > 0 ? new HashSet<string>(body.Keys) : null;
        var data = new Dictionary<string, UserDataRecord>();
        foreach (var r in rows)
        {
            if (keys != null && !keys.Contains(r.Key)) continue;
            data[r.Key] = new UserDataRecord
            {
                Value       = r.Value,
                LastUpdated = DateTimeOffset.FromUnixTimeSeconds(r.LastUpdated).UtcDateTime,
                Permission  = r.Permission == "Public" ? UserDataPermission.Public : UserDataPermission.Private,
            };
        }
        return Ok(new GetUserDataResult { Data = data, DataVersion = (uint)data.Count });
    }

    /// <summary>Get the player's title-scoped user data (mutable).</summary>
    [HttpPost("/Client/GetUserData")]
    public Task<IHttpActionResult> Client_GetUserData(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult(ReadUserData(request, "Title", false));

    /// <summary>Get the player's read-only title-scoped user data.</summary>
    [HttpPost("/Client/GetUserReadOnlyData")]
    public Task<IHttpActionResult> Client_GetUserReadOnlyData(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult(ReadUserData(request, "TitleReadOnly", true));

    /// <summary>Get the publisher-scoped user data.</summary>
    [HttpPost("/Client/GetUserPublisherData")]
    public Task<IHttpActionResult> Client_GetUserPublisherData(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult(ReadUserData(request, "Publisher", false));

    /// <summary>Get the read-only publisher-scoped user data.</summary>
    [HttpPost("/Client/GetUserPublisherReadOnlyData")]
    public Task<IHttpActionResult> Client_GetUserPublisherReadOnlyData(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult(ReadUserData(request, "PublisherReadOnly", true));

    private IHttpActionResult WriteUserData(IHttpRequest request, string scope)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Unauthorized();

        var body = ParseRequest<UpdateUserDataRequest>(request);
        if (body == null) return Err("InvalidParams", 1000, "Body required");

        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Unauthorized();

        var coll = Database.GetCollection<PlayfabUserDataDbObject>(true)!;
        var perm = body.Permission == UserDataPermission.Public ? "Public" : "Private";
        var now  = NowUnix();

        if (body.Data != null)
        {
            foreach (var (k, v) in body.Data)
            {
                var key = k;
                var existing = coll.FindOne(d => d.PlayFabId == pid && d.Scope == scope && d.Key == key);
                if (existing == null)
                {
                    coll.Insert(new PlayfabUserDataDbObject
                    {
                        PlayFabId   = pid,
                        Scope       = scope,
                        Key         = k,
                        Value       = v ?? "",
                        Permission  = perm,
                        LastUpdated = now,
                    });
                }
                else
                {
                    existing.Value       = v ?? "";
                    existing.Permission  = perm;
                    existing.LastUpdated = now;
                    coll.Update(existing);
                }
            }
        }
        if (body.KeysToRemove != null)
        {
            foreach (var key in body.KeysToRemove)
            {
                var k = key;
                coll.DeleteMany(d => d.PlayFabId == pid && d.Scope == scope && d.Key == k);
            }
        }
        var version = (uint)coll.Count(d => d.PlayFabId == pid && d.Scope == scope);
        return Ok(new UpdateUserDataResult { DataVersion = version });
    }

    /// <summary>Update the player's title-scoped user data.</summary>
    [HttpPost("/Client/UpdateUserData")]
    public Task<IHttpActionResult> Client_UpdateUserData(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult(WriteUserData(request, "Title"));

    /// <summary>Update the publisher-scoped user data.</summary>
    [HttpPost("/Client/UpdateUserPublisherData")]
    public Task<IHttpActionResult> Client_UpdateUserPublisherData(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult(WriteUserData(request, "Publisher"));

    /* ─── Friends ─────────────────────────────────────────────────────────── */

    /// <summary>List the calling player's friends.</summary>
    [HttpPost("/Client/GetFriendsList")]
    public Task<IHttpActionResult> Client_GetFriendsList(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());

        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var fcoll = Database.GetCollection<PlayfabFriendDbObject>(true)!;
        var pcoll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;

        var rows = fcoll.Find(f => f.OwnerPlayFabId == pid).ToList();
        var friends = rows.Select(f =>
        {
            var fid = f.FriendPlayFabId;
            var profile = pcoll.FindOne(p => p.PlayFabId == fid);
            return new FriendInfo
            {
                FriendPlayFabId  = f.FriendPlayFabId,
                Username         = profile?.Username ?? f.Username,
                TitleDisplayName = profile?.DisplayName ?? f.TitleDisplayName,
                Tags             = f.Tags ?? new List<string>(),
            };
        }).ToList();
        return Task.FromResult(Ok(new GetFriendsListResult { Friends = friends }));
    }

    /// <summary>Add a friend to the calling player's friend list.</summary>
    [HttpPost("/Client/AddFriend")]
    public Task<IHttpActionResult> Client_AddFriend(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<AddFriendRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));

        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var pcoll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        PlayfabPlayerDbObject? target = null;
        if (!string.IsNullOrEmpty(body.FriendPlayFabId))
            target = pcoll.FindOne(p => p.PlayFabId == body.FriendPlayFabId);
        else if (!string.IsNullOrEmpty(body.FriendUsername))
            target = pcoll.FindOne(p => p.Username == body.FriendUsername);
        else if (!string.IsNullOrEmpty(body.FriendEmail))
            target = pcoll.FindOne(p => p.Email == body.FriendEmail);
        else if (!string.IsNullOrEmpty(body.FriendTitleDisplayName))
            target = pcoll.FindOne(p => p.DisplayName == body.FriendTitleDisplayName);

        if (target == null)
            return Task.FromResult(Err("AccountNotFound", 1001, "Friend not found", HttpStatusCode.NotFound));

        var fcoll = Database.GetCollection<PlayfabFriendDbObject>(true)!;
        var tid   = target.PlayFabId;
        var dupe  = fcoll.FindOne(f => f.OwnerPlayFabId == pid && f.FriendPlayFabId == tid);
        if (dupe != null) return Task.FromResult(Ok(new AddFriendResult { Created = false }));

        fcoll.Insert(new PlayfabFriendDbObject
        {
            OwnerPlayFabId   = pid,
            FriendPlayFabId  = target.PlayFabId,
            Username         = target.Username,
            TitleDisplayName = target.DisplayName,
            Created          = NowUnix(),
        });
        return Task.FromResult(Ok(new AddFriendResult { Created = true }));
    }

    /// <summary>Remove a friend from the calling player's friend list.</summary>
    [HttpPost("/Client/RemoveFriend")]
    public Task<IHttpActionResult> Client_RemoveFriend(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RemoveFriendRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FriendPlayFabId))
            return Task.FromResult(Err("InvalidParams", 1000, "FriendPlayFabId is required"));

        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var coll = Database.GetCollection<PlayfabFriendDbObject>(true)!;
        var tid = body.FriendPlayFabId;
        coll.DeleteMany(f => f.OwnerPlayFabId == pid && f.FriendPlayFabId == tid);
        return Task.FromResult(Ok(new RemoveFriendResult()));
    }

    /// <summary>Replace the tag list on an existing friend entry.</summary>
    [HttpPost("/Client/SetFriendTags")]
    public Task<IHttpActionResult> Client_SetFriendTags(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SetFriendTagsRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FriendPlayFabId))
            return Task.FromResult(Err("InvalidParams", 1000, "FriendPlayFabId is required"));

        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var coll = Database.GetCollection<PlayfabFriendDbObject>(true)!;
        var tid  = body.FriendPlayFabId;
        var entry = coll.FindOne(f => f.OwnerPlayFabId == pid && f.FriendPlayFabId == tid);
        if (entry == null) return Task.FromResult(Err("AccountNotFound", 1001, "Friend not found", HttpStatusCode.NotFound));

        entry.Tags = body.Tags ?? new List<string>();
        coll.Update(entry);
        return Task.FromResult(Ok(new SetFriendTagsResult()));
    }

    /* ─── Account / profile / time / news ─────────────────────────────────── */

    /// <summary>Returns the calling player's account info.</summary>
    [HttpPost("/Client/GetAccountInfo")]
    public Task<IHttpActionResult> Client_GetAccountInfo(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetAccountInfoRequest>(request) ?? new GetAccountInfoRequest();
        var pid = !string.IsNullOrEmpty(body.PlayFabId) ? body.PlayFabId : PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var p = coll.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound));

        return Task.FromResult(Ok(new GetAccountInfoResult
        {
            AccountInfo = new UserAccountInfo
            {
                PlayFabId    = p.PlayFabId,
                Created      = DateTimeOffset.FromUnixTimeSeconds(p.Created).UtcDateTime,
                Username     = p.Username,
                TitleInfo    = new UserTitleInfo
                {
                    DisplayName = p.DisplayName,
                    Created     = DateTimeOffset.FromUnixTimeSeconds(p.Created).UtcDateTime,
                    LastLogin   = DateTimeOffset.FromUnixTimeSeconds(p.LastLogin).UtcDateTime,
                    AvatarUrl   = p.AvatarUrl,
                },
                PrivateInfo  = new UserPrivateAccountInfo { Email = p.Email },
            }
        }));
    }

    /// <summary>Returns the player's public profile.</summary>
    [HttpPost("/Client/GetPlayerProfile")]
    public Task<IHttpActionResult> Client_GetPlayerProfile(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayerProfileRequest>(request) ?? new GetPlayerProfileRequest();
        var pid = !string.IsNullOrEmpty(body.PlayFabId) ? body.PlayFabId : PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var p = coll.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound));

        return Task.FromResult(Ok(new GetPlayerProfileResult
        {
            PlayerProfile = new PlayerProfileModel
            {
                PlayerId       = p.PlayFabId,
                TitleId        = p.TitleId,
                DisplayName    = p.DisplayName,
                AvatarUrl      = p.AvatarUrl,
                Created        = DateTimeOffset.FromUnixTimeSeconds(p.Created).UtcDateTime,
                LastLogin      = DateTimeOffset.FromUnixTimeSeconds(p.LastLogin).UtcDateTime,
                BannedUntil    = null,
                Tags           = p.Tags?.Select(t => new TagModel { TagValue = t }).ToList() ?? new(),
            }
        }));
    }

    /// <summary>Returns the server's current UTC time.</summary>
    [HttpPost("/Client/GetTime")]
    public Task<IHttpActionResult> Client_GetTime(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        return Task.FromResult(Ok(new GetTimeResult { Time = DateTime.UtcNow }));
    }

    /// <summary>Returns the latest title news items (currently empty list).</summary>
    [HttpPost("/Client/GetTitleNews")]
    public Task<IHttpActionResult> Client_GetTitleNews(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        return Task.FromResult(Ok(new GetTitleNewsResult { News = new List<TitleNewsItem>() }));
    }
}
