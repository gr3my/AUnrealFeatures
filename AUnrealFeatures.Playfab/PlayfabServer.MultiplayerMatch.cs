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

    private List<MatchmakingPlayer> ParseMembers(string json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<List<MatchmakingPlayer>>(json, JsonOpts) ?? new(); }
        catch { return new(); }
    }

    private List<MatchmakingPlayerWithTeamAssignment> ParseTeamMembers(string json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<List<MatchmakingPlayerWithTeamAssignment>>(json, JsonOpts) ?? new(); }
        catch { return new(); }
    }

    private GetMatchmakingTicketResult TicketToResult(PlayfabMatchmakingTicketDbObject t) => new()
    {
        TicketId           = t.TicketId,
        QueueName          = t.QueueName,
        Status             = t.Status,
        Created            = DateTimeOffset.FromUnixTimeSeconds(t.Created).UtcDateTime,
        Creator            = new EntityKey { Id = t.CreatorEntityId, Type = "title_player_account" },
        Members            = ParseMembers(t.AttributesJson),
        MembersToMatchWith = t.MembersEntityIds.Select(id => new EntityKey { Id = id, Type = "title_player_account" }).ToList(),
        GiveUpAfterSeconds = (int)t.GiveUpAfterSeconds,
        MatchId            = t.MatchId,
        ChangeNumber       = 1,
    };

    private GetServerBackfillTicketResult BackfillToResult(PlayfabMatchmakingTicketDbObject t) => new()
    {
        TicketId           = t.TicketId,
        QueueName          = t.QueueName,
        Status             = t.Status,
        Created            = DateTimeOffset.FromUnixTimeSeconds(t.Created).UtcDateTime,
        Members            = ParseTeamMembers(t.AttributesJson),
        GiveUpAfterSeconds = (int)t.GiveUpAfterSeconds,
        MatchId            = t.MatchId,
        ServerDetails      = null,
    };

    /* ─── Matchmaking Tickets (player) ────────────────────────────────────── */

    /// <summary>Creates a matchmaking ticket for the calling player.</summary>
    [HttpPost("/Match/CreateMatchmakingTicket")]
    public Task<IHttpActionResult> Multiplayer_CreateMatchmakingTicket(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateMatchmakingTicketRequest>(request);
        if (body == null || body.Creator == null || string.IsNullOrEmpty(body.QueueName))
            return Task.FromResult(Err("InvalidParams", 1000, "Creator and QueueName are required"));

        var ticket = new PlayfabMatchmakingTicketDbObject
        {
            TicketId           = NewId(),
            QueueName          = body.QueueName,
            CreatorEntityId    = body.Creator.Entity?.Id ?? "",
            Status             = "WaitingForMatch",
            GiveUpAfterSeconds = (uint)body.GiveUpAfterSeconds,
            AttributesJson     = JsonSerializer.Serialize(new List<MatchmakingPlayer> { body.Creator }, JsonOpts),
            MembersEntityIds   = (body.MembersToMatchWith ?? new()).Select(e => e.Id).Where(id => !string.IsNullOrEmpty(id)).ToList(),
            Created            = NowUnix(),
        };
        Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!.Insert(ticket);
        return Task.FromResult(Ok(new CreateMatchmakingTicketResult { TicketId = ticket.TicketId }));
    }

    /// <summary>Creates a multi-player matchmaking ticket initiated by a server.</summary>
    [HttpPost("/Match/CreateServerMatchmakingTicket")]
    public Task<IHttpActionResult> Multiplayer_CreateServerMatchmakingTicket(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateServerMatchmakingTicketRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.QueueName))
            return Task.FromResult(Err("InvalidParams", 1000, "QueueName is required"));

        var ticket = new PlayfabMatchmakingTicketDbObject
        {
            TicketId           = NewId(),
            QueueName          = body.QueueName,
            CreatorEntityId    = "server",
            Status             = "WaitingForMatch",
            GiveUpAfterSeconds = (uint)body.GiveUpAfterSeconds,
            AttributesJson     = JsonSerializer.Serialize(body.Members ?? new(), JsonOpts),
            MembersEntityIds   = (body.Members ?? new()).Select(m => m.Entity?.Id ?? "").Where(id => !string.IsNullOrEmpty(id)).ToList(),
            Created            = NowUnix(),
        };
        Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!.Insert(ticket);
        return Task.FromResult(Ok(new CreateMatchmakingTicketResult { TicketId = ticket.TicketId }));
    }

    /// <summary>Returns the current state of a matchmaking ticket.</summary>
    [HttpPost("/Match/GetMatchmakingTicket")]
    public Task<IHttpActionResult> Multiplayer_GetMatchmakingTicket(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetMatchmakingTicketRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.TicketId))
            return Task.FromResult(Err("InvalidParams", 1000, "TicketId is required"));
        var tid = body.TicketId;
        var t = Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!.FindOne(x => x.TicketId == tid);
        if (t == null) return Task.FromResult(Err("MatchmakingTicketNotFound", 1820, "Ticket not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(TicketToResult(t)));
    }

    /// <summary>Cancels a matchmaking ticket.</summary>
    [HttpPost("/Match/CancelMatchmakingTicket")]
    public Task<IHttpActionResult> Multiplayer_CancelMatchmakingTicket(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CancelMatchmakingTicketRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.TicketId))
            return Task.FromResult(Err("InvalidParams", 1000, "TicketId is required"));
        var tid = body.TicketId;
        var coll = Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!;
        var t = coll.FindOne(x => x.TicketId == tid);
        if (t != null) { t.Status = "Canceled"; coll.Update(t); }
        return Task.FromResult(Ok(new CancelMatchmakingTicketResult()));
    }

    /// <summary>Cancels all matchmaking tickets for the given player in the queue.</summary>
    [HttpPost("/Match/CancelAllMatchmakingTicketsForPlayer")]
    public Task<IHttpActionResult> Multiplayer_CancelAllMatchmakingTicketsForPlayer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CancelAllMatchmakingTicketsForPlayerRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId = body.Entity?.Id ?? PlayFabIdFromRequest(request);
        var queueName = body.QueueName;
        var coll = Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!;
        foreach (var t in coll.Find(x => x.CreatorEntityId == entityId && (string.IsNullOrEmpty(queueName) || x.QueueName == queueName)).ToList())
        { t.Status = "Canceled"; coll.Update(t); }
        return Task.FromResult(Ok(new CancelAllMatchmakingTicketsForPlayerResult()));
    }

    /// <summary>Adds a player to an existing matchmaking ticket.</summary>
    [HttpPost("/Match/JoinMatchmakingTicket")]
    public Task<IHttpActionResult> Multiplayer_JoinMatchmakingTicket(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<JoinMatchmakingTicketRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.TicketId) || body.Member == null)
            return Task.FromResult(Err("InvalidParams", 1000, "TicketId and Member are required"));
        var coll = Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!;
        var tid = body.TicketId;
        var t = coll.FindOne(x => x.TicketId == tid);
        if (t == null) return Task.FromResult(Err("MatchmakingTicketNotFound", 1820, "Ticket not found", HttpStatusCode.NotFound));
        var members = ParseMembers(t.AttributesJson);
        members.Add(body.Member);
        t.AttributesJson = JsonSerializer.Serialize(members, JsonOpts);
        var memberId = body.Member.Entity?.Id ?? "";
        if (!string.IsNullOrEmpty(memberId) && !t.MembersEntityIds.Contains(memberId)) t.MembersEntityIds.Add(memberId);
        coll.Update(t);
        return Task.FromResult(Ok(new JoinMatchmakingTicketResult()));
    }

    /// <summary>Returns all matchmaking ticket ids the player is currently in.</summary>
    [HttpPost("/Match/ListMatchmakingTicketsForPlayer")]
    public Task<IHttpActionResult> Multiplayer_ListMatchmakingTicketsForPlayer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ListMatchmakingTicketsForPlayerRequest>(request);
        var entityId = body?.Entity?.Id ?? PlayFabIdFromRequest(request);
        var queue = body?.QueueName;
        var ids = Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!
                          .Find(x => (x.CreatorEntityId == entityId || x.MembersEntityIds.Contains(entityId))
                                  && (string.IsNullOrEmpty(queue) || x.QueueName == queue))
                          .Select(x => x.TicketId).ToList();
        return Task.FromResult(Ok(new ListMatchmakingTicketsForPlayerResult { TicketIds = ids }));
    }

    /// <summary>Returns the details of a completed match.</summary>
    [HttpPost("/Match/GetMatch")]
    public Task<IHttpActionResult> Multiplayer_GetMatch(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetMatchRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.MatchId))
            return Task.FromResult(Err("InvalidParams", 1000, "MatchId is required"));
        var mid = body.MatchId;
        var match = Database.GetCollection<PlayfabMatchDbObject>(true)!.FindOne(x => x.MatchId == mid);
        if (match == null) return Task.FromResult(Err("MatchNotFound", 1821, "Match not found", HttpStatusCode.NotFound));

        ServerDetails? sd = null;
        try
        {
            if (!string.IsNullOrEmpty(match.ServerDetailsJson))
                sd = JsonSerializer.Deserialize<ServerDetails>(match.ServerDetailsJson, JsonOpts);
        }
        catch { }
        return Task.FromResult(Ok(new GetMatchResult
        {
            MatchId           = match.MatchId,
            ArrangementString = match.ArrangementString,
            Members           = match.MembersEntityIds.Select(id => new MatchmakingPlayerWithTeamAssignment
            {
                Entity = new EntityKey { Id = id, Type = "title_player_account" },
                TeamId = "",
            }).ToList(),
            ServerDetails    = sd,
            RegionPreferences = new List<string>(),
        }));
    }

    /* ─── Server backfill tickets ─────────────────────────────────────────── */

    /// <summary>Creates a server backfill ticket so an in-progress match can find new players.</summary>
    [HttpPost("/Match/CreateServerBackfillTicket")]
    public Task<IHttpActionResult> Multiplayer_CreateServerBackfillTicket(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateServerBackfillTicketRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.QueueName))
            return Task.FromResult(Err("InvalidParams", 1000, "QueueName is required"));

        var ticket = new PlayfabMatchmakingTicketDbObject
        {
            TicketId           = NewId(),
            QueueName          = body.QueueName,
            CreatorEntityId    = "server-backfill",
            Status             = "WaitingForMatch",
            GiveUpAfterSeconds = (uint)body.GiveUpAfterSeconds,
            AttributesJson     = JsonSerializer.Serialize(body.Members ?? new(), JsonOpts),
            MembersEntityIds   = (body.Members ?? new()).Select(m => m.Entity?.Id ?? "").Where(id => !string.IsNullOrEmpty(id)).ToList(),
            Created            = NowUnix(),
        };
        Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!.Insert(ticket);
        return Task.FromResult(Ok(new CreateMatchmakingTicketResult { TicketId = ticket.TicketId }));
    }

    /// <summary>Returns the state of a server backfill ticket.</summary>
    [HttpPost("/Match/GetServerBackfillTicket")]
    public Task<IHttpActionResult> Multiplayer_GetServerBackfillTicket(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetServerBackfillTicketRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.TicketId))
            return Task.FromResult(Err("InvalidParams", 1000, "TicketId is required"));
        var tid = body.TicketId;
        var t = Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!.FindOne(x => x.TicketId == tid);
        if (t == null) return Task.FromResult(Err("MatchmakingTicketNotFound", 1820, "Ticket not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(BackfillToResult(t)));
    }

    /// <summary>Cancels a server backfill ticket.</summary>
    [HttpPost("/Match/CancelServerBackfillTicket")]
    public Task<IHttpActionResult> Multiplayer_CancelServerBackfillTicket(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CancelServerBackfillTicketRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.TicketId))
            return Task.FromResult(Err("InvalidParams", 1000, "TicketId is required"));
        var tid = body.TicketId;
        var coll = Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!;
        var t = coll.FindOne(x => x.TicketId == tid);
        if (t != null) { t.Status = "Canceled"; coll.Update(t); }
        return Task.FromResult(Ok(new CancelServerBackfillTicketResult()));
    }

    /// <summary>Cancels all server backfill tickets for a given player.</summary>
    [HttpPost("/Match/CancelAllServerBackfillTicketsForPlayer")]
    public Task<IHttpActionResult> Multiplayer_CancelAllServerBackfillTicketsForPlayer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CancelAllServerBackfillTicketsForPlayerRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId = body.Entity?.Id ?? "";
        var queueName = body.QueueName;
        var coll = Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!;
        foreach (var t in coll.Find(x => x.MembersEntityIds.Contains(entityId)
                                      && (string.IsNullOrEmpty(queueName) || x.QueueName == queueName)).ToList())
        { t.Status = "Canceled"; coll.Update(t); }
        return Task.FromResult(Ok(new CancelAllServerBackfillTicketsForPlayerResult()));
    }

    /// <summary>Lists all server backfill tickets the player is part of.</summary>
    [HttpPost("/Match/ListServerBackfillTicketsForPlayer")]
    public Task<IHttpActionResult> Multiplayer_ListServerBackfillTicketsForPlayer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ListServerBackfillTicketsForPlayerRequest>(request);
        var entityId = body?.Entity?.Id ?? "";
        var queue = body?.QueueName;
        var ids = Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!
                          .Find(x => x.MembersEntityIds.Contains(entityId)
                                  && (string.IsNullOrEmpty(queue) || x.QueueName == queue))
                          .Select(x => x.TicketId).ToList();
        return Task.FromResult(Ok(new ListServerBackfillTicketsForPlayerResult { TicketIds = ids }));
    }

    /* ─── Matchmaking Queues ──────────────────────────────────────────────── */

    /// <summary>Creates or updates a matchmaking queue config.</summary>
    [HttpPost("/Match/SetMatchmakingQueue")]
    public Task<IHttpActionResult> Multiplayer_SetMatchmakingQueue(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SetMatchmakingQueueRequest>(request);
        if (body == null || body.MatchmakingQueue == null || string.IsNullOrEmpty(body.MatchmakingQueue.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "MatchmakingQueue.Name is required"));
        var coll = Database.GetCollection<PlayfabMatchmakingQueueDbObject>(true)!;
        var name = body.MatchmakingQueue.Name;
        var existing = coll.FindOne(x => x.QueueName == name);
        var json = JsonSerializer.Serialize(body.MatchmakingQueue, JsonOpts);
        if (existing == null)
        {
            coll.Insert(new PlayfabMatchmakingQueueDbObject
            {
                QueueName    = body.MatchmakingQueue.Name,
                MinMatchSize = body.MatchmakingQueue.MinMatchSize,
                MaxMatchSize = body.MatchmakingQueue.MaxMatchSize,
                ConfigJson   = json,
                Created      = NowUnix(),
            });
        }
        else
        {
            existing.MinMatchSize = body.MatchmakingQueue.MinMatchSize;
            existing.MaxMatchSize = body.MatchmakingQueue.MaxMatchSize;
            existing.ConfigJson   = json;
            coll.Update(existing);
        }
        return Task.FromResult(Ok(new SetMatchmakingQueueResult()));
    }

    /// <summary>Returns a single matchmaking queue config.</summary>
    [HttpPost("/Match/GetMatchmakingQueue")]
    public Task<IHttpActionResult> Multiplayer_GetMatchmakingQueue(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetMatchmakingQueueRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.QueueName))
            return Task.FromResult(Err("InvalidParams", 1000, "QueueName is required"));
        var name = body.QueueName;
        var row = Database.GetCollection<PlayfabMatchmakingQueueDbObject>(true)!.FindOne(x => x.QueueName == name);
        if (row == null) return Task.FromResult(Err("MatchmakingQueueNotFound", 1822, "Queue not found", HttpStatusCode.NotFound));
        MatchmakingQueueConfig? cfg = null;
        try { cfg = JsonSerializer.Deserialize<MatchmakingQueueConfig>(row.ConfigJson, JsonOpts); }
        catch { }
        return Task.FromResult(Ok(new GetMatchmakingQueueResult { MatchmakingQueue = cfg ?? new MatchmakingQueueConfig { Name = row.QueueName } }));
    }

    /// <summary>Returns all matchmaking queue configs.</summary>
    [HttpPost("/Match/ListMatchmakingQueues")]
    public Task<IHttpActionResult> Multiplayer_ListMatchmakingQueues(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabMatchmakingQueueDbObject>(true)!.FindAll().ToList();
        var queues = rows.Select(r =>
        {
            try { return JsonSerializer.Deserialize<MatchmakingQueueConfig>(r.ConfigJson, JsonOpts) ?? new MatchmakingQueueConfig { Name = r.QueueName }; }
            catch { return new MatchmakingQueueConfig { Name = r.QueueName }; }
        }).ToList();
        return Task.FromResult(Ok(new ListMatchmakingQueuesResult { MatchMakingQueues = queues }));
    }

    /// <summary>Removes a matchmaking queue.</summary>
    [HttpPost("/Match/RemoveMatchmakingQueue")]
    public Task<IHttpActionResult> Multiplayer_RemoveMatchmakingQueue(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RemoveMatchmakingQueueRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.QueueName))
            return Task.FromResult(Err("InvalidParams", 1000, "QueueName is required"));
        var name = body.QueueName;
        Database.GetCollection<PlayfabMatchmakingQueueDbObject>(true)!.DeleteMany(x => x.QueueName == name);
        return Task.FromResult(Ok(new RemoveMatchmakingQueueResult()));
    }

    /// <summary>Returns aggregate statistics for a matchmaking queue.</summary>
    [HttpPost("/Match/GetQueueStatistics")]
    public Task<IHttpActionResult> Multiplayer_GetQueueStatistics(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetQueueStatisticsRequest>(request);
        var queue = body?.QueueName;
        var coll = Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!;
        var waiting = coll.Find(x => x.Status == "WaitingForMatch" && (string.IsNullOrEmpty(queue) || x.QueueName == queue)).ToList();
        return Task.FromResult(Ok(new GetQueueStatisticsResult
        {
            NumberOfPlayersMatching       = (uint)waiting.SelectMany(x => x.MembersEntityIds).Distinct().Count(),
            TimeToMatchStatisticsInSeconds = new Statistics
            {
                Average      = 30,
                Percentile50 = 25,
                Percentile90 = 60,
                Percentile99 = 120,
            }
        }));
    }

    /// <summary>Subscribes the caller to match events (no-op without PubSub backend).</summary>
    [HttpPost("/Match/SubscribeToMatchmakingResource")]
    public Task<IHttpActionResult> Multiplayer_SubscribeToMatchmakingResource(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SubscribeToMatchResourceRequest>(request);
        return Task.FromResult(Ok(new SubscribeToMatchResourceResult { Topic = body != null ? $"match/{body.Type}/{body.ResourceId}" : "" }));
    }

    /// <summary>Unsubscribes the caller from match events.</summary>
    [HttpPost("/Match/UnsubscribeFromMatchmakingResource")]
    public Task<IHttpActionResult> Multiplayer_UnsubscribeFromMatchmakingResource(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        ParseRequest<UnsubscribeFromMatchResourceRequest>(request);
        return Task.FromResult(Ok(new UnsubscribeFromMatchResourceResult()));
    }
}
