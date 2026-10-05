using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Database;
using AUnrealFeatures.Playfab.Models;
using PlayFab.ExperimentationModels;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed class PlayfabExclusionGroupDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string ExclusionGroupId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public long Created { get; set; }
}

public sealed partial class PlayfabServer
{
    /* ─── Helpers ─────────────────────────────────────────────────────────── */

    private static Experiment ExperimentToDto(PlayfabExperimentDbObject e)
    {
        var variants = new List<Variant>();
        try
        {
            variants = e.Variants.Select(v => JsonSerializer.Deserialize<Variant>(v, JsonOpts)!)
                                 .Where(v => v != null).ToList();
        }
        catch { variants = new(); }
        return new Experiment
        {
            Id                              = e.ExperimentId,
            Name                            = e.Name,
            Description                     = e.Description,
            SegmentId                       = e.SegmentId,
            ExclusionGroupId                = e.Exclusion,
            ExperimentType                  = Enum.TryParse<ExperimentType>(e.ExperimentType, out var t) ? t : ExperimentType.Active,
            State                           = Enum.TryParse<ExperimentState>(e.State, out var s) ? s : ExperimentState.New,
            StartDate                       = e.StartDate.HasValue
                                                ? DateTimeOffset.FromUnixTimeSeconds(e.StartDate.Value).UtcDateTime
                                                : DateTime.UtcNow,
            EndDate                         = e.Duration > 0 && e.StartDate.HasValue
                                                ? DateTimeOffset.FromUnixTimeSeconds(e.StartDate.Value + e.Duration).UtcDateTime
                                                : (DateTime?)null,
            Variants                        = variants,
            TitlePlayerAccountTestIds       = new List<string>(),
        };
    }

    private static ExperimentExclusionGroup ExclusionGroupToDto(PlayfabExclusionGroupDbObject g) => new()
    {
        ExclusionGroupId = g.ExclusionGroupId,
        Name             = g.Name,
        Description      = g.Description,
    };

    private static uint StableHash(string s)
    {
        // FNV-1a 32-bit so the same entity always lands in the same variant deterministically
        uint hash = 2166136261U;
        foreach (var c in s) { hash ^= c; hash *= 16777619U; }
        return hash;
    }

    /* ─── Experiments ─────────────────────────────────────────────────────── */

    /// <summary>Creates a new experiment in the New state.</summary>
    [HttpPost("/Experimentation/CreateExperiment")]
    public Task<IHttpActionResult> Experimentation_CreateExperiment(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateExperimentRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var coll = Database.GetCollection<PlayfabExperimentDbObject>(true)!;
        var name = body.Name;
        if (coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name) != null)
            return Task.FromResult(Err("ExperimentAlreadyExists", 1542, "Experiment with that name already exists"));

        var exp = new PlayfabExperimentDbObject
        {
            TitleId        = TITLE_ID,
            ExperimentId   = NewId(),
            Name           = body.Name,
            Description    = body.Description ?? "",
            SegmentId      = body.SegmentId ?? "",
            Exclusion      = body.ExclusionGroupId ?? "",
            ExperimentType = (body.ExperimentType ?? ExperimentType.Active).ToString(),
            State          = "New",
            StartDate      = new DateTimeOffset(body.StartDate, TimeSpan.Zero).ToUnixTimeSeconds(),
            Duration       = body.EndDate.HasValue
                                ? (uint)Math.Max(0, (new DateTimeOffset(body.EndDate.Value, TimeSpan.Zero).ToUnixTimeSeconds()
                                                     - new DateTimeOffset(body.StartDate, TimeSpan.Zero).ToUnixTimeSeconds()))
                                : 0,
            Variants       = (body.Variants ?? new()).Select(v => JsonSerializer.Serialize(v, JsonOpts)).ToList(),
        };
        coll.Insert(exp);
        return Task.FromResult(Ok(new CreateExperimentResult { ExperimentId = exp.ExperimentId }));
    }

    /// <summary>Deletes an experiment.</summary>
    [HttpPost("/Experimentation/DeleteExperiment")]
    public Task<IHttpActionResult> Experimentation_DeleteExperiment(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteExperimentRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ExperimentId))
            return Task.FromResult(Err("InvalidParams", 1000, "ExperimentId is required"));

        var coll = Database.GetCollection<PlayfabExperimentDbObject>(true)!;
        var eid = body.ExperimentId;
        var e = coll.FindOne(x => x.TitleId == TITLE_ID && x.ExperimentId == eid);
        if (e == null) return Task.FromResult(Err("ExperimentNotFound", 1543, "Experiment not found", HttpStatusCode.NotFound));
        coll.Delete(e.Id);
        // Clean up treatments
        Database.GetCollection<PlayfabExperimentTreatmentDbObject>(true)!
                .DeleteMany(t => t.ExperimentId == eid);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Updates an existing experiment.</summary>
    [HttpPost("/Experimentation/UpdateExperiment")]
    public Task<IHttpActionResult> Experimentation_UpdateExperiment(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateExperimentRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Id is required"));

        var coll = Database.GetCollection<PlayfabExperimentDbObject>(true)!;
        var eid = body.Id;
        var e = coll.FindOne(x => x.TitleId == TITLE_ID && x.ExperimentId == eid);
        if (e == null) return Task.FromResult(Err("ExperimentNotFound", 1543, "Experiment not found", HttpStatusCode.NotFound));

        if (!string.IsNullOrEmpty(body.Name))             e.Name = body.Name;
        if (body.Description != null)                     e.Description = body.Description;
        if (body.SegmentId != null)                       e.SegmentId = body.SegmentId;
        if (body.ExclusionGroupId != null)                e.Exclusion = body.ExclusionGroupId;
        if (body.ExperimentType.HasValue)                 e.ExperimentType = body.ExperimentType.Value.ToString();
        e.StartDate = new DateTimeOffset(body.StartDate, TimeSpan.Zero).ToUnixTimeSeconds();
        e.Duration = body.EndDate.HasValue
            ? (uint)Math.Max(0, (new DateTimeOffset(body.EndDate.Value, TimeSpan.Zero).ToUnixTimeSeconds() - e.StartDate.Value))
            : 0;
        if (body.Variants != null)
            e.Variants = body.Variants.Select(v => JsonSerializer.Serialize(v, JsonOpts)).ToList();
        coll.Update(e);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Returns all experiments for the title.</summary>
    [HttpPost("/Experimentation/GetExperiments")]
    public Task<IHttpActionResult> Experimentation_GetExperiments(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var coll = Database.GetCollection<PlayfabExperimentDbObject>(true)!;
        var experiments = coll.Find(x => x.TitleId == TITLE_ID).Select(ExperimentToDto).ToList();
        return Task.FromResult(Ok(new GetExperimentsResult { Experiments = experiments }));
    }

    /// <summary>Marks an experiment as Started.</summary>
    [HttpPost("/Experimentation/StartExperiment")]
    public Task<IHttpActionResult> Experimentation_StartExperiment(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<StartExperimentRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ExperimentId))
            return Task.FromResult(Err("InvalidParams", 1000, "ExperimentId is required"));

        var coll = Database.GetCollection<PlayfabExperimentDbObject>(true)!;
        var eid = body.ExperimentId;
        var e = coll.FindOne(x => x.TitleId == TITLE_ID && x.ExperimentId == eid);
        if (e == null) return Task.FromResult(Err("ExperimentNotFound", 1543, "Experiment not found", HttpStatusCode.NotFound));
        e.State = "Started";
        if (!e.StartDate.HasValue) e.StartDate = NowUnix();
        coll.Update(e);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Marks an experiment as Stopped.</summary>
    [HttpPost("/Experimentation/StopExperiment")]
    public Task<IHttpActionResult> Experimentation_StopExperiment(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<StopExperimentRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ExperimentId))
            return Task.FromResult(Err("InvalidParams", 1000, "ExperimentId is required"));

        var coll = Database.GetCollection<PlayfabExperimentDbObject>(true)!;
        var eid = body.ExperimentId;
        var e = coll.FindOne(x => x.TitleId == TITLE_ID && x.ExperimentId == eid);
        if (e == null) return Task.FromResult(Err("ExperimentNotFound", 1543, "Experiment not found", HttpStatusCode.NotFound));
        e.State = "Stopped";
        coll.Update(e);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Returns the variant assignment for an entity, deterministic by entity id + experiment id.</summary>
    [HttpPost("/Experimentation/GetTreatmentAssignment")]
    public Task<IHttpActionResult> Experimentation_GetTreatmentAssignment(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetTreatmentAssignmentRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId = body.Entity?.Id ?? "";
        if (string.IsNullOrEmpty(entityId)) entityId = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(entityId)) return Task.FromResult(Unauthorized());

        var expColl = Database.GetCollection<PlayfabExperimentDbObject>(true)!;
        var trColl  = Database.GetCollection<PlayfabExperimentTreatmentDbObject>(true)!;
        var running = expColl.Find(x => x.TitleId == TITLE_ID && x.State == "Started").ToList();

        var pickedVariants = new List<string>();
        var pickedVariables = new List<Variable>();
        foreach (var exp in running)
        {
            // Get or create deterministic assignment
            var trEid = entityId;
            var trEid2 = exp.ExperimentId;
            var t = trColl.FindOne(x => x.EntityId == trEid && x.ExperimentId == trEid2);
            if (t == null)
            {
                // Pick a variant by stable hash modulo total traffic
                var variants = exp.Variants.Select(v => { try { return JsonSerializer.Deserialize<Variant>(v, JsonOpts); } catch { return null; } })
                                           .Where(v => v != null).Cast<Variant>().ToList();
                if (variants.Count == 0) continue;
                var totalTraffic = variants.Sum(v => (long)v.TrafficPercentage);
                if (totalTraffic <= 0) totalTraffic = 100;
                var bucket = StableHash(entityId + ":" + exp.ExperimentId) % (uint)totalTraffic;
                long acc = 0;
                Variant? chosen = variants[0];
                foreach (var v in variants)
                {
                    acc += v.TrafficPercentage;
                    if (bucket < acc) { chosen = v; break; }
                }
                t = new PlayfabExperimentTreatmentDbObject
                {
                    EntityId     = entityId,
                    ExperimentId = exp.ExperimentId,
                    VariantId    = chosen?.Id ?? "",
                    VariantName  = chosen?.Name ?? "",
                };
                trColl.Insert(t);
            }
            pickedVariants.Add(t.VariantName);
            // Variables for the chosen variant
            try
            {
                var varDoc = exp.Variants.Select(v => { try { return JsonSerializer.Deserialize<Variant>(v, JsonOpts); } catch { return null; } })
                                         .FirstOrDefault(v => v != null && v.Name == t.VariantName);
                if (varDoc?.Variables != null) pickedVariables.AddRange(varDoc.Variables);
            }
            catch { }
        }
        return Task.FromResult(Ok(new GetTreatmentAssignmentResult
        {
            TreatmentAssignment = new TreatmentAssignment
            {
                Variants  = pickedVariants,
                Variables = pickedVariables,
            }
        }));
    }

    /// <summary>Returns a deterministic scorecard for an experiment.</summary>
    [HttpPost("/Experimentation/GetLatestScorecard")]
    public Task<IHttpActionResult> Experimentation_GetLatestScorecard(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetLatestScorecardRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ExperimentId))
            return Task.FromResult(Err("InvalidParams", 1000, "ExperimentId is required"));

        var eid = body.ExperimentId;
        var exp = Database.GetCollection<PlayfabExperimentDbObject>(true)!
                          .FindOne(x => x.TitleId == TITLE_ID && x.ExperimentId == eid);
        if (exp == null) return Task.FromResult(Err("ExperimentNotFound", 1543, "Experiment not found", HttpStatusCode.NotFound));

        var trColl = Database.GetCollection<PlayfabExperimentTreatmentDbObject>(true)!;
        var assigned = trColl.Find(t => t.ExperimentId == eid).ToList();
        var variants = exp.Variants.Select(v => { try { return JsonSerializer.Deserialize<Variant>(v, JsonOpts); } catch { return null; } })
                                   .Where(v => v != null).Cast<Variant>().ToList();
        var rows = variants.Select(v => new ScorecardDataRow
        {
            VariantName    = v.Name,
            IsControl      = v.IsControl,
            PlayerCount    = (uint)assigned.Count(a => a.VariantName == v.Name),
            MetricDataRows = new Dictionary<string, MetricData>(),
        }).ToList();
        return Task.FromResult(Ok(new GetLatestScorecardResult
        {
            Scorecard = new Scorecard
            {
                ExperimentId      = exp.ExperimentId,
                ExperimentName    = exp.Name,
                DateGenerated     = DateTime.UtcNow.ToString("o"),
                Duration          = exp.Duration.ToString(),
                EventsProcessed   = 0,
                LatestJobStatus   = AnalysisTaskState.Completed,
                ScorecardDataRows = rows,
            }
        }));
    }

    /* ─── Exclusion groups ────────────────────────────────────────────────── */

    /// <summary>Creates a new exclusion group.</summary>
    [HttpPost("/Experimentation/CreateExclusionGroup")]
    public Task<IHttpActionResult> Experimentation_CreateExclusionGroup(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateExclusionGroupRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var coll = Database.GetCollection<PlayfabExclusionGroupDbObject>(true)!;
        var g = new PlayfabExclusionGroupDbObject
        {
            TitleId          = TITLE_ID,
            ExclusionGroupId = NewId(),
            Name             = body.Name,
            Description      = body.Description ?? "",
            Created          = NowUnix(),
        };
        coll.Insert(g);
        return Task.FromResult(Ok(new CreateExclusionGroupResult { ExclusionGroupId = g.ExclusionGroupId }));
    }

    /// <summary>Deletes an exclusion group.</summary>
    [HttpPost("/Experimentation/DeleteExclusionGroup")]
    public Task<IHttpActionResult> Experimentation_DeleteExclusionGroup(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteExclusionGroupRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ExclusionGroupId))
            return Task.FromResult(Err("InvalidParams", 1000, "ExclusionGroupId is required"));

        var coll = Database.GetCollection<PlayfabExclusionGroupDbObject>(true)!;
        var gid = body.ExclusionGroupId;
        var g = coll.FindOne(x => x.TitleId == TITLE_ID && x.ExclusionGroupId == gid);
        if (g == null) return Task.FromResult(Err("ExclusionGroupNotFound", 1545, "Exclusion group not found", HttpStatusCode.NotFound));
        coll.Delete(g.Id);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Updates an exclusion group.</summary>
    [HttpPost("/Experimentation/UpdateExclusionGroup")]
    public Task<IHttpActionResult> Experimentation_UpdateExclusionGroup(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateExclusionGroupRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ExclusionGroupId))
            return Task.FromResult(Err("InvalidParams", 1000, "ExclusionGroupId is required"));

        var coll = Database.GetCollection<PlayfabExclusionGroupDbObject>(true)!;
        var gid = body.ExclusionGroupId;
        var g = coll.FindOne(x => x.TitleId == TITLE_ID && x.ExclusionGroupId == gid);
        if (g == null) return Task.FromResult(Err("ExclusionGroupNotFound", 1545, "Exclusion group not found", HttpStatusCode.NotFound));
        if (!string.IsNullOrEmpty(body.Name)) g.Name = body.Name;
        if (body.Description != null) g.Description = body.Description;
        coll.Update(g);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Returns all exclusion groups.</summary>
    [HttpPost("/Experimentation/GetExclusionGroups")]
    public Task<IHttpActionResult> Experimentation_GetExclusionGroups(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var groups = Database.GetCollection<PlayfabExclusionGroupDbObject>(true)!
                             .Find(x => x.TitleId == TITLE_ID).Select(ExclusionGroupToDto).ToList();
        return Task.FromResult(Ok(new GetExclusionGroupsResult { ExclusionGroups = groups }));
    }

    /// <summary>Returns the experiments competing in an exclusion group.</summary>
    [HttpPost("/Experimentation/GetExclusionGroupTraffic")]
    public Task<IHttpActionResult> Experimentation_GetExclusionGroupTraffic(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetExclusionGroupTrafficRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ExclusionGroupId))
            return Task.FromResult(Err("InvalidParams", 1000, "ExclusionGroupId is required"));

        var gid = body.ExclusionGroupId;
        var experiments = Database.GetCollection<PlayfabExperimentDbObject>(true)!
                                  .Find(x => x.TitleId == TITLE_ID && x.Exclusion == gid).ToList();
        var allocations = experiments.Select(e => new ExclusionGroupTrafficAllocation
        {
            ExperimentId     = e.ExperimentId,
            TrafficAllocation = 0,
        }).ToList();
        return Task.FromResult(Ok(new GetExclusionGroupTrafficResult { TrafficAllocations = allocations }));
    }
}
