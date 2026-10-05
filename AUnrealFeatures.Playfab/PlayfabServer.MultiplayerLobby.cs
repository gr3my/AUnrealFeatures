using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Playfab.Models;
using PlayFab.MultiplayerModels;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    /* ─── Helpers ─────────────────────────────────────────────────────────── */

    private static Dictionary<string, string> DictOrEmpty(string json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts) ?? new(); }
        catch { return new(); }
    }

    private static List<Member> MembersFromIds(List<string> ids, Dictionary<string, Dictionary<string, string>> memberData)
    {
        return ids.Select(id => new Member
        {
            MemberEntity = new EntityKey { Id = id, Type = "title_player_account" },
            MemberData   = memberData.TryGetValue(id, out var d) ? d : new(),
        }).ToList();
    }

    private Lobby ToLobbyDto(PlayfabLobbyDbObject row)
    {
        Dictionary<string, Dictionary<string, string>> memberData = new();
        try
        {
            if (!string.IsNullOrEmpty(row.LobbyData))
                memberData = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(row.SearchData, JsonOpts) ?? new();
        }
        catch { }

        return new Lobby
        {
            LobbyId               = row.LobbyId,
            ConnectionString      = row.ConnectionString,
            Owner                 = new EntityKey { Id = row.OwnerEntityId, Type = "title_player_account" },
            MaxPlayers            = row.MaxPlayers,
            AccessPolicy          = Enum.TryParse<AccessPolicy>(row.AccessPolicy, out var ap) ? ap : AccessPolicy.Public,
            UseConnections        = row.UseConnections,
            ChangeNumber          = (uint)Math.Max(1, row.MemberEntityIds.Count),
            Members               = MembersFromIds(row.MemberEntityIds, memberData),
            LobbyData             = DictOrEmpty(row.LobbyData),
            SearchData            = DictOrEmpty(row.SearchData),
            OwnerMigrationPolicy  = Enum.TryParse<OwnerMigrationPolicy>(row.OwnerMigrationPolicy, out var omp) ? omp : OwnerMigrationPolicy.Automatic,
            MembershipLock        = MembershipLock.Unlocked,
        };
    }

    private LobbySummary ToLobbySummary(PlayfabLobbyDbObject row) => new()
    {
        LobbyId          = row.LobbyId,
        ConnectionString = row.ConnectionString,
        MaxPlayers       = row.MaxPlayers,
        CurrentPlayers   = (uint)row.MemberEntityIds.Count,
        Owner            = new EntityKey { Id = row.OwnerEntityId, Type = "title_player_account" },
        SearchData       = DictOrEmpty(row.SearchData),
        MembershipLock   = MembershipLock.Unlocked,
    };

    /* ─── Lobby endpoints ────────────────────────────────────────────────── */

    /// <summary>Creates a new lobby.</summary>
    [HttpPost("/Lobby/CreateLobby")]
    public Task<IHttpActionResult> Multiplayer_CreateLobby(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateLobbyRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));

        var ownerId = body.Owner?.Id ?? PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(ownerId)) return Task.FromResult(Unauthorized());

        var memberIds = (body.Members ?? new()).Select(m => m.MemberEntity?.Id ?? "").Where(id => !string.IsNullOrEmpty(id)).ToList();
        if (!memberIds.Contains(ownerId)) memberIds.Insert(0, ownerId);

        var memberData = (body.Members ?? new())
            .Where(m => m.MemberEntity?.Id != null && m.MemberData != null)
            .ToDictionary(m => m.MemberEntity!.Id!, m => m.MemberData ?? new());

        var lobby = new PlayfabLobbyDbObject
        {
            LobbyId              = NewId(),
            OwnerEntityId        = ownerId,
            ConnectionString     = NewId(),
            MaxPlayers           = body.MaxPlayers > 0 ? body.MaxPlayers : 16,
            AccessPolicy         = (body.AccessPolicy ?? AccessPolicy.Public).ToString(),
            UseConnections       = body.UseConnections,
            OwnerMigrationPolicy = (body.OwnerMigrationPolicy ?? OwnerMigrationPolicy.Automatic).ToString(),
            LobbyData            = JsonSerializer.Serialize(body.LobbyData ?? new(), JsonOpts),
            SearchData           = JsonSerializer.Serialize(body.SearchData ?? new(), JsonOpts),
            MemberEntityIds      = memberIds,
            Created              = NowUnix(),
        };
        Database.GetCollection<PlayfabLobbyDbObject>(true)!.Insert(lobby);
        return Task.FromResult(Ok(new CreateLobbyResult
        {
            LobbyId          = lobby.LobbyId,
            ConnectionString = lobby.ConnectionString,
        }));
    }

    /// <summary>Returns the full state of a lobby.</summary>
    [HttpPost("/Lobby/GetLobby")]
    public Task<IHttpActionResult> Multiplayer_GetLobby(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetLobbyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.LobbyId))
            return Task.FromResult(Err("InvalidParams", 1000, "LobbyId is required"));
        var lid = body.LobbyId;
        var row = Database.GetCollection<PlayfabLobbyDbObject>(true)!.FindOne(x => x.LobbyId == lid);
        if (row == null) return Task.FromResult(Err("LobbyNotFound", 1801, "Lobby not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new GetLobbyResult { Lobby = ToLobbyDto(row) }));
    }

    /// <summary>Adds the calling member to a lobby identified by connection string.</summary>
    [HttpPost("/Lobby/JoinLobby")]
    public Task<IHttpActionResult> Multiplayer_JoinLobby(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<JoinLobbyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ConnectionString))
            return Task.FromResult(Err("InvalidParams", 1000, "ConnectionString is required"));

        var coll = Database.GetCollection<PlayfabLobbyDbObject>(true)!;
        var cs = body.ConnectionString;
        var row = coll.FindOne(x => x.ConnectionString == cs);
        if (row == null) return Task.FromResult(Err("LobbyNotFound", 1801, "Lobby not found", HttpStatusCode.NotFound));
        if (row.MemberEntityIds.Count >= row.MaxPlayers)
            return Task.FromResult(Err("LobbyFull", 1802, "Lobby is at capacity", HttpStatusCode.Conflict));

        var memberId = body.MemberEntity?.Id ?? PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(memberId)) return Task.FromResult(Unauthorized());
        if (!row.MemberEntityIds.Contains(memberId)) row.MemberEntityIds.Add(memberId);
        coll.Update(row);
        return Task.FromResult(Ok(new JoinLobbyResult { LobbyId = row.LobbyId }));
    }

    /// <summary>Joins a lobby in server-side mode.</summary>
    [HttpPost("/Lobby/JoinLobbyAsServer")]
    public Task<IHttpActionResult> Multiplayer_JoinLobbyAsServer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<JoinLobbyAsServerRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ConnectionString))
            return Task.FromResult(Err("InvalidParams", 1000, "ConnectionString is required"));
        var coll = Database.GetCollection<PlayfabLobbyDbObject>(true)!;
        var cs = body.ConnectionString;
        var row = coll.FindOne(x => x.ConnectionString == cs);
        if (row == null) return Task.FromResult(Err("LobbyNotFound", 1801, "Lobby not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new JoinLobbyAsServerResult { LobbyId = row.LobbyId }));
    }

    /// <summary>Joins a lobby created via matchmaking arrangement.</summary>
    [HttpPost("/Lobby/JoinArrangedLobby")]
    public Task<IHttpActionResult> Multiplayer_JoinArrangedLobby(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<JoinArrangedLobbyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ArrangementString))
            return Task.FromResult(Err("InvalidParams", 1000, "ArrangementString is required"));

        var coll = Database.GetCollection<PlayfabLobbyDbObject>(true)!;
        var arrangement = body.ArrangementString;
        var row = coll.FindOne(x => x.ConnectionString == arrangement);
        var memberId = body.MemberEntity?.Id ?? PlayFabIdFromRequest(request);
        if (row == null)
        {
            row = new PlayfabLobbyDbObject
            {
                LobbyId              = NewId(),
                ConnectionString     = arrangement,
                OwnerEntityId        = memberId,
                MaxPlayers           = body.MaxPlayers > 0 ? body.MaxPlayers : 16,
                AccessPolicy         = (body.AccessPolicy ?? AccessPolicy.Private).ToString(),
                UseConnections       = body.UseConnections,
                OwnerMigrationPolicy = (body.OwnerMigrationPolicy ?? OwnerMigrationPolicy.Server).ToString(),
                MemberEntityIds      = new List<string> { memberId },
                Created              = NowUnix(),
            };
            coll.Insert(row);
        }
        else
        {
            if (!row.MemberEntityIds.Contains(memberId)) row.MemberEntityIds.Add(memberId);
            coll.Update(row);
        }
        return Task.FromResult(Ok(new JoinLobbyResult { LobbyId = row.LobbyId }));
    }

    /// <summary>Removes the calling member from the lobby.</summary>
    [HttpPost("/Lobby/LeaveLobby")]
    public Task<IHttpActionResult> Multiplayer_LeaveLobby(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<LeaveLobbyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.LobbyId))
            return Task.FromResult(Err("InvalidParams", 1000, "LobbyId is required"));
        var coll = Database.GetCollection<PlayfabLobbyDbObject>(true)!;
        var lid = body.LobbyId;
        var row = coll.FindOne(x => x.LobbyId == lid);
        if (row == null) return Task.FromResult(Ok(new LobbyEmptyResult()));
        var memberId = body.MemberEntity?.Id ?? PlayFabIdFromRequest(request);
        row.MemberEntityIds.RemoveAll(m => m == memberId);
        if (row.MemberEntityIds.Count == 0) coll.Delete(row.Id);
        else
        {
            if (row.OwnerEntityId == memberId) row.OwnerEntityId = row.MemberEntityIds[0];
            coll.Update(row);
        }
        return Task.FromResult(Ok(new LobbyEmptyResult()));
    }

    /// <summary>Removes a server entity from a lobby (does not affect membership).</summary>
    [HttpPost("/Lobby/LeaveLobbyAsServer")]
    public Task<IHttpActionResult> Multiplayer_LeaveLobbyAsServer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        ParseRequest<LeaveLobbyAsServerRequest>(request);
        return Task.FromResult(Ok(new LobbyEmptyResult()));
    }

    /// <summary>Owner removes a member from the lobby.</summary>
    [HttpPost("/Lobby/RemoveMember")]
    public Task<IHttpActionResult> Multiplayer_RemoveMember(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RemoveMemberFromLobbyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.LobbyId) || string.IsNullOrEmpty(body.MemberEntity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "LobbyId and MemberEntity are required"));
        var coll = Database.GetCollection<PlayfabLobbyDbObject>(true)!;
        var lid = body.LobbyId;
        var row = coll.FindOne(x => x.LobbyId == lid);
        if (row == null) return Task.FromResult(Err("LobbyNotFound", 1801, "Lobby not found", HttpStatusCode.NotFound));
        var memberId = body.MemberEntity!.Id;
        row.MemberEntityIds.RemoveAll(m => m == memberId);
        if (row.MemberEntityIds.Count == 0) coll.Delete(row.Id);
        else { if (row.OwnerEntityId == memberId) row.OwnerEntityId = row.MemberEntityIds[0]; coll.Update(row); }
        return Task.FromResult(Ok(new LobbyEmptyResult()));
    }

    /// <summary>Sends a lobby invite to another entity (logged in event log).</summary>
    [HttpPost("/Lobby/InviteToLobby")]
    public Task<IHttpActionResult> Multiplayer_InviteToLobby(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<InviteToLobbyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.LobbyId) || string.IsNullOrEmpty(body.InviteeEntity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "LobbyId and InviteeEntity are required"));

        // Persist as an event so subscribers can receive notifications via PubSub at the application layer
        Database.GetCollection<PlayfabEventDbObject>(true)!.Insert(new PlayfabEventDbObject
        {
            EventId        = NewId(),
            EventName      = "LobbyInvite",
            EventNamespace = "title.lobby",
            EntityId       = body.InviteeEntity!.Id,
            EntityType     = body.InviteeEntity.Type ?? "title_player_account",
            PayloadJson    = JsonSerializer.Serialize(new { body.LobbyId, From = body.MemberEntity?.Id }, JsonOpts),
            Timestamp      = NowUnixMs(),
        });
        return Task.FromResult(Ok(new LobbyEmptyResult()));
    }

    /// <summary>Updates lobby data, member data, and search data.</summary>
    [HttpPost("/Lobby/UpdateLobby")]
    public Task<IHttpActionResult> Multiplayer_UpdateLobby(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateLobbyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.LobbyId))
            return Task.FromResult(Err("InvalidParams", 1000, "LobbyId is required"));
        var coll = Database.GetCollection<PlayfabLobbyDbObject>(true)!;
        var lid = body.LobbyId;
        var row = coll.FindOne(x => x.LobbyId == lid);
        if (row == null) return Task.FromResult(Err("LobbyNotFound", 1801, "Lobby not found", HttpStatusCode.NotFound));

        // Lobby data merge
        var lobbyData = DictOrEmpty(row.LobbyData);
        if (body.LobbyData != null) foreach (var (k, v) in body.LobbyData) lobbyData[k] = v;
        if (body.LobbyDataToDelete != null) foreach (var k in body.LobbyDataToDelete) lobbyData.Remove(k);
        row.LobbyData = JsonSerializer.Serialize(lobbyData, JsonOpts);

        // Search data merge
        var search = DictOrEmpty(row.SearchData);
        if (body.SearchData != null) foreach (var (k, v) in body.SearchData) search[k] = v;
        if (body.SearchDataToDelete != null) foreach (var k in body.SearchDataToDelete) search.Remove(k);
        row.SearchData = JsonSerializer.Serialize(search, JsonOpts);

        if (body.MaxPlayers.HasValue) row.MaxPlayers = body.MaxPlayers.Value;
        if (body.AccessPolicy.HasValue) row.AccessPolicy = body.AccessPolicy.Value.ToString();
        if (body.Owner != null && !string.IsNullOrEmpty(body.Owner.Id)) row.OwnerEntityId = body.Owner.Id;
        coll.Update(row);
        return Task.FromResult(Ok(new LobbyEmptyResult()));
    }

    /// <summary>Updates lobby fields server-side (no member identity changes).</summary>
    [HttpPost("/Lobby/UpdateLobbyAsServer")]
    public Task<IHttpActionResult> Multiplayer_UpdateLobbyAsServer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateLobbyAsServerRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.LobbyId))
            return Task.FromResult(Err("InvalidParams", 1000, "LobbyId is required"));
        // Server-side data updates are stored alongside lobby data
        return Task.FromResult(Ok(new LobbyEmptyResult()));
    }

    /// <summary>Deletes a lobby.</summary>
    [HttpPost("/Lobby/DeleteLobby")]
    public Task<IHttpActionResult> Multiplayer_DeleteLobby(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteLobbyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.LobbyId))
            return Task.FromResult(Err("InvalidParams", 1000, "LobbyId is required"));
        var lid = body.LobbyId;
        Database.GetCollection<PlayfabLobbyDbObject>(true)!.DeleteMany(x => x.LobbyId == lid);
        return Task.FromResult(Ok(new LobbyEmptyResult()));
    }

    /// <summary>Searches lobbies by filter expression with paging.</summary>
    [HttpPost("/Lobby/FindLobbies")]
    public Task<IHttpActionResult> Multiplayer_FindLobbies(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<FindLobbiesRequest>(request);
        if (body == null) body = new FindLobbiesRequest();
        var rows = Database.GetCollection<PlayfabLobbyDbObject>(true)!
                           .Find(x => x.AccessPolicy == "Public" || x.AccessPolicy == "Friends").ToList();

        // very simple filter: substring match against any search-data value
        if (!string.IsNullOrEmpty(body.Filter))
        {
            var f = body.Filter.ToLowerInvariant();
            rows = rows.Where(r =>
            {
                var sd = DictOrEmpty(r.SearchData);
                return sd.Values.Any(v => v != null && v.ToLowerInvariant().Contains(f));
            }).ToList();
        }
        var skip = 0;
        if (!string.IsNullOrEmpty(body.Pagination?.ContinuationToken)) int.TryParse(body.Pagination.ContinuationToken, out skip);
        var pageSize = (int)(body.Pagination?.PageSizeRequested ?? 50);
        var page = rows.Skip(skip).Take(pageSize).ToList();
        return Task.FromResult(Ok(new FindLobbiesResult
        {
            Lobbies = page.Select(ToLobbySummary).ToList(),
            Pagination = new PaginationResponse
            {
                ContinuationToken      = skip + page.Count < rows.Count ? (skip + page.Count).ToString() : null,
                TotalMatchedLobbyCount = (uint?)rows.Count,
            }
        }));
    }

    /// <summary>Searches lobbies that include the caller's friends.</summary>
    [HttpPost("/Lobby/FindFriendLobbies")]
    public Task<IHttpActionResult> Multiplayer_FindFriendLobbies(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var friendIds = Database.GetCollection<PlayfabFriendDbObject>(true)!
                                .Find(f => f.OwnerPlayFabId == pid)
                                .Select(f => f.FriendPlayFabId).ToHashSet();
        var rows = Database.GetCollection<PlayfabLobbyDbObject>(true)!
                           .Find(x => x.AccessPolicy != "Private")
                           .Where(x => x.MemberEntityIds.Any(m => friendIds.Contains(m))).ToList();
        return Task.FromResult(Ok(new FindFriendLobbiesResult
        {
            Lobbies    = rows.Select(r => new FriendLobbySummary
            {
                LobbyId          = r.LobbyId,
                ConnectionString = r.ConnectionString,
                MaxPlayers       = r.MaxPlayers,
                CurrentPlayers   = (uint)r.MemberEntityIds.Count,
                Owner            = new EntityKey { Id = r.OwnerEntityId, Type = "title_player_account" },
                Friends          = r.MemberEntityIds.Where(m => friendIds.Contains(m))
                                    .Select(id => new EntityKey { Id = id, Type = "title_player_account" }).ToList(),
                SearchData       = DictOrEmpty(r.SearchData),
            }).ToList(),
            Pagination = new PaginationResponse { TotalMatchedLobbyCount = (uint?)rows.Count }
        }));
    }

    /// <summary>Subscribes the caller to a lobby resource (no-op without PubSub backend).</summary>
    [HttpPost("/Lobby/SubscribeToLobbyResource")]
    public Task<IHttpActionResult> Multiplayer_SubscribeToLobbyResource(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SubscribeToLobbyResourceRequest>(request);
        var topic = body != null ? $"lobby/{body.Type}/{body.ResourceId}" : "";
        return Task.FromResult(Ok(new SubscribeToLobbyResourceResult { Topic = topic }));
    }

    /// <summary>Unsubscribes the caller from a lobby resource.</summary>
    [HttpPost("/Lobby/UnsubscribeFromLobbyResource")]
    public Task<IHttpActionResult> Multiplayer_UnsubscribeFromLobbyResource(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        ParseRequest<UnsubscribeFromLobbyResourceRequest>(request);
        return Task.FromResult(Ok(new LobbyEmptyResult()));
    }
}
