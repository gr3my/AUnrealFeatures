using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Database;
using PlayFab.InsightsModels;

namespace AUnrealFeatures.Playfab;

public sealed class PlayfabInsightsConfigDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public int PerformanceLevel { get; set; } = 1;
    public int RetentionDays { get; set; } = 30;
    public uint DataUsageMb { get; set; } = 0;
}

public sealed class PlayfabInsightsOperationDbObject : AstraDbObject
{
    public string OperationId { get; set; } = "";
    public string OperationType { get; set; } = "";
    public int OperationValue { get; set; }
    public string Status { get; set; } = "Pending";  // Pending | Running | Succeeded | Failed
    public string Message { get; set; } = "";
    public long StartedTime { get; set; }
    public long LastUpdated { get; set; }
    public long CompletedTime { get; set; }
}

public sealed partial class PlayfabServer
{
    /* ─── Insights API ────────────────────────────────────────────────────── */

    private PlayfabInsightsConfigDbObject GetOrCreateInsightsConfig()
    {
        var coll = Database.GetCollection<PlayfabInsightsConfigDbObject>(true)!;
        var c = coll.FindOne(x => x.TitleId == TITLE_ID);
        if (c != null) return c;
        c = new PlayfabInsightsConfigDbObject { TitleId = TITLE_ID, PerformanceLevel = 1, RetentionDays = 30 };
        coll.Insert(c);
        return c;
    }

    private static InsightsGetOperationStatusResponse OperationToResponse(PlayfabInsightsOperationDbObject op) => new()
    {
        OperationId            = op.OperationId,
        OperationType          = op.OperationType,
        OperationValue         = op.OperationValue,
        Status                 = op.Status,
        Message                = op.Message,
        OperationStartedTime   = DateTimeOffset.FromUnixTimeSeconds(op.StartedTime).UtcDateTime,
        OperationLastUpdated   = DateTimeOffset.FromUnixTimeSeconds(op.LastUpdated).UtcDateTime,
        OperationCompletedTime = DateTimeOffset.FromUnixTimeSeconds(op.CompletedTime).UtcDateTime,
    };

    private static InsightsGetLimitsResponse BuildLimits() => new()
    {
        DefaultPerformanceLevel     = 1,
        DefaultStorageRetentionDays = 30,
        StorageMaxRetentionDays     = 90,
        StorageMinRetentionDays     = 7,
        SubMeters = new List<InsightsPerformanceLevel>
        {
            new() { Level = 1, ActiveEventExports = 1, CacheSizeMB = 256,  Concurrency = 4,  CreditsPerMinute = 0.10, EventsPerSecond = 100,  MaxMemoryPerQueryMB = 128,  VirtualCpuCores = 1 },
            new() { Level = 2, ActiveEventExports = 2, CacheSizeMB = 512,  Concurrency = 8,  CreditsPerMinute = 0.25, EventsPerSecond = 250,  MaxMemoryPerQueryMB = 256,  VirtualCpuCores = 2 },
            new() { Level = 3, ActiveEventExports = 4, CacheSizeMB = 1024, Concurrency = 16, CreditsPerMinute = 0.50, EventsPerSecond = 500,  MaxMemoryPerQueryMB = 512,  VirtualCpuCores = 4 },
            new() { Level = 4, ActiveEventExports = 8, CacheSizeMB = 2048, Concurrency = 32, CreditsPerMinute = 1.00, EventsPerSecond = 1000, MaxMemoryPerQueryMB = 1024, VirtualCpuCores = 8 },
        }
    };

    /// <summary>Returns the title's Insights configuration and pending operations.</summary>
    [HttpPost("/Insights/GetDetails")]
    public Task<IHttpActionResult> Insights_GetDetails(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());

        var cfg = GetOrCreateInsightsConfig();
        var ops = Database.GetCollection<PlayfabInsightsOperationDbObject>(true)!
                          .Find(o => o.Status == "Pending" || o.Status == "Running")
                          .Select(OperationToResponse).ToList();

        return Task.FromResult(Ok(new InsightsGetDetailsResponse
        {
            DataUsageMb       = cfg.DataUsageMb,
            ErrorMessage      = "",
            Limits            = BuildLimits(),
            PendingOperations = ops,
            PerformanceLevel  = cfg.PerformanceLevel,
            RetentionDays     = cfg.RetentionDays,
        }));
    }

    /// <summary>Returns the platform's tier limits and per-level performance metrics.</summary>
    [HttpPost("/Insights/GetLimits")]
    public Task<IHttpActionResult> Insights_GetLimits(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        return Task.FromResult(Ok(BuildLimits()));
    }

    /// <summary>Returns the status of a specific operation.</summary>
    [HttpPost("/Insights/GetOperationStatus")]
    public Task<IHttpActionResult> Insights_GetOperationStatus(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<InsightsGetOperationStatusRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.OperationId))
            return Task.FromResult(Err("InvalidParams", 1000, "OperationId is required"));

        var oid = body.OperationId;
        var op = Database.GetCollection<PlayfabInsightsOperationDbObject>(true)!
                         .FindOne(o => o.OperationId == oid);
        if (op == null)
            return Task.FromResult(Err("OperationNotFound", 1115, "Operation not found", System.Net.HttpStatusCode.NotFound));

        // Auto-progress: any operation more than 2 seconds old is considered complete.
        if (op.Status == "Pending" || op.Status == "Running")
        {
            if (NowUnix() - op.StartedTime > 2)
            {
                op.Status        = "Succeeded";
                op.CompletedTime = NowUnix();
                op.LastUpdated   = NowUnix();
                op.Message       = "Operation completed successfully";
                Database.GetCollection<PlayfabInsightsOperationDbObject>(true)!.Update(op);
            }
        }
        return Task.FromResult(Ok(OperationToResponse(op)));
    }

    /// <summary>Returns all currently pending Insights operations.</summary>
    [HttpPost("/Insights/GetPendingOperations")]
    public Task<IHttpActionResult> Insights_GetPendingOperations(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var ops = Database.GetCollection<PlayfabInsightsOperationDbObject>(true)!
                          .Find(o => o.Status == "Pending" || o.Status == "Running")
                          .Select(OperationToResponse).ToList();
        return Task.FromResult(Ok(new InsightsGetPendingOperationsResponse { PendingOperations = ops }));
    }

    /// <summary>Schedules a performance-level change for the title.</summary>
    [HttpPost("/Insights/SetPerformance")]
    public Task<IHttpActionResult> Insights_SetPerformance(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<InsightsSetPerformanceRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        if (body.PerformanceLevel < 1 || body.PerformanceLevel > 4)
            return Task.FromResult(Err("InvalidParams", 1000, "PerformanceLevel must be 1..4"));

        var cfgColl = Database.GetCollection<PlayfabInsightsConfigDbObject>(true)!;
        var cfg = GetOrCreateInsightsConfig();
        cfg.PerformanceLevel = body.PerformanceLevel;
        cfgColl.Update(cfg);

        var opColl = Database.GetCollection<PlayfabInsightsOperationDbObject>(true)!;
        var op = new PlayfabInsightsOperationDbObject
        {
            OperationId    = NewId(),
            OperationType  = "SetPerformance",
            OperationValue = body.PerformanceLevel,
            Status         = "Running",
            Message        = $"Setting performance to level {body.PerformanceLevel}",
            StartedTime    = NowUnix(),
            LastUpdated    = NowUnix(),
        };
        opColl.Insert(op);
        return Task.FromResult(Ok(new InsightsOperationResponse
        {
            OperationId   = op.OperationId,
            OperationType = op.OperationType,
            Message       = op.Message,
        }));
    }

    /// <summary>Schedules a storage-retention change for the title.</summary>
    [HttpPost("/Insights/SetStorageRetention")]
    public Task<IHttpActionResult> Insights_SetStorageRetention(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<InsightsSetStorageRetentionRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        if (body.RetentionDays < 7 || body.RetentionDays > 90)
            return Task.FromResult(Err("InvalidParams", 1000, "RetentionDays must be 7..90"));

        var cfgColl = Database.GetCollection<PlayfabInsightsConfigDbObject>(true)!;
        var cfg = GetOrCreateInsightsConfig();
        cfg.RetentionDays = body.RetentionDays;
        cfgColl.Update(cfg);

        var opColl = Database.GetCollection<PlayfabInsightsOperationDbObject>(true)!;
        var op = new PlayfabInsightsOperationDbObject
        {
            OperationId    = NewId(),
            OperationType  = "SetStorageRetention",
            OperationValue = body.RetentionDays,
            Status         = "Running",
            Message        = $"Setting retention to {body.RetentionDays} days",
            StartedTime    = NowUnix(),
            LastUpdated    = NowUnix(),
        };
        opColl.Insert(op);
        return Task.FromResult(Ok(new InsightsOperationResponse
        {
            OperationId   = op.OperationId,
            OperationType = op.OperationType,
            Message       = op.Message,
        }));
    }
}
