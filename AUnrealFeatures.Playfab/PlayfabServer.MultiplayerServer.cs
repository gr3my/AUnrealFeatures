using AUnrealFeatures.Hosting.Configuration;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Database;
using AUnrealFeatures.Playfab.Models;
using PlayFab.MultiplayerModels;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed class PlayfabBuildAliasDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string AliasId { get; set; } = "";
    public string AliasName { get; set; } = "";
    public string SelectionCriteriaJson { get; set; } = "";
}

public sealed class PlayfabAssetDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string MetadataJson { get; set; } = "";
    public long Created { get; set; }
}

public sealed class PlayfabCertificateDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Thumbprint { get; set; } = "";
    public string Base64 { get; set; } = "";
    public long Created { get; set; }
}

public sealed class PlayfabSecretDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1";
    public string Value { get; set; } = "";
    public long? ExpirationDate { get; set; }
}

public sealed class PlayfabContainerImageDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string ImageName { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}

public sealed class PlayfabRemoteUserDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string BuildId { get; set; } = "";
    public string Region { get; set; } = "";
    public string VmId { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public long? ExpirationTime { get; set; }
}

public sealed class PlayfabTitleMpsConfigDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public bool Enabled { get; set; }
    public string QuotasJson { get; set; } = "";
    public long LastUpdated { get; set; }
}

public sealed class PlayfabQuotaChangeDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string ChangeDescription { get; set; } = "";
    public string ChangesJson { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string Notes { get; set; } = "";
    public long? StartDate { get; set; }
    public bool WasApproved { get; set; }
    public long Created { get; set; }
}

public sealed partial class PlayfabServer
{
    /* ─── Build helpers ───────────────────────────────────────────────────── */

    private static GetBuildResponse BuildToGetResponse(PlayfabBuildDbObject b)
    {
        var regions = (b.RegionConfigurations ?? new()).Select(r =>
        {
            try { return JsonSerializer.Deserialize<BuildRegion>(r, JsonOpts); }
            catch { return new BuildRegion { Region = r }; }
        }).Where(r => r != null).Cast<BuildRegion>().ToList();
        var ports = (b.Ports ?? new()).Select(p =>
        {
            try { return JsonSerializer.Deserialize<Port>(p, JsonOpts); }
            catch { return new Port { Name = p, Num = 7777, Protocol = ProtocolType.UDP }; }
        }).Where(p => p != null).Cast<Port>().ToList();
        return new GetBuildResponse
        {
            BuildId              = b.BuildId,
            BuildName            = b.BuildName,
            BuildStatus          = "Deployed",
            ContainerFlavor      = Enum.TryParse<ContainerFlavor>(b.ContainerFlavor, out var cf) ? cf : ContainerFlavor.ManagedWindowsServerCore,
            CreationTime         = DateTimeOffset.FromUnixTimeSeconds(b.Created).UtcDateTime,
            MultiplayerServerCountPerVm = (int)b.MultiplayerServerCountPerVm,
            RegionConfigurations = regions,
            Ports                = ports,
        };
    }

    private static BuildSummary BuildToSummary(PlayfabBuildDbObject b)
    {
        var regions = (b.RegionConfigurations ?? new()).Select(r =>
        {
            try { return JsonSerializer.Deserialize<BuildRegion>(r, JsonOpts); }
            catch { return new BuildRegion { Region = r }; }
        }).Where(r => r != null).Cast<BuildRegion>().ToList();
        return new BuildSummary
        {
            BuildId      = b.BuildId,
            BuildName    = b.BuildName,
            CreationTime = DateTimeOffset.FromUnixTimeSeconds(b.Created).UtcDateTime,
            RegionConfigurations = regions,
        };
    }

    private PlayfabBuildDbObject CreateBuildRow(string buildName, ContainerFlavor? flavor, uint countPerVm, List<string> regions, List<string> ports)
    {
        var build = new PlayfabBuildDbObject
        {
            BuildId                     = NewId(),
            BuildName                   = buildName ?? "",
            ContainerFlavor             = (flavor ?? ContainerFlavor.ManagedWindowsServerCore).ToString(),
            MultiplayerServerCountPerVm = countPerVm,
            RegionConfigurations        = regions,
            Ports                       = ports,
            Created                     = NowUnix(),
        };
        Database.GetCollection<PlayfabBuildDbObject>(true)!.Insert(build);
        return build;
    }

    /* ─── Build CRUD ─────────────────────────────────────────────────────── */

    /// <summary>Creates a build with a custom container.</summary>
    [HttpPost("/MultiplayerServer/CreateBuildWithCustomContainer")]
    public Task<IHttpActionResult> Multiplayer_CreateBuildWithCustomContainer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateBuildWithCustomContainerRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));

        var regions = (body.RegionConfigurations ?? new()).Select(r => JsonSerializer.Serialize(r, JsonOpts)).ToList();
        var ports   = (body.Ports ?? new()).Select(p => JsonSerializer.Serialize(p, JsonOpts)).ToList();
        var b = CreateBuildRow(body.BuildName, body.ContainerFlavor, (uint)body.MultiplayerServerCountPerVm, regions, ports);
        return Task.FromResult(Ok(new CreateBuildWithCustomContainerResponse
        {
            BuildId                     = b.BuildId,
            BuildName                   = b.BuildName,
            ContainerFlavor             = body.ContainerFlavor,
            CreationTime                = DateTimeOffset.FromUnixTimeSeconds(b.Created).UtcDateTime,
            MultiplayerServerCountPerVm = body.MultiplayerServerCountPerVm,
            CustomGameContainerImage    = body.ContainerImageReference,
            ContainerRunCommand         = body.ContainerRunCommand,
            Ports                       = body.Ports,
        }));
    }

    /// <summary>Creates a build with a managed container.</summary>
    [HttpPost("/MultiplayerServer/CreateBuildWithManagedContainer")]
    public Task<IHttpActionResult> Multiplayer_CreateBuildWithManagedContainer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateBuildWithManagedContainerRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));

        var regions = (body.RegionConfigurations ?? new()).Select(r => JsonSerializer.Serialize(r, JsonOpts)).ToList();
        var ports   = (body.Ports ?? new()).Select(p => JsonSerializer.Serialize(p, JsonOpts)).ToList();
        var b = CreateBuildRow(body.BuildName, body.ContainerFlavor, (uint)body.MultiplayerServerCountPerVm, regions, ports);
        return Task.FromResult(Ok(new CreateBuildWithManagedContainerResponse
        {
            BuildId                     = b.BuildId,
            BuildName                   = b.BuildName,
            ContainerFlavor             = body.ContainerFlavor,
            CreationTime                = DateTimeOffset.FromUnixTimeSeconds(b.Created).UtcDateTime,
            MultiplayerServerCountPerVm = body.MultiplayerServerCountPerVm,
            Ports                       = body.Ports,
        }));
    }

    /// <summary>Creates a build using process-based servers.</summary>
    [HttpPost("/MultiplayerServer/CreateBuildWithProcessBasedServer")]
    public Task<IHttpActionResult> Multiplayer_CreateBuildWithProcessBasedServer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateBuildWithProcessBasedServerRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));

        var regions = (body.RegionConfigurations ?? new()).Select(r => JsonSerializer.Serialize(r, JsonOpts)).ToList();
        var ports   = (body.Ports ?? new()).Select(p => JsonSerializer.Serialize(p, JsonOpts)).ToList();
        var b = CreateBuildRow(body.BuildName, null, (uint)body.MultiplayerServerCountPerVm, regions, ports);
        return Task.FromResult(Ok(new CreateBuildWithProcessBasedServerResponse
        {
            BuildId                     = b.BuildId,
            BuildName                   = b.BuildName,
            CreationTime                = DateTimeOffset.FromUnixTimeSeconds(b.Created).UtcDateTime,
            MultiplayerServerCountPerVm = body.MultiplayerServerCountPerVm,
            Ports                       = body.Ports,
        }));
    }

    /// <summary>Returns the build details.</summary>
    [HttpPost("/MultiplayerServer/GetBuild")]
    public Task<IHttpActionResult> Multiplayer_GetBuild(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetBuildRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.BuildId)) return Task.FromResult(Err("InvalidParams", 1000, "BuildId is required"));
        var bid = body.BuildId;
        var b = Database.GetCollection<PlayfabBuildDbObject>(true)!.FindOne(x => x.BuildId == bid);
        if (b == null) return Task.FromResult(Err("BuildNotFound", 1840, "Build not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(BuildToGetResponse(b)));
    }

    /// <summary>Deletes a build.</summary>
    [HttpPost("/MultiplayerServer/DeleteBuild")]
    public Task<IHttpActionResult> Multiplayer_DeleteBuild(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteBuildRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.BuildId)) return Task.FromResult(Err("InvalidParams", 1000, "BuildId is required"));
        var bid = body.BuildId;
        Database.GetCollection<PlayfabBuildDbObject>(true)!.DeleteMany(x => x.BuildId == bid);
        Database.GetCollection<PlayfabServerDbObject>(true)!.DeleteMany(x => x.BuildId == bid);
        Database.GetCollection<PlayfabRemoteUserDbObject>(true)!.DeleteMany(x => x.BuildId == bid);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Updates a build's name.</summary>
    [HttpPost("/MultiplayerServer/UpdateBuildName")]
    public Task<IHttpActionResult> Multiplayer_UpdateBuildName(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateBuildNameRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.BuildId)) return Task.FromResult(Err("InvalidParams", 1000, "BuildId is required"));
        var coll = Database.GetCollection<PlayfabBuildDbObject>(true)!;
        var bid = body.BuildId;
        var b = coll.FindOne(x => x.BuildId == bid);
        if (b == null) return Task.FromResult(Err("BuildNotFound", 1840, "Build not found", HttpStatusCode.NotFound));
        b.BuildName = body.BuildName ?? "";
        coll.Update(b);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Updates regional configuration for one region.</summary>
    [HttpPost("/MultiplayerServer/UpdateBuildRegion")]
    public Task<IHttpActionResult> Multiplayer_UpdateBuildRegion(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateBuildRegionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.BuildId) || body.BuildRegion == null)
            return Task.FromResult(Err("InvalidParams", 1000, "BuildId and BuildRegion are required"));
        var coll = Database.GetCollection<PlayfabBuildDbObject>(true)!;
        var bid = body.BuildId;
        var b = coll.FindOne(x => x.BuildId == bid);
        if (b == null) return Task.FromResult(Err("BuildNotFound", 1840, "Build not found", HttpStatusCode.NotFound));
        var json = JsonSerializer.Serialize(body.BuildRegion, JsonOpts);
        var existingIdx = b.RegionConfigurations.FindIndex(r => r.Contains($"\"Region\":\"{body.BuildRegion.Region}\""));
        if (existingIdx >= 0) b.RegionConfigurations[existingIdx] = json;
        else b.RegionConfigurations.Add(json);
        coll.Update(b);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Replaces the regional configuration list.</summary>
    [HttpPost("/MultiplayerServer/UpdateBuildRegions")]
    public Task<IHttpActionResult> Multiplayer_UpdateBuildRegions(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateBuildRegionsRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.BuildId)) return Task.FromResult(Err("InvalidParams", 1000, "BuildId is required"));
        var coll = Database.GetCollection<PlayfabBuildDbObject>(true)!;
        var bid = body.BuildId;
        var b = coll.FindOne(x => x.BuildId == bid);
        if (b == null) return Task.FromResult(Err("BuildNotFound", 1840, "Build not found", HttpStatusCode.NotFound));
        b.RegionConfigurations = (body.BuildRegions ?? new()).Select(r => JsonSerializer.Serialize(r, JsonOpts)).ToList();
        coll.Update(b);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Removes a region from a build.</summary>
    [HttpPost("/MultiplayerServer/DeleteBuildRegion")]
    public Task<IHttpActionResult> Multiplayer_DeleteBuildRegion(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteBuildRegionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.BuildId) || string.IsNullOrEmpty(body.Region))
            return Task.FromResult(Err("InvalidParams", 1000, "BuildId and Region are required"));
        var coll = Database.GetCollection<PlayfabBuildDbObject>(true)!;
        var bid = body.BuildId;
        var b = coll.FindOne(x => x.BuildId == bid);
        if (b == null) return Task.FromResult(Err("BuildNotFound", 1840, "Build not found", HttpStatusCode.NotFound));
        b.RegionConfigurations.RemoveAll(r => r.Contains($"\"Region\":\"{body.Region}\""));
        coll.Update(b);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Lists builds (paged).</summary>
    [HttpPost("/MultiplayerServer/ListBuildSummariesV2")]
    public Task<IHttpActionResult> Multiplayer_ListBuildSummariesV2(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabBuildDbObject>(true)!.FindAll().ToList();
        return Task.FromResult(Ok(new ListBuildSummariesResponse
        {
            BuildSummaries = rows.Select(BuildToSummary).ToList(),
            PageSize       = rows.Count,
            SkipToken      = "",
        }));
    }

    /* ─── Build Aliases ───────────────────────────────────────────────────── */

    /// <summary>Creates a build alias.</summary>
    [HttpPost("/MultiplayerServer/CreateBuildAlias")]
    public Task<IHttpActionResult> Multiplayer_CreateBuildAlias(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateBuildAliasRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.AliasName)) return Task.FromResult(Err("InvalidParams", 1000, "AliasName required"));
        var alias = new PlayfabBuildAliasDbObject
        {
            TitleId               = TITLE_ID,
            AliasId               = NewId(),
            AliasName             = body.AliasName,
            SelectionCriteriaJson = JsonSerializer.Serialize(body.BuildSelectionCriteria ?? new(), JsonOpts),
        };
        Database.GetCollection<PlayfabBuildAliasDbObject>(true)!.Insert(alias);
        return Task.FromResult(Ok(new BuildAliasDetailsResponse
        {
            AliasId                = alias.AliasId,
            AliasName              = alias.AliasName,
            BuildSelectionCriteria = body.BuildSelectionCriteria ?? new(),
        }));
    }

    /// <summary>Updates a build alias.</summary>
    [HttpPost("/MultiplayerServer/UpdateBuildAlias")]
    public Task<IHttpActionResult> Multiplayer_UpdateBuildAlias(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateBuildAliasRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.AliasId)) return Task.FromResult(Err("InvalidParams", 1000, "AliasId required"));
        var coll = Database.GetCollection<PlayfabBuildAliasDbObject>(true)!;
        var aid = body.AliasId;
        var a = coll.FindOne(x => x.AliasId == aid);
        if (a == null) return Task.FromResult(Err("BuildAliasNotFound", 1841, "Alias not found", HttpStatusCode.NotFound));
        if (!string.IsNullOrEmpty(body.AliasName)) a.AliasName = body.AliasName;
        if (body.BuildSelectionCriteria != null)
            a.SelectionCriteriaJson = JsonSerializer.Serialize(body.BuildSelectionCriteria, JsonOpts);
        coll.Update(a);
        return Task.FromResult(Ok(new BuildAliasDetailsResponse
        {
            AliasId                = a.AliasId,
            AliasName              = a.AliasName,
            BuildSelectionCriteria = body.BuildSelectionCriteria ?? new(),
        }));
    }

    /// <summary>Returns a build alias.</summary>
    [HttpPost("/MultiplayerServer/GetBuildAlias")]
    public Task<IHttpActionResult> Multiplayer_GetBuildAlias(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetBuildAliasRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.AliasId)) return Task.FromResult(Err("InvalidParams", 1000, "AliasId required"));
        var aid = body.AliasId;
        var a = Database.GetCollection<PlayfabBuildAliasDbObject>(true)!.FindOne(x => x.AliasId == aid);
        if (a == null) return Task.FromResult(Err("BuildAliasNotFound", 1841, "Alias not found", HttpStatusCode.NotFound));
        List<BuildSelectionCriterion> criteria = new();
        try { criteria = JsonSerializer.Deserialize<List<BuildSelectionCriterion>>(a.SelectionCriteriaJson, JsonOpts) ?? new(); } catch { }
        return Task.FromResult(Ok(new BuildAliasDetailsResponse
        {
            AliasId                = a.AliasId,
            AliasName              = a.AliasName,
            BuildSelectionCriteria = criteria,
        }));
    }

    /// <summary>Deletes a build alias.</summary>
    [HttpPost("/MultiplayerServer/DeleteBuildAlias")]
    public Task<IHttpActionResult> Multiplayer_DeleteBuildAlias(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteBuildAliasRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.AliasId)) return Task.FromResult(Err("InvalidParams", 1000, "AliasId required"));
        var aid = body.AliasId;
        Database.GetCollection<PlayfabBuildAliasDbObject>(true)!.DeleteMany(x => x.AliasId == aid);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Lists build aliases.</summary>
    [HttpPost("/MultiplayerServer/ListBuildAliases")]
    public Task<IHttpActionResult> Multiplayer_ListBuildAliases(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabBuildAliasDbObject>(true)!.FindAll().ToList();
        var aliases = rows.Select(a =>
        {
            List<BuildSelectionCriterion> criteria = new();
            try { criteria = JsonSerializer.Deserialize<List<BuildSelectionCriterion>>(a.SelectionCriteriaJson, JsonOpts) ?? new(); } catch { }
            return new BuildAliasDetailsResponse { AliasId = a.AliasId, AliasName = a.AliasName, BuildSelectionCriteria = criteria };
        }).ToList();
        return Task.FromResult(Ok(new ListBuildAliasesResponse { BuildAliases = aliases, PageSize = aliases.Count, SkipToken = "" }));
    }

    /* ─── Multiplayer Servers (sessions) ──────────────────────────────────── */

    /// <summary>Allocates a multiplayer server.</summary>
    [HttpPost("/MultiplayerServer/RequestMultiplayerServer")]
    public Task<IHttpActionResult> Multiplayer_RequestMultiplayerServer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RequestMultiplayerServerRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var region = (body.PreferredRegions != null && body.PreferredRegions.Count > 0) ? body.PreferredRegions[0] : "EastUs";
        var server = new PlayfabServerDbObject
        {
            ServerId                = NewId(),
            BuildId                 = body.BuildId ?? "",
            Region                  = region,
            SessionId               = string.IsNullOrEmpty(body.SessionId) ? NewId() : body.SessionId,
            IPV4Address             = "127.0.0.1",
            Ports                   = new List<string> { "7777/UDP" },
            State                   = "Active",
            LastStateTransitionTime = NowUnix(),
            VmId                    = "vm-" + NewId().Substring(0, 8),
            ConnectedPlayers        = body.InitialPlayers ?? new(),
        };
        Database.GetCollection<PlayfabServerDbObject>(true)!.Insert(server);
        return Task.FromResult(Ok(new RequestMultiplayerServerResponse
        {
            BuildId                 = server.BuildId,
            SessionId               = server.SessionId,
            ServerId                = server.ServerId,
            Region                  = server.Region,
            IPV4Address             = server.IPV4Address,
            Ports                   = new List<Port> { new() { Name = "game_port", Num = 7777, Protocol = ProtocolType.UDP } },
            State                   = server.State,
            LastStateTransitionTime = DateTimeOffset.FromUnixTimeSeconds(server.LastStateTransitionTime).UtcDateTime,
            VmId                    = server.VmId,
            ConnectedPlayers        = (server.ConnectedPlayers ?? new()).Select(p => new ConnectedPlayer { PlayerId = p }).ToList(),
        }));
    }

    /// <summary>Shuts down a running multiplayer server.</summary>
    [HttpPost("/MultiplayerServer/ShutdownMultiplayerServer")]
    public Task<IHttpActionResult> Multiplayer_ShutdownMultiplayerServer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ShutdownMultiplayerServerRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.SessionId)) return Task.FromResult(Err("InvalidParams", 1000, "SessionId required"));
        var coll = Database.GetCollection<PlayfabServerDbObject>(true)!;
        var sid = body.SessionId;
        var s = coll.FindOne(x => x.SessionId == sid);
        if (s != null) { s.State = "Terminating"; s.LastStateTransitionTime = NowUnix(); coll.Update(s); }
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Returns details for a server session.</summary>
    [HttpPost("/MultiplayerServer/GetMultiplayerServerDetails")]
    public Task<IHttpActionResult> Multiplayer_GetMultiplayerServerDetails(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetMultiplayerServerDetailsRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.SessionId)) return Task.FromResult(Err("InvalidParams", 1000, "SessionId required"));
        var sid = body.SessionId;
        var s = Database.GetCollection<PlayfabServerDbObject>(true)!.FindOne(x => x.SessionId == sid);
        if (s == null) return Task.FromResult(Err("MultiplayerServerNotFound", 1842, "Server not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new GetMultiplayerServerDetailsResponse
        {
            BuildId                 = s.BuildId,
            SessionId               = s.SessionId,
            ServerId                = s.ServerId,
            Region                  = s.Region,
            IPV4Address             = s.IPV4Address,
            Ports                   = new List<Port> { new() { Name = "game_port", Num = 7777, Protocol = ProtocolType.UDP } },
            State                   = s.State,
            LastStateTransitionTime = DateTimeOffset.FromUnixTimeSeconds(s.LastStateTransitionTime).UtcDateTime,
            VmId                    = s.VmId,
            ConnectedPlayers        = (s.ConnectedPlayers ?? new()).Select(p => new ConnectedPlayer { PlayerId = p }).ToList(),
        }));
    }

    /// <summary>Lists multiplayer servers (paged).</summary>
    [HttpPost("/MultiplayerServer/ListMultiplayerServers")]
    public Task<IHttpActionResult> Multiplayer_ListMultiplayerServers(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ListMultiplayerServersRequest>(request);
        var coll = Database.GetCollection<PlayfabServerDbObject>(true)!;
        var rows = coll.FindAll().Where(s =>
            (string.IsNullOrEmpty(body?.BuildId) || s.BuildId == body.BuildId) &&
            (string.IsNullOrEmpty(body?.Region)  || s.Region  == body.Region)).ToList();
        return Task.FromResult(Ok(new ListMultiplayerServersResponse
        {
            MultiplayerServerSummaries = rows.Select(s => new MultiplayerServerSummary
            {
                ServerId                = s.ServerId,
                SessionId               = s.SessionId,
                Region                  = s.Region,
                State                   = s.State,
                VmId                    = s.VmId,
                LastStateTransitionTime = DateTimeOffset.FromUnixTimeSeconds(s.LastStateTransitionTime).UtcDateTime,
                ConnectedPlayers        = (s.ConnectedPlayers ?? new()).Select(p => new ConnectedPlayer { PlayerId = p }).ToList(),
            }).ToList(),
            PageSize  = rows.Count,
            SkipToken = "",
        }));
    }

    /// <summary>Lists archived multiplayer servers.</summary>
    [HttpPost("/MultiplayerServer/ListArchivedMultiplayerServers")]
    public Task<IHttpActionResult> Multiplayer_ListArchivedMultiplayerServers(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabServerDbObject>(true)!
                           .Find(s => s.State == "Terminated" || s.State == "Archived").ToList();
        return Task.FromResult(Ok(new ListMultiplayerServersResponse
        {
            MultiplayerServerSummaries = rows.Select(s => new MultiplayerServerSummary
            {
                ServerId  = s.ServerId,
                SessionId = s.SessionId,
                Region    = s.Region,
                State     = s.State,
                VmId      = s.VmId,
            }).ToList(),
            PageSize  = rows.Count,
            SkipToken = "",
        }));
    }

    /// <summary>Returns a download URL for server logs.</summary>
    [HttpPost("/MultiplayerServer/GetMultiplayerServerLogs")]
    public Task<IHttpActionResult> Multiplayer_GetMultiplayerServerLogs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetMultiplayerServerLogsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        return Task.FromResult(Ok(new GetMultiplayerServerLogsResponse
        {
            LogDownloadUrl = $"/MultiplayerServer/Logs/{Uri.EscapeDataString(body.ServerId ?? "")}",
        }));
    }

    /// <summary>Returns a download URL for session logs.</summary>
    [HttpPost("/MultiplayerServer/GetMultiplayerSessionLogsBySessionId")]
    public Task<IHttpActionResult> Multiplayer_GetMultiplayerSessionLogsBySessionId(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetMultiplayerSessionLogsBySessionIdRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        return Task.FromResult(Ok(new GetMultiplayerServerLogsResponse
        {
            LogDownloadUrl = $"/MultiplayerServer/SessionLogs/{Uri.EscapeDataString(body.SessionId ?? "")}",
        }));
    }

    /// <summary>Lists VMs hosting multiplayer servers.</summary>
    [HttpPost("/MultiplayerServer/ListVirtualMachineSummaries")]
    public Task<IHttpActionResult> Multiplayer_ListVirtualMachineSummaries(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ListVirtualMachineSummariesRequest>(request);
        var rows = Database.GetCollection<PlayfabServerDbObject>(true)!.FindAll().ToList();
        var vms = rows.Where(s =>
            (string.IsNullOrEmpty(body?.BuildId) || s.BuildId == body.BuildId) &&
            (string.IsNullOrEmpty(body?.Region)  || s.Region  == body.Region))
            .Select(s => s.VmId).Distinct()
            .Select(vm => new VirtualMachineSummary { VmId = vm, State = "Healthy", HealthStatus = "Available" }).ToList();
        return Task.FromResult(Ok(new ListVirtualMachineSummariesResponse { VirtualMachines = vms, PageSize = vms.Count, SkipToken = "" }));
    }

    /* ─── Remote users ────────────────────────────────────────────────────── */

    /// <summary>Creates a remote user account on the host VM.</summary>
    [HttpPost("/MultiplayerServer/CreateRemoteUser")]
    public Task<IHttpActionResult> Multiplayer_CreateRemoteUser(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateRemoteUserRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Username)) return Task.FromResult(Err("InvalidParams", 1000, "Username required"));
        var pw = NewId().Substring(0, 16);
        var exp = body.ExpirationTime ?? DateTime.UtcNow.AddDays(7);
        Database.GetCollection<PlayfabRemoteUserDbObject>(true)!.Insert(new PlayfabRemoteUserDbObject
        {
            TitleId        = TITLE_ID,
            BuildId        = body.BuildId ?? "",
            Region         = body.Region ?? "",
            VmId           = body.VmId ?? "",
            Username       = body.Username,
            Password       = pw,
            ExpirationTime = new DateTimeOffset(exp, TimeSpan.Zero).ToUnixTimeSeconds(),
        });
        return Task.FromResult(Ok(new CreateRemoteUserResponse { Username = body.Username, Password = pw, ExpirationTime = exp }));
    }

    /// <summary>Deletes a remote user account.</summary>
    [HttpPost("/MultiplayerServer/DeleteRemoteUser")]
    public Task<IHttpActionResult> Multiplayer_DeleteRemoteUser(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteRemoteUserRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var u = body.Username;
        var v = body.VmId;
        Database.GetCollection<PlayfabRemoteUserDbObject>(true)!.DeleteMany(x => x.Username == u && x.VmId == v);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Returns the RDP/SSH endpoint for a VM.</summary>
    [HttpPost("/MultiplayerServer/GetRemoteLoginEndpoint")]
    public Task<IHttpActionResult> Multiplayer_GetRemoteLoginEndpoint(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        return Task.FromResult(Ok(new GetRemoteLoginEndpointResponse { IPV4Address = "127.0.0.1", Port = 3389 }));
    }

    /* ─── Assets ──────────────────────────────────────────────────────────── */

    /// <summary>Returns a presigned URL to upload an asset.</summary>
    [HttpPost("/MultiplayerServer/GetAssetUploadUrl")]
    public Task<IHttpActionResult> Multiplayer_GetAssetUploadUrl(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetAssetUploadUrlRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FileName)) return Task.FromResult(Err("InvalidParams", 1000, "FileName required"));
        // Pre-create an asset entry so subsequent ListAssetSummaries can show it
        var coll = Database.GetCollection<PlayfabAssetDbObject>(true)!;
        var fn = body.FileName;
        var existing = coll.FindOne(x => x.TitleId == TITLE_ID && x.FileName == fn);
        if (existing == null)
            coll.Insert(new PlayfabAssetDbObject { TitleId = TITLE_ID, FileName = fn, Created = NowUnix() });
        return Task.FromResult(Ok(new GetAssetUploadUrlResponse
        {
            FileName       = body.FileName,
            AssetUploadUrl = $"/MultiplayerServer/Assets/Upload/{Uri.EscapeDataString(body.FileName)}",
        }));
    }

    /// <summary>Returns a presigned URL to download an asset.</summary>
    [HttpPost("/MultiplayerServer/GetAssetDownloadUrl")]
    public Task<IHttpActionResult> Multiplayer_GetAssetDownloadUrl(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetAssetDownloadUrlRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FileName)) return Task.FromResult(Err("InvalidParams", 1000, "FileName required"));
        return Task.FromResult(Ok(new GetAssetDownloadUrlResponse
        {
            FileName         = body.FileName,
            AssetDownloadUrl = $"/MultiplayerServer/Assets/Download/{Uri.EscapeDataString(body.FileName)}",
        }));
    }

    /// <summary>Deletes an uploaded asset.</summary>
    [HttpPost("/MultiplayerServer/DeleteAsset")]
    public Task<IHttpActionResult> Multiplayer_DeleteAsset(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteAssetRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FileName)) return Task.FromResult(Err("InvalidParams", 1000, "FileName required"));
        var fn = body.FileName;
        Database.GetCollection<PlayfabAssetDbObject>(true)!.DeleteMany(x => x.TitleId == TITLE_ID && x.FileName == fn);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Lists asset summaries.</summary>
    [HttpPost("/MultiplayerServer/ListAssetSummaries")]
    public Task<IHttpActionResult> Multiplayer_ListAssetSummaries(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabAssetDbObject>(true)!
                           .Find(x => x.TitleId == TITLE_ID).ToList();
        var summaries = rows.Select(r => new AssetSummary
        {
            FileName = r.FileName,
            Metadata = string.IsNullOrEmpty(r.MetadataJson) ? new() : (JsonSerializer.Deserialize<Dictionary<string, string>>(r.MetadataJson, JsonOpts) ?? new()),
        }).ToList();
        return Task.FromResult(Ok(new ListAssetSummariesResponse { AssetSummaries = summaries, PageSize = summaries.Count, SkipToken = "" }));
    }

    /* ─── Certificates ────────────────────────────────────────────────────── */

    /// <summary>Uploads a certificate.</summary>
    [HttpPost("/MultiplayerServer/UploadCertificate")]
    public Task<IHttpActionResult> Multiplayer_UploadCertificate(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UploadCertificateRequest>(request);
        if (body == null || body.GameCertificate == null || string.IsNullOrEmpty(body.GameCertificate.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "GameCertificate.Name required"));
        var coll = Database.GetCollection<PlayfabCertificateDbObject>(true)!;
        var name = body.GameCertificate.Name;
        var existing = coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (existing == null)
            coll.Insert(new PlayfabCertificateDbObject
            {
                TitleId    = TITLE_ID,
                Name       = body.GameCertificate.Name,
                Thumbprint = "",
                Base64     = body.GameCertificate.Base64EncodedValue ?? "",
                Created    = NowUnix(),
            });
        else
        {
            // Thumbprint is computed from the base64 cert; keep existing
            existing.Base64     = body.GameCertificate.Base64EncodedValue ?? existing.Base64;
            coll.Update(existing);
        }
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Deletes a certificate.</summary>
    [HttpPost("/MultiplayerServer/DeleteCertificate")]
    public Task<IHttpActionResult> Multiplayer_DeleteCertificate(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteCertificateRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name)) return Task.FromResult(Err("InvalidParams", 1000, "Name required"));
        var n = body.Name;
        Database.GetCollection<PlayfabCertificateDbObject>(true)!.DeleteMany(x => x.TitleId == TITLE_ID && x.Name == n);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Lists certificate summaries.</summary>
    [HttpPost("/MultiplayerServer/ListCertificateSummaries")]
    public Task<IHttpActionResult> Multiplayer_ListCertificateSummaries(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabCertificateDbObject>(true)!
                           .Find(x => x.TitleId == TITLE_ID).ToList();
        var summaries = rows.Select(r => new CertificateSummary { Name = r.Name, Thumbprint = r.Thumbprint }).ToList();
        return Task.FromResult(Ok(new ListCertificateSummariesResponse
        {
            CertificateSummaries = summaries,
            PageSize             = summaries.Count,
            SkipToken            = "",
        }));
    }

    /* ─── Secrets ─────────────────────────────────────────────────────────── */

    /// <summary>Uploads a secret.</summary>
    [HttpPost("/MultiplayerServer/UploadSecret")]
    public Task<IHttpActionResult> Multiplayer_UploadSecret(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UploadSecretRequest>(request);
        if (body == null || body.GameSecret == null || string.IsNullOrEmpty(body.GameSecret.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "GameSecret.Name required"));
        var coll = Database.GetCollection<PlayfabSecretDbObject>(true)!;
        var name = body.GameSecret.Name;
        var existing = coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (existing == null)
            coll.Insert(new PlayfabSecretDbObject
            {
                TitleId        = TITLE_ID,
                Name           = body.GameSecret.Name,
                Value          = body.GameSecret.Value ?? "",
                Version        = "1",
                ExpirationDate = body.GameSecret.ExpirationDate.HasValue
                    ? new DateTimeOffset(body.GameSecret.ExpirationDate.Value, TimeSpan.Zero).ToUnixTimeSeconds()
                    : (long?)null,
            });
        else
        {
            existing.Value          = body.GameSecret.Value ?? existing.Value;
            existing.Version        = (int.TryParse(existing.Version, out var v) ? v + 1 : 2).ToString();
            existing.ExpirationDate = body.GameSecret.ExpirationDate.HasValue
                ? new DateTimeOffset(body.GameSecret.ExpirationDate.Value, TimeSpan.Zero).ToUnixTimeSeconds()
                : existing.ExpirationDate;
            coll.Update(existing);
        }
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Deletes a secret.</summary>
    [HttpPost("/MultiplayerServer/DeleteSecret")]
    public Task<IHttpActionResult> Multiplayer_DeleteSecret(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteSecretRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name)) return Task.FromResult(Err("InvalidParams", 1000, "Name required"));
        var n = body.Name;
        Database.GetCollection<PlayfabSecretDbObject>(true)!.DeleteMany(x => x.TitleId == TITLE_ID && x.Name == n);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Lists secret summaries.</summary>
    [HttpPost("/MultiplayerServer/ListSecretSummaries")]
    public Task<IHttpActionResult> Multiplayer_ListSecretSummaries(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabSecretDbObject>(true)!
                           .Find(x => x.TitleId == TITLE_ID).ToList();
        var summaries = rows.Select(r => new SecretSummary
        {
            Name           = r.Name,
            Version        = r.Version,
            ExpirationDate = r.ExpirationDate.HasValue ? DateTimeOffset.FromUnixTimeSeconds(r.ExpirationDate.Value).UtcDateTime : (DateTime?)null,
        }).ToList();
        return Task.FromResult(Ok(new ListSecretSummariesResponse
        {
            SecretSummaries = summaries,
            PageSize        = summaries.Count,
            SkipToken       = "",
        }));
    }

    /* ─── Container Registry ──────────────────────────────────────────────── */

    /// <summary>Lists container images.</summary>
    [HttpPost("/MultiplayerServer/ListContainerImages")]
    public Task<IHttpActionResult> Multiplayer_ListContainerImages(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabContainerImageDbObject>(true)!
                           .Find(x => x.TitleId == TITLE_ID).ToList();
        return Task.FromResult(Ok(new ListContainerImagesResponse
        {
            Images    = rows.Select(r => r.ImageName).ToList(),
            PageSize  = rows.Count,
            SkipToken = "",
        }));
    }

    /// <summary>Lists tags for a container image.</summary>
    [HttpPost("/MultiplayerServer/ListContainerImageTags")]
    public Task<IHttpActionResult> Multiplayer_ListContainerImageTags(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ListContainerImageTagsRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ImageName)) return Task.FromResult(Err("InvalidParams", 1000, "ImageName required"));
        var img = body.ImageName;
        var row = Database.GetCollection<PlayfabContainerImageDbObject>(true)!
                          .FindOne(x => x.TitleId == TITLE_ID && x.ImageName == img);
        return Task.FromResult(Ok(new ListContainerImageTagsResponse
        {
            Tags      = row?.Tags ?? new(),
            PageSize  = row?.Tags?.Count ?? 0,
            SkipToken = "",
        }));
    }

    /// <summary>Removes a tag from a container image.</summary>
    [HttpPost("/MultiplayerServer/UntagContainerImage")]
    public Task<IHttpActionResult> Multiplayer_UntagContainerImage(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UntagContainerImageRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ImageName) || string.IsNullOrEmpty(body.Tag))
            return Task.FromResult(Err("InvalidParams", 1000, "ImageName and Tag required"));
        var coll = Database.GetCollection<PlayfabContainerImageDbObject>(true)!;
        var img = body.ImageName;
        var row = coll.FindOne(x => x.TitleId == TITLE_ID && x.ImageName == img);
        if (row != null) { row.Tags.RemoveAll(t => t == body.Tag); coll.Update(row); }
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Deletes a container image repository.</summary>
    [HttpPost("/MultiplayerServer/DeleteContainerImageRepository")]
    public Task<IHttpActionResult> Multiplayer_DeleteContainerImageRepository(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteContainerImageRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ImageName)) return Task.FromResult(Err("InvalidParams", 1000, "ImageName required"));
        var img = body.ImageName;
        Database.GetCollection<PlayfabContainerImageDbObject>(true)!.DeleteMany(x => x.TitleId == TITLE_ID && x.ImageName == img);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Returns container registry credentials.</summary>
    [HttpPost("/MultiplayerServer/GetContainerRegistryCredentials")]
    public Task<IHttpActionResult> Multiplayer_GetContainerRegistryCredentials(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        return Task.FromResult(Ok(new GetContainerRegistryCredentialsResponse
        {
            DnsName  = $"{TITLE_ID.ToLowerInvariant()}.azurecr.io",
            Username = TITLE_ID,
            Password = EnvironmentSettings.Get("PLAYFAB_CONTAINER_REGISTRY_PASSWORD"),
        }));
    }

    /// <summary>Rolls over container registry credentials.</summary>
    [HttpPost("/MultiplayerServer/RolloverContainerRegistryCredentials")]
    public Task<IHttpActionResult> Multiplayer_RolloverContainerRegistryCredentials(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /* ─── Title status, quotas, QoS ──────────────────────────────────────── */

    private PlayfabTitleMpsConfigDbObject GetOrCreateMpsConfig()
    {
        var coll = Database.GetCollection<PlayfabTitleMpsConfigDbObject>(true)!;
        var row = coll.FindOne(x => x.TitleId == TITLE_ID);
        if (row != null) return row;
        row = new PlayfabTitleMpsConfigDbObject { TitleId = TITLE_ID, Enabled = false, LastUpdated = NowUnix() };
        coll.Insert(row);
        return row;
    }

    /// <summary>Enables multiplayer servers for the title.</summary>
    [HttpPost("/MultiplayerServer/EnableMultiplayerServersForTitle")]
    public Task<IHttpActionResult> Multiplayer_EnableMultiplayerServersForTitle(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var coll = Database.GetCollection<PlayfabTitleMpsConfigDbObject>(true)!;
        var row = GetOrCreateMpsConfig();
        row.Enabled = true; row.LastUpdated = NowUnix();
        coll.Update(row);
        return Task.FromResult(Ok(new EnableMultiplayerServersForTitleResponse { Status = TitleMultiplayerServerEnabledStatus.Enabled }));
    }

    /// <summary>Returns whether multiplayer servers are enabled for the title.</summary>
    [HttpPost("/MultiplayerServer/GetTitleEnabledForMultiplayerServersStatus")]
    public Task<IHttpActionResult> Multiplayer_GetTitleEnabledForMultiplayerServersStatus(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var row = GetOrCreateMpsConfig();
        return Task.FromResult(Ok(new GetTitleEnabledForMultiplayerServersStatusResponse
        {
            Status = row.Enabled ? TitleMultiplayerServerEnabledStatus.Enabled : TitleMultiplayerServerEnabledStatus.Disabled,
        }));
    }

    /// <summary>Returns the title's quotas.</summary>
    [HttpPost("/MultiplayerServer/GetTitleMultiplayerServersQuotas")]
    public Task<IHttpActionResult> Multiplayer_GetTitleMultiplayerServersQuotas(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var row = GetOrCreateMpsConfig();
        TitleMultiplayerServersQuotas? quotas = null;
        if (!string.IsNullOrEmpty(row.QuotasJson))
        {
            try { quotas = JsonSerializer.Deserialize<TitleMultiplayerServersQuotas>(row.QuotasJson, JsonOpts); }
            catch { }
        }
        quotas ??= new TitleMultiplayerServersQuotas
        {
            CoreCapacities = new List<CoreCapacity>
            {
                new() { Region = "EastUs", Total = 100, Available = 100, VmFamily = AzureVmFamily.Dasv4 },
            }
        };
        return Task.FromResult(Ok(new GetTitleMultiplayerServersQuotasResponse { Quotas = quotas }));
    }

    /// <summary>Creates a quota change request.</summary>
    [HttpPost("/MultiplayerServer/CreateTitleMultiplayerServersQuotaChange")]
    public Task<IHttpActionResult> Multiplayer_CreateTitleMultiplayerServersQuotaChange(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateTitleMultiplayerServersQuotaChangeRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var change = new PlayfabQuotaChangeDbObject
        {
            TitleId           = TITLE_ID,
            RequestId         = NewId(),
            ChangeDescription = body.ChangeDescription ?? "",
            ChangesJson       = JsonSerializer.Serialize(body.Changes ?? new(), JsonOpts),
            ContactEmail      = body.ContactEmail ?? "",
            Notes             = body.Notes ?? "",
            StartDate         = body.StartDate.HasValue ? new DateTimeOffset(body.StartDate.Value, TimeSpan.Zero).ToUnixTimeSeconds() : (long?)null,
            WasApproved       = false,
            Created           = NowUnix(),
        };
        Database.GetCollection<PlayfabQuotaChangeDbObject>(true)!.Insert(change);
        return Task.FromResult(Ok(new CreateTitleMultiplayerServersQuotaChangeResponse { RequestId = change.RequestId, WasApproved = false }));
    }

    /// <summary>Returns a quota change by request id.</summary>
    [HttpPost("/MultiplayerServer/GetTitleMultiplayerServersQuotaChange")]
    public Task<IHttpActionResult> Multiplayer_GetTitleMultiplayerServersQuotaChange(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetTitleMultiplayerServersQuotaChangeRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.RequestId)) return Task.FromResult(Err("InvalidParams", 1000, "RequestId required"));
        var rid = body.RequestId;
        var c = Database.GetCollection<PlayfabQuotaChangeDbObject>(true)!.FindOne(x => x.TitleId == TITLE_ID && x.RequestId == rid);
        if (c == null) return Task.FromResult(Err("QuotaChangeNotFound", 1850, "Quota change not found", HttpStatusCode.NotFound));
        List<CoreCapacityChange> changes = new();
        try { changes = JsonSerializer.Deserialize<List<CoreCapacityChange>>(c.ChangesJson, JsonOpts) ?? new(); } catch { }
        return Task.FromResult(Ok(new GetTitleMultiplayerServersQuotaChangeResponse
        {
            Change = new QuotaChange
            {
                RequestId         = c.RequestId,
                ChangeDescription = c.ChangeDescription,
                Notes             = c.Notes,
                WasApproved       = c.WasApproved,
                IsPendingReview   = !c.WasApproved,
                Changes           = changes,
            }
        }));
    }

    /// <summary>Lists all quota changes for the title.</summary>
    [HttpPost("/MultiplayerServer/ListTitleMultiplayerServersQuotaChanges")]
    public Task<IHttpActionResult> Multiplayer_ListTitleMultiplayerServersQuotaChanges(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabQuotaChangeDbObject>(true)!
                           .Find(x => x.TitleId == TITLE_ID).ToList();
        var changes = rows.Select(c =>
        {
            List<CoreCapacityChange> ch = new();
            try { ch = JsonSerializer.Deserialize<List<CoreCapacityChange>>(c.ChangesJson, JsonOpts) ?? new(); } catch { }
            return new QuotaChange
            {
                RequestId         = c.RequestId,
                ChangeDescription = c.ChangeDescription,
                Notes             = c.Notes,
                WasApproved       = c.WasApproved,
                IsPendingReview   = !c.WasApproved,
                Changes           = ch,
            };
        }).ToList();
        return Task.FromResult(Ok(new ListTitleMultiplayerServersQuotaChangesResponse { Changes = changes }));
    }

    /// <summary>Lists Party QoS servers.</summary>
    [HttpPost("/MultiplayerServer/ListPartyQosServers")]
    public Task<IHttpActionResult> Multiplayer_ListPartyQosServers(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        return Task.FromResult(Ok(new ListPartyQosServersResponse
        {
            QosServers = new List<QosServer>
            {
                new() { Region = "EastUs",     ServerUrl = "qos.eastus.playfab.com" },
                new() { Region = "WestUs",     ServerUrl = "qos.westus.playfab.com" },
                new() { Region = "NorthEurope", ServerUrl = "qos.northeu.playfab.com" },
            },
            PageSize  = 3,
            SkipToken = "",
        }));
    }

    /// <summary>Lists QoS servers for the title.</summary>
    [HttpPost("/MultiplayerServer/ListQosServersForTitle")]
    public Task<IHttpActionResult> Multiplayer_ListQosServersForTitle(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        return Task.FromResult(Ok(new ListQosServersForTitleResponse
        {
            QosServers = new List<QosServer>
            {
                new() { Region = "EastUs",     ServerUrl = "qos.eastus.playfab.com" },
                new() { Region = "WestUs",     ServerUrl = "qos.westus.playfab.com" },
                new() { Region = "NorthEurope", ServerUrl = "qos.northeu.playfab.com" },
            },
            PageSize  = 3,
            SkipToken = "",
        }));
    }

    /* ─── Party ───────────────────────────────────────────────────────────── */

    /// <summary>Requests a Party network for a player or group.</summary>
    [HttpPost("/Party/RequestPartyService")]
    public Task<IHttpActionResult> Multiplayer_RequestPartyService(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RequestPartyServiceRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var pid = string.IsNullOrEmpty(body.PartyId) ? NewId() : body.PartyId;
        var coll = Database.GetCollection<PlayfabPartyDbObject>(true)!;
        var existing = coll.FindOne(x => x.PartyId == pid);
        if (existing == null)
        {
            existing = new PlayfabPartyDbObject
            {
                PartyId          = pid,
                LeaderEntityId   = body.PlayFabId ?? PlayFabIdFromRequest(request),
                MemberEntityIds  = string.IsNullOrEmpty(body.PlayFabId) ? new() : new() { body.PlayFabId },
                Created          = NowUnix(),
            };
            coll.Insert(existing);
        }
        return Task.FromResult(Ok(new RequestPartyServiceResponse
        {
            PartyId                     = pid,
            InvitationId                = NewId(),
            SerializedNetworkDescriptor = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"party:{pid}")),
        }));
    }
}
