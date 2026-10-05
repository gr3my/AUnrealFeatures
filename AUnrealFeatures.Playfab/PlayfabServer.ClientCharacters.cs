using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Playfab.Models;
using PlayFab.ClientModels;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    /* ─── Characters ──────────────────────────────────────────────────────── */

    /// <summary>Returns the list of characters owned by a player.</summary>
    [HttpPost("/Client/GetAllUsersCharacters")]
    public Task<IHttpActionResult> Client_GetAllUsersCharacters(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ListUsersCharactersRequest>(request);
        var pid = !string.IsNullOrEmpty(body?.PlayFabId) ? body!.PlayFabId : PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabCharacterDbObject>(true)!
                           .Find(c => c.PlayFabId == pid).ToList();
        return Task.FromResult(Ok(new ListUsersCharactersResult
        {
            Characters = rows.Select(r => new CharacterResult
            {
                CharacterId   = r.CharacterId,
                CharacterName = r.CharacterName,
                CharacterType = r.CharacterType,
            }).ToList(),
        }));
    }

    /// <summary>Grants a character to the calling player.</summary>
    [HttpPost("/Client/GrantCharacterToUser")]
    public Task<IHttpActionResult> Client_GrantCharacterToUser(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GrantCharacterToUserRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());
        var c = new PlayfabCharacterDbObject
        {
            PlayFabId     = pid,
            CharacterId   = NewId(),
            CharacterName = body.CharacterName ?? "Character",
            CharacterType = body.ItemId ?? "default",
            Created       = NowUnix(),
        };
        Database.GetCollection<PlayfabCharacterDbObject>(true)!.Insert(c);
        return Task.FromResult(Ok(new GrantCharacterToUserResult
        {
            CharacterId   = c.CharacterId,
            CharacterType = c.CharacterType,
            Result        = true,
        }));
    }

    /* ─── Character Data ──────────────────────────────────────────────────── */

    private IHttpActionResult ReadCharData(IHttpRequest req, string scope)
    {
        if (!CheckAuth(req, AuthType.LoginSession)) return Unauthorized();
        var body = ParseRequest<GetCharacterDataRequest>(req);
        if (body == null || string.IsNullOrEmpty(body.CharacterId))
            return Err("InvalidParams", 1000, "CharacterId required");
        var pid = !string.IsNullOrEmpty(body.PlayFabId) ? body.PlayFabId : PlayFabIdFromRequest(req);
        if (string.IsNullOrEmpty(pid)) return Unauthorized();

        var cid = body.CharacterId;
        var coll = Database.GetCollection<PlayfabCharacterDataDbObject>(true)!;
        var rows = coll.Find(d => d.PlayFabId == pid && d.CharacterId == cid).ToList();
        if (scope == "ReadOnly") rows = rows.Where(r => r.ReadOnly).ToList();
        else                     rows = rows.Where(r => !r.ReadOnly).ToList();
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
        return Ok(new GetCharacterDataResult { CharacterId = cid, Data = data, DataVersion = (uint)data.Count });
    }

    /// <summary>Returns mutable character custom data.</summary>
    [HttpPost("/Client/GetCharacterData")]
    public Task<IHttpActionResult> Client_GetCharacterData(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult(ReadCharData(request, "ReadWrite"));

    /// <summary>Returns read-only character custom data.</summary>
    [HttpPost("/Client/GetCharacterReadOnlyData")]
    public Task<IHttpActionResult> Client_GetCharacterReadOnlyData(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult(ReadCharData(request, "ReadOnly"));

    /// <summary>Updates mutable character custom data.</summary>
    [HttpPost("/Client/UpdateCharacterData")]
    public Task<IHttpActionResult> Client_UpdateCharacterData(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateCharacterDataRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.CharacterId))
            return Task.FromResult(Err("InvalidParams", 1000, "CharacterId required"));
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var coll = Database.GetCollection<PlayfabCharacterDataDbObject>(true)!;
        var cid  = body.CharacterId;
        var perm = body.Permission == UserDataPermission.Public ? "Public" : "Private";

        if (body.Data != null)
            foreach (var (k, v) in body.Data)
            {
                var key = k;
                var existing = coll.FindOne(d => d.PlayFabId == pid && d.CharacterId == cid && d.Key == key && !d.ReadOnly);
                if (existing == null)
                    coll.Insert(new PlayfabCharacterDataDbObject
                    {
                        PlayFabId   = pid,
                        CharacterId = cid,
                        Key         = k,
                        Value       = v ?? "",
                        Permission  = perm,
                        ReadOnly    = false,
                        LastUpdated = NowUnix(),
                    });
                else
                {
                    existing.Value      = v ?? "";
                    existing.Permission = perm;
                    existing.LastUpdated = NowUnix();
                    coll.Update(existing);
                }
            }
        if (body.KeysToRemove != null)
            foreach (var key in body.KeysToRemove)
            {
                var k = key;
                coll.DeleteMany(d => d.PlayFabId == pid && d.CharacterId == cid && d.Key == k && !d.ReadOnly);
            }
        var version = (uint)coll.Count(d => d.PlayFabId == pid && d.CharacterId == cid && !d.ReadOnly);
        return Task.FromResult(Ok(new UpdateCharacterDataResult { DataVersion = version }));
    }

    /* ─── Character Inventory ─────────────────────────────────────────────── */

    /// <summary>Returns a character's inventory.</summary>
    [HttpPost("/Client/GetCharacterInventory")]
    public Task<IHttpActionResult> Client_GetCharacterInventory(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetCharacterInventoryRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.CharacterId))
            return Task.FromResult(Err("InvalidParams", 1000, "CharacterId required"));
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var cid = body.CharacterId;
        var inventory = Database.GetCollection<PlayfabInventoryItemDbObject>(true)!
                                .Find(i => i.PlayFabId == pid && i.CharacterId == cid)
                                .Select(InventoryRowToInstance).ToList();
        var currencies = Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!
                                 .Find(v => v.PlayFabId == pid && v.CharacterId == cid)
                                 .ToDictionary(v => v.CurrencyCode, v => v.Balance);
        return Task.FromResult(Ok(new GetCharacterInventoryResult
        {
            CharacterId               = cid,
            Inventory                 = inventory,
            VirtualCurrency           = currencies,
            VirtualCurrencyRechargeTimes = new(),
        }));
    }

    private static ItemInstance InventoryRowToInstance(PlayfabInventoryItemDbObject r) => new()
    {
        ItemId            = r.ItemId,
        ItemInstanceId    = r.ItemInstanceId,
        ItemClass         = r.ItemClass,
        CatalogVersion    = r.CatalogVersion,
        DisplayName       = r.DisplayName,
        Annotation        = r.Annotation,
        PurchaseDate      = r.PurchaseDate > 0 ? DateTimeOffset.FromUnixTimeSeconds(r.PurchaseDate).UtcDateTime : (DateTime?)null,
        Expiration        = r.Expiration.HasValue ? DateTimeOffset.FromUnixTimeSeconds(r.Expiration.Value).UtcDateTime : (DateTime?)null,
        RemainingUses     = r.RemainingUses,
        UsesIncrementedBy = r.UsesIncrementedBy,
        UnitCurrency      = r.UnitCurrency,
        UnitPrice         = r.UnitPrice,
        BundleParent      = r.BundleParent,
        BundleContents    = r.BundleContents,
        CustomData        = r.CustomData,
    };

    /* ─── Character Statistics ────────────────────────────────────────────── */

    /// <summary>Returns a character's statistics.</summary>
    [HttpPost("/Client/GetCharacterStatistics")]
    public Task<IHttpActionResult> Client_GetCharacterStatistics(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetCharacterStatisticsRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.CharacterId))
            return Task.FromResult(Err("InvalidParams", 1000, "CharacterId required"));
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var cid = body.CharacterId;
        var stats = Database.GetCollection<PlayfabStatisticDbObject>(true)!
                            .Find(s => s.PlayFabId == pid && s.CharacterId == cid)
                            .ToDictionary(s => s.StatisticName, s => s.Value);
        return Task.FromResult(Ok(new GetCharacterStatisticsResult { CharacterStatistics = stats }));
    }

    /// <summary>Updates a character's statistics.</summary>
    [HttpPost("/Client/UpdateCharacterStatistics")]
    public Task<IHttpActionResult> Client_UpdateCharacterStatistics(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateCharacterStatisticsRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.CharacterId))
            return Task.FromResult(Err("InvalidParams", 1000, "CharacterId required"));
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var cid = body.CharacterId;
        var coll = Database.GetCollection<PlayfabStatisticDbObject>(true)!;
        if (body.CharacterStatistics != null)
            foreach (var (name, value) in body.CharacterStatistics)
            {
                var n = name;
                var existing = coll.FindOne(s => s.PlayFabId == pid && s.CharacterId == cid && s.StatisticName == n);
                if (existing == null)
                    coll.Insert(new PlayfabStatisticDbObject { PlayFabId = pid, CharacterId = cid, StatisticName = n, Value = value });
                else { existing.Value = value; coll.Update(existing); }
            }
        return Task.FromResult(Ok(new UpdateCharacterStatisticsResult()));
    }

    /* ─── Character Leaderboards ──────────────────────────────────────────── */

    private List<CharacterLeaderboardEntry> RankCharStat(string stat)
    {
        var pcoll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var ccoll = Database.GetCollection<PlayfabCharacterDbObject>(true)!;
        var rows = Database.GetCollection<PlayfabStatisticDbObject>(true)!
                           .Find(s => s.StatisticName == stat && !string.IsNullOrEmpty(s.CharacterId))
                           .OrderByDescending(s => s.Value).ToList();
        var ranked = new List<CharacterLeaderboardEntry>();
        int pos = 0;
        foreach (var r in rows)
        {
            var cid = r.CharacterId;
            var rid = r.PlayFabId;
            var c = ccoll.FindOne(x => x.CharacterId == cid);
            var p = pcoll.FindOne(x => x.PlayFabId == rid);
            ranked.Add(new CharacterLeaderboardEntry
            {
                PlayFabId     = r.PlayFabId,
                CharacterId   = r.CharacterId,
                CharacterName = c?.CharacterName ?? "",
                CharacterType = c?.CharacterType ?? "",
                DisplayName   = p?.DisplayName ?? "",
                StatValue     = r.Value,
                Position      = pos++,
            });
        }
        return ranked;
    }

    /// <summary>Returns a paged character leaderboard.</summary>
    [HttpPost("/Client/GetCharacterLeaderboard")]
    public Task<IHttpActionResult> Client_GetCharacterLeaderboard(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetCharacterLeaderboardRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.StatisticName))
            return Task.FromResult(Err("InvalidParams", 1000, "StatisticName required"));
        var ranked = RankCharStat(body.StatisticName);
        var page = ranked.Skip(body.StartPosition).Take(body.MaxResultsCount ?? 100).ToList();
        return Task.FromResult(Ok(new GetCharacterLeaderboardResult { Leaderboard = page }));
    }

    /// <summary>Returns the leaderboard centered on a specific character.</summary>
    [HttpPost("/Client/GetLeaderboardAroundCharacter")]
    public Task<IHttpActionResult> Client_GetLeaderboardAroundCharacter(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetLeaderboardAroundCharacterRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.StatisticName) || string.IsNullOrEmpty(body.CharacterId))
            return Task.FromResult(Err("InvalidParams", 1000, "StatisticName and CharacterId required"));
        var ranked = RankCharStat(body.StatisticName);
        var idx = ranked.FindIndex(r => r.CharacterId == body.CharacterId);
        var radius = (body.MaxResultsCount ?? 100) / 2;
        var start = Math.Max(0, idx - radius);
        var take  = Math.Min(ranked.Count - start, radius * 2 + 1);
        return Task.FromResult(Ok(new GetCharacterLeaderboardResult
        {
            Leaderboard = idx < 0 ? new() : ranked.GetRange(start, take),
        }));
    }

    /// <summary>Returns a leaderboard scoped to the calling player's characters.</summary>
    [HttpPost("/Client/GetLeaderboardForUserCharacters")]
    public Task<IHttpActionResult> Client_GetLeaderboardForUserCharacters(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetLeaderboardForUsersCharactersRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.StatisticName))
            return Task.FromResult(Err("InvalidParams", 1000, "StatisticName required"));
        var pid = PlayFabIdFromRequest(request);
        var ranked = RankCharStat(body.StatisticName).Where(e => e.PlayFabId == pid).ToList();
        return Task.FromResult(Ok(new GetLeaderboardForUsersCharactersResult { Leaderboard = ranked }));
    }

    /// <summary>Writes a character analytics event.</summary>
    [HttpPost("/Client/WriteCharacterEvent")]
    public Task<IHttpActionResult> Client_WriteCharacterEvent(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<WriteClientCharacterEventRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.EventName))
            return Task.FromResult(Err("InvalidParams", 1000, "EventName required"));
        var ev = new PlayfabEventDbObject
        {
            EventId        = NewId(),
            EventName      = body.EventName,
            EventNamespace = "title.character",
            EntityId       = body.CharacterId ?? "",
            EntityType     = "character",
            PayloadJson    = JsonSerializer.Serialize(body.Body ?? new(), JsonOpts),
            Timestamp      = body.Timestamp.HasValue
                                ? new DateTimeOffset(body.Timestamp.Value, TimeSpan.Zero).ToUnixTimeMilliseconds()
                                : NowUnixMs(),
        };
        Database.GetCollection<PlayfabEventDbObject>(true)!.Insert(ev);
        return Task.FromResult(Ok(new WriteEventResponse { EventId = ev.EventId }));
    }

    /* ─── Shared Groups ───────────────────────────────────────────────────── */

    /// <summary>Creates a shared group.</summary>
    [HttpPost("/Client/CreateSharedGroup")]
    public Task<IHttpActionResult> Client_CreateSharedGroup(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateSharedGroupRequest>(request);
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());
        var sgid = string.IsNullOrEmpty(body?.SharedGroupId) ? NewId() : body.SharedGroupId;
        var coll = Database.GetCollection<PlayfabSharedGroupDbObject>(true)!;
        var sgidLocal = sgid;
        var existing = coll.FindOne(g => g.SharedGroupId == sgidLocal);
        if (existing == null)
        {
            coll.Insert(new PlayfabSharedGroupDbObject
            {
                SharedGroupId = sgid,
                Members       = new List<string> { pid },
                Data          = new(),
                Created       = NowUnix(),
            });
        }
        return Task.FromResult(Ok(new CreateSharedGroupResult { SharedGroupId = sgid }));
    }

    /// <summary>Adds members to a shared group.</summary>
    [HttpPost("/Client/AddSharedGroupMembers")]
    public Task<IHttpActionResult> Client_AddSharedGroupMembers(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<AddSharedGroupMembersRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.SharedGroupId) || body.PlayFabIds == null)
            return Task.FromResult(Err("InvalidParams", 1000, "SharedGroupId and PlayFabIds required"));
        var coll = Database.GetCollection<PlayfabSharedGroupDbObject>(true)!;
        var sgid = body.SharedGroupId;
        var g = coll.FindOne(x => x.SharedGroupId == sgid);
        if (g == null) return Task.FromResult(Err("SharedGroupNotFound", 1310, "Shared group not found", HttpStatusCode.NotFound));
        foreach (var id in body.PlayFabIds) if (!g.Members.Contains(id)) g.Members.Add(id);
        coll.Update(g);
        return Task.FromResult(Ok(new AddSharedGroupMembersResult()));
    }

    /// <summary>Removes members from a shared group.</summary>
    [HttpPost("/Client/RemoveSharedGroupMembers")]
    public Task<IHttpActionResult> Client_RemoveSharedGroupMembers(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RemoveSharedGroupMembersRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.SharedGroupId) || body.PlayFabIds == null)
            return Task.FromResult(Err("InvalidParams", 1000, "SharedGroupId and PlayFabIds required"));
        var coll = Database.GetCollection<PlayfabSharedGroupDbObject>(true)!;
        var sgid = body.SharedGroupId;
        var g = coll.FindOne(x => x.SharedGroupId == sgid);
        if (g == null) return Task.FromResult(Err("SharedGroupNotFound", 1310, "Shared group not found", HttpStatusCode.NotFound));
        g.Members.RemoveAll(m => body.PlayFabIds.Contains(m));
        coll.Update(g);
        return Task.FromResult(Ok(new RemoveSharedGroupMembersResult()));
    }

    /// <summary>Returns shared group data.</summary>
    [HttpPost("/Client/GetSharedGroupData")]
    public Task<IHttpActionResult> Client_GetSharedGroupData(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetSharedGroupDataRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.SharedGroupId))
            return Task.FromResult(Err("InvalidParams", 1000, "SharedGroupId required"));
        var sgid = body.SharedGroupId;
        var g = Database.GetCollection<PlayfabSharedGroupDbObject>(true)!.FindOne(x => x.SharedGroupId == sgid);
        if (g == null) return Task.FromResult(Err("SharedGroupNotFound", 1310, "Shared group not found", HttpStatusCode.NotFound));
        var keys = body.Keys != null && body.Keys.Count > 0 ? new HashSet<string>(body.Keys) : null;
        var data = new Dictionary<string, SharedGroupDataRecord>();
        foreach (var (k, v) in g.Data)
        {
            if (keys != null && !keys.Contains(k)) continue;
            data[k] = new SharedGroupDataRecord
            {
                Value         = v.Value,
                LastUpdated   = DateTimeOffset.FromUnixTimeSeconds(v.LastUpdated).UtcDateTime,
                LastUpdatedBy = v.LastUpdatedBy,
                Permission    = v.Permission == "Public" ? UserDataPermission.Public : UserDataPermission.Private,
            };
        }
        return Task.FromResult(Ok(new GetSharedGroupDataResult
        {
            Data    = data,
            Members = body.GetMembers == true ? g.Members : null,
        }));
    }

    /// <summary>Updates shared group data.</summary>
    [HttpPost("/Client/UpdateSharedGroupData")]
    public Task<IHttpActionResult> Client_UpdateSharedGroupData(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateSharedGroupDataRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.SharedGroupId))
            return Task.FromResult(Err("InvalidParams", 1000, "SharedGroupId required"));
        var coll = Database.GetCollection<PlayfabSharedGroupDbObject>(true)!;
        var sgid = body.SharedGroupId;
        var g = coll.FindOne(x => x.SharedGroupId == sgid);
        if (g == null) return Task.FromResult(Err("SharedGroupNotFound", 1310, "Shared group not found", HttpStatusCode.NotFound));
        var pid = PlayFabIdFromRequest(request);
        var perm = body.Permission == UserDataPermission.Public ? "Public" : "Private";
        if (body.Data != null)
            foreach (var (k, v) in body.Data)
                g.Data[k] = new PlayfabSharedGroupValue
                {
                    Value         = v ?? "",
                    LastUpdated   = NowUnix(),
                    LastUpdatedBy = pid,
                    Permission    = perm,
                };
        if (body.KeysToRemove != null) foreach (var k in body.KeysToRemove) g.Data.Remove(k);
        coll.Update(g);
        return Task.FromResult(Ok(new UpdateSharedGroupDataResult()));
    }
}
