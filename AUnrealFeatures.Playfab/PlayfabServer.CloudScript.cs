using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Playfab.Models;
using PlayFab.CloudScriptModels;
using System.Net;
using System.Text.Json;
using System.Diagnostics;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    private PlayfabCloudScriptFunctionDbObject? FindFunction(string name) =>
        Database.GetCollection<PlayfabCloudScriptFunctionDbObject>(true)!
                .FindOne(f => f.TitleId == TITLE_ID && f.FunctionName == name);

    /* ─── Execute legacy CloudScript revision ─────────────────────────────── */

    /// <summary>Executes a server-side CloudScript revision (Live/Latest/Specific) by running the
    /// uploaded JavaScript inside an embedded Jint engine. The script's `handlers.<FunctionName>`
    /// is invoked with (args, context); its return value becomes FunctionResult.</summary>
    [HttpPost("/CloudScript/ExecuteEntityCloudScript")]
    public Task<IHttpActionResult> CloudScript_ExecuteEntityCloudScript(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ExecuteEntityCloudScriptRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FunctionName))
            return Task.FromResult(Err("InvalidParams", 1000, "FunctionName is required"));

        var pid = PlayFabIdFromRequest(request);
        int? specificRev = body.RevisionSelection == CloudScriptRevisionOption.Specific ? body.SpecificRevision : null;
        bool useTest     = body.RevisionSelection == CloudScriptRevisionOption.Latest;

        var inv = InvokeCloudScript(body.FunctionName, body.FunctionParameter, pid, specificRev, useTest);

        // Persist execution log
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
            Logs                   = inv.Logs,
            LogsTooLarge           = JsonSerializer.Serialize(inv.Logs, JsonOpts).Length > 256_000,
            FunctionResult         = inv.FunctionResult,
            FunctionResultTooLarge = inv.FunctionResult != null && JsonSerializer.Serialize(inv.FunctionResult, JsonOpts).Length > 256_000,
            Error                  = inv.ExecutionError,
        }));
    }


    /* ─── ExecuteFunction (Azure Functions style) ─────────────────────────── */

    /// <summary>Executes a CloudScript Function. If the function is registered as HTTP-triggered,
    /// the HTTP URL is invoked. Otherwise the embedded JS engine attempts to call a same-named
    /// `handlers.<FunctionName>` from the uploaded source.</summary>
    [HttpPost("/CloudScript/ExecuteFunction")]
    public async Task<IHttpActionResult> CloudScript_ExecuteFunction(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Unauthorized();
        var body = ParseRequest<ExecuteFunctionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FunctionName))
            return Err("InvalidParams", 1000, "FunctionName is required");

        var pid = PlayFabIdFromRequest(request);
        var fn  = FindFunction(body.FunctionName);
        var sw  = Stopwatch.StartNew();

        object? funcResult = null;
        FunctionExecutionError? error = null;
        var logs = new List<LogStatement>();

        if (fn != null && fn.TriggerType == "HTTP" && !string.IsNullOrEmpty(fn.FunctionUrl))
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var payload = JsonSerializer.Serialize(new
                {
                    FunctionArgument = body.FunctionParameter,
                    CallerEntityProfile = (object?)null,
                    TitleAuthenticationContext = new { Id = TITLE_ID },
                }, JsonOpts);
                using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                using var resp = await http.PostAsync(fn.FunctionUrl, content);
                var text = await resp.Content.ReadAsStringAsync();
                try { funcResult = JsonSerializer.Deserialize<JsonElement>(text); }
                catch { funcResult = text; }
                if (!resp.IsSuccessStatusCode)
                    error = new FunctionExecutionError { Error = "HttpFunctionError", Message = $"Status {(int)resp.StatusCode}: {text}" };
            }
            catch (Exception e)
            {
                error = new FunctionExecutionError { Error = "HttpFunctionInvocationFailure", Message = e.Message, StackTrace = e.StackTrace ?? "" };
            }
        }
        else if (fn != null && fn.TriggerType != "HTTP")
        {
            // Queue/EventHub triggers can't be invoked synchronously
            error = new FunctionExecutionError { Error = "TriggerNotSupported", Message = $"Trigger '{fn.TriggerType}' cannot be invoked synchronously" };
        }
        else
        {
            // No registered HTTP function — fall back to inline JS handler from the uploaded source
            var inv = InvokeCloudScript(body.FunctionName, body.FunctionParameter, pid);
            if (inv.HandlerNotFound)
            {
                sw.Stop();
                return Err("CloudScriptFunctionNotFound", 1620, $"Function '{body.FunctionName}' not registered and no inline handler found", HttpStatusCode.NotFound);
            }
            funcResult = inv.FunctionResult;
            logs       = inv.Logs;
            error      = inv.FunctionError;
        }
        sw.Stop();

        Database.GetCollection<PlayfabCloudScriptExecutionLogDbObject>(true)!.Insert(new PlayfabCloudScriptExecutionLogDbObject
        {
            ExecutionId          = NewId(),
            PlayFabId            = pid,
            FunctionName         = body.FunctionName,
            ResultJson           = funcResult != null ? JsonSerializer.Serialize(funcResult, JsonOpts) : "",
            LogsJson             = JsonSerializer.Serialize(logs, JsonOpts),
            ExecutionTimeSeconds = (long)sw.Elapsed.TotalSeconds,
            Timestamp            = NowUnix(),
            Error                = error != null,
        });

        var resultJson = funcResult != null ? JsonSerializer.Serialize(funcResult, JsonOpts) : "";
        return Ok(new ExecuteFunctionResult
        {
            FunctionName              = body.FunctionName,
            ExecutionTimeMilliseconds = (int)sw.ElapsedMilliseconds,
            FunctionResult            = funcResult,
            FunctionResultSize        = resultJson.Length,
            FunctionResultTooLarge    = resultJson.Length > 256_000,
            Error                     = error,
        });
    }

    /* ─── Function registry ───────────────────────────────────────────────── */

    /// <summary>Returns details for a single registered function.</summary>
    [HttpPost("/CloudScript/GetFunction")]
    public Task<IHttpActionResult> CloudScript_GetFunction(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetFunctionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FunctionName))
            return Task.FromResult(Err("InvalidParams", 1000, "FunctionName is required"));

        var fn = FindFunction(body.FunctionName);
        if (fn == null) return Task.FromResult(Err("CloudScriptFunctionNotFound", 1620, "Function not registered", HttpStatusCode.NotFound));

        return Task.FromResult(Ok(new GetFunctionResult
        {
            ConnectionString = fn.ConnectionString,
            FunctionUrl      = fn.FunctionUrl,
            QueueName        = fn.QueueName,
            TriggerType      = fn.TriggerType,
        }));
    }

    /// <summary>Returns all registered functions across all trigger types.</summary>
    [HttpPost("/CloudScript/ListFunctions")]
    public Task<IHttpActionResult> CloudScript_ListFunctions(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var fns = Database.GetCollection<PlayfabCloudScriptFunctionDbObject>(true)!
                          .Find(x => x.TitleId == TITLE_ID)
                          .Select(f => new FunctionModel
                          {
                              FunctionName    = f.FunctionName,
                              FunctionAddress = f.TriggerType == "HTTP" ? f.FunctionUrl : f.QueueName,
                              TriggerType     = f.TriggerType,
                          }).ToList();
        return Task.FromResult(Ok(new ListFunctionsResult { Functions = fns }));
    }

    /// <summary>Returns all HTTP-triggered functions.</summary>
    [HttpPost("/CloudScript/ListHttpFunctions")]
    public Task<IHttpActionResult> CloudScript_ListHttpFunctions(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var fns = Database.GetCollection<PlayfabCloudScriptFunctionDbObject>(true)!
                          .Find(x => x.TitleId == TITLE_ID && x.TriggerType == "HTTP")
                          .Select(f => new HttpFunctionModel { FunctionName = f.FunctionName, FunctionUrl = f.FunctionUrl })
                          .ToList();
        return Task.FromResult(Ok(new ListHttpFunctionsResult { Functions = fns }));
    }

    /// <summary>Returns all queue-triggered functions.</summary>
    [HttpPost("/CloudScript/ListQueuedFunctions")]
    public Task<IHttpActionResult> CloudScript_ListQueuedFunctions(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var fns = Database.GetCollection<PlayfabCloudScriptFunctionDbObject>(true)!
                          .Find(x => x.TitleId == TITLE_ID && x.TriggerType == "Queue")
                          .Select(f => new QueuedFunctionModel
                          {
                              FunctionName     = f.FunctionName,
                              QueueName        = f.QueueName,
                              ConnectionString = f.ConnectionString,
                          }).ToList();
        return Task.FromResult(Ok(new ListQueuedFunctionsResult { Functions = fns }));
    }

    /// <summary>Returns all Event Hub-triggered functions.</summary>
    [HttpPost("/CloudScript/ListEventHubFunctions")]
    public Task<IHttpActionResult> CloudScript_ListEventHubFunctions(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var fns = Database.GetCollection<PlayfabCloudScriptFunctionDbObject>(true)!
                          .Find(x => x.TitleId == TITLE_ID && x.TriggerType == "EventHub")
                          .Select(f => new EventHubFunctionModel
                          {
                              FunctionName     = f.FunctionName,
                              EventHubName     = f.QueueName,
                              ConnectionString = f.ConnectionString,
                          }).ToList();
        return Task.FromResult(Ok(new ListEventHubFunctionsResult { Functions = fns }));
    }

    /// <summary>Registers an HTTP-triggered function.</summary>
    [HttpPost("/CloudScript/RegisterHttpFunction")]
    public Task<IHttpActionResult> CloudScript_RegisterHttpFunction(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RegisterHttpFunctionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FunctionName) || string.IsNullOrEmpty(body.FunctionUrl))
            return Task.FromResult(Err("InvalidParams", 1000, "FunctionName and FunctionUrl are required"));

        var coll = Database.GetCollection<PlayfabCloudScriptFunctionDbObject>(true)!;
        var existing = FindFunction(body.FunctionName);
        if (existing != null)
        {
            existing.TriggerType = "HTTP";
            existing.FunctionUrl = body.FunctionUrl;
            existing.QueueName = "";
            existing.ConnectionString = "";
            coll.Update(existing);
        }
        else
        {
            coll.Insert(new PlayfabCloudScriptFunctionDbObject
            {
                TitleId      = TITLE_ID,
                FunctionName = body.FunctionName,
                FunctionUrl  = body.FunctionUrl,
                TriggerType  = "HTTP",
            });
        }
        return Task.FromResult(Ok(new EmptyResult()));
    }

    /// <summary>Registers a queue-triggered function.</summary>
    [HttpPost("/CloudScript/RegisterQueuedFunction")]
    public Task<IHttpActionResult> CloudScript_RegisterQueuedFunction(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RegisterQueuedFunctionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FunctionName) || string.IsNullOrEmpty(body.QueueName))
            return Task.FromResult(Err("InvalidParams", 1000, "FunctionName and QueueName are required"));

        var coll = Database.GetCollection<PlayfabCloudScriptFunctionDbObject>(true)!;
        var existing = FindFunction(body.FunctionName);
        if (existing != null)
        {
            existing.TriggerType = "Queue";
            existing.QueueName = body.QueueName;
            existing.ConnectionString = body.ConnectionString ?? "";
            existing.FunctionUrl = "";
            coll.Update(existing);
        }
        else
        {
            coll.Insert(new PlayfabCloudScriptFunctionDbObject
            {
                TitleId          = TITLE_ID,
                FunctionName     = body.FunctionName,
                TriggerType      = "Queue",
                QueueName        = body.QueueName,
                ConnectionString = body.ConnectionString ?? "",
            });
        }
        return Task.FromResult(Ok(new EmptyResult()));
    }

    /// <summary>Registers an Event Hub-triggered function.</summary>
    [HttpPost("/CloudScript/RegisterEventHubFunction")]
    public Task<IHttpActionResult> CloudScript_RegisterEventHubFunction(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RegisterEventHubFunctionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FunctionName) || string.IsNullOrEmpty(body.EventHubName))
            return Task.FromResult(Err("InvalidParams", 1000, "FunctionName and EventHubName are required"));

        var coll = Database.GetCollection<PlayfabCloudScriptFunctionDbObject>(true)!;
        var existing = FindFunction(body.FunctionName);
        if (existing != null)
        {
            existing.TriggerType = "EventHub";
            existing.QueueName = body.EventHubName;
            existing.ConnectionString = body.ConnectionString ?? "";
            existing.FunctionUrl = "";
            coll.Update(existing);
        }
        else
        {
            coll.Insert(new PlayfabCloudScriptFunctionDbObject
            {
                TitleId          = TITLE_ID,
                FunctionName     = body.FunctionName,
                TriggerType      = "EventHub",
                QueueName        = body.EventHubName,
                ConnectionString = body.ConnectionString ?? "",
            });
        }
        return Task.FromResult(Ok(new EmptyResult()));
    }

    /// <summary>Removes a registered function (any trigger type).</summary>
    [HttpPost("/CloudScript/UnregisterFunction")]
    public Task<IHttpActionResult> CloudScript_UnregisterFunction(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UnregisterFunctionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.FunctionName))
            return Task.FromResult(Err("InvalidParams", 1000, "FunctionName is required"));

        var coll = Database.GetCollection<PlayfabCloudScriptFunctionDbObject>(true)!;
        var name = body.FunctionName;
        coll.DeleteMany(x => x.TitleId == TITLE_ID && x.FunctionName == name);
        return Task.FromResult(Ok(new EmptyResult()));
    }

    /* ─── PostFunctionResult (logging hooks) ──────────────────────────────── */

    private IHttpActionResult LogFunctionResult(IHttpRequest request, ExecuteFunctionResult? r, string source)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Unauthorized();
        Database.GetCollection<PlayfabCloudScriptExecutionLogDbObject>(true)!.Insert(new PlayfabCloudScriptExecutionLogDbObject
        {
            ExecutionId          = NewId(),
            PlayFabId            = PlayFabIdFromRequest(request),
            FunctionName         = r?.FunctionName ?? source,
            ResultJson           = r?.FunctionResult != null ? JsonSerializer.Serialize(r.FunctionResult, JsonOpts) : "",
            LogsJson             = source,
            ExecutionTimeSeconds = r?.ExecutionTimeMilliseconds / 1000 ?? 0,
            Timestamp            = NowUnix(),
            Error                = r?.Error != null,
        });
        return Ok(new EmptyResult());
    }

    /// <summary>Records an entity-triggered function execution.</summary>
    [HttpPost("/CloudScript/PostFunctionResultForEntityTriggeredAction")]
    public Task<IHttpActionResult> CloudScript_PostEntityTriggered(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<PostFunctionResultForEntityTriggeredActionRequest>(request);
        return Task.FromResult(LogFunctionResult(request, body?.FunctionResult, "EntityTriggered"));
    }

    /// <summary>Records a function execution.</summary>
    [HttpPost("/CloudScript/PostFunctionResultForFunctionExecution")]
    public Task<IHttpActionResult> CloudScript_PostFunctionExecution(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<PostFunctionResultForFunctionExecutionRequest>(request);
        return Task.FromResult(LogFunctionResult(request, body?.FunctionResult, "FunctionExecution"));
    }

    /// <summary>Records a player-triggered function execution.</summary>
    [HttpPost("/CloudScript/PostFunctionResultForPlayerTriggeredAction")]
    public Task<IHttpActionResult> CloudScript_PostPlayerTriggered(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<PostFunctionResultForPlayerTriggeredActionRequest>(request);
        return Task.FromResult(LogFunctionResult(request, body?.FunctionResult, "PlayerTriggered"));
    }

    /// <summary>Records a scheduled-task function execution.</summary>
    [HttpPost("/CloudScript/PostFunctionResultForScheduledTask")]
    public Task<IHttpActionResult> CloudScript_PostScheduledTask(IHttpRequest request, IHttpResponse response)
    {
        var body = ParseRequest<PostFunctionResultForScheduledTaskRequest>(request);
        return Task.FromResult(LogFunctionResult(request, body?.FunctionResult, "ScheduledTask"));
    }
}
