using AUnrealFeatures.Playfab.Models;
using Jint;
using Jint.Native;
using Jint.Native.Array;
using Jint.Native.Object;
using Jint.Runtime;
using PlayFab.CloudScriptModels;
using System.Diagnostics;
using System.Text.Json;
using JintJsonParser     = Jint.Native.Json.JsonParser;
using JintJsonSerializer = Jint.Native.Json.JsonSerializer;
using StjJsonSerializer  = System.Text.Json.JsonSerializer;

namespace AUnrealFeatures.Playfab;

/// <summary>
/// Result of executing a CloudScript handler. Mirrors the shape PlayFab's ExecuteCloudScript /
/// ExecuteFunction responses expect, plus we keep some extra fields for our own use.
/// </summary>
public sealed class CloudScriptInvocation
{
    public string FunctionName = "";
    public int Revision;
    public object FunctionResult;
    public List<LogStatement> Logs = new();
    public ScriptExecutionError ExecutionError;     // legacy ExecuteCloudScript error
    public FunctionExecutionError FunctionError;    // ExecuteFunction error
    public double ExecutionTimeSeconds;
    public uint MemoryConsumedBytes;
    public int APIRequestsIssued;
    public int HttpRequestsIssued;
    public bool HandlerNotFound;
    public bool? FunctionResultTooLarge;
    public bool? LogsTooLarge;
}

public sealed partial class PlayfabServer
{
    /* ─── CloudScript runtime ────────────────────────────────────────────── */

    /// <summary>Loads every published JS file for the title and concatenates them so each file's
    /// `handlers.X = function(...)` registrations are reachable in a single engine.</summary>
    private string LoadCloudScriptSource(int? specificRevision = null, bool useTestRevision = false)
    {
        var fileColl = Database.GetCollection<PlayfabCloudScriptFileDbObject>(true)!;
        var files    = fileColl.Find(f => f.TitleId == TITLE_ID).ToList();
        if (files.Count == 0) return "";

        IEnumerable<PlayfabCloudScriptFileDbObject> chosen;
        if (specificRevision.HasValue)
            chosen = files.Where(f => f.Version == (uint)specificRevision.Value);
        else if (useTestRevision)
            chosen = files.OrderByDescending(f => f.Version).Take(1)
                          .SelectMany(f => files.Where(x => x.Version == f.Version));
        else
        {
            // Latest published revision
            var top = files.Where(f => f.Published).Select(f => (int?)f.Version).Max();
            if (top == null) return "";
            chosen = files.Where(f => f.Version == top.Value);
        }

        var sb = new System.Text.StringBuilder();
        foreach (var f in chosen.OrderBy(f => f.FileName))
        {
            sb.AppendLine($"// ── {f.FileName} (rev {f.Version})");
            sb.AppendLine(f.FileContents);
        }
        return sb.ToString();
    }

    /// <summary>Resolved revision number used during the last LoadCloudScriptSource call (for echoing
    /// in ExecuteCloudScriptResult.Revision).</summary>
    private int CurrentLiveRevision()
    {
        var coll = Database.GetCollection<PlayfabCloudScriptFileDbObject>(true)!;
        var live = coll.Find(f => f.TitleId == TITLE_ID && f.Published)
                       .OrderByDescending(f => f.Version)
                       .FirstOrDefault();
        return live != null ? (int)live.Version : 1;
    }

    /// <summary>Build a Jint engine pre-loaded with the title's CloudScript and the standard PlayFab
    /// CloudScript globals: `handlers`, `log`, `server`, `entity`, `currentPlayerId`, `script`.</summary>
    private Engine BuildEngine(string sourceCode, string playFabId, string functionName, List<LogStatement> logs)
    {
        var engine = new Engine(opts =>
        {
            opts.LimitRecursion(64);
            opts.MaxStatements(100_000);
            opts.TimeoutInterval(TimeSpan.FromSeconds(10));
            opts.Strict(false);
        });

        // Pre-create the handlers object so user scripts can do `handlers.X = function(){...}`
        engine.Execute("var handlers = handlers || {};");

        // log.info / log.debug / log.error capture into the .NET-side list. The same shim is
        // exposed as both `log` (the PlayFab-canonical name) and `Logger` (a common alias used
        // in Photon-derived scripts) so either spelling works.
        var logObj = new LogShim(logs);
        engine.SetValue("log",    logObj);
        engine.SetValue("Logger", logObj);

        // server / entity / http surfaces
        var ctx = new CloudScriptContext(this, playFabId);
        engine.SetValue("server", new ServerShim(ctx));
        engine.SetValue("entity", new EntityShim(ctx));
        engine.SetValue("http",   new HttpShim(logs));

        // currentPlayerId
        engine.SetValue("currentPlayerId", playFabId ?? "");

        // script context object
        engine.Execute("var script = { revision: 1, titleId: '" + TITLE_ID + "', functionName: '" + (functionName ?? "") + "' };");

        if (!string.IsNullOrEmpty(sourceCode))
        {
            try { engine.Execute(sourceCode); }
            catch (Exception ex)
            {
                logs.Add(new LogStatement { Level = "Error", Message = $"Script load error: {ex.Message}" });
                throw;
            }
        }
        return engine;
    }

    /// <summary>Invoke a registered CloudScript handler by name. Returns the value the handler
    /// returned (or null) along with any execution / handler-not-found state in the result.</summary>
    private CloudScriptInvocation InvokeCloudScript(string functionName, object functionParameter, string playFabId,
                                                    int? specificRevision = null, bool useTestRevision = false)
    {
        var result = new CloudScriptInvocation { FunctionName = functionName };
        var src = LoadCloudScriptSource(specificRevision, useTestRevision);
        result.Revision = specificRevision ?? CurrentLiveRevision();

        var sw = Stopwatch.StartNew();
        try
        {
            var engine = BuildEngine(src, playFabId, functionName, result.Logs);

            // Resolve handlers.<functionName>
            JsValue handlers;
            try { handlers = engine.GetValue("handlers"); }
            catch { handlers = JsValue.Undefined; }
            if (handlers.IsUndefined() || handlers.IsNull())
            {
                result.HandlerNotFound = true;
                return result;
            }

            var handlerObj = handlers.AsObject();
            var fn = handlerObj.Get(functionName);
            if (fn.IsUndefined() || fn.IsNull() || !fn.IsObject())
            {
                result.HandlerNotFound = true;
                return result;
            }

            // Convert the .NET argument into a Jint JS value
            var jintArg = ConvertToJsValue(engine, functionParameter);

            // Build a `context` shell with non-null sub-objects so legacy scripts that do
            // `context.playerProfile.PlayerId` or `context.playStreamEvent["DeviceInfo"]`
            // don't immediately TypeError. PlayStream-triggered handlers will only get
            // meaningful values once we wire a PlayStream pipeline; for direct invocations
            // through ExecuteCloudScript or the webhook router these are empty placeholders.
            var contextJson = StjJsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["playerProfile"] = new Dictionary<string, object>
                {
                    ["PlayerId"]    = playFabId ?? "",
                    ["TitleId"]     = TITLE_ID,
                    ["DisplayName"] = "",
                },
                ["titleAuthenticationContext"] = new Dictionary<string, object>
                {
                    ["Id"]            = TITLE_ID,
                    ["EntityToken"]   = "",
                },
                ["titleEntityToken"] = "",
                ["playStreamEvent"]  = new Dictionary<string, object>(),
            }, JsonOpts);
            var ctxArg = new JintJsonParser(engine).Parse(contextJson);

            var callable = fn.AsFunctionInstance();
            var ret = engine.Invoke(callable, jintArg, ctxArg);
            result.FunctionResult = JsToClr(ret);
        }
        catch (JavaScriptException jex)
        {
            result.ExecutionError = new ScriptExecutionError
            {
                Error      = "JavaScriptException",
                Message    = jex.Message,
                StackTrace = jex.StackTrace ?? "",
            };
            result.FunctionError = new FunctionExecutionError
            {
                Error      = "JavaScriptException",
                Message    = jex.Message,
                StackTrace = jex.StackTrace ?? "",
            };
            result.Logs.Add(new LogStatement { Level = "Error", Message = jex.Message });
        }
        catch (TimeoutException)
        {
            result.ExecutionError = new ScriptExecutionError { Error = "Timeout", Message = "Script execution exceeded the time limit" };
            result.FunctionError  = new FunctionExecutionError { Error = "Timeout", Message = "Script execution exceeded the time limit" };
        }
        catch (Exception ex)
        {
            result.ExecutionError = new ScriptExecutionError { Error = ex.GetType().Name, Message = ex.Message, StackTrace = ex.StackTrace ?? "" };
            result.FunctionError  = new FunctionExecutionError { Error = ex.GetType().Name, Message = ex.Message, StackTrace = ex.StackTrace ?? "" };
        }
        finally
        {
            sw.Stop();
            result.ExecutionTimeSeconds = sw.Elapsed.TotalSeconds;
        }
        return result;
    }

    /// <summary>Convert a CLR object (typically a JsonElement / Dictionary / list) into a Jint JsValue.</summary>
    private JsValue ConvertToJsValue(Engine engine, object value)
    {
        if (value == null) return JsValue.Null;
        if (value is JsonElement je)
        {
            // Round-trip via the engine's JSON parser to preserve nested structure faithfully
            var json = StjJsonSerializer.Serialize(je, JsonOpts);
            return new JintJsonParser(engine).Parse(json);
        }
        // Dictionaries / lists: serialize-then-parse for safety
        try
        {
            var json = StjJsonSerializer.Serialize(value, JsonOpts);
            return new JintJsonParser(engine).Parse(json);
        }
        catch { return JsValue.FromObject(engine, value); }
    }

    /// <summary>Convert a Jint return value into a plain CLR object so it can be JSON-serialized.</summary>
    private object JsToClr(JsValue v)
    {
        if (v.IsUndefined() || v.IsNull()) return null;
        if (v.IsString())  return v.AsString();
        if (v.IsBoolean()) return v.AsBoolean();
        if (v.IsNumber())
        {
            var n = v.AsNumber();
            if (n == Math.Floor(n) && !double.IsInfinity(n)) return (long)n;
            return n;
        }
        if (v.IsArray())
        {
            var arr = v.AsArray();
            var list = new List<object>();
            for (uint i = 0; i < arr.Length; i++) list.Add(JsToClr(arr.Get(i)));
            return list;
        }
        if (v.IsObject())
        {
            // Use Jint's own JSON serializer to flatten the object, then parse it back
            try
            {
                var json = new JintJsonSerializer(new Engine()).Serialize(v).AsString();
                return StjJsonSerializer.Deserialize<JsonElement>(json);
            }
            catch
            {
                var obj = v.AsObject();
                var dict = new Dictionary<string, object>();
                foreach (var prop in obj.GetOwnProperties()) dict[prop.Key.ToString()] = JsToClr(obj.Get(prop.Key));
                return dict;
            }
        }
        return v.ToString();
    }
}

/* ─── Globals exposed to the JavaScript runtime ─────────────────────────── */

internal sealed class CloudScriptContext
{
    public PlayfabServer Server;
    public string CurrentPlayerId;
    public CloudScriptContext(PlayfabServer server, string currentPlayerId)
    { Server = server; CurrentPlayerId = currentPlayerId ?? ""; }
}

/// <summary>JS-facing log shim. PlayFab's CloudScript exposes log.info/debug/error. We also surface
/// PascalCase aliases (Info/Debug/Warn/Error) so legacy scripts that call `log.Debug(...)` or
/// `Logger.Debug(...)` (a non-standard alias some Photon-derived codebases use) keep working.</summary>
internal sealed class LogShim
{
    private readonly List<LogStatement> _logs;
    public LogShim(List<LogStatement> logs) => _logs = logs;

    public void info(object message, object data = null)  => Append("Info",  message, data);
    public void debug(object message, object data = null) => Append("Debug", message, data);
    public void warn(object message, object data = null)  => Append("Warning", message, data);
    public void error(object message, object data = null) => Append("Error", message, data);

    public void Info(object message, object data = null)  => Append("Info",  message, data);
    public void Debug(object message, object data = null) => Append("Debug", message, data);
    public void Warn(object message, object data = null)  => Append("Warning", message, data);
    public void Error(object message, object data = null) => Append("Error", message, data);

    private void Append(string level, object message, object data)
    {
        _logs.Add(new LogStatement
        {
            Level   = level,
            Message = message?.ToString() ?? "",
            Data    = data,
        });
    }
}

/// <summary>JS-facing `server` object. Implements the most-used PlayFab Server API methods that
/// CloudScript scripts traditionally call: GetUserData, UpdateUserData, GetTitleData, GetUserInventory,
/// AddUserVirtualCurrency, SubtractUserVirtualCurrency, GrantItemsToUser, GetPlayerStatistics,
/// UpdatePlayerStatistics, WritePlayerEvent, WriteTitleEvent.</summary>
internal sealed class ServerShim
{
    private readonly CloudScriptContext _ctx;
    public ServerShim(CloudScriptContext ctx) => _ctx = ctx;

    private string Pid(System.Dynamic.ExpandoObject _) => _ctx.CurrentPlayerId;

    public object CreateSharedGroup(object args)
    {
        var groupId = ServerShim.ToString_(args, "SharedGroupId") ?? "";
        var coll = PlayfabServer.Database.GetCollection<PlayfabSharedGroupDbObject>(true)!;
        var existing = coll.FindOne(g => g.SharedGroupId == groupId);
        if (existing == null)
            coll.Insert(new PlayfabSharedGroupDbObject { SharedGroupId = groupId });
        return new { SharedGroupId = groupId };
    }

    public object AddSharedGroupMembers(object args)
    {
        var groupId = ServerShim.ToString_(args, "SharedGroupId") ?? "";
        var members = ServerShim.ToStringList(args, "PlayFabIds") ?? new();
        var coll = PlayfabServer.Database.GetCollection<PlayfabSharedGroupDbObject>(true)!;
        var group = coll.FindOne(g => g.SharedGroupId == groupId);
        if (group == null) return new { };
        foreach (var m in members)
            if (!group.Members.Contains(m)) group.Members.Add(m);
        coll.Update(group);
        return new { };
    }

    public object RemoveSharedGroupMembers(object args)
    {
        var groupId = ServerShim.ToString_(args, "SharedGroupId") ?? "";
        var members = ServerShim.ToStringList(args, "PlayFabIds") ?? new();
        var coll = PlayfabServer.Database.GetCollection<PlayfabSharedGroupDbObject>(true)!;
        var group = coll.FindOne(g => g.SharedGroupId == groupId);
        if (group == null) return new { };
        group.Members.RemoveAll(m => members.Contains(m));
        coll.Update(group);
        return new { };
    }

    public object UpdateSharedGroupData(object args)
    {
        var groupId = ServerShim.ToString_(args, "SharedGroupId") ?? "";
        var data = ServerShim.ToStringDict(args, "Data");
        var keysToRemove = ServerShim.ToStringList(args, "KeysToRemove");
        var perm = ServerShim.ToString_(args, "Permission") ?? "Private";
        var coll = PlayfabServer.Database.GetCollection<PlayfabSharedGroupDbObject>(true)!;
        var group = coll.FindOne(g => g.SharedGroupId == groupId);
        if (group == null) return new { };
        if (data != null)
            foreach (var kv in data)
                group.Data[kv.Key] = new PlayfabSharedGroupValue { Value = kv.Value, Permission = perm };
        if (keysToRemove != null)
            foreach (var k in keysToRemove) group.Data.Remove(k);
        coll.Update(group);
        return new { };
    }

    public object GetSharedGroupData(object args)
    {
        var groupId = ServerShim.ToString_(args, "SharedGroupId") ?? "";
        var keys = ServerShim.ToStringList(args, "Keys");
        var coll = PlayfabServer.Database.GetCollection<PlayfabSharedGroupDbObject>(true)!;
        var group = coll.FindOne(g => g.SharedGroupId == groupId);
        // Members is returned as string[] (not List<string>) so Jint exposes it as a real JS
        // array — `for (var i in members)` then iterates indexes, not the List<T> property bag.
        if (group == null) return new { Data = new Dictionary<string, object>(), Members = Array.Empty<string>() };
        var data = group.Data.AsEnumerable();
        if (keys != null && keys.Count > 0)
            data = data.Where(kv => keys.Contains(kv.Key));
        return new
        {
            SharedGroupId = groupId,
            Members = group.Members.ToArray(),
            Data = data.ToDictionary(kv => kv.Key, kv => (object)new { Value = kv.Value.Value, Permission = kv.Value.Permission }),
        };
    }

    public object DeleteSharedGroup(object args)
    {
        var groupId = ServerShim.ToString_(args, "SharedGroupId") ?? "";
        var coll = PlayfabServer.Database.GetCollection<PlayfabSharedGroupDbObject>(true)!;
        coll.DeleteMany(g => g.SharedGroupId == groupId);
        return new { };
    }

    public object GetTitleData(object _ = null)
    {
        var coll = PlayfabServer.Database.GetCollection<PlayfabTitleDataDbObject>(true)!;
        var data = coll.FindAll().Where(t => t.Scope == "Title").ToDictionary(t => t.Key, t => t.Value);
        return new { Data = data };
    }

    public object GetUserData(object args)             => GetUserDataScoped(args, "Title");
    public object GetUserReadOnlyData(object args)     => GetUserDataScoped(args, "ReadOnly");
    public object GetUserInternalData(object args)     => GetUserDataScoped(args, "Internal");
    public object GetUserPublisherData(object args)    => GetUserDataScoped(args, "Publisher");
    public object GetUserPublisherReadOnlyData(object args) => GetUserDataScoped(args, "PublisherReadOnly");
    public object GetUserPublisherInternalData(object args) => GetUserDataScoped(args, "PublisherInternal");

    public object UpdateUserData(object args)             => UpdateUserDataScoped(args, "Title");
    public object UpdateUserReadOnlyData(object args)     => UpdateUserDataScoped(args, "ReadOnly");
    public object UpdateUserInternalData(object args)     => UpdateUserDataScoped(args, "Internal");
    public object UpdateUserPublisherData(object args)    => UpdateUserDataScoped(args, "Publisher");
    public object UpdateUserPublisherReadOnlyData(object args) => UpdateUserDataScoped(args, "PublisherReadOnly");
    public object UpdateUserPublisherInternalData(object args) => UpdateUserDataScoped(args, "PublisherInternal");

    private object GetUserDataScoped(object args, string scope)
    {
        var pid = ResolvePlayFabId(args, _ctx.CurrentPlayerId);
        var keyFilter = ToStringList(args, "Keys");
        var coll = PlayfabServer.Database.GetCollection<PlayfabUserDataDbObject>(true)!;
        var rows = coll.Find(d => d.PlayFabId == pid && d.Scope == scope).ToList();
        if (keyFilter != null && keyFilter.Count > 0)
            rows = rows.Where(r => keyFilter.Contains(r.Key)).ToList();
        return new
        {
            PlayFabId = pid,
            DataVersion = (uint)rows.Count,
            Data = rows.ToDictionary(r => r.Key, r => new
            {
                r.Value,
                r.Permission,
                LastUpdated = DateTimeOffset.FromUnixTimeSeconds(r.LastUpdated).UtcDateTime,
            }),
        };
    }

    private object UpdateUserDataScoped(object args, string scope)
    {
        var pid = ResolvePlayFabId(args, _ctx.CurrentPlayerId);
        var coll = PlayfabServer.Database.GetCollection<PlayfabUserDataDbObject>(true)!;
        var data = ToStringDict(args, "Data");
        var keysToRemove = ToStringList(args, "KeysToRemove");
        var perm = ToString_(args, "Permission") ?? "Private";
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (data != null)
            foreach (var (k, v) in data)
            {
                var key = k;
                var existing = coll.FindOne(d => d.PlayFabId == pid && d.Scope == scope && d.Key == key);
                if (existing == null)
                    coll.Insert(new PlayfabUserDataDbObject { PlayFabId = pid, Scope = scope, Key = k, Value = v, Permission = perm, LastUpdated = now });
                else { existing.Value = v; existing.Permission = perm; existing.LastUpdated = now; coll.Update(existing); }
            }
        if (keysToRemove != null)
            foreach (var k in keysToRemove) { var key = k; coll.DeleteMany(d => d.PlayFabId == pid && d.Scope == scope && d.Key == key); }
        return new { DataVersion = (uint)coll.Count(d => d.PlayFabId == pid && d.Scope == scope) };
    }

    public object GetUserInventory(object args)
    {
        var pid = ResolvePlayFabId(args, _ctx.CurrentPlayerId);
        var inv = PlayfabServer.Database.GetCollection<PlayfabInventoryItemDbObject>(true)!
                                .Find(i => i.PlayFabId == pid && string.IsNullOrEmpty(i.CharacterId)).ToList();
        var vc = PlayfabServer.Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!
                              .Find(v => v.PlayFabId == pid && string.IsNullOrEmpty(v.CharacterId))
                              .ToDictionary(v => v.CurrencyCode, v => v.Balance);
        return new
        {
            PlayFabId       = pid,
            Inventory       = inv.Select(i => new { i.ItemId, i.ItemInstanceId, i.DisplayName, i.CatalogVersion, i.ItemClass, i.PurchaseDate }).ToArray(),
            VirtualCurrency = vc,
        };
    }

    public object AddUserVirtualCurrency(object args)
    {
        var pid     = ResolvePlayFabId(args, _ctx.CurrentPlayerId);
        var amount  = ToInt(args, "Amount");
        var code    = ToString_(args, "VirtualCurrency") ?? "";
        var coll    = PlayfabServer.Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!;
        var existing = coll.FindOne(v => v.PlayFabId == pid && string.IsNullOrEmpty(v.CharacterId) && v.CurrencyCode == code);
        if (existing == null) { existing = new PlayfabVirtualCurrencyDbObject { PlayFabId = pid, CurrencyCode = code, Balance = Math.Max(0, amount) }; coll.Insert(existing); }
        else { existing.Balance = Math.Max(0, existing.Balance + amount); coll.Update(existing); }
        return new { PlayFabId = pid, VirtualCurrency = code, Balance = existing.Balance, BalanceChange = amount };
    }

    public object SubtractUserVirtualCurrency(object args)
    {
        var pid     = ResolvePlayFabId(args, _ctx.CurrentPlayerId);
        var amount  = ToInt(args, "Amount");
        var code    = ToString_(args, "VirtualCurrency") ?? "";
        var coll    = PlayfabServer.Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!;
        var existing = coll.FindOne(v => v.PlayFabId == pid && string.IsNullOrEmpty(v.CharacterId) && v.CurrencyCode == code);
        if (existing == null) { existing = new PlayfabVirtualCurrencyDbObject { PlayFabId = pid, CurrencyCode = code, Balance = 0 }; coll.Insert(existing); }
        var prev = existing.Balance;
        existing.Balance = Math.Max(0, existing.Balance - amount);
        coll.Update(existing);
        return new { PlayFabId = pid, VirtualCurrency = code, Balance = existing.Balance, BalanceChange = existing.Balance - prev };
    }

    public object GetPlayerStatistics(object args)
    {
        var pid = ResolvePlayFabId(args, _ctx.CurrentPlayerId);
        var rows = PlayfabServer.Database.GetCollection<PlayfabStatisticDbObject>(true)!
                               .Find(s => s.PlayFabId == pid && string.IsNullOrEmpty(s.CharacterId)).ToList();
        return new { PlayFabId = pid, Statistics = rows.Select(r => new { r.StatisticName, r.Value, r.Version }).ToArray() };
    }

    public object UpdatePlayerStatistics(object args)
    {
        var pid = ResolvePlayFabId(args, _ctx.CurrentPlayerId);
        var stats = ToObjectList(args, "Statistics");
        var coll = PlayfabServer.Database.GetCollection<PlayfabStatisticDbObject>(true)!;
        if (stats != null)
            foreach (var s in stats)
            {
                var name = ToString_(s, "StatisticName") ?? "";
                if (string.IsNullOrEmpty(name)) continue;
                var value = ToInt(s, "Value");
                var n = name;
                var existing = coll.FindOne(x => x.PlayFabId == pid && string.IsNullOrEmpty(x.CharacterId) && x.StatisticName == n);
                if (existing == null) coll.Insert(new PlayfabStatisticDbObject { PlayFabId = pid, StatisticName = n, Value = value });
                else { existing.Value = value; coll.Update(existing); }
            }
        return new { };
    }

    public object GrantItemsToUser(object args)
    {
        var pid = ResolvePlayFabId(args, _ctx.CurrentPlayerId);
        var itemIds = ToStringList(args, "ItemIds") ?? new();
        var catalog = PlayfabServer.Database.GetCollection<PlayfabCatalogItemDbObject>(true)!;
        var inv     = PlayfabServer.Database.GetCollection<PlayfabInventoryItemDbObject>(true)!;
        var granted = new List<object>();
        foreach (var id in itemIds)
        {
            var iid = id;
            var ci = catalog.FindOne(c => c.ItemId == iid);
            var instId = Guid.NewGuid().ToString("N");
            inv.Insert(new PlayfabInventoryItemDbObject
            {
                PlayFabId      = pid,
                ItemInstanceId = instId,
                ItemId         = iid,
                DisplayName    = ci?.DisplayName ?? "",
                CatalogVersion = ci?.CatalogVersion ?? "",
                ItemClass      = ci?.ItemClass ?? "",
                PurchaseDate   = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
            granted.Add(new { ItemId = iid, ItemInstanceId = instId });
        }
        return new { ItemGrantResults = granted.ToArray() };
    }

    public object WritePlayerEvent(object args)
    {
        var pid       = ResolvePlayFabId(args, _ctx.CurrentPlayerId);
        var eventName = ToString_(args, "EventName") ?? "Untitled";
        var bodyDict  = ToObject(args, "Body");
        var ev = new PlayfabEventDbObject
        {
            EventId        = Guid.NewGuid().ToString("N"),
            EventName      = eventName,
            EventNamespace = "title.player",
            EntityId       = pid,
            EntityType     = "title_player_account",
            PayloadJson    = SafeSerialize(bodyDict),
            Timestamp      = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        PlayfabServer.Database.GetCollection<PlayfabEventDbObject>(true)!.Insert(ev);
        return new { EventId = ev.EventId };
    }

    /// <summary>Serialize a value coming from the JS engine to a JSON string. Jint passes complex
    /// objects as JsValue / ObjectInstance which System.Text.Json can't handle natively (it tries
    /// to walk private internals and either errors or produces garbage). Round-trip through Jint's
    /// own JSON serializer first when we see a JsValue.</summary>
    private static string SafeSerialize(object value)
    {
        if (value == null) return "";
        if (value is JsValue jv)
        {
            if (jv.IsUndefined() || jv.IsNull()) return "";
            try { return new JintJsonSerializer(new Engine()).Serialize(jv).AsString(); }
            catch { return ""; }
        }
        try { return StjJsonSerializer.Serialize(value, PlayfabServer.JsonOpts); }
        catch { return ""; }
    }

    /* ─── Account info / bans / delete ──────────────────────────────────── */

    public object GetUserAccountInfo(object args)
    {
        var pid = ResolvePlayFabId(args, _ctx.CurrentPlayerId);
        var p = PlayfabServer.Database.GetCollection<PlayfabPlayerDbObject>(true)!.FindOne(x => x.PlayFabId == pid);
        if (p == null) return new { UserInfo = (object)null };

        // Match PlayFab's GetUserAccountInfoResult shape — every linked-account block is omitted
        // when the corresponding id is empty so user scripts can do `if (!info.CustomIdInfo)`.
        object Block(string id, string field) =>
            string.IsNullOrEmpty(id) ? null : new Dictionary<string, object> { [field] = id };

        var userInfo = new Dictionary<string, object>
        {
            ["PlayFabId"]    = p.PlayFabId,
            ["Created"]      = DateTimeOffset.FromUnixTimeSeconds(p.Created).UtcDateTime,
            ["Username"]     = p.Username,
            ["TitleInfo"]    = new Dictionary<string, object>
            {
                ["DisplayName"]    = p.DisplayName,
                ["Origination"]    = p.OriginPlatform,
                ["Created"]        = DateTimeOffset.FromUnixTimeSeconds(p.Created).UtcDateTime,
                ["LastLogin"]      = DateTimeOffset.FromUnixTimeSeconds(p.LastLogin).UtcDateTime,
                ["TitlePlayerAccount"] = new { Id = p.EntityId, Type = "title_player_account" },
                ["isBanned"]       = p.IsBanned,
            },
            ["PrivateInfo"]  = new Dictionary<string, object> { ["Email"] = p.Email },
        };
        if (Block(p.CustomId,         "CustomId")        is { } cid)  userInfo["CustomIdInfo"]        = cid;
        if (Block(p.CustomId,         "CustomId")        is { } scid) userInfo["ServerCustomIdInfo"]  = scid;
        if (Block(p.SteamId,          "SteamId")         is { } sid)  userInfo["SteamInfo"]           = sid;
        if (Block(p.XboxId,           "XboxUserId")      is { } xid)  userInfo["XboxInfo"]            = xid;
        if (Block(p.PsnId,            "PsnAccountId")    is { } pid2) userInfo["PsnInfo"]             = pid2;
        if (Block(p.NintendoId,       "NintendoSwitchAccountId") is { } nid) userInfo["NintendoSwitchAccountInfo"] = nid;
        if (Block(p.FacebookId,       "FacebookId")      is { } fid)  userInfo["FacebookInfo"]        = fid;
        if (Block(p.GoogleId,         "GoogleId")        is { } gid)  userInfo["GoogleInfo"]          = gid;
        if (Block(p.GoogleAccountId,  "GoogleAccountId") is { } gpid) userInfo["GooglePlayGamesInfo"] = gpid;
        if (Block(p.GameCenterId,     "GameCenterId")    is { } gcid) userInfo["GameCenterInfo"]     = gcid;
        if (Block(p.AndroidDeviceId,  "AndroidDeviceId") is { } aid)  userInfo["AndroidDeviceInfo"]   = aid;
        if (Block(p.IosDeviceId,      "IosDeviceId")     is { } iid)  userInfo["IosDeviceInfo"]       = iid;
        if (Block(p.TwitchId,         "TwitchId")        is { } tid)  userInfo["TwitchInfo"]          = tid;
        if (Block(p.KongregateId,     "KongregateId")    is { } kid)  userInfo["KongregateInfo"]      = kid;
        if (Block(p.OpenIdSubject,    "Subject")         is { } oid)  userInfo["OpenIdInfo"]          = oid;
        return new { UserInfo = userInfo };
    }

    public object BanUsers(object args)
    {
        var bans = ToObjectList(args, "Bans") ?? new();
        var coll = PlayfabServer.Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var results = new List<object>();
        foreach (var ban in bans)
        {
            var pid = ToString_(ban, "PlayFabId");
            if (string.IsNullOrEmpty(pid)) continue;
            var lookup = pid;
            var player = coll.FindOne(p => p.PlayFabId == lookup);
            if (player == null) continue;
            player.IsBanned = true;
            coll.Update(player);
            results.Add(new
            {
                BanId      = Guid.NewGuid().ToString("N"),
                PlayFabId  = pid,
                Active     = true,
                Created    = DateTimeOffset.UtcNow.UtcDateTime,
                Reason     = ToString_(ban, "Reason") ?? "",
                IPAddress  = ToString_(ban, "IPAddress") ?? "",
                Expires    = (object)null,
            });
        }
        return new { BanData = results.ToArray() };
    }

    public object RevokeAllBansForUser(object args)
    {
        var pid = ResolvePlayFabId(args, _ctx.CurrentPlayerId);
        var coll = PlayfabServer.Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var lookup = pid;
        var player = coll.FindOne(p => p.PlayFabId == lookup);
        if (player == null) return new { BanData = Array.Empty<object>() };
        player.IsBanned = false;
        coll.Update(player);
        return new { BanData = Array.Empty<object>() };
    }

    /// <summary>Cascading delete that mirrors PlayFab's /Admin/DeletePlayer: drops the player row plus
    /// every record keyed off PlayFabId across the title.</summary>
    public object DeletePlayer(object args)
    {
        var pid = ResolvePlayFabId(args, _ctx.CurrentPlayerId);
        var lookup = pid;
        PlayfabServer.Database.GetCollection<PlayfabPlayerDbObject>(true)!.DeleteMany(x => x.PlayFabId == lookup);
        PlayfabServer.Database.GetCollection<PlayfabSessionDbObject>(true)!.DeleteMany(x => x.PlayFabId == lookup);
        PlayfabServer.Database.GetCollection<PlayfabUserDataDbObject>(true)!.DeleteMany(x => x.PlayFabId == lookup);
        PlayfabServer.Database.GetCollection<PlayfabStatisticDbObject>(true)!.DeleteMany(x => x.PlayFabId == lookup);
        PlayfabServer.Database.GetCollection<PlayfabInventoryItemDbObject>(true)!.DeleteMany(x => x.PlayFabId == lookup);
        PlayfabServer.Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!.DeleteMany(x => x.PlayFabId == lookup);
        PlayfabServer.Database.GetCollection<PlayfabFriendDbObject>(true)!.DeleteMany(x => x.OwnerPlayFabId == lookup || x.FriendPlayFabId == lookup);
        PlayfabServer.Database.GetCollection<PlayfabPlayerTagDbObject>(true)!.DeleteMany(x => x.PlayFabId == lookup);
        PlayfabServer.Database.GetCollection<PlayfabCharacterDbObject>(true)!.DeleteMany(x => x.PlayFabId == lookup);
        PlayfabServer.Database.GetCollection<PlayfabCharacterDataDbObject>(true)!.DeleteMany(x => x.PlayFabId == lookup);
        PlayfabServer.Database.GetCollection<PlayfabPushTokenDbObject>(true)!.DeleteMany(x => x.PlayFabId == lookup);
        return new { };
    }

    public object WriteTitleEvent(object args)
    {
        var eventName = ToString_(args, "EventName") ?? "Untitled";
        var bodyDict  = ToObject(args, "Body");
        var ev = new PlayfabEventDbObject
        {
            EventId        = Guid.NewGuid().ToString("N"),
            EventName      = eventName,
            EventNamespace = "title.events",
            PayloadJson    = SafeSerialize(bodyDict),
            Timestamp      = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        PlayfabServer.Database.GetCollection<PlayfabEventDbObject>(true)!.Insert(ev);
        return new { EventId = ev.EventId };
    }

    /* ─── arg helpers ─────────────────────────────────────────────────────── */
    // CloudScript args reach the shim as Jint values (the JS engine creates the object
    // literal and passes it across interop). Jint may give us a JsValue, an ObjectInstance,
    // or — for some primitives — a CLR string/number/bool already unwrapped. The helpers
    // below handle all of those plus IDictionary<string, object> and plain CLR objects.

    private static string ResolvePlayFabId(object args, string fallback)
    {
        var v = ToString_(args, "PlayFabId");
        return string.IsNullOrEmpty(v) ? fallback : v;
    }

    /// <summary>Read a single named slot from whatever shape `obj` is. Returns the raw value
    /// (possibly still a JsValue) or null if the slot is missing/undefined.</summary>
    private static object GetSlot(object obj, string key)
    {
        if (obj == null) return null;
        if (obj is IDictionary<string, object> d) return d.TryGetValue(key, out var v) ? v : null;
        if (obj is JsValue jv)
        {
            if (jv.IsUndefined() || jv.IsNull()) return null;
            if (jv.IsObject())
            {
                var pv = jv.AsObject().Get(key);
                if (pv.IsUndefined()) return null;
                return pv;
            }
            return null;
        }
        var p = obj.GetType().GetProperty(key);
        return p?.GetValue(obj);
    }

    internal static string ToString_(object obj, string key)
    {
        var v = GetSlot(obj, key);
        if (v == null) return null;
        if (v is JsValue jv)
        {
            if (jv.IsUndefined() || jv.IsNull()) return null;
            if (jv.IsString()) return jv.AsString();
            if (jv.IsNumber())  return jv.AsNumber().ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (jv.IsBoolean()) return jv.AsBoolean() ? "true" : "false";
            return jv.ToString();
        }
        return v.ToString();
    }

    internal static int ToInt(object obj, string key)
    {
        var v = GetSlot(obj, key);
        if (v == null) return 0;
        if (v is JsValue jv)
        {
            if (jv.IsNumber()) return (int)jv.AsNumber();
            if (jv.IsString() && int.TryParse(jv.AsString(), out var i1)) return i1;
            return 0;
        }
        return int.TryParse(v.ToString(), out var i) ? i : 0;
    }

    /// <summary>Convert a JS object (or .NET dict) at <paramref name="key"/> into a string→string
    /// dictionary. Numeric / bool values are stringified.</summary>
    internal static Dictionary<string, string> ToStringDict(object obj, string key)
    {
        var v = GetSlot(obj, key);
        if (v == null) return null;
        if (v is JsValue jv)
        {
            if (!jv.IsObject()) return null;
            var oi = jv.AsObject();
            var dict = new Dictionary<string, string>();
            foreach (var prop in oi.GetOwnProperties())
            {
                var k = prop.Key.ToString();
                var pv = oi.Get(prop.Key);
                if (pv.IsUndefined() || pv.IsNull()) dict[k] = "";
                else if (pv.IsString())              dict[k] = pv.AsString();
                else if (pv.IsNumber())              dict[k] = pv.AsNumber().ToString(System.Globalization.CultureInfo.InvariantCulture);
                else if (pv.IsBoolean())             dict[k] = pv.AsBoolean() ? "true" : "false";
                else                                 dict[k] = pv.ToString();
            }
            return dict;
        }
        if (v is IDictionary<string, object> map)
            return map.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
        return null;
    }

    /// <summary>Convert a JS array (or .NET sequence) at <paramref name="key"/> into a list of
    /// strings. A single string value is also accepted and returned as a one-element list — many
    /// legacy CloudScripts call APIs like AddSharedGroupMembers with a single PlayFabId instead
    /// of an array.</summary>
    internal static List<string> ToStringList(object obj, string key)
    {
        var v = GetSlot(obj, key);
        if (v == null) return null;
        if (v is JsValue jv)
        {
            if (jv.IsString()) return new List<string> { jv.AsString() };
            if (jv.IsArray())
            {
                var arr = jv.AsArray();
                var list = new List<string>();
                for (uint i = 0; i < arr.Length; i++)
                {
                    var el = arr.Get(i);
                    if (el.IsUndefined() || el.IsNull()) continue;
                    if (el.IsString())       list.Add(el.AsString());
                    else if (el.IsNumber())  list.Add(el.AsNumber().ToString(System.Globalization.CultureInfo.InvariantCulture));
                    else if (el.IsBoolean()) list.Add(el.AsBoolean() ? "true" : "false");
                    else                     list.Add(el.ToString());
                }
                return list;
            }
            return null;
        }
        if (v is string s) return new List<string> { s };
        if (v is IEnumerable<string> sseq) return sseq.ToList();
        if (v is IEnumerable<object> seq)  return seq.Select(x => x?.ToString() ?? "").ToList();
        return null;
    }

    internal static List<object> ToObjectList(object obj, string key)
    {
        var v = GetSlot(obj, key);
        if (v == null) return null;
        if (v is JsValue jv)
        {
            if (!jv.IsArray()) return null;
            var arr = jv.AsArray();
            var list = new List<object>();
            for (uint i = 0; i < arr.Length; i++) list.Add(arr.Get(i));
            return list;
        }
        if (v is IEnumerable<object> seq) return seq.ToList();
        return null;
    }

    internal static object ToObject(object obj, string key) => GetSlot(obj, key);
}

/// <summary>JS-facing `http` object. Mirrors PlayFab CloudScript's synchronous http.request signature:
/// http.request(url, method, body, contentType, headers) → string. An empty/missing URL is treated as a
/// no-op (returns ""), so the many discord-webhook calls in legacy scripts that have their URL stripped
/// for safety don't tank the script.</summary>
internal sealed class HttpShim
{
    private readonly List<LogStatement> _logs;
    public HttpShim(List<LogStatement> logs) => _logs = logs;

    public string request(string url, string method = "GET", string body = "", string contentType = "application/json", object headers = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var req = new HttpRequestMessage(new HttpMethod((method ?? "GET").ToUpperInvariant()), url);
            if (!string.IsNullOrEmpty(body) && req.Method != HttpMethod.Get && req.Method != HttpMethod.Head)
                req.Content = new StringContent(body, System.Text.Encoding.UTF8, contentType ?? "application/json");
            if (headers is IDictionary<string, object> hd)
                foreach (var kv in hd)
                {
                    var k = kv.Key; var v = kv.Value?.ToString() ?? "";
                    if (string.IsNullOrEmpty(k)) continue;
                    if (k.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) && req.Content != null)
                        req.Content.Headers.TryAddWithoutValidation(k, v);
                    else
                        req.Headers.TryAddWithoutValidation(k, v);
                }
            var resp = http.Send(req);
            return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logs.Add(new LogStatement { Level = "Warning", Message = $"http.request('{url}') failed: {ex.Message}" });
            return "";
        }
    }
}

/// <summary>JS-facing `entity` object. Provides a very small subset for entity profile data.</summary>
internal sealed class EntityShim
{
    private readonly CloudScriptContext _ctx;
    public EntityShim(CloudScriptContext ctx) => _ctx = ctx;


    public object GetProfile(object args)
    {
        var entityId = ServerShim.ToString_(args, "EntityId") ?? _ctx.CurrentPlayerId;
        var coll = PlayfabServer.Database.GetCollection<PlayfabProfileDbObject>(true)!;
        var p = coll.FindOne(x => x.EntityId == entityId);
        if (p == null) return null;
        return new { p.EntityId, p.EntityType, p.DisplayName, p.AvatarUrl, p.Language };
    }

    public object GetObjects(object args)
    {
        var entityId = ServerShim.ToString_(args, "EntityId") ?? _ctx.CurrentPlayerId;
        var coll = PlayfabServer.Database.GetCollection<PlayfabObjectDbObject>(true)!;
        var rows = coll.Find(x => x.EntityId == entityId).ToList();
        return new { Objects = rows.ToDictionary(r => r.ObjectName, r => r.DataObjectJson) };
    }
}
