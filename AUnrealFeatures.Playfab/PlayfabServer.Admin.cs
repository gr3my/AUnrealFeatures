using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Database;
using AUnrealFeatures.Playfab.Models;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed class PlayfabVirtualCurrencyDefDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int InitialDeposit { get; set; }
    public int RechargeRate { get; set; }
    public int RechargeMax { get; set; }
}

public sealed class PlayfabTitleNewsDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string NewsId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public long Timestamp { get; set; }
}

public sealed partial class PlayfabServer
{
    /* ─── Admin: title metadata read ───────────────────────────────────────── */

    /// <summary>Returns a snapshot of every title-level config object so an operator can verify the
    /// title is bootstrapped: title data, catalog items, virtual currencies, statistic definitions,
    /// CloudScript revision, news, addon configs.</summary>
    [HttpPost("/Admin/GetTitleSettings")]
    public Task<IHttpActionResult> Admin_GetTitleSettings(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var summary = new
        {
            TitleId            = TITLE_ID,
            TitleData          = Database.GetCollection<PlayfabTitleDataDbObject>(true)!.FindAll().Where(d => d.TitleId == TITLE_ID && d.Scope == "Title").ToDictionary(d => d.Key, d => d.Value),
            TitleInternalData  = Database.GetCollection<PlayfabTitleDataDbObject>(true)!.FindAll().Where(d => d.TitleId == TITLE_ID && d.Scope == "TitleInternal").ToDictionary(d => d.Key, d => d.Value),
            CatalogItemCount   = Database.GetCollection<PlayfabCatalogItemDbObject>(true)!.Count(c => true),
            VirtualCurrencies  = Database.GetCollection<PlayfabVirtualCurrencyDefDbObject>(true)!.Find(v => v.TitleId == TITLE_ID).Select(v => new { v.CurrencyCode, v.DisplayName, v.InitialDeposit, v.RechargeRate, v.RechargeMax }).ToList(),
            StatDefinitions    = Database.GetCollection<PlayfabStatisticDefinitionDbObject>(true)!.Find(s => s.TitleId == TITLE_ID).Select(s => new { s.StatisticName, s.AggregationMethod, s.ResetInterval, s.CurrentVersion }).ToList(),
            NewsCount          = Database.GetCollection<PlayfabTitleNewsDbObject>(true)!.Count(n => n.TitleId == TITLE_ID),
            CloudScriptVersion = CurrentLiveRevision(),
            PlayerCount        = Database.GetCollection<PlayfabPlayerDbObject>(true)!.Count(p => true),
            PhotonConfigured   = !string.IsNullOrEmpty(PHOTON_APP_ID) && PHOTON_APP_ID != "00000000-0000-0000-0000-000000000000",
        };
        return Task.FromResult(Ok(summary));
    }

    /* ─── Admin: title data ───────────────────────────────────────────────── */

    private sealed class SetTitleDataBody
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public Dictionary<string, string> KeyValues { get; set; }  // bulk form
    }

    private IHttpActionResult SetTitleDataInternal(IHttpRequest request, string scope)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Unauthorized();
        SetTitleDataBody body;
        try { body = JsonSerializer.Deserialize<SetTitleDataBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}"); }
        if (body == null) return Err("InvalidParams", 1000, "Body required");

        var coll = Database.GetCollection<PlayfabTitleDataDbObject>(true)!;
        var pairs = body.KeyValues ?? new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(body.Key)) pairs[body.Key] = body.Value ?? "";
        if (pairs.Count == 0) return Err("InvalidParams", 1000, "Provide Key/Value or KeyValues");

        foreach (var (k, v) in pairs)
        {
            var key = k;
            var existing = coll.FindOne(x => x.TitleId == TITLE_ID && x.Scope == scope && x.Key == key);
            if (existing == null)
                coll.Insert(new PlayfabTitleDataDbObject { TitleId = TITLE_ID, Key = k, Value = v, Scope = scope, LastUpdated = NowUnix() });
            else { existing.Value = v; existing.LastUpdated = NowUnix(); coll.Update(existing); }
        }
        return Ok(new { Updated = pairs.Count });
    }

    /// <summary>Set one or more title-data key/value entries (visible to clients via GetTitleData).</summary>
    [HttpPost("/Admin/SetTitleData")]
    public Task<IHttpActionResult> Admin_SetTitleData(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult(SetTitleDataInternal(request, "Title"));

    /// <summary>Set one or more internal title-data entries (server-only).</summary>
    [HttpPost("/Admin/SetTitleInternalData")]
    public Task<IHttpActionResult> Admin_SetTitleInternalData(IHttpRequest request, IHttpResponse response) =>
        Task.FromResult(SetTitleDataInternal(request, "TitleInternal"));

    /// <summary>Returns title data (admin view — includes both Title and TitleInternal).</summary>
    [HttpPost("/Admin/GetTitleData")]
    public Task<IHttpActionResult> Admin_GetTitleData(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabTitleDataDbObject>(true)!
                          .Find(d => d.TitleId == TITLE_ID).ToList();
        return Task.FromResult(Ok(new
        {
            Title          = rows.Where(r => r.Scope == "Title").ToDictionary(r => r.Key, r => r.Value),
            TitleInternal  = rows.Where(r => r.Scope == "TitleInternal").ToDictionary(r => r.Key, r => r.Value),
            Publisher      = rows.Where(r => r.Scope == "Publisher").ToDictionary(r => r.Key, r => r.Value),
        }));
    }

    /* ─── Admin: virtual currencies ───────────────────────────────────────── */

    private sealed class AddVirtualCurrencyBody
    {
        public string CurrencyCode { get; set; }
        public string DisplayName  { get; set; }
        public int InitialDeposit  { get; set; }
        public int RechargeRate    { get; set; }
        public int RechargeMax     { get; set; }
        public List<AddVirtualCurrencyBody> VirtualCurrencies { get; set; }  // bulk form
    }

    /// <summary>Defines a virtual currency type for the title.</summary>
    [HttpPost("/Admin/AddVirtualCurrencyTypes")]
    public Task<IHttpActionResult> Admin_AddVirtualCurrencyTypes(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        AddVirtualCurrencyBody body;
        try { body = JsonSerializer.Deserialize<AddVirtualCurrencyBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }

        var coll = Database.GetCollection<PlayfabVirtualCurrencyDefDbObject>(true)!;
        var list = body?.VirtualCurrencies ?? (body != null && !string.IsNullOrEmpty(body.CurrencyCode) ? new List<AddVirtualCurrencyBody> { body } : new());
        if (list.Count == 0) return Task.FromResult(Err("InvalidParams", 1000, "Provide CurrencyCode (single) or VirtualCurrencies (bulk)"));

        foreach (var vc in list)
        {
            if (string.IsNullOrEmpty(vc.CurrencyCode)) continue;
            var code = vc.CurrencyCode;
            var existing = coll.FindOne(x => x.TitleId == TITLE_ID && x.CurrencyCode == code);
            if (existing == null)
                coll.Insert(new PlayfabVirtualCurrencyDefDbObject
                {
                    TitleId        = TITLE_ID,
                    CurrencyCode   = vc.CurrencyCode,
                    DisplayName    = vc.DisplayName ?? "",
                    InitialDeposit = vc.InitialDeposit,
                    RechargeRate   = vc.RechargeRate,
                    RechargeMax    = vc.RechargeMax,
                });
            else
            {
                existing.DisplayName    = vc.DisplayName ?? existing.DisplayName;
                existing.InitialDeposit = vc.InitialDeposit;
                existing.RechargeRate   = vc.RechargeRate;
                existing.RechargeMax    = vc.RechargeMax;
                coll.Update(existing);
            }
        }
        return Task.FromResult(Ok(new { Added = list.Count }));
    }

    /// <summary>Lists every virtual currency type defined for the title.</summary>
    [HttpPost("/Admin/ListVirtualCurrencyTypes")]
    public Task<IHttpActionResult> Admin_ListVirtualCurrencyTypes(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabVirtualCurrencyDefDbObject>(true)!
                           .Find(v => v.TitleId == TITLE_ID).ToList();
        return Task.FromResult(Ok(new
        {
            VirtualCurrencies = rows.Select(v => new
            {
                v.CurrencyCode, v.DisplayName, v.InitialDeposit, v.RechargeRate, v.RechargeMax
            }).ToList()
        }));
    }

    /* ─── Admin: catalog (legacy v1 / dev seeding) ───────────────────────── */

    /// <summary>Bulk-add or replace catalog items in a catalog version. Accepts the full PlayFab
    /// catalog export shape (including Bundle / Consumable / Container / ActivatedMembership /
    /// DeepLinks). Each item's full JSON is preserved verbatim and replayed to clients on read.
    /// Body shape: { "CatalogVersion": "DLC", "Catalog": [ ...items ] } (matches PlayFab's
    /// /Admin/SetCatalogItems body).</summary>
    [HttpPost("/Admin/AddCatalogItems")]
    public Task<IHttpActionResult> Admin_AddCatalogItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (root.ValueKind != JsonValueKind.Object) return Task.FromResult(Err("InvalidParams", 1000, "Body must be a JSON object"));
        if (!root.TryGetProperty("Catalog", out var catEl) || catEl.ValueKind != JsonValueKind.Array)
            return Task.FromResult(Err("InvalidParams", 1000, "Catalog array required"));

        string version = "main";
        if (root.TryGetProperty("CatalogVersion", out var vEl) && vEl.ValueKind == JsonValueKind.String)
        {
            var v = vEl.GetString();
            if (!string.IsNullOrEmpty(v)) version = v!;
        }

        var coll = Database.GetCollection<PlayfabCatalogItemDbObject>(true)!;
        bool replace = root.TryGetProperty("SetAsDefaultCatalog", out var rEl) && rEl.ValueKind == JsonValueKind.True;
        if (replace)
        {
            var v = version;
            coll.DeleteMany(c => c.CatalogVersion == v);
        }

        int added = 0, skipped = 0;
        foreach (var item in catEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { skipped++; continue; }

            // Required: ItemId
            string iid = item.TryGetProperty("ItemId", out var iidEl) && iidEl.ValueKind == JsonValueKind.String
                ? iidEl.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(iid)) { skipped++; continue; }

            string itemVersion = item.TryGetProperty("CatalogVersion", out var ivEl) && ivEl.ValueKind == JsonValueKind.String
                ? (ivEl.GetString() ?? version) : version;
            if (string.IsNullOrEmpty(itemVersion)) itemVersion = version;

            var v = itemVersion;
            var iidLocal = iid;
            var existing = coll.FindOne(c => c.CatalogVersion == v && c.ItemId == iidLocal);
            var row = existing ?? new PlayfabCatalogItemDbObject { CatalogVersion = itemVersion, ItemId = iid };

            row.CatalogVersion             = itemVersion;
            row.ItemId                     = iid;
            row.ItemClass                  = ReadStringOrEmpty(item, "ItemClass");
            row.DisplayName                = ReadStringOrEmpty(item, "DisplayName");
            row.Description                = ReadStringOrEmpty(item, "Description");
            row.VirtualCurrencyPrices      = ReadUintDict(item, "VirtualCurrencyPrices");
            row.RealCurrencyPrices         = ReadUintDict(item, "RealCurrencyPrices");
            row.Tags                       = ReadStringList(item, "Tags");
            row.CustomData                 = ReadStringOrEmpty(item, "CustomData");
            row.IsLimitedEdition           = ReadBool(item, "IsLimitedEdition");
            row.InitialLimitedEditionCount = ReadInt(item, "InitialLimitedEditionCount");
            row.IsStackable                = ReadBool(item, "IsStackable");
            row.IsTradable                 = ReadBool(item, "IsTradable");
            row.ItemImageUrl               = ReadStringOrEmpty(item, "ItemImageUrl");
            row.FullJson                   = item.GetRawText();

            if (existing == null) coll.Insert(row);
            else                  coll.Update(row);
            added++;
        }
        return Task.FromResult(Ok(new { Added = added, Skipped = skipped, CatalogVersion = version }));
    }

    /// <summary>Lists every catalog item, optionally filtered by version.</summary>
    [HttpPost("/Admin/GetCatalogItems")]
    public Task<IHttpActionResult> Admin_GetCatalogItemsAdmin(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        string filterVersion = "";
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            if (doc.RootElement.TryGetProperty("CatalogVersion", out var v) && v.ValueKind == JsonValueKind.String)
                filterVersion = v.GetString() ?? "";
        }
        catch { /* empty body OK */ }

        var allRows = Database.GetCollection<PlayfabCatalogItemDbObject>(true)!.FindAll().ToList();
        // Histogram is computed across every version so the dashboard can keep showing
        // the version chips even when a specific version is selected.
        var versions = allRows.GroupBy(r => r.CatalogVersion).ToDictionary(g => g.Key, g => g.Count());
        var rows = string.IsNullOrEmpty(filterVersion)
            ? allRows
            : allRows.Where(r => r.CatalogVersion == filterVersion).ToList();
        var catalog = rows.Select(r =>
        {
            if (!string.IsNullOrEmpty(r.FullJson))
            {
                try { return JsonSerializer.Deserialize<JsonElement>(r.FullJson) as object; }
                catch { /* fallthrough */ }
            }
            return (object)new
            {
                r.CatalogVersion, r.ItemId, r.ItemClass, r.DisplayName, r.Description,
                r.VirtualCurrencyPrices, r.RealCurrencyPrices, r.Tags, r.CustomData,
                r.IsLimitedEdition, r.InitialLimitedEditionCount, r.IsStackable, r.IsTradable, r.ItemImageUrl,
            };
        }).ToList();
        return Task.FromResult(Ok(new { Catalog = catalog, Versions = versions, Count = rows.Count }));
    }

    /// <summary>Deletes a single catalog item.</summary>
    [HttpPost("/Admin/DeleteCatalogItem")]
    public Task<IHttpActionResult> Admin_DeleteCatalogItem(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        string iid = "", v = "";
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            if (doc.RootElement.TryGetProperty("ItemId", out var i) && i.ValueKind == JsonValueKind.String)
                iid = i.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("CatalogVersion", out var cv) && cv.ValueKind == JsonValueKind.String)
                v = cv.GetString() ?? "";
        }
        catch { }
        if (string.IsNullOrEmpty(iid)) return Task.FromResult(Err("InvalidParams", 1000, "ItemId required"));
        var n = string.IsNullOrEmpty(v)
            ? Database.GetCollection<PlayfabCatalogItemDbObject>(true)!.DeleteMany(c => c.ItemId == iid)
            : Database.GetCollection<PlayfabCatalogItemDbObject>(true)!.DeleteMany(c => c.ItemId == iid && c.CatalogVersion == v);
        return Task.FromResult(Ok(new { Deleted = n }));
    }

    /* ─── JsonElement helpers ─────────────────────────────────────────────── */

    private static string ReadStringOrEmpty(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var v)) return "";
        return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
    }
    private static bool ReadBool(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var v)) return false;
        return v.ValueKind == JsonValueKind.True;
    }
    private static int ReadInt(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
    }
    private static List<string> ReadStringList(JsonElement obj, string key)
    {
        var result = new List<string>();
        if (!obj.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) return result;
        foreach (var x in v.EnumerateArray())
            if (x.ValueKind == JsonValueKind.String) result.Add(x.GetString() ?? "");
        return result;
    }
    private static Dictionary<string, uint> ReadUintDict(JsonElement obj, string key)
    {
        var result = new Dictionary<string, uint>();
        if (!obj.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Object) return result;
        foreach (var p in v.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetUInt32(out var n))
                result[p.Name] = n;
        }
        return result;
    }

    /* ─── Admin: statistic definitions (legacy v1) ────────────────────────── */

    private sealed class CreateStatDefBody
    {
        public string StatisticName { get; set; }
        public string AggregationMethod { get; set; } = "Last";   // Last | Min | Max | Sum
        public string VersionChangeInterval { get; set; } = "Never"; // Never | Hour | Day | Week | Month
    }

    /// <summary>Defines a player statistic (legacy v1 — used by GetPlayerStatistics / leaderboards).</summary>
    [HttpPost("/Admin/CreatePlayerStatisticDefinition")]
    public Task<IHttpActionResult> Admin_CreatePlayerStatisticDefinition(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        CreateStatDefBody body;
        try { body = JsonSerializer.Deserialize<CreateStatDefBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (body == null || string.IsNullOrEmpty(body.StatisticName))
            return Task.FromResult(Err("InvalidParams", 1000, "StatisticName required"));

        var coll = Database.GetCollection<PlayfabStatisticDefinitionDbObject>(true)!;
        var name = body.StatisticName;
        var existing = coll.FindOne(d => d.TitleId == TITLE_ID && d.StatisticName == name);
        if (existing == null)
            coll.Insert(new PlayfabStatisticDefinitionDbObject
            {
                TitleId           = TITLE_ID,
                StatisticName     = body.StatisticName,
                AggregationMethod = body.AggregationMethod ?? "Last",
                ResetInterval     = body.VersionChangeInterval ?? "Never",
                CurrentVersion    = 1,
            });
        else
        {
            existing.AggregationMethod = body.AggregationMethod ?? existing.AggregationMethod;
            existing.ResetInterval     = body.VersionChangeInterval ?? existing.ResetInterval;
            coll.Update(existing);
        }
        return Task.FromResult(Ok(new { Statistic = body.StatisticName }));
    }

    /// <summary>Returns every player statistic definition for the title.</summary>
    [HttpPost("/Admin/GetPlayerStatisticDefinitions")]
    public Task<IHttpActionResult> Admin_GetPlayerStatisticDefinitions(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabStatisticDefinitionDbObject>(true)!
                           .Find(d => d.TitleId == TITLE_ID).ToList();
        return Task.FromResult(Ok(new
        {
            Statistics = rows.Select(s => new
            {
                s.StatisticName, s.AggregationMethod,
                VersionChangeInterval = s.ResetInterval,
                s.CurrentVersion,
            }).ToList()
        }));
    }

    /* ─── Admin: title news ───────────────────────────────────────────────── */

    private sealed class AddTitleNewsBody
    {
        public string Title { get; set; }
        public string Body  { get; set; }
        public DateTime? Timestamp { get; set; }
    }

    /// <summary>Posts a title news item (returned to clients via GetTitleNews).</summary>
    [HttpPost("/Admin/AddNews")]
    public Task<IHttpActionResult> Admin_AddNews(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        AddTitleNewsBody body;
        try { body = JsonSerializer.Deserialize<AddTitleNewsBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (body == null || string.IsNullOrEmpty(body.Title))
            return Task.FromResult(Err("InvalidParams", 1000, "Title required"));

        var news = new PlayfabTitleNewsDbObject
        {
            TitleId   = TITLE_ID,
            NewsId    = NewId(),
            Title     = body.Title,
            Body      = body.Body ?? "",
            Timestamp = body.Timestamp.HasValue ? new DateTimeOffset(body.Timestamp.Value, TimeSpan.Zero).ToUnixTimeSeconds() : NowUnix(),
        };
        Database.GetCollection<PlayfabTitleNewsDbObject>(true)!.Insert(news);
        return Task.FromResult(Ok(new { NewsId = news.NewsId }));
    }

    /* ─── Admin: bans / unbans ────────────────────────────────────────────── */

    private sealed class BanUserBody { public string PlayFabId { get; set; } public string Reason { get; set; } }

    /// <summary>Bans a player by PlayFabId.</summary>
    [HttpPost("/Admin/BanUser")]
    public Task<IHttpActionResult> Admin_BanUser(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        BanUserBody body;
        try { body = JsonSerializer.Deserialize<BanUserBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (body == null || string.IsNullOrEmpty(body.PlayFabId))
            return Task.FromResult(Err("InvalidParams", 1000, "PlayFabId required"));

        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var pid  = body.PlayFabId;
        var p    = coll.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound));
        p.IsBanned = true;
        coll.Update(p);
        return Task.FromResult(Ok(new { Banned = true, PlayFabId = body.PlayFabId, Reason = body.Reason ?? "" }));
    }

    /// <summary>Unbans a player by PlayFabId.</summary>
    [HttpPost("/Admin/UnbanUser")]
    public Task<IHttpActionResult> Admin_UnbanUser(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        BanUserBody body;
        try { body = JsonSerializer.Deserialize<BanUserBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (body == null || string.IsNullOrEmpty(body.PlayFabId))
            return Task.FromResult(Err("InvalidParams", 1000, "PlayFabId required"));

        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var pid  = body.PlayFabId;
        var p    = coll.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound));
        p.IsBanned = false;
        coll.Update(p);
        return Task.FromResult(Ok(new { Banned = false, PlayFabId = body.PlayFabId }));
    }

    /* ─── Admin: update / delete a player ─────────────────────────────────── */

    private sealed class UpdatePlayerBody
    {
        public string PlayFabId    { get; set; }
        public string DisplayName  { get; set; }
        public string Username     { get; set; }
        public string Email        { get; set; }
        public string ContactEmail { get; set; }
        public string CustomId     { get; set; }
        public string AvatarUrl    { get; set; }
        public string OriginPlatform { get; set; }
        public bool?  IsBanned     { get; set; }
        public List<string> Tags   { get; set; }
    }

    /// <summary>Updates editable profile fields on a player. Null/missing fields are left untouched.</summary>
    [HttpPost("/Admin/UpdatePlayer")]
    public Task<IHttpActionResult> Admin_UpdatePlayer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        UpdatePlayerBody body;
        try { body = JsonSerializer.Deserialize<UpdatePlayerBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (body == null || string.IsNullOrEmpty(body.PlayFabId))
            return Task.FromResult(Err("InvalidParams", 1000, "PlayFabId required"));

        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var pid  = body.PlayFabId;
        var p    = coll.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound));

        if (body.DisplayName    != null) p.DisplayName    = body.DisplayName;
        if (body.Username       != null) p.Username       = body.Username;
        if (body.Email          != null) p.Email          = body.Email;
        if (body.ContactEmail   != null) p.ContactEmail   = body.ContactEmail;
        if (body.CustomId       != null) p.CustomId       = body.CustomId;
        if (body.AvatarUrl      != null) p.AvatarUrl      = body.AvatarUrl;
        if (body.OriginPlatform != null) p.OriginPlatform = body.OriginPlatform;
        if (body.IsBanned.HasValue)      p.IsBanned       = body.IsBanned.Value;
        if (body.Tags           != null) p.Tags           = body.Tags;
        coll.Update(p);
        return Task.FromResult(Ok(new { Updated = true, PlayFabId = p.PlayFabId }));
    }

    /// <summary>Deletes a player and every record that belongs to them (sessions, user data, stats,
    /// inventory, virtual currency, friends, tags, characters, push tokens). Mirrors PlayFab's
    /// /Admin/DeleteMasterPlayerAccount semantics.</summary>
    [HttpPost("/Admin/DeletePlayer")]
    public Task<IHttpActionResult> Admin_DeletePlayer(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        string pid;
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            pid = doc.RootElement.TryGetProperty("PlayFabId", out var x) ? x.GetString() ?? "" : "";
        }
        catch { pid = request.GetQueryParameter("PlayFabId") ?? ""; }
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Err("InvalidParams", 1000, "PlayFabId required"));

        var coll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var p    = coll.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound));

        var counts = new
        {
            Player        = coll.DeleteMany(x => x.PlayFabId == pid),
            Sessions      = Database.GetCollection<PlayfabSessionDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid),
            UserData      = Database.GetCollection<PlayfabUserDataDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid),
            Statistics    = Database.GetCollection<PlayfabStatisticDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid),
            Inventory     = Database.GetCollection<PlayfabInventoryItemDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid),
            Currencies    = Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid),
            Friends       = Database.GetCollection<PlayfabFriendDbObject>(true)!.DeleteMany(x => x.OwnerPlayFabId == pid || x.FriendPlayFabId == pid),
            Tags          = Database.GetCollection<PlayfabPlayerTagDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid),
            Characters    = Database.GetCollection<PlayfabCharacterDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid),
            CharacterData = Database.GetCollection<PlayfabCharacterDataDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid),
            PushTokens    = Database.GetCollection<PlayfabPushTokenDbObject>(true)!.DeleteMany(x => x.PlayFabId == pid),
        };
        return Task.FromResult(Ok(new { Deleted = true, PlayFabId = pid, Removed = counts }));
    }

    /* ─── Admin: player inventory ─────────────────────────────────────────── */

    private sealed class GrantItemsBody
    {
        public string PlayFabId      { get; set; }
        public string CatalogVersion { get; set; }
        public string Annotation     { get; set; }
        public List<string> ItemIds  { get; set; }
    }

    /// <summary>Grants one or more catalog items to a player. Each ItemId is materialized as an
    /// inventory instance (ItemInstanceId is auto-generated). Looks up DisplayName / ItemClass /
    /// CatalogVersion from the catalog so the resulting row is queryable. If CatalogVersion is
    /// supplied, only items in that version are granted; otherwise the first match wins.</summary>
    [HttpPost("/Admin/GrantItemsToUser")]
    public Task<IHttpActionResult> Admin_GrantItemsToUser(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        GrantItemsBody body;
        try { body = JsonSerializer.Deserialize<GrantItemsBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (body == null || string.IsNullOrEmpty(body.PlayFabId))
            return Task.FromResult(Err("InvalidParams", 1000, "PlayFabId required"));
        if (body.ItemIds == null || body.ItemIds.Count == 0)
            return Task.FromResult(Err("InvalidParams", 1000, "ItemIds (non-empty) required"));

        var playerColl = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var pid = body.PlayFabId;
        if (playerColl.FindOne(p => p.PlayFabId == pid) == null)
            return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", HttpStatusCode.NotFound));

        var catalogColl   = Database.GetCollection<PlayfabCatalogItemDbObject>(true)!;
        var inventoryColl = Database.GetCollection<PlayfabInventoryItemDbObject>(true)!;
        var version       = body.CatalogVersion ?? "";
        var annotation    = body.Annotation ?? "";

        var granted = new List<object>();
        var skipped = new List<string>();
        foreach (var rawId in body.ItemIds)
        {
            if (string.IsNullOrEmpty(rawId)) continue;
            var iid = rawId;
            var ci = string.IsNullOrEmpty(version)
                ? catalogColl.FindOne(c => c.ItemId == iid)
                : catalogColl.FindOne(c => c.ItemId == iid && c.CatalogVersion == version);
            if (ci == null) { skipped.Add(iid); continue; }

            var inst = new PlayfabInventoryItemDbObject
            {
                PlayFabId      = pid,
                CharacterId    = "",
                ItemInstanceId = NewId(),
                ItemId         = ci.ItemId,
                ItemClass      = ci.ItemClass,
                CatalogVersion = ci.CatalogVersion,
                DisplayName    = ci.DisplayName,
                Annotation     = annotation,
                PurchaseDate   = NowUnix(),
            };
            inventoryColl.Insert(inst);
            granted.Add(new { ItemId = inst.ItemId, ItemInstanceId = inst.ItemInstanceId, DisplayName = inst.DisplayName, CatalogVersion = inst.CatalogVersion });
        }
        return Task.FromResult(Ok(new { Granted = granted, Skipped = skipped }));
    }

    /// <summary>Revokes a single inventory item instance from a player. Body: { PlayFabId, ItemInstanceId }
    /// — or, if no ItemInstanceId is supplied, all instances of the named ItemId for that player.</summary>
    [HttpPost("/Admin/RevokeInventoryItem")]
    public Task<IHttpActionResult> Admin_RevokeInventoryItem(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        string pid = "", instanceId = "", itemId = "";
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            if (doc.RootElement.TryGetProperty("PlayFabId", out var p)      && p.ValueKind == JsonValueKind.String) pid        = p.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("ItemInstanceId", out var i) && i.ValueKind == JsonValueKind.String) instanceId = i.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("ItemId", out var iid)       && iid.ValueKind == JsonValueKind.String) itemId   = iid.GetString() ?? "";
        }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Err("InvalidParams", 1000, "PlayFabId required"));
        if (string.IsNullOrEmpty(instanceId) && string.IsNullOrEmpty(itemId))
            return Task.FromResult(Err("InvalidParams", 1000, "ItemInstanceId or ItemId required"));

        var coll = Database.GetCollection<PlayfabInventoryItemDbObject>(true)!;
        int n;
        if (!string.IsNullOrEmpty(instanceId))
        {
            var inst = instanceId;
            n = coll.DeleteMany(x => x.PlayFabId == pid && x.ItemInstanceId == inst);
        }
        else
        {
            var iidLocal = itemId;
            n = coll.DeleteMany(x => x.PlayFabId == pid && x.ItemId == iidLocal);
        }
        return Task.FromResult(Ok(new { Revoked = n, PlayFabId = pid }));
    }

    /* ─── Admin: player virtual currency balances ─────────────────────────── */

    private sealed class CurrencyBody
    {
        public string PlayFabId       { get; set; }
        public string VirtualCurrency { get; set; }
        public int    Amount          { get; set; }
    }

    private (int balance, int change) ApplyCurrencyDelta(string playFabId, string code, int delta)
    {
        var coll = Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!;
        var cc = code;
        var existing = coll.FindOne(v => v.PlayFabId == playFabId && string.IsNullOrEmpty(v.CharacterId) && v.CurrencyCode == cc);
        if (existing == null)
        {
            existing = new PlayfabVirtualCurrencyDbObject { PlayFabId = playFabId, CurrencyCode = code, Balance = Math.Max(0, delta) };
            coll.Insert(existing);
            return (existing.Balance, existing.Balance);
        }
        var newBalance = Math.Max(0, existing.Balance + delta);
        var realChange = newBalance - existing.Balance;
        existing.Balance = newBalance;
        coll.Update(existing);
        return (newBalance, realChange);
    }

    /// <summary>Adds the given amount of a virtual currency to the player's balance.</summary>
    [HttpPost("/Admin/AddUserVirtualCurrency")]
    public Task<IHttpActionResult> Admin_AddUserVirtualCurrency(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        CurrencyBody body;
        try { body = JsonSerializer.Deserialize<CurrencyBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (body == null || string.IsNullOrEmpty(body.PlayFabId) || string.IsNullOrEmpty(body.VirtualCurrency))
            return Task.FromResult(Err("InvalidParams", 1000, "PlayFabId and VirtualCurrency required"));
        var (balance, change) = ApplyCurrencyDelta(body.PlayFabId, body.VirtualCurrency, body.Amount);
        return Task.FromResult(Ok(new { PlayFabId = body.PlayFabId, VirtualCurrency = body.VirtualCurrency, Balance = balance, BalanceChange = change }));
    }

    /// <summary>Subtracts the given amount of a virtual currency from the player's balance (clamped to 0).</summary>
    [HttpPost("/Admin/SubtractUserVirtualCurrency")]
    public Task<IHttpActionResult> Admin_SubtractUserVirtualCurrency(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        CurrencyBody body;
        try { body = JsonSerializer.Deserialize<CurrencyBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (body == null || string.IsNullOrEmpty(body.PlayFabId) || string.IsNullOrEmpty(body.VirtualCurrency))
            return Task.FromResult(Err("InvalidParams", 1000, "PlayFabId and VirtualCurrency required"));
        var (balance, change) = ApplyCurrencyDelta(body.PlayFabId, body.VirtualCurrency, -body.Amount);
        return Task.FromResult(Ok(new { PlayFabId = body.PlayFabId, VirtualCurrency = body.VirtualCurrency, Balance = balance, BalanceChange = change }));
    }

    /// <summary>Sets the player's balance for a virtual currency to an exact value (admin convenience —
    /// not a real PlayFab API but useful for dashboards). Negative values are clamped to 0.</summary>
    [HttpPost("/Admin/SetUserVirtualCurrency")]
    public Task<IHttpActionResult> Admin_SetUserVirtualCurrency(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        CurrencyBody body;
        try { body = JsonSerializer.Deserialize<CurrencyBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (body == null || string.IsNullOrEmpty(body.PlayFabId) || string.IsNullOrEmpty(body.VirtualCurrency))
            return Task.FromResult(Err("InvalidParams", 1000, "PlayFabId and VirtualCurrency required"));

        var coll = Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!;
        var pid = body.PlayFabId;
        var code = body.VirtualCurrency;
        var target = Math.Max(0, body.Amount);
        var existing = coll.FindOne(v => v.PlayFabId == pid && string.IsNullOrEmpty(v.CharacterId) && v.CurrencyCode == code);
        if (existing == null)
            coll.Insert(new PlayfabVirtualCurrencyDbObject { PlayFabId = pid, CurrencyCode = code, Balance = target });
        else { existing.Balance = target; coll.Update(existing); }
        return Task.FromResult(Ok(new { PlayFabId = pid, VirtualCurrency = code, Balance = target }));
    }

    /* ─── Admin: shared groups ────────────────────────────────────────────── */

    /// <summary>Paged list of every shared group, optionally filtered by group-id substring.</summary>
    [HttpPost("/Admin/ListSharedGroups")]
    public Task<IHttpActionResult> Admin_ListSharedGroups(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var (skip, take, query) = ReadPage(request, request.Body);
        var rows = Database.GetCollection<PlayfabSharedGroupDbObject>(true)!.FindAll().ToList();
        if (!string.IsNullOrEmpty(query))
        {
            var q = query.ToLowerInvariant();
            rows = rows.Where(g => (g.SharedGroupId ?? "").ToLowerInvariant().Contains(q)).ToList();
        }
        var total = rows.Count;
        var page = rows.OrderBy(g => g.SharedGroupId).Skip(skip).Take(take).Select(g => new
        {
            g.SharedGroupId,
            MemberCount = g.Members?.Count ?? 0,
            KeyCount    = g.Data?.Count ?? 0,
            CreatedUnix = g.Created,
        }).ToList();
        return Task.FromResult(Ok(new { Total = total, Groups = page, Skip = skip, Take = take }));
    }

    /// <summary>Returns the full data + member list for a single shared group.</summary>
    [HttpPost("/Admin/GetSharedGroup")]
    public Task<IHttpActionResult> Admin_GetSharedGroup(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        string gid;
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            gid = doc.RootElement.TryGetProperty("SharedGroupId", out var x) ? x.GetString() ?? "" : "";
        }
        catch { gid = request.GetQueryParameter("SharedGroupId") ?? ""; }
        if (string.IsNullOrEmpty(gid)) return Task.FromResult(Err("InvalidParams", 1000, "SharedGroupId required"));

        var coll = Database.GetCollection<PlayfabSharedGroupDbObject>(true)!;
        var g = coll.FindOne(x => x.SharedGroupId == gid);
        if (g == null) return Task.FromResult(Err("SharedGroupNotFound", 1052, "Shared group not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new
        {
            g.SharedGroupId,
            CreatedUnix = g.Created,
            Members     = (g.Members ?? new List<string>()).ToArray(),
            // Defensive: a null PlayfabSharedGroupValue (possible from older BSON-roundtripped
            // records where individual fields weren't present) gets normalized to empty defaults.
            Data        = (g.Data ?? new()).ToDictionary(kv => kv.Key, kv => new
            {
                Value         = kv.Value?.Value         ?? "",
                Permission    = kv.Value?.Permission    ?? "Private",
                LastUpdatedBy = kv.Value?.LastUpdatedBy ?? "",
                LastUpdated   = kv.Value?.LastUpdated   ?? 0L,
            }),
        }));
    }

    private sealed class SharedGroupKeyBody
    {
        public string SharedGroupId { get; set; }
        public string Key           { get; set; }
        public string Value         { get; set; }
        public string Permission    { get; set; } = "Private";
    }

    /// <summary>Sets a key on a shared group's data (admin convenience). Creates the group if absent.</summary>
    [HttpPost("/Admin/SetSharedGroupKey")]
    public Task<IHttpActionResult> Admin_SetSharedGroupKey(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        SharedGroupKeyBody body;
        try { body = JsonSerializer.Deserialize<SharedGroupKeyBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (body == null || string.IsNullOrEmpty(body.SharedGroupId) || string.IsNullOrEmpty(body.Key))
            return Task.FromResult(Err("InvalidParams", 1000, "SharedGroupId and Key required"));

        var coll = Database.GetCollection<PlayfabSharedGroupDbObject>(true)!;
        var gid = body.SharedGroupId;
        var g = coll.FindOne(x => x.SharedGroupId == gid);
        if (g == null)
        {
            g = new PlayfabSharedGroupDbObject { SharedGroupId = body.SharedGroupId, Created = NowUnix() };
            g.Data[body.Key] = new PlayfabSharedGroupValue { Value = body.Value ?? "", Permission = body.Permission ?? "Private", LastUpdated = NowUnix() };
            coll.Insert(g);
        }
        else
        {
            g.Data ??= new();
            g.Data[body.Key] = new PlayfabSharedGroupValue { Value = body.Value ?? "", Permission = body.Permission ?? "Private", LastUpdated = NowUnix() };
            coll.Update(g);
        }
        return Task.FromResult(Ok(new { Updated = true, SharedGroupId = body.SharedGroupId, Key = body.Key }));
    }

    /// <summary>Removes a key from a shared group's data.</summary>
    [HttpPost("/Admin/DeleteSharedGroupKey")]
    public Task<IHttpActionResult> Admin_DeleteSharedGroupKey(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        string gid = "", key = "";
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            if (doc.RootElement.TryGetProperty("SharedGroupId", out var s) && s.ValueKind == JsonValueKind.String) gid = s.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("Key", out var k)           && k.ValueKind == JsonValueKind.String) key = k.GetString() ?? "";
        }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (string.IsNullOrEmpty(gid) || string.IsNullOrEmpty(key))
            return Task.FromResult(Err("InvalidParams", 1000, "SharedGroupId and Key required"));

        var coll = Database.GetCollection<PlayfabSharedGroupDbObject>(true)!;
        var g = coll.FindOne(x => x.SharedGroupId == gid);
        if (g?.Data == null || !g.Data.Remove(key)) return Task.FromResult(Ok(new { Removed = false }));
        coll.Update(g);
        return Task.FromResult(Ok(new { Removed = true }));
    }

    /// <summary>Deletes an entire shared group.</summary>
    [HttpPost("/Admin/DeleteSharedGroup")]
    public Task<IHttpActionResult> Admin_DeleteSharedGroup(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        string gid;
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            gid = doc.RootElement.TryGetProperty("SharedGroupId", out var x) ? x.GetString() ?? "" : "";
        }
        catch { gid = ""; }
        if (string.IsNullOrEmpty(gid)) return Task.FromResult(Err("InvalidParams", 1000, "SharedGroupId required"));
        var n = Database.GetCollection<PlayfabSharedGroupDbObject>(true)!.DeleteMany(g => g.SharedGroupId == gid);
        return Task.FromResult(Ok(new { Deleted = n }));
    }

    /// <summary>Wipes every persisted record for the title (developer-only kill switch). Use with care.</summary>
    [HttpPost("/Admin/ResetTitle")]
    public Task<IHttpActionResult> Admin_ResetTitle(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var confirm = request.GetHeaderValue("X-Confirm-Title-Reset");
        if (confirm != TITLE_ID)
            return Task.FromResult(Err("Forbidden", 1006, $"Set header 'X-Confirm-Title-Reset: {TITLE_ID}' to confirm.", HttpStatusCode.Forbidden));

        // Drop everything title-scoped
        Database.DropCollection<PlayfabPlayerDbObject>();
        Database.DropCollection<PlayfabUserDataDbObject>();
        Database.DropCollection<PlayfabTitleDataDbObject>();
        Database.DropCollection<PlayfabFriendDbObject>();
        Database.DropCollection<PlayfabStatisticDbObject>();
        Database.DropCollection<PlayfabStatisticDefinitionDbObject>();
        Database.DropCollection<PlayfabInventoryItemDbObject>();
        Database.DropCollection<PlayfabVirtualCurrencyDbObject>();
        Database.DropCollection<PlayfabVirtualCurrencyDefDbObject>();
        Database.DropCollection<PlayfabCatalogItemDbObject>();
        Database.DropCollection<PlayfabCloudScriptFileDbObject>();
        Database.DropCollection<PlayfabCloudScriptFunctionDbObject>();
        Database.DropCollection<PlayfabTitleNewsDbObject>();
        Database.DropCollection<PlayfabEventDbObject>();
        Database.DropCollection<PlayfabSessionDbObject>();
        return Task.FromResult(Ok(new { Reset = TITLE_ID, ResetAt = NowUnix() }));
    }
}
