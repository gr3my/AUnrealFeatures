using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Database;
using AUnrealFeatures.Playfab.Models;
using PlayFab.ProgressionModels;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed class PlayfabStatisticDefinitionV2DbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string EntityType { get; set; } = "title_player_account";
    public string ColumnsJson { get; set; } = "";          // List<StatisticColumn>
    public string AggregationSourcesJson { get; set; } = "";
    public string AggregationDestinationsJson { get; set; } = "";
    public string LinkedLeaderboardsJson { get; set; } = "";
    public uint Version { get; set; } = 1;
    public long Created { get; set; }
    public long? LastResetTime { get; set; }
    public string ResetInterval { get; set; } = "Manual";
    public int MaxQueryableVersions { get; set; } = 1;
}

public sealed class PlayfabEntityStatisticDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string StatisticName { get; set; } = "";
    public List<string> Scores { get; set; } = new();
    public string Metadata { get; set; } = "";
    public uint Version { get; set; }
    public long LastUpdated { get; set; }
}

public sealed class PlayfabLeaderboardDefinitionDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string EntityType { get; set; } = "title_player_account";
    public int SizeLimit { get; set; } = 1000;
    public string ColumnsJson { get; set; } = "";
    public uint Version { get; set; } = 1;
    public long Created { get; set; }
    public long? LastResetTime { get; set; }
    public string ResetInterval { get; set; } = "Manual";
    public int MaxQueryableVersions { get; set; } = 1;
    public string LinkedStatisticName { get; set; } = "";
}

public sealed partial class PlayfabServer
{
    /* ─── Helpers ─────────────────────────────────────────────────────────── */

    private static List<StatisticColumn> StatColumns(string json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<List<StatisticColumn>>(json, JsonOpts) ?? new(); }
        catch { return new(); }
    }
    private static List<LeaderboardColumn> LbColumns(string json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<List<LeaderboardColumn>>(json, JsonOpts) ?? new(); }
        catch { return new(); }
    }
    private static List<string> StringList(string json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? new(); }
        catch { return new(); }
    }
    private static ResetInterval ParseResetInterval(string s) =>
        Enum.TryParse<ResetInterval>(s, out var v) ? v : ResetInterval.Manual;

    /* ─── Statistic Definitions ───────────────────────────────────────────── */

    /// <summary>Creates a statistic definition.</summary>
    [HttpPost("/Statistic/CreateStatisticDefinition")]
    public Task<IHttpActionResult> Progression_CreateStatisticDefinition(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateStatisticDefinitionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var coll = Database.GetCollection<PlayfabStatisticDefinitionV2DbObject>(true)!;
        var name = body.Name;
        if (coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name) != null)
            return Task.FromResult(Err("StatisticDefinitionAlreadyExists", 1666, "Statistic already exists"));

        var def = new PlayfabStatisticDefinitionV2DbObject
        {
            TitleId                = TITLE_ID,
            Name                   = body.Name,
            EntityType             = body.EntityType ?? "title_player_account",
            ColumnsJson            = JsonSerializer.Serialize(body.Columns ?? new(), JsonOpts),
            AggregationSourcesJson = JsonSerializer.Serialize(body.AggregationSources ?? new(), JsonOpts),
            Version                = 1,
            Created                = NowUnix(),
            ResetInterval          = (body.VersionConfiguration?.ResetInterval ?? ResetInterval.Manual).ToString(),
            MaxQueryableVersions   = body.VersionConfiguration?.MaxQueryableVersions ?? 1,
        };
        coll.Insert(def);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Deletes a statistic definition and all of its values.</summary>
    [HttpPost("/Statistic/DeleteStatisticDefinition")]
    public Task<IHttpActionResult> Progression_DeleteStatisticDefinition(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteStatisticDefinitionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var name = body.Name;
        Database.GetCollection<PlayfabStatisticDefinitionV2DbObject>(true)!.DeleteMany(x => x.TitleId == TITLE_ID && x.Name == name);
        Database.GetCollection<PlayfabEntityStatisticDbObject>(true)!.DeleteMany(x => x.TitleId == TITLE_ID && x.StatisticName == name);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Returns a statistic definition by name.</summary>
    [HttpPost("/Statistic/GetStatisticDefinition")]
    public Task<IHttpActionResult> Progression_GetStatisticDefinition(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetStatisticDefinitionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var name = body.Name;
        var def = Database.GetCollection<PlayfabStatisticDefinitionV2DbObject>(true)!
                          .FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (def == null) return Task.FromResult(Err("StatisticDefinitionNotFound", 1665, "Statistic not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new GetStatisticDefinitionResponse
        {
            Name                    = def.Name,
            EntityType              = def.EntityType,
            Columns                 = StatColumns(def.ColumnsJson),
            AggregationSources      = StringList(def.AggregationSourcesJson),
            AggregationDestinations = StringList(def.AggregationDestinationsJson),
            LinkedLeaderboardNames  = StringList(def.LinkedLeaderboardsJson),
            Version                 = def.Version,
            Created                 = DateTimeOffset.FromUnixTimeSeconds(def.Created).UtcDateTime,
            LastResetTime           = def.LastResetTime.HasValue ? DateTimeOffset.FromUnixTimeSeconds(def.LastResetTime.Value).UtcDateTime : (DateTime?)null,
            VersionConfiguration    = new VersionConfiguration { MaxQueryableVersions = def.MaxQueryableVersions, ResetInterval = ParseResetInterval(def.ResetInterval) },
        }));
    }

    /// <summary>Returns all statistic definitions for the title.</summary>
    [HttpPost("/Statistic/ListStatisticDefinitions")]
    public Task<IHttpActionResult> Progression_ListStatisticDefinitions(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var defs = Database.GetCollection<PlayfabStatisticDefinitionV2DbObject>(true)!
                           .Find(x => x.TitleId == TITLE_ID).Select(d => new StatisticDefinition
        {
            Name                    = d.Name,
            EntityType              = d.EntityType,
            Columns                 = StatColumns(d.ColumnsJson),
            AggregationSources      = StringList(d.AggregationSourcesJson),
            AggregationDestinations = StringList(d.AggregationDestinationsJson),
            LinkedLeaderboardNames  = StringList(d.LinkedLeaderboardsJson),
            Version                 = d.Version,
            Created                 = DateTimeOffset.FromUnixTimeSeconds(d.Created).UtcDateTime,
            LastResetTime           = d.LastResetTime.HasValue ? DateTimeOffset.FromUnixTimeSeconds(d.LastResetTime.Value).UtcDateTime : (DateTime?)null,
            VersionConfiguration    = new VersionConfiguration { MaxQueryableVersions = d.MaxQueryableVersions, ResetInterval = ParseResetInterval(d.ResetInterval) },
        }).ToList();
        return Task.FromResult(Ok(new ListStatisticDefinitionsResponse
        {
            StatisticDefinitions = defs,
            PageSize             = defs.Count,
            SkipToken            = "",
        }));
    }

    /// <summary>Updates a statistic definition's version configuration.</summary>
    [HttpPost("/Statistic/UpdateStatisticDefinition")]
    public Task<IHttpActionResult> Progression_UpdateStatisticDefinition(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateStatisticDefinitionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var coll = Database.GetCollection<PlayfabStatisticDefinitionV2DbObject>(true)!;
        var name = body.Name;
        var def = coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (def == null) return Task.FromResult(Err("StatisticDefinitionNotFound", 1665, "Statistic not found", HttpStatusCode.NotFound));
        if (body.VersionConfiguration != null)
        {
            def.MaxQueryableVersions = body.VersionConfiguration.MaxQueryableVersions;
            def.ResetInterval        = body.VersionConfiguration.ResetInterval.ToString();
        }
        coll.Update(def);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Increments a statistic's current version (resets entity values into the previous version).</summary>
    [HttpPost("/Statistic/IncrementStatisticVersion")]
    public Task<IHttpActionResult> Progression_IncrementStatisticVersion(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<IncrementStatisticVersionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var coll = Database.GetCollection<PlayfabStatisticDefinitionV2DbObject>(true)!;
        var name = body.Name;
        var def = coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (def == null) return Task.FromResult(Err("StatisticDefinitionNotFound", 1665, "Statistic not found", HttpStatusCode.NotFound));
        def.Version       += 1;
        def.LastResetTime  = NowUnix();
        coll.Update(def);
        return Task.FromResult(Ok(new IncrementStatisticVersionResponse { Version = def.Version }));
    }

    /// <summary>Removes an aggregation source from a statistic.</summary>
    [HttpPost("/Statistic/UnlinkAggregationSourceFromStatistic")]
    public Task<IHttpActionResult> Progression_UnlinkAggregationSourceFromStatistic(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UnlinkAggregationSourceFromStatisticRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name) || string.IsNullOrEmpty(body.SourceStatisticName))
            return Task.FromResult(Err("InvalidParams", 1000, "Name and SourceStatisticName are required"));

        var coll = Database.GetCollection<PlayfabStatisticDefinitionV2DbObject>(true)!;
        var name = body.Name;
        var def = coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (def == null) return Task.FromResult(Err("StatisticDefinitionNotFound", 1665, "Statistic not found", HttpStatusCode.NotFound));
        var sources = StringList(def.AggregationSourcesJson);
        sources.RemoveAll(s => s == body.SourceStatisticName);
        def.AggregationSourcesJson = JsonSerializer.Serialize(sources, JsonOpts);
        coll.Update(def);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /* ─── Statistic values ────────────────────────────────────────────────── */

    /// <summary>Returns statistic values for a single entity.</summary>
    [HttpPost("/Statistic/GetStatistics")]
    public Task<IHttpActionResult> Progression_GetStatistics(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetStatisticsRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Entity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Entity is required"));

        var entityId = body.Entity.Id;
        var statColl = Database.GetCollection<PlayfabEntityStatisticDbObject>(true)!;
        var values = statColl.Find(s => s.TitleId == TITLE_ID && s.EntityId == entityId).ToList();
        if (body.StatisticNames != null && body.StatisticNames.Count > 0)
        {
            var names = new HashSet<string>(body.StatisticNames);
            values = values.Where(v => names.Contains(v.StatisticName)).ToList();
        }
        var stats = values.ToDictionary(v => v.StatisticName, v => new EntityStatisticValue
        {
            Name     = v.StatisticName,
            Scores   = v.Scores,
            Metadata = v.Metadata,
            Version  = (int)v.Version,
        });

        var defs = Database.GetCollection<PlayfabStatisticDefinitionV2DbObject>(true)!.Find(x => x.TitleId == TITLE_ID).ToList();
        var columnDetails = defs.Where(d => stats.ContainsKey(d.Name))
                                .ToDictionary(d => d.Name, d => new StatisticColumnCollection { Columns = StatColumns(d.ColumnsJson) });
        return Task.FromResult(Ok(new GetStatisticsResponse
        {
            Entity        = new EntityKey { Id = entityId, Type = body.Entity.Type ?? "title_player_account" },
            Statistics    = stats,
            ColumnDetails = columnDetails,
        }));
    }

    /// <summary>Returns statistics for a batch of entities.</summary>
    [HttpPost("/Statistic/GetStatisticsForEntities")]
    public Task<IHttpActionResult> Progression_GetStatisticsForEntities(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetStatisticsForEntitiesRequest>(request);
        if (body == null || body.Entities == null)
            return Task.FromResult(Err("InvalidParams", 1000, "Entities are required"));

        var statColl = Database.GetCollection<PlayfabEntityStatisticDbObject>(true)!;
        var nameFilter = body.StatisticNames != null && body.StatisticNames.Count > 0 ? new HashSet<string>(body.StatisticNames) : null;

        var entities = new List<EntityStatistics>();
        foreach (var e in body.Entities)
        {
            var eid = e.Id;
            var rows = statColl.Find(s => s.TitleId == TITLE_ID && s.EntityId == eid).ToList();
            if (nameFilter != null) rows = rows.Where(r => nameFilter.Contains(r.StatisticName)).ToList();
            entities.Add(new EntityStatistics
            {
                EntityKey  = new EntityKey { Id = eid, Type = e.Type ?? "title_player_account" },
                Statistics = rows.Select(r => new EntityStatisticValue
                {
                    Name     = r.StatisticName,
                    Scores   = r.Scores,
                    Metadata = r.Metadata,
                    Version  = (int)r.Version,
                }).ToList(),
            });
        }
        var defs = Database.GetCollection<PlayfabStatisticDefinitionV2DbObject>(true)!.Find(x => x.TitleId == TITLE_ID).ToList();
        var columnDetails = defs.ToDictionary(d => d.Name, d => new StatisticColumnCollection { Columns = StatColumns(d.ColumnsJson) });
        return Task.FromResult(Ok(new GetStatisticsForEntitiesResponse
        {
            EntitiesStatistics = entities,
            ColumnDetails      = columnDetails,
        }));
    }

    /// <summary>Updates statistics for an entity using each statistic's aggregation method.</summary>
    [HttpPost("/Statistic/UpdateStatistics")]
    public Task<IHttpActionResult> Progression_UpdateStatistics(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateStatisticsRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Entity?.Id) || body.Statistics == null)
            return Task.FromResult(Err("InvalidParams", 1000, "Entity and Statistics are required"));

        var entityId   = body.Entity.Id;
        var entityType = body.Entity.Type ?? "title_player_account";
        var statColl   = Database.GetCollection<PlayfabEntityStatisticDbObject>(true)!;
        var defColl    = Database.GetCollection<PlayfabStatisticDefinitionV2DbObject>(true)!;
        var resultStats = new Dictionary<string, EntityStatisticValue>();
        var columnDetails = new Dictionary<string, StatisticColumnCollection>();

        foreach (var u in body.Statistics)
        {
            if (string.IsNullOrEmpty(u.Name)) continue;
            var name = u.Name;
            var def = defColl.FindOne(d => d.TitleId == TITLE_ID && d.Name == name);
            if (def == null) continue;
            var columns = StatColumns(def.ColumnsJson);
            columnDetails[name] = new StatisticColumnCollection { Columns = columns };

            var existing = statColl.FindOne(s => s.TitleId == TITLE_ID && s.EntityId == entityId && s.StatisticName == name);
            var newScores = u.Scores ?? new();

            if (existing == null)
            {
                statColl.Insert(new PlayfabEntityStatisticDbObject
                {
                    TitleId       = TITLE_ID,
                    EntityId      = entityId,
                    EntityType    = entityType,
                    StatisticName = name,
                    Scores        = newScores,
                    Metadata      = u.Metadata ?? "",
                    Version       = u.Version ?? def.Version,
                    LastUpdated   = NowUnix(),
                });
                resultStats[name] = new EntityStatisticValue { Name = name, Scores = newScores, Metadata = u.Metadata ?? "", Version = (int)def.Version };
                continue;
            }

            // Aggregate per column
            var combined = new List<string>();
            for (int i = 0; i < Math.Max(existing.Scores.Count, newScores.Count); i++)
            {
                var oldStr = i < existing.Scores.Count ? existing.Scores[i] : "0";
                var newStr = i < newScores.Count       ? newScores[i]       : oldStr;
                var col    = i < columns.Count         ? columns[i]         : null;
                var method = col?.AggregationMethod ?? StatisticAggregationMethod.Last;

                if (double.TryParse(oldStr, out var oldVal) && double.TryParse(newStr, out var newVal))
                {
                    double aggregated = method switch
                    {
                        StatisticAggregationMethod.Min => Math.Min(oldVal, newVal),
                        StatisticAggregationMethod.Max => Math.Max(oldVal, newVal),
                        StatisticAggregationMethod.Sum => oldVal + newVal,
                        _                              => newVal, // Last
                    };
                    combined.Add(aggregated.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    combined.Add(newStr);
                }
            }
            existing.Scores      = combined;
            existing.Metadata    = u.Metadata ?? existing.Metadata;
            existing.Version     = u.Version ?? def.Version;
            existing.LastUpdated = NowUnix();
            statColl.Update(existing);
            resultStats[name] = new EntityStatisticValue
            {
                Name     = name,
                Scores   = combined,
                Metadata = existing.Metadata,
                Version  = (int)existing.Version,
            };
        }

        return Task.FromResult(Ok(new UpdateStatisticsResponse
        {
            Entity        = new EntityKey { Id = entityId, Type = entityType },
            Statistics    = resultStats,
            ColumnDetails = columnDetails,
        }));
    }

    /// <summary>Deletes statistics from an entity.</summary>
    [HttpPost("/Statistic/DeleteStatistics")]
    public Task<IHttpActionResult> Progression_DeleteStatistics(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteStatisticsRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Entity?.Id) || body.Statistics == null)
            return Task.FromResult(Err("InvalidParams", 1000, "Entity and Statistics are required"));

        var entityId = body.Entity.Id;
        var coll = Database.GetCollection<PlayfabEntityStatisticDbObject>(true)!;
        foreach (var s in body.Statistics)
        {
            if (string.IsNullOrEmpty(s.Name)) continue;
            var name = s.Name;
            coll.DeleteMany(x => x.TitleId == TITLE_ID && x.EntityId == entityId && x.StatisticName == name);
        }
        return Task.FromResult(Ok(new DeleteStatisticsResponse
        {
            Entity = new EntityKey { Id = entityId, Type = body.Entity.Type ?? "title_player_account" },
        }));
    }

    /* ─── Leaderboard Definitions ─────────────────────────────────────────── */

    /// <summary>Creates a leaderboard definition.</summary>
    [HttpPost("/Leaderboard/CreateLeaderboardDefinition")]
    public Task<IHttpActionResult> Progression_CreateLeaderboardDefinition(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateLeaderboardDefinitionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var coll = Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!;
        var name = body.Name;
        if (coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name) != null)
            return Task.FromResult(Err("LeaderboardAlreadyExists", 1672, "Leaderboard already exists"));

        var linkedStat = body.Columns?.FirstOrDefault()?.LinkedStatisticColumn?.LinkedStatisticName ?? "";
        coll.Insert(new PlayfabLeaderboardDefinitionDbObject
        {
            TitleId              = TITLE_ID,
            Name                 = body.Name,
            EntityType           = body.EntityType ?? "title_player_account",
            SizeLimit            = body.SizeLimit,
            ColumnsJson          = JsonSerializer.Serialize(body.Columns ?? new(), JsonOpts),
            Version              = 1,
            Created              = NowUnix(),
            ResetInterval        = (body.VersionConfiguration?.ResetInterval ?? ResetInterval.Manual).ToString(),
            MaxQueryableVersions = body.VersionConfiguration?.MaxQueryableVersions ?? 1,
            LinkedStatisticName  = linkedStat,
        });
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Deletes a leaderboard definition and all its entries.</summary>
    [HttpPost("/Leaderboard/DeleteLeaderboardDefinition")]
    public Task<IHttpActionResult> Progression_DeleteLeaderboardDefinition(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteLeaderboardDefinitionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var name = body.Name;
        Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!.DeleteMany(x => x.TitleId == TITLE_ID && x.Name == name);
        Database.GetCollection<PlayfabLeaderboardEntryDbObject>(true)!.DeleteMany(x => x.TitleId == TITLE_ID && x.LeaderboardName == name);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Returns a leaderboard definition.</summary>
    [HttpPost("/Leaderboard/GetLeaderboardDefinition")]
    public Task<IHttpActionResult> Progression_GetLeaderboardDefinition(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetLeaderboardDefinitionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var name = body.Name;
        var def = Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!
                          .FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (def == null) return Task.FromResult(Err("LeaderboardNotFound", 1671, "Leaderboard not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new GetLeaderboardDefinitionResponse
        {
            Name                 = def.Name,
            EntityType           = def.EntityType,
            SizeLimit            = def.SizeLimit,
            Columns              = LbColumns(def.ColumnsJson),
            Version              = def.Version,
            Created              = DateTimeOffset.FromUnixTimeSeconds(def.Created).UtcDateTime,
            LastResetTime        = def.LastResetTime.HasValue ? DateTimeOffset.FromUnixTimeSeconds(def.LastResetTime.Value).UtcDateTime : (DateTime?)null,
            VersionConfiguration = new VersionConfiguration { MaxQueryableVersions = def.MaxQueryableVersions, ResetInterval = ParseResetInterval(def.ResetInterval) },
        }));
    }

    /// <summary>Returns all leaderboard definitions.</summary>
    [HttpPost("/Leaderboard/ListLeaderboardDefinitions")]
    public Task<IHttpActionResult> Progression_ListLeaderboardDefinitions(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var defs = Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!
                           .Find(x => x.TitleId == TITLE_ID).Select(d => new LeaderboardDefinition
        {
            Name                 = d.Name,
            EntityType           = d.EntityType,
            SizeLimit            = d.SizeLimit,
            Columns              = LbColumns(d.ColumnsJson),
            Version              = d.Version,
            Created              = DateTimeOffset.FromUnixTimeSeconds(d.Created).UtcDateTime,
            LastResetTime        = d.LastResetTime.HasValue ? DateTimeOffset.FromUnixTimeSeconds(d.LastResetTime.Value).UtcDateTime : (DateTime?)null,
            VersionConfiguration = new VersionConfiguration { MaxQueryableVersions = d.MaxQueryableVersions, ResetInterval = ParseResetInterval(d.ResetInterval) },
        }).ToList();
        return Task.FromResult(Ok(new ListLeaderboardDefinitionsResponse
        {
            LeaderboardDefinitions = defs,
            PageSize               = defs.Count,
            SkipToken              = "",
        }));
    }

    /// <summary>Updates a leaderboard definition.</summary>
    [HttpPost("/Leaderboard/UpdateLeaderboardDefinition")]
    public Task<IHttpActionResult> Progression_UpdateLeaderboardDefinition(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateLeaderboardDefinitionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var coll = Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!;
        var name = body.Name;
        var def = coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (def == null) return Task.FromResult(Err("LeaderboardNotFound", 1671, "Leaderboard not found", HttpStatusCode.NotFound));
        if (body.SizeLimit.HasValue) def.SizeLimit = body.SizeLimit.Value;
        if (body.VersionConfiguration != null)
        {
            def.MaxQueryableVersions = body.VersionConfiguration.MaxQueryableVersions;
            def.ResetInterval        = body.VersionConfiguration.ResetInterval.ToString();
        }
        coll.Update(def);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Increments a leaderboard's version.</summary>
    [HttpPost("/Leaderboard/IncrementLeaderboardVersion")]
    public Task<IHttpActionResult> Progression_IncrementLeaderboardVersion(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<IncrementLeaderboardVersionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var coll = Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!;
        var name = body.Name;
        var def = coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (def == null) return Task.FromResult(Err("LeaderboardNotFound", 1671, "Leaderboard not found", HttpStatusCode.NotFound));
        def.Version       += 1;
        def.LastResetTime  = NowUnix();
        coll.Update(def);
        return Task.FromResult(Ok(new IncrementLeaderboardVersionResponse { Version = def.Version }));
    }

    /// <summary>Removes a leaderboard's link to its source statistic.</summary>
    [HttpPost("/Leaderboard/UnlinkLeaderboardFromStatistic")]
    public Task<IHttpActionResult> Progression_UnlinkLeaderboardFromStatistic(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UnlinkLeaderboardFromStatisticRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var coll = Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!;
        var name = body.Name;
        var def = coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (def == null) return Task.FromResult(Err("LeaderboardNotFound", 1671, "Leaderboard not found", HttpStatusCode.NotFound));
        def.LinkedStatisticName = "";
        coll.Update(def);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /* ─── Leaderboard Entries ─────────────────────────────────────────────── */

    private List<EntityLeaderboardEntry> RankAndProject(string lbName, uint version, List<PlayfabLeaderboardEntryDbObject> rows, LeaderboardSortDirection dir)
    {
        // Sort by primary score
        IOrderedEnumerable<PlayfabLeaderboardEntryDbObject> sorted = dir == LeaderboardSortDirection.Ascending
            ? rows.OrderBy(r => r.Scores.Count > 0 ? r.Scores[0] : 0.0)
            : rows.OrderByDescending(r => r.Scores.Count > 0 ? r.Scores[0] : 0.0);
        // Tie-break by LastUpdated
        sorted = sorted.ThenBy(r => r.LastUpdated);
        var ranked = new List<EntityLeaderboardEntry>();
        int rank = 1;
        foreach (var r in sorted)
        {
            ranked.Add(new EntityLeaderboardEntry
            {
                Entity      = new EntityKey { Id = r.EntityId, Type = r.EntityType },
                DisplayName = r.DisplayName,
                Scores      = r.Scores.Select(s => s.ToString()).ToList(),
                Metadata    = r.Metadata,
                Rank        = rank++,
                LastUpdated = DateTimeOffset.FromUnixTimeSeconds(r.LastUpdated).UtcDateTime,
            });
        }
        return ranked;
    }

    /// <summary>Returns paged leaderboard entries.</summary>
    [HttpPost("/Leaderboard/GetLeaderboard")]
    public Task<IHttpActionResult> Progression_GetLeaderboard(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetEntityLeaderboardRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.LeaderboardName))
            return Task.FromResult(Err("InvalidParams", 1000, "LeaderboardName is required"));

        var name = body.LeaderboardName;
        var def = Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!
                          .FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (def == null) return Task.FromResult(Err("LeaderboardNotFound", 1671, "Leaderboard not found", HttpStatusCode.NotFound));
        var version = body.Version ?? def.Version;
        var entryColl = Database.GetCollection<PlayfabLeaderboardEntryDbObject>(true)!;
        var v = version;
        var rows = entryColl.Find(r => r.TitleId == TITLE_ID && r.LeaderboardName == name && r.Version == v).ToList();

        var columns = LbColumns(def.ColumnsJson);
        var sortDir = columns.FirstOrDefault()?.SortDirection ?? LeaderboardSortDirection.Descending;
        var ranked  = RankAndProject(name, version, rows, sortDir);
        var start   = (int)(body.StartingPosition ?? 0);
        var pageSize = body.PageSize == 0 ? 100 : (int)body.PageSize;
        var page = ranked.Skip(start).Take(pageSize).ToList();

        return Task.FromResult(Ok(new GetEntityLeaderboardResponse
        {
            Columns    = columns,
            EntryCount = (uint)ranked.Count,
            NextReset  = null,
            Rankings   = page,
            Version    = version,
        }));
    }

    /// <summary>Returns leaderboard entries surrounding an entity.</summary>
    [HttpPost("/Leaderboard/GetLeaderboardAroundEntity")]
    public Task<IHttpActionResult> Progression_GetLeaderboardAroundEntity(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetLeaderboardAroundEntityRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.LeaderboardName) || string.IsNullOrEmpty(body.Entity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "LeaderboardName and Entity are required"));

        var name = body.LeaderboardName;
        var def = Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!
                          .FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (def == null) return Task.FromResult(Err("LeaderboardNotFound", 1671, "Leaderboard not found", HttpStatusCode.NotFound));
        var version = body.Version ?? def.Version;
        var v = version;
        var rows = Database.GetCollection<PlayfabLeaderboardEntryDbObject>(true)!
                           .Find(r => r.TitleId == TITLE_ID && r.LeaderboardName == name && r.Version == v).ToList();
        var columns = LbColumns(def.ColumnsJson);
        var sortDir = columns.FirstOrDefault()?.SortDirection ?? LeaderboardSortDirection.Descending;
        var ranked = RankAndProject(name, version, rows, sortDir);
        var idx = ranked.FindIndex(r => r.Entity?.Id == body.Entity.Id);
        if (idx < 0)
            return Task.FromResult(Ok(new GetEntityLeaderboardResponse { Rankings = new(), Columns = columns, EntryCount = (uint)ranked.Count, Version = version }));
        var radius = (int)body.MaxSurroundingEntries;
        var start  = Math.Max(0, idx - radius);
        var take   = Math.Min(ranked.Count - start, radius * 2 + 1);
        return Task.FromResult(Ok(new GetEntityLeaderboardResponse
        {
            Columns    = columns,
            EntryCount = (uint)ranked.Count,
            Rankings   = ranked.GetRange(start, take),
            Version    = version,
        }));
    }

    /// <summary>Returns leaderboard entries for a specific list of entities.</summary>
    [HttpPost("/Leaderboard/GetLeaderboardForEntities")]
    public Task<IHttpActionResult> Progression_GetLeaderboardForEntities(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetLeaderboardForEntitiesRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.LeaderboardName) || body.EntityIds == null)
            return Task.FromResult(Err("InvalidParams", 1000, "LeaderboardName and EntityIds are required"));

        var name = body.LeaderboardName;
        var def = Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!
                          .FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (def == null) return Task.FromResult(Err("LeaderboardNotFound", 1671, "Leaderboard not found", HttpStatusCode.NotFound));
        var version = body.Version ?? def.Version;
        var v = version;
        var rows = Database.GetCollection<PlayfabLeaderboardEntryDbObject>(true)!
                           .Find(r => r.TitleId == TITLE_ID && r.LeaderboardName == name && r.Version == v).ToList();
        var columns = LbColumns(def.ColumnsJson);
        var sortDir = columns.FirstOrDefault()?.SortDirection ?? LeaderboardSortDirection.Descending;
        var ranked  = RankAndProject(name, version, rows, sortDir);
        var ids     = new HashSet<string>(body.EntityIds);
        var subset  = ranked.Where(r => r.Entity != null && ids.Contains(r.Entity.Id)).ToList();
        return Task.FromResult(Ok(new GetEntityLeaderboardResponse
        {
            Columns    = columns,
            EntryCount = (uint)ranked.Count,
            Rankings   = subset,
            Version    = version,
        }));
    }

    /// <summary>Returns the friend leaderboard for an entity (uses friend collection from the Client API).</summary>
    [HttpPost("/Leaderboard/GetFriendLeaderboardForEntity")]
    public Task<IHttpActionResult> Progression_GetFriendLeaderboardForEntity(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetFriendLeaderboardForEntityRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.LeaderboardName) || string.IsNullOrEmpty(body.Entity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "LeaderboardName and Entity are required"));

        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());
        var friendIds = Database.GetCollection<PlayfabFriendDbObject>(true)!
                                .Find(f => f.OwnerPlayFabId == pid).Select(f => f.FriendPlayFabId).ToHashSet();
        friendIds.Add(pid);

        var name = body.LeaderboardName;
        var def = Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!
                          .FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (def == null) return Task.FromResult(Err("LeaderboardNotFound", 1671, "Leaderboard not found", HttpStatusCode.NotFound));
        var version = body.Version ?? def.Version;
        var v = version;
        var rows = Database.GetCollection<PlayfabLeaderboardEntryDbObject>(true)!
                           .Find(r => r.TitleId == TITLE_ID && r.LeaderboardName == name && r.Version == v).ToList();
        var columns = LbColumns(def.ColumnsJson);
        var sortDir = columns.FirstOrDefault()?.SortDirection ?? LeaderboardSortDirection.Descending;
        var ranked  = RankAndProject(name, version, rows, sortDir);
        var subset  = ranked.Where(r => r.Entity != null && friendIds.Contains(r.Entity.Id)).ToList();
        return Task.FromResult(Ok(new GetEntityLeaderboardResponse
        {
            Columns    = columns,
            EntryCount = (uint)ranked.Count,
            Rankings   = subset,
            Version    = version,
        }));
    }

    /// <summary>Inserts or updates leaderboard entries for one or more entities.</summary>
    [HttpPost("/Leaderboard/UpdateLeaderboardEntries")]
    public Task<IHttpActionResult> Progression_UpdateLeaderboardEntries(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateLeaderboardEntriesRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.LeaderboardName) || body.Entries == null)
            return Task.FromResult(Err("InvalidParams", 1000, "LeaderboardName and Entries are required"));

        var name = body.LeaderboardName;
        var def = Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!
                          .FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (def == null) return Task.FromResult(Err("LeaderboardNotFound", 1671, "Leaderboard not found", HttpStatusCode.NotFound));
        var coll = Database.GetCollection<PlayfabLeaderboardEntryDbObject>(true)!;
        var version = def.Version;

        foreach (var entry in body.Entries)
        {
            if (string.IsNullOrEmpty(entry.EntityId)) continue;
            var eid = entry.EntityId;
            var existing = coll.FindOne(r => r.TitleId == TITLE_ID && r.LeaderboardName == name && r.Version == version && r.EntityId == eid);
            if (existing == null)
            {
                coll.Insert(new PlayfabLeaderboardEntryDbObject
                {
                    TitleId         = TITLE_ID,
                    LeaderboardName = name,
                    Version         = version,
                    EntityId        = eid,
                    EntityType      = def.EntityType,
                    Scores          = (entry.Scores ?? new()).Select(s => double.TryParse(s, out var v) ? v : 0.0).ToList(),
                    Metadata        = entry.Metadata ?? "",
                    LastUpdated     = NowUnix(),
                });
            }
            else
            {
                existing.Scores      = (entry.Scores ?? new()).Select(s => double.TryParse(s, out var v) ? v : 0.0).ToList();
                existing.Metadata    = entry.Metadata ?? "";
                existing.LastUpdated = NowUnix();
                coll.Update(existing);
            }
        }

        // Trim to size limit (keep top SizeLimit entries by primary score)
        if (def.SizeLimit > 0)
        {
            var current = coll.Find(r => r.TitleId == TITLE_ID && r.LeaderboardName == name && r.Version == version).ToList();
            var columns = LbColumns(def.ColumnsJson);
            var sortDir = columns.FirstOrDefault()?.SortDirection ?? LeaderboardSortDirection.Descending;
            var ranked = sortDir == LeaderboardSortDirection.Ascending
                ? current.OrderBy(r => r.Scores.Count > 0 ? r.Scores[0] : 0.0).ToList()
                : current.OrderByDescending(r => r.Scores.Count > 0 ? r.Scores[0] : 0.0).ToList();
            foreach (var stale in ranked.Skip(def.SizeLimit)) coll.Delete(stale.Id);
        }
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Deletes leaderboard entries for specific entities.</summary>
    [HttpPost("/Leaderboard/DeleteLeaderboardEntries")]
    public Task<IHttpActionResult> Progression_DeleteLeaderboardEntries(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteLeaderboardEntriesRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name) || body.EntityIds == null)
            return Task.FromResult(Err("InvalidParams", 1000, "Name and EntityIds are required"));

        var name = body.Name;
        var coll = Database.GetCollection<PlayfabLeaderboardEntryDbObject>(true)!;
        foreach (var eid in body.EntityIds)
        {
            var id = eid;
            coll.DeleteMany(r => r.TitleId == TITLE_ID && r.LeaderboardName == name && r.EntityId == id);
        }
        return Task.FromResult(Ok(new EmptyResponse()));
    }
}
