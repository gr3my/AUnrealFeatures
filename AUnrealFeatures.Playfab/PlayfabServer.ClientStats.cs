using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Database;
using AUnrealFeatures.Playfab.Models;
using PlayFab.ClientModels;
using PlayFab.ProfilesModels;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed class PlayfabPlayerCustomPropertyDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ValueJson { get; set; } = "";
    public int Version { get; set; }
}

public sealed partial class PlayfabServer
{
    /* ─── Player statistics (legacy v1) ───────────────────────────────────── */

    /// <summary>Returns the player's statistics.</summary>
    [HttpPost("/Client/GetPlayerStatistics")]
    public Task<IHttpActionResult> Client_GetPlayerStatistics(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayerStatisticsRequest>(request);
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());
        var coll = Database.GetCollection<PlayfabStatisticDbObject>(true)!;
        var rows = coll.Find(s => s.PlayFabId == pid && string.IsNullOrEmpty(s.CharacterId)).ToList();
        if (body?.StatisticNames != null && body.StatisticNames.Count > 0)
        {
            var names = new HashSet<string>(body.StatisticNames);
            rows = rows.Where(s => names.Contains(s.StatisticName)).ToList();
        }
        return Task.FromResult(Ok(new GetPlayerStatisticsResult
        {
            Statistics = rows.Select(r => new StatisticValue
            {
                StatisticName = r.StatisticName, Value = r.Value, Version = r.Version,
            }).ToList()
        }));
    }

    /// <summary>Updates the player's statistics.</summary>
    [HttpPost("/Client/UpdatePlayerStatistics")]
    public Task<IHttpActionResult> Client_UpdatePlayerStatistics(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdatePlayerStatisticsRequest>(request);
        if (body == null || body.Statistics == null) return Task.FromResult(Err("InvalidParams", 1000, "Statistics required"));
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());
        var coll = Database.GetCollection<PlayfabStatisticDbObject>(true)!;
        var defColl = Database.GetCollection<PlayfabStatisticDefinitionDbObject>(true)!;
        foreach (var u in body.Statistics)
        {
            if (string.IsNullOrEmpty(u.StatisticName)) continue;
            var name = u.StatisticName;
            var existing = coll.FindOne(s => s.PlayFabId == pid && string.IsNullOrEmpty(s.CharacterId) && s.StatisticName == name);
            // Determine aggregation
            var def = defColl.FindOne(d => d.TitleId == TITLE_ID && d.StatisticName == name);
            var aggregation = def?.AggregationMethod ?? "Last";
            int newValue = u.Value;
            if (existing != null)
            {
                newValue = aggregation switch
                {
                    "Min" => Math.Min(existing.Value, u.Value),
                    "Max" => Math.Max(existing.Value, u.Value),
                    "Sum" => existing.Value + u.Value,
                    _     => u.Value, // Last
                };
            }
            if (existing == null)
                coll.Insert(new PlayfabStatisticDbObject { PlayFabId = pid, StatisticName = name, Value = newValue, Version = u.Version ?? 1 });
            else { existing.Value = newValue; existing.Version = u.Version ?? existing.Version; coll.Update(existing); }
        }
        return Task.FromResult(Ok(new UpdatePlayerStatisticsResult()));
    }

    /// <summary>Returns the version history for a statistic.</summary>
    [HttpPost("/Client/GetPlayerStatisticVersions")]
    public Task<IHttpActionResult> Client_GetPlayerStatisticVersions(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayerStatisticVersionsRequest>(request);
        var name = body?.StatisticName;
        var defs = Database.GetCollection<PlayfabStatisticDefinitionDbObject>(true)!
                           .Find(d => d.TitleId == TITLE_ID && (string.IsNullOrEmpty(name) || d.StatisticName == name)).ToList();
        return Task.FromResult(Ok(new GetPlayerStatisticVersionsResult
        {
            StatisticVersions = defs.Select(d => new PlayerStatisticVersion
            {
                StatisticName  = d.StatisticName,
                Version        = d.CurrentVersion,
                ActivationTime = DateTime.UtcNow,
            }).ToList()
        }));
    }

    /// <summary>Returns the calling player's tags.</summary>
    [HttpPost("/Client/GetPlayerTags")]
    public Task<IHttpActionResult> Client_GetPlayerTags(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayerTagsRequest>(request);
        var pid = !string.IsNullOrEmpty(body?.PlayFabId) ? body!.PlayFabId : PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());
        var ns = body?.Namespace;
        var tags = Database.GetCollection<PlayfabPlayerTagDbObject>(true)!
                           .Find(t => t.PlayFabId == pid && (string.IsNullOrEmpty(ns) || t.Namespace == ns))
                           .Select(t => t.Tag).ToList();
        return Task.FromResult(Ok(new GetPlayerTagsResult { PlayFabId = pid, Tags = tags }));
    }

    /// <summary>Returns the segments the calling player belongs to.</summary>
    [HttpPost("/Client/GetPlayerSegments")]
    public Task<IHttpActionResult> Client_GetPlayerSegments(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabSegmentDbObject>(true)!
                           .Find(s => s.TitleId == TITLE_ID).ToList();
        return Task.FromResult(Ok(new GetPlayerSegmentsResult
        {
            Segments = rows.Select(s => new GetSegmentResult
            {
                Id   = s.SegmentId,
                Name = s.Name,
            }).ToList()
        }));
    }

    /* ─── Leaderboards (legacy v1) ────────────────────────────────────────── */

    private List<PlayerLeaderboardEntry> RankPlayerStat(string statisticName)
    {
        var pcoll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var rows = Database.GetCollection<PlayfabStatisticDbObject>(true)!
                           .Find(s => s.StatisticName == statisticName && string.IsNullOrEmpty(s.CharacterId))
                           .OrderByDescending(s => s.Value).ToList();
        var ranked = new List<PlayerLeaderboardEntry>();
        int pos = 0;
        foreach (var r in rows)
        {
            var rid = r.PlayFabId;
            var p = pcoll.FindOne(x => x.PlayFabId == rid);
            ranked.Add(new PlayerLeaderboardEntry
            {
                PlayFabId   = r.PlayFabId,
                DisplayName = p?.DisplayName ?? "",
                StatValue   = r.Value,
                Position    = pos++,
            });
        }
        return ranked;
    }

    /// <summary>Returns a paged player leaderboard.</summary>
    [HttpPost("/Client/GetLeaderboard")]
    public Task<IHttpActionResult> Client_GetLeaderboard(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetLeaderboardRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.StatisticName))
            return Task.FromResult(Err("InvalidParams", 1000, "StatisticName required"));
        var ranked = RankPlayerStat(body.StatisticName);
        var page = ranked.Skip(body.StartPosition).Take(body.MaxResultsCount ?? 100).ToList();
        return Task.FromResult(Ok(new GetLeaderboardResult { Leaderboard = page, Version = body.Version ?? 0 }));
    }

    /// <summary>Returns leaderboard centered on the calling player.</summary>
    [HttpPost("/Client/GetLeaderboardAroundPlayer")]
    public Task<IHttpActionResult> Client_GetLeaderboardAroundPlayer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetLeaderboardAroundPlayerRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.StatisticName))
            return Task.FromResult(Err("InvalidParams", 1000, "StatisticName required"));
        var pid = !string.IsNullOrEmpty(body.PlayFabId) ? body.PlayFabId : PlayFabIdFromRequest(request);
        var ranked = RankPlayerStat(body.StatisticName);
        var idx = ranked.FindIndex(r => r.PlayFabId == pid);
        var radius = (body.MaxResultsCount ?? 100) / 2;
        var start = Math.Max(0, idx - radius);
        var take  = idx < 0 ? 0 : Math.Min(ranked.Count - start, radius * 2 + 1);
        return Task.FromResult(Ok(new GetLeaderboardAroundPlayerResult
        {
            Leaderboard = idx < 0 ? new() : ranked.GetRange(start, take),
            Version     = body.Version ?? 0,
        }));
    }

    /// <summary>Returns a paged friend leaderboard.</summary>
    [HttpPost("/Client/GetFriendLeaderboard")]
    public Task<IHttpActionResult> Client_GetFriendLeaderboard(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetFriendLeaderboardRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.StatisticName))
            return Task.FromResult(Err("InvalidParams", 1000, "StatisticName required"));
        var pid = PlayFabIdFromRequest(request);
        var friendIds = Database.GetCollection<PlayfabFriendDbObject>(true)!
                                .Find(f => f.OwnerPlayFabId == pid).Select(f => f.FriendPlayFabId).ToHashSet();
        friendIds.Add(pid);
        var ranked = RankPlayerStat(body.StatisticName).Where(r => friendIds.Contains(r.PlayFabId)).ToList();
        var page = ranked.Skip(body.StartPosition).Take(body.MaxResultsCount ?? 100).ToList();
        return Task.FromResult(Ok(new GetLeaderboardResult { Leaderboard = page, Version = body.Version ?? 0 }));
    }

    /// <summary>Returns friend leaderboard centered on the calling player.</summary>
    [HttpPost("/Client/GetFriendLeaderboardAroundPlayer")]
    public Task<IHttpActionResult> Client_GetFriendLeaderboardAroundPlayer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetFriendLeaderboardAroundPlayerRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.StatisticName))
            return Task.FromResult(Err("InvalidParams", 1000, "StatisticName required"));
        var pid = !string.IsNullOrEmpty(body.PlayFabId) ? body.PlayFabId : PlayFabIdFromRequest(request);
        var friendIds = Database.GetCollection<PlayfabFriendDbObject>(true)!
                                .Find(f => f.OwnerPlayFabId == pid).Select(f => f.FriendPlayFabId).ToHashSet();
        friendIds.Add(pid);
        var ranked = RankPlayerStat(body.StatisticName).Where(r => friendIds.Contains(r.PlayFabId)).ToList();
        var idx = ranked.FindIndex(r => r.PlayFabId == pid);
        var radius = (body.MaxResultsCount ?? 100) / 2;
        var start = Math.Max(0, idx - radius);
        var take  = idx < 0 ? 0 : Math.Min(ranked.Count - start, radius * 2 + 1);
        return Task.FromResult(Ok(new GetFriendLeaderboardAroundPlayerResult
        {
            Leaderboard = idx < 0 ? new() : ranked.GetRange(start, take),
            Version     = body.Version ?? 0,
        }));
    }

    /* ─── Player combined info ────────────────────────────────────────────── */

    /// <summary>Returns a single payload containing several pieces of player info at once.</summary>
    [HttpPost("/Client/GetPlayerCombinedInfo")]
    public Task<IHttpActionResult> Client_GetPlayerCombinedInfo(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayerCombinedInfoRequest>(request);
        var pid = !string.IsNullOrEmpty(body?.PlayFabId) ? body!.PlayFabId : PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());
        var p = Database.GetCollection<PlayfabPlayerDbObject>(true)!.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound));

        var payload = new GetPlayerCombinedInfoResultPayload();
        var p2 = body?.InfoRequestParameters;
        if (p2?.GetUserAccountInfo == true)
            payload.AccountInfo = new UserAccountInfo
            {
                PlayFabId = p.PlayFabId,
                Created   = DateTimeOffset.FromUnixTimeSeconds(p.Created).UtcDateTime,
                Username  = p.Username,
                TitleInfo = new UserTitleInfo { DisplayName = p.DisplayName, AvatarUrl = p.AvatarUrl },
                PrivateInfo = new UserPrivateAccountInfo { Email = p.Email },
            };
        if (p2?.GetPlayerProfile == true)
            payload.PlayerProfile = new PlayerProfileModel
            {
                PlayerId    = p.PlayFabId,
                TitleId     = p.TitleId,
                DisplayName = p.DisplayName,
                AvatarUrl   = p.AvatarUrl,
                Created     = DateTimeOffset.FromUnixTimeSeconds(p.Created).UtcDateTime,
                LastLogin   = DateTimeOffset.FromUnixTimeSeconds(p.LastLogin).UtcDateTime,
            };
        if (p2?.GetUserData == true)
            payload.UserData = Database.GetCollection<PlayfabUserDataDbObject>(true)!
                .Find(d => d.PlayFabId == pid && d.Scope == "Title")
                .ToDictionary(d => d.Key, d => new UserDataRecord
                {
                    Value       = d.Value,
                    LastUpdated = DateTimeOffset.FromUnixTimeSeconds(d.LastUpdated).UtcDateTime,
                    Permission  = d.Permission == "Public" ? UserDataPermission.Public : UserDataPermission.Private,
                });
        if (p2?.GetUserReadOnlyData == true)
            payload.UserReadOnlyData = Database.GetCollection<PlayfabUserDataDbObject>(true)!
                .Find(d => d.PlayFabId == pid && d.Scope == "TitleReadOnly")
                .ToDictionary(d => d.Key, d => new UserDataRecord
                {
                    Value       = d.Value,
                    LastUpdated = DateTimeOffset.FromUnixTimeSeconds(d.LastUpdated).UtcDateTime,
                });
        if (p2?.GetUserInventory == true)
        {
            payload.UserInventory = Database.GetCollection<PlayfabInventoryItemDbObject>(true)!
                .Find(i => i.PlayFabId == pid && string.IsNullOrEmpty(i.CharacterId))
                .Select(InventoryRowToInstance).ToList();
            payload.UserVirtualCurrency = Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!
                .Find(v => v.PlayFabId == pid && string.IsNullOrEmpty(v.CharacterId))
                .ToDictionary(v => v.CurrencyCode, v => v.Balance);
            payload.UserVirtualCurrencyRechargeTimes = new();
        }
        if (p2?.GetTitleData == true)
            payload.TitleData = Database.GetCollection<PlayfabTitleDataDbObject>(true)!
                .Find(d => d.TitleId == TITLE_ID && d.Scope == "Title")
                .ToDictionary(d => d.Key, d => d.Value);
        if (p2?.GetPlayerStatistics == true)
            payload.PlayerStatistics = Database.GetCollection<PlayfabStatisticDbObject>(true)!
                .Find(s => s.PlayFabId == pid && string.IsNullOrEmpty(s.CharacterId))
                .Select(s => new StatisticValue { StatisticName = s.StatisticName, Value = s.Value, Version = s.Version }).ToList();
        if (p2?.GetCharacterList == true)
            payload.CharacterList = Database.GetCollection<PlayfabCharacterDbObject>(true)!
                .Find(c => c.PlayFabId == pid)
                .Select(c => new CharacterResult { CharacterId = c.CharacterId, CharacterName = c.CharacterName, CharacterType = c.CharacterType }).ToList();
        return Task.FromResult(Ok(new GetPlayerCombinedInfoResult { PlayFabId = pid, InfoResultPayload = payload }));
    }

    /* ─── Custom properties ───────────────────────────────────────────────── */

    private int CurrentPropertyVersion(string playFabId) =>
        (int)Database.GetCollection<PlayfabPlayerCustomPropertyDbObject>(true)!.Count(p => p.PlayFabId == playFabId);

    /// <summary>Returns a single custom property by name.</summary>
    [HttpPost("/Client/GetPlayerCustomProperty")]
    public Task<IHttpActionResult> Client_GetPlayerCustomProperty(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPlayerCustomPropertyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.PropertyName))
            return Task.FromResult(Err("InvalidParams", 1000, "PropertyName required"));
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());
        var name = body.PropertyName;
        var row = Database.GetCollection<PlayfabPlayerCustomPropertyDbObject>(true)!
                          .FindOne(p => p.PlayFabId == pid && p.Name == name);
        if (row == null) return Task.FromResult(Err("PropertyNotFound", 1467, "Property not found", HttpStatusCode.NotFound));
        object? value = null;
        try { value = JsonSerializer.Deserialize<JsonElement>(row.ValueJson); } catch { value = row.ValueJson; }
        return Task.FromResult(Ok(new GetPlayerCustomPropertyResult
        {
            PropertiesVersion = CurrentPropertyVersion(pid),
            Property          = new CustomPropertyDetails { Name = row.Name, Value = value },
        }));
    }

    /// <summary>Lists all custom properties for the calling player.</summary>
    [HttpPost("/Client/ListPlayerCustomProperties")]
    public Task<IHttpActionResult> Client_ListPlayerCustomProperties(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<ListPlayerCustomPropertiesRequest>(request);
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabPlayerCustomPropertyDbObject>(true)!
                           .Find(p => p.PlayFabId == pid).ToList();
        return Task.FromResult(Ok(new ListPlayerCustomPropertiesResult
        {
            PropertiesVersion = CurrentPropertyVersion(pid),
            Properties = rows.Select(r =>
            {
                object? v = null;
                try { v = JsonSerializer.Deserialize<JsonElement>(r.ValueJson); } catch { v = r.ValueJson; }
                return new CustomPropertyDetails { Name = r.Name, Value = v };
            }).ToList(),
        }));
    }

    /// <summary>Updates one or more custom properties.</summary>
    [HttpPost("/Client/UpdatePlayerCustomProperties")]
    public Task<IHttpActionResult> Client_UpdatePlayerCustomProperties(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdatePlayerCustomPropertiesRequest>(request);
        if (body == null || body.Properties == null) return Task.FromResult(Err("InvalidParams", 1000, "Properties required"));
        var pid = PlayFabIdFromRequest(request);
        var coll = Database.GetCollection<PlayfabPlayerCustomPropertyDbObject>(true)!;
        foreach (var prop in body.Properties)
        {
            if (string.IsNullOrEmpty(prop.Name)) continue;
            var name = prop.Name;
            var existing = coll.FindOne(p => p.PlayFabId == pid && p.Name == name);
            var json = prop.Value != null ? JsonSerializer.Serialize(prop.Value, JsonOpts) : "null";
            if (existing == null)
                coll.Insert(new PlayfabPlayerCustomPropertyDbObject { PlayFabId = pid, Name = name, ValueJson = json, Version = 1 });
            else { existing.ValueJson = json; existing.Version++; coll.Update(existing); }
        }
        return Task.FromResult(Ok(new UpdatePlayerCustomPropertiesResult { PropertiesVersion = CurrentPropertyVersion(pid) }));
    }

    /// <summary>Deletes named custom properties.</summary>
    [HttpPost("/Client/DeletePlayerCustomProperties")]
    public Task<IHttpActionResult> Client_DeletePlayerCustomProperties(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeletePlayerCustomPropertiesRequest>(request);
        if (body == null || body.PropertyNames == null) return Task.FromResult(Err("InvalidParams", 1000, "PropertyNames required"));
        var pid = PlayFabIdFromRequest(request);
        var coll = Database.GetCollection<PlayfabPlayerCustomPropertyDbObject>(true)!;
        var deleted = new List<DeletedPropertyDetails>();
        foreach (var name in body.PropertyNames)
        {
            var n = name;
            var existed = coll.FindOne(p => p.PlayFabId == pid && p.Name == n) != null;
            coll.DeleteMany(p => p.PlayFabId == pid && p.Name == n);
            deleted.Add(new DeletedPropertyDetails { Name = n, WasDeleted = existed });
        }
        return Task.FromResult(Ok(new DeletePlayerCustomPropertiesResult
        {
            DeletedProperties = deleted,
            PropertiesVersion = CurrentPropertyVersion(pid),
        }));
    }

    /* ─── Publisher data ──────────────────────────────────────────────────── */

    /// <summary>Returns publisher-scoped title data (read-only).</summary>
    [HttpPost("/Client/GetPublisherData")]
    public Task<IHttpActionResult> Client_GetPublisherData(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPublisherDataRequest>(request);
        var rows = Database.GetCollection<PlayfabTitleDataDbObject>(true)!
                           .Find(t => t.TitleId == TITLE_ID && t.Scope == "Publisher").ToList();
        Dictionary<string, string> data;
        if (body?.Keys != null && body.Keys.Count > 0)
        {
            var keys = new HashSet<string>(body.Keys);
            data = rows.Where(r => keys.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value);
        }
        else
        {
            data = rows.ToDictionary(r => r.Key, r => r.Value);
        }
        return Task.FromResult(Ok(new GetPublisherDataResult { Data = data }));
    }

    /* ─── Events / CloudScript ────────────────────────────────────────────── */

    /// <summary>Writes a player analytics event.</summary>
    [HttpPost("/Client/WritePlayerEvent")]
    public Task<IHttpActionResult> Client_WritePlayerEvent(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<WriteClientPlayerEventRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.EventName))
            return Task.FromResult(Err("InvalidParams", 1000, "EventName required"));
        var ev = new PlayfabEventDbObject
        {
            EventId        = NewId(),
            EventName      = body.EventName,
            EventNamespace = "title.player",
            EntityId       = PlayFabIdFromRequest(request),
            EntityType     = "title_player_account",
            PayloadJson    = JsonSerializer.Serialize(body.Body ?? new(), JsonOpts),
            Timestamp      = body.Timestamp.HasValue ? new DateTimeOffset(body.Timestamp.Value, TimeSpan.Zero).ToUnixTimeMilliseconds() : NowUnixMs(),
        };
        Database.GetCollection<PlayfabEventDbObject>(true)!.Insert(ev);
        return Task.FromResult(Ok(new WriteEventResponse { EventId = ev.EventId }));
    }

    /// <summary>Writes a title-scoped analytics event.</summary>
    [HttpPost("/Client/WriteTitleEvent")]
    public Task<IHttpActionResult> Client_WriteTitleEvent(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<WriteTitleEventRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.EventName))
            return Task.FromResult(Err("InvalidParams", 1000, "EventName required"));
        var ev = new PlayfabEventDbObject
        {
            EventId        = NewId(),
            EventName      = body.EventName,
            EventNamespace = "title.events",
            PayloadJson    = JsonSerializer.Serialize(body.Body ?? new(), JsonOpts),
            Timestamp      = body.Timestamp.HasValue ? new DateTimeOffset(body.Timestamp.Value, TimeSpan.Zero).ToUnixTimeMilliseconds() : NowUnixMs(),
        };
        Database.GetCollection<PlayfabEventDbObject>(true)!.Insert(ev);
        return Task.FromResult(Ok(new WriteEventResponse { EventId = ev.EventId }));
    }

    /// <summary>Executes a legacy CloudScript revision by running the uploaded JavaScript source
    /// inside the embedded Jint engine.</summary>
    [HttpPost("/Client/ExecuteCloudScript")]
    public Task<IHttpActionResult> Client_ExecuteCloudScript(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ExecuteCloudScriptRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FunctionName))
            return Task.FromResult(Err("InvalidParams", 1000, "FunctionName required"));

        var pid = PlayFabIdFromRequest(request);
        int? specificRev = body.RevisionSelection == CloudScriptRevisionOption.Specific ? body.SpecificRevision : null;
        bool useTest     = body.RevisionSelection == CloudScriptRevisionOption.Latest;

        var inv = InvokeCloudScript(body.FunctionName, body.FunctionParameter, pid, specificRev, useTest);

        Database.GetCollection<PlayfabCloudScriptExecutionLogDbObject>(true)!.Insert(new PlayfabCloudScriptExecutionLogDbObject
        {
            ExecutionId          = NewId(),
            PlayFabId            = pid,
            FunctionName         = body.FunctionName,
            ResultJson           = inv.FunctionResult != null ? JsonSerializer.Serialize(inv.FunctionResult, JsonOpts) : "",
            LogsJson             = JsonSerializer.Serialize(inv.Logs, JsonOpts),
            ExecutionTimeSeconds = (long)inv.ExecutionTimeSeconds,
            Timestamp            = NowUnix(),
            Error                = inv.ExecutionError != null,
        });

        return Task.FromResult(Ok(new ExecuteCloudScriptResult
        {
            FunctionName           = body.FunctionName,
            Revision               = inv.Revision,
            ExecutionTimeSeconds   = inv.ExecutionTimeSeconds,
            ProcessorTimeSeconds   = inv.ExecutionTimeSeconds,
            MemoryConsumedBytes    = inv.MemoryConsumedBytes,
            APIRequestsIssued      = inv.APIRequestsIssued,
            HttpRequestsIssued     = inv.HttpRequestsIssued,
            Logs                   = inv.Logs.Select(l => new LogStatement
            {
                Level   = l.Level,
                Message = l.Message,
                Data    = l.Data,
            }).ToList(),
            LogsTooLarge           = JsonSerializer.Serialize(inv.Logs, JsonOpts).Length > 256_000,
            FunctionResult         = inv.FunctionResult,
            FunctionResultTooLarge = inv.FunctionResult != null && JsonSerializer.Serialize(inv.FunctionResult, JsonOpts).Length > 256_000,
            Error                  = inv.ExecutionError != null ? new ScriptExecutionError
            {
                Error      = inv.ExecutionError.Error,
                Message    = inv.ExecutionError.Message,
                StackTrace = inv.ExecutionError.StackTrace,
            } : null,
        }));
    }

    /* ─── Profile global policy ───────────────────────────────────────────── */

    /// <summary>Returns the title's global access policy.</summary>
    [HttpPost("/Profile/GetGlobalPolicy")]
    public Task<IHttpActionResult> Profile_GetGlobalPolicy(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var coll = Database.GetCollection<PlayfabPolicyDbObject>(true)!;
        var row = coll.FindOne(p => p.TitleId == TITLE_ID && p.PolicyName == "GlobalPolicy");
        List<EntityPermissionStatement> permissions = new();
        if (row != null && !string.IsNullOrEmpty(row.StatementsJson))
        {
            try { permissions = JsonSerializer.Deserialize<List<EntityPermissionStatement>>(row.StatementsJson, JsonOpts) ?? new(); }
            catch { }
        }
        return Task.FromResult(Ok(new GetGlobalPolicyResponse { Permissions = permissions }));
    }

    /* ─── Authentication delete ───────────────────────────────────────────── */

    /// <summary>Deletes an entity (player) account.</summary>
    [HttpPost("/GameServerIdentity/Delete")]
    public Task<IHttpActionResult> Authentication_DeleteEntity(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<PlayFab.AuthenticationModels.DeleteRequest>(request);
        if (body == null || body.Entity == null || string.IsNullOrEmpty(body.Entity.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Entity required"));
        var eid = body.Entity.Id;
        // Cascade delete across a number of related tables
        var pid = Database.GetCollection<PlayfabPlayerDbObject>(true)!.FindOne(x => x.EntityId == eid)?.PlayFabId ?? "";
        Database.GetCollection<PlayfabPlayerDbObject>(true)!.DeleteMany(x => x.EntityId == eid);
        if (!string.IsNullOrEmpty(pid))
        {
            Database.GetCollection<PlayfabUserDataDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid);
            Database.GetCollection<PlayfabFriendDbObject>(true)!.DeleteMany(x => x.OwnerPlayFabId == pid || x.FriendPlayFabId == pid);
            Database.GetCollection<PlayfabStatisticDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid);
            Database.GetCollection<PlayfabInventoryItemDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid);
            Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid);
            Database.GetCollection<PlayfabCharacterDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid);
            Database.GetCollection<PlayfabCharacterDataDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid);
            Database.GetCollection<PlayfabPlayerCustomPropertyDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid);
            Database.GetCollection<PlayfabContactEmailDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid);
        }
        Database.GetCollection<PlayfabProfileDbObject>(true)!.DeleteMany(x => x.EntityId == eid);
        return Task.FromResult(Ok(new PlayFab.AuthenticationModels.EmptyResponse()));
    }
}
