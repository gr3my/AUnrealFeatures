using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Playfab.Models;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    /* ─── Admin paged queries used by the dashboard ──────────────────────── */

    private sealed class PageBody
    {
        public int? Skip   { get; set; }
        public int? Take   { get; set; }
        public string Query { get; set; }
    }

    private static (int skip, int take, string query) ReadPage(IHttpRequest req, byte[] body)
    {
        try
        {
            var p = JsonSerializer.Deserialize<PageBody>(body) ?? new();
            return (Math.Max(0, p.Skip ?? 0), Math.Clamp(p.Take ?? 50, 1, 500), p.Query ?? "");
        }
        catch
        {
            int.TryParse(req.GetQueryParameter("skip") ?? "0", out var s);
            int.TryParse(req.GetQueryParameter("take") ?? "50", out var t);
            return (Math.Max(0, s), Math.Clamp(t == 0 ? 50 : t, 1, 500), req.GetQueryParameter("query") ?? "");
        }
    }

    /// <summary>Returns a paged list of players, optionally filtered by display-name/username/email substring.</summary>
    [HttpPost("/Admin/ListPlayers")]
    public Task<IHttpActionResult> Admin_ListPlayers(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var (skip, take, query) = ReadPage(request, request.Body);
        var rows = Database.GetCollection<PlayfabPlayerDbObject>(true)!.FindAll().ToList();
        if (!string.IsNullOrEmpty(query))
        {
            var q = query.ToLowerInvariant();
            rows = rows.Where(p =>
                (p.PlayFabId    ?? "").ToLowerInvariant().Contains(q) ||
                (p.Username     ?? "").ToLowerInvariant().Contains(q) ||
                (p.DisplayName  ?? "").ToLowerInvariant().Contains(q) ||
                (p.Email        ?? "").ToLowerInvariant().Contains(q) ||
                (p.CustomId     ?? "").ToLowerInvariant().Contains(q)).ToList();
        }
        var total = rows.Count;
        var page  = rows.OrderByDescending(p => p.LastLogin).Skip(skip).Take(take).Select(p => new
        {
            p.PlayFabId, p.EntityId, p.Username, p.DisplayName, p.Email,
            p.CustomId, p.OriginPlatform, p.IsBanned,
            CreatedUnix   = p.Created,
            LastLoginUnix = p.LastLogin,
            Tags          = p.Tags ?? new List<string>(),
        }).ToList();
        return Task.FromResult(Ok(new { Total = total, Players = page, Skip = skip, Take = take }));
    }

    /// <summary>Returns full details for a single player including linked accounts, currencies, stats, inventory, friends.</summary>
    [HttpPost("/Admin/GetPlayer")]
    public Task<IHttpActionResult> Admin_GetPlayer(IHttpRequest request, IHttpResponse response)
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

        var p = Database.GetCollection<PlayfabPlayerDbObject>(true)!.FindOne(x => x.PlayFabId == pid);
        if (p == null) return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", System.Net.HttpStatusCode.NotFound));

        var stats     = Database.GetCollection<PlayfabStatisticDbObject>(true)!.Find(s => s.PlayFabId == pid && string.IsNullOrEmpty(s.CharacterId)).ToList();
        var vc        = Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!.Find(v => v.PlayFabId == pid && string.IsNullOrEmpty(v.CharacterId)).ToList();
        var inventory = Database.GetCollection<PlayfabInventoryItemDbObject>(true)!.Find(i => i.PlayFabId == pid && string.IsNullOrEmpty(i.CharacterId)).ToList();
        var friends   = Database.GetCollection<PlayfabFriendDbObject>(true)!.Find(f => f.OwnerPlayFabId == pid).ToList();
        var data      = Database.GetCollection<PlayfabUserDataDbObject>(true)!.Find(d => d.PlayFabId == pid).ToList();
        var tags      = Database.GetCollection<PlayfabPlayerTagDbObject>(true)!.Find(t => t.PlayFabId == pid).ToList();

        return Task.FromResult(Ok(new
        {
            Player = new
            {
                p.PlayFabId, p.EntityId, p.Username, p.DisplayName, p.Email, p.ContactEmail,
                p.CustomId, p.OriginPlatform, p.AvatarUrl, p.IsBanned,
                Linked = new
                {
                    p.SteamId, p.XboxId, p.PsnId, p.NintendoId, p.FacebookId, p.GoogleId, p.GoogleAccountId,
                    p.GameCenterId, p.AndroidDeviceId, p.IosDeviceId, p.TwitchId, p.KongregateId, p.OpenIdSubject,
                },
                CreatedUnix   = p.Created,
                LastLoginUnix = p.LastLogin,
                Tags          = p.Tags ?? new(),
            },
            Statistics      = stats.Select(s => new { s.StatisticName, s.Value, s.Version }).ToList(),
            VirtualCurrency = vc.ToDictionary(v => v.CurrencyCode, v => v.Balance),
            Inventory       = inventory.Select(i => new { i.ItemId, i.ItemInstanceId, i.DisplayName, i.RemainingUses, i.PurchaseDate }).ToList(),
            Friends         = friends.Select(f => new { f.FriendPlayFabId, f.Username, f.TitleDisplayName, f.Tags, f.Created }).ToList(),
            UserData        = data.Select(d => new { d.Scope, d.Key, d.Value, d.Permission, d.LastUpdated }).ToList(),
            ServiceTags     = tags.Select(t => new { t.Namespace, t.Tag }).ToList(),
        }));
    }

    /// <summary>Generic paginated list-any-collection helper. The dashboard's secondary nav pages
    /// (Add-ons, Audit, Content, Experiments, Segments, Scheduled Tasks, Rewarded Ads, Telemetry
    /// Keys, etc.) all use this so we don't need a bespoke endpoint per collection. The Name field
    /// in the body picks which DB collection to dump.</summary>
    [HttpPost("/Admin/ListCollection")]
    public Task<IHttpActionResult> Admin_ListCollection(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        string name = "";
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            if (doc.RootElement.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String)
                name = n.GetString() ?? "";
        }
        catch { name = request.GetQueryParameter("Name") ?? ""; }

        var (skip, take, query) = ReadPage(request, request.Body);

        IEnumerable<object> rows = name switch
        {
            "addons"             => Database.GetCollection<PlayfabAddonConfigDbObject>(true)!.FindAll().Cast<object>(),
            "experiments"        => Database.GetCollection<PlayfabExperimentDbObject>(true)!.FindAll().Cast<object>(),
            "experiment-treatments" => Database.GetCollection<PlayfabExperimentTreatmentDbObject>(true)!.FindAll().Cast<object>(),
            "segments"           => Database.GetCollection<PlayfabSegmentDbObject>(true)!.FindAll().Cast<object>(),
            "ad-placements"      => Database.GetCollection<PlayfabAdPlacementDbObject>(true)!.FindAll().Cast<object>(),
            "telemetry-keys"     => Database.GetCollection<PlayfabTelemetryKeyDbObject>(true)!.FindAll().Cast<object>(),
            "data-connections"   => Database.GetCollection<PlayfabDataConnectionDbObject>(true)!.FindAll().Cast<object>(),
            "content"            => Database.GetCollection<PlayfabContentDbObject>(true)!.FindAll().Cast<object>(),
            "files"              => Database.GetCollection<PlayfabFileDbObject>(true)!.FindAll().Cast<object>(),
            "objects"            => Database.GetCollection<PlayfabObjectDbObject>(true)!.FindAll().Cast<object>(),
            "policies"           => Database.GetCollection<PlayfabPolicyDbObject>(true)!.FindAll().Cast<object>(),
            "exclusion-groups"   => Database.GetCollection<PlayfabExclusionGroupDbObject>(true)!.FindAll().Cast<object>(),
            "stat-defs-v2"       => Database.GetCollection<PlayfabStatisticDefinitionV2DbObject>(true)!.FindAll().Cast<object>(),
            "stat-defs-v1"       => Database.GetCollection<PlayfabStatisticDefinitionDbObject>(true)!.FindAll().Cast<object>(),
            "leaderboard-defs"   => Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!.FindAll().Cast<object>(),
            "leaderboard-entries" => Database.GetCollection<PlayfabLeaderboardEntryDbObject>(true)!.FindAll().Cast<object>(),
            "stats-v1"           => Database.GetCollection<PlayfabStatisticDbObject>(true)!.FindAll().Cast<object>(),
            "stats-v2"           => Database.GetCollection<PlayfabEntityStatisticDbObject>(true)!.FindAll().Cast<object>(),
            "matchmaking-queues" => Database.GetCollection<PlayfabMatchmakingQueueDbObject>(true)!.FindAll().Cast<object>(),
            "matchmaking-tickets" => Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!.FindAll().Cast<object>(),
            "matches"            => Database.GetCollection<PlayfabMatchDbObject>(true)!.FindAll().Cast<object>(),
            "lobbies"            => Database.GetCollection<PlayfabLobbyDbObject>(true)!.FindAll().Cast<object>(),
            "parties"            => Database.GetCollection<PlayfabPartyDbObject>(true)!.FindAll().Cast<object>(),
            "builds"             => Database.GetCollection<PlayfabBuildDbObject>(true)!.FindAll().Cast<object>(),
            "build-aliases"      => Database.GetCollection<PlayfabBuildAliasDbObject>(true)!.FindAll().Cast<object>(),
            "servers"            => Database.GetCollection<PlayfabServerDbObject>(true)!.FindAll().Cast<object>(),
            "assets"             => Database.GetCollection<PlayfabAssetDbObject>(true)!.FindAll().Cast<object>(),
            "certificates"       => Database.GetCollection<PlayfabCertificateDbObject>(true)!.FindAll().Cast<object>(),
            "secrets"            => Database.GetCollection<PlayfabSecretDbObject>(true)!.FindAll().Cast<object>(),
            "cloudscript-files"  => Database.GetCollection<PlayfabCloudScriptFileDbObject>(true)!.FindAll().Cast<object>(),
            "cloudscript-functions" => Database.GetCollection<PlayfabCloudScriptFunctionDbObject>(true)!.FindAll().Cast<object>(),
            "cloudscript-logs"   => Database.GetCollection<PlayfabCloudScriptExecutionLogDbObject>(true)!.FindAll().Cast<object>(),
            "events"             => Database.GetCollection<PlayfabEventDbObject>(true)!.FindAll().Cast<object>(),
            "transactions"       => Database.GetCollection<PlayfabTransactionDbObject>(true)!.FindAll().Cast<object>(),
            "currencies"         => Database.GetCollection<PlayfabVirtualCurrencyDefDbObject>(true)!.FindAll().Cast<object>(),
            "catalog-config"     => Database.GetCollection<PlayfabCatalogConfigDbObject>(true)!.FindAll().Cast<object>(),
            "title-data"         => Database.GetCollection<PlayfabTitleDataDbObject>(true)!.FindAll().Cast<object>(),
            "user-data"          => Database.GetCollection<PlayfabUserDataDbObject>(true)!.FindAll().Cast<object>(),
            "shared-groups"      => Database.GetCollection<PlayfabSharedGroupDbObject>(true)!.FindAll().Cast<object>(),
            "groups"             => Database.GetCollection<PlayfabGroupDbObject>(true)!.FindAll().Cast<object>(),
            "group-memberships"  => Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!.FindAll().Cast<object>(),
            "characters"         => Database.GetCollection<PlayfabCharacterDbObject>(true)!.FindAll().Cast<object>(),
            "trades"             => Database.GetCollection<PlayfabTradeDbObject>(true)!.FindAll().Cast<object>(),
            "purchases"          => Database.GetCollection<PlayfabPurchaseDbObject>(true)!.FindAll().Cast<object>(),
            "pending-purchases"  => Database.GetCollection<PlayfabPendingPurchaseDbObject>(true)!.FindAll().Cast<object>(),
            "push-tokens"        => Database.GetCollection<PlayfabPushTokenDbObject>(true)!.FindAll().Cast<object>(),
            "contact-emails"     => Database.GetCollection<PlayfabContactEmailDbObject>(true)!.FindAll().Cast<object>(),
            "title-news"         => Database.GetCollection<PlayfabTitleNewsDbObject>(true)!.FindAll().Cast<object>(),
            _                    => Enumerable.Empty<object>(),
        };

        var list = rows.ToList();
        var total = list.Count;
        var page = list.Skip(skip).Take(take).ToList();
        return Task.FromResult(Ok(new { Total = total, Rows = page, Skip = skip, Take = take, Name = name }));
    }

    /// <summary>Generic upsert for any of the registered collections. The dashboard uses this to
    /// edit/create rows without a per-collection endpoint. Body: { Name, Row, KeyField? }. The
    /// KeyField (defaults to "Id") is used to locate an existing row by exact match — found rows
    /// are replaced (preserving the LiteDB Id), missing rows are inserted.</summary>
    [HttpPost("/Admin/UpsertCollection")]
    public Task<IHttpActionResult> Admin_UpsertCollection(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        string name = "", keyField = "";
        JsonElement rowEl;
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            name     = doc.RootElement.TryGetProperty("Name",     out var n) ? n.GetString() ?? "" : "";
            keyField = doc.RootElement.TryGetProperty("KeyField", out var k) ? k.GetString() ?? "" : "";
            if (!doc.RootElement.TryGetProperty("Row", out var r) || r.ValueKind != JsonValueKind.Object)
                return Task.FromResult(Err("InvalidParams", 1000, "Row object required"));
            rowEl = r.Clone();
        }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body invalid: {e.Message}")); }
        if (string.IsNullOrEmpty(name)) return Task.FromResult(Err("InvalidParams", 1000, "Name required"));

        var json = rowEl.GetRawText();
        var (inserted, updated) = name switch
        {
            "addons"             => Upsert<PlayfabAddonConfigDbObject>(json, keyField, "AddonName"),
            "experiments"        => Upsert<PlayfabExperimentDbObject>(json, keyField, "ExperimentId"),
            "segments"           => Upsert<PlayfabSegmentDbObject>(json, keyField, "SegmentId"),
            "ad-placements"      => Upsert<PlayfabAdPlacementDbObject>(json, keyField, "PlacementId"),
            "telemetry-keys"     => Upsert<PlayfabTelemetryKeyDbObject>(json, keyField, "Name"),
            "data-connections"   => Upsert<PlayfabDataConnectionDbObject>(json, keyField, "Name"),
            "content"            => Upsert<PlayfabContentDbObject>(json, keyField, "Key"),
            "policies"           => Upsert<PlayfabPolicyDbObject>(json, keyField, "EntityId"),
            "exclusion-groups"   => Upsert<PlayfabExclusionGroupDbObject>(json, keyField, "ExclusionGroupId"),
            "stat-defs-v2"       => Upsert<PlayfabStatisticDefinitionV2DbObject>(json, keyField, "Name"),
            "stat-defs-v1"       => Upsert<PlayfabStatisticDefinitionDbObject>(json, keyField, "StatisticName"),
            "leaderboard-defs"   => Upsert<PlayfabLeaderboardDefinitionDbObject>(json, keyField, "Name"),
            "matchmaking-queues" => Upsert<PlayfabMatchmakingQueueDbObject>(json, keyField, "QueueName"),
            "currencies"         => Upsert<PlayfabVirtualCurrencyDefDbObject>(json, keyField, "CurrencyCode"),
            "catalog-config"     => Upsert<PlayfabCatalogConfigDbObject>(json, keyField, "TitleId"),
            "title-data"         => Upsert<PlayfabTitleDataDbObject>(json, keyField, "Key"),
            "title-news"         => Upsert<PlayfabTitleNewsDbObject>(json, keyField, "NewsId"),
            "shared-groups"      => Upsert<PlayfabSharedGroupDbObject>(json, keyField, "SharedGroupId"),
            "groups"             => Upsert<PlayfabGroupDbObject>(json, keyField, "GroupId"),
            "characters"         => Upsert<PlayfabCharacterDbObject>(json, keyField, "CharacterId"),
            "cloudscript-files"  => Upsert<PlayfabCloudScriptFileDbObject>(json, keyField, "FileName"),
            "cloudscript-functions" => Upsert<PlayfabCloudScriptFunctionDbObject>(json, keyField, "FunctionName"),
            _                    => (0, 0),
        };
        return Task.FromResult(Ok(new { Inserted = inserted, Updated = updated, Name = name }));
    }

    /// <summary>Generic delete for any registered collection. Body: { Name, KeyField, KeyValue }.</summary>
    [HttpPost("/Admin/DeleteCollectionRow")]
    public Task<IHttpActionResult> Admin_DeleteCollectionRow(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        string name = "", keyField = "", keyValue = "";
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            name     = doc.RootElement.TryGetProperty("Name",     out var n) ? n.GetString() ?? "" : "";
            keyField = doc.RootElement.TryGetProperty("KeyField", out var k) ? k.GetString() ?? "" : "";
            keyValue = doc.RootElement.TryGetProperty("KeyValue", out var v) ? v.GetString() ?? "" : "";
        }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body invalid: {e.Message}")); }
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(keyField) || string.IsNullOrEmpty(keyValue))
            return Task.FromResult(Err("InvalidParams", 1000, "Name, KeyField and KeyValue required"));

        var n2 = DeleteByField(name, keyField, keyValue);
        return Task.FromResult(Ok(new { Deleted = n2, Name = name }));
    }

    /// <summary>Reflective upsert helper. Reads <paramref name="json"/> as <typeparamref name="T"/>,
    /// looks up an existing row matching the named property, and replaces or inserts.</summary>
    private (int inserted, int updated) Upsert<T>(string json, string keyFieldOverride, string defaultKey) where T : AUnrealFeatures.Hosting.Database.AstraDbObject, new()
    {
        var row = JsonSerializer.Deserialize<T>(json, JsonOpts);
        if (row == null) return (0, 0);
        var key = string.IsNullOrEmpty(keyFieldOverride) ? defaultKey : keyFieldOverride;
        var prop = typeof(T).GetProperty(key);
        if (prop == null) return (0, 0);
        var keyVal = prop.GetValue(row)?.ToString() ?? "";
        var coll = Database.GetCollection<T>(true)!;
        // Find the existing row by reading back every record and checking the key field — cheaper
        // than building a dynamic LINQ expression and these collections are small (<100K rows).
        var existing = coll.FindAll().FirstOrDefault(x => string.Equals(prop.GetValue(x)?.ToString() ?? "", keyVal, StringComparison.Ordinal));
        if (existing == null) { coll.Insert(row); return (1, 0); }
        // Carry the existing LiteDB Id over so the update lands on the same row
        row.Id = existing.Id;
        coll.Update(row);
        return (0, 1);
    }

    private int DeleteByField(string name, string keyField, string keyValue) => name switch
    {
        "addons"             => DeleteWhere<PlayfabAddonConfigDbObject>(keyField, keyValue),
        "experiments"        => DeleteWhere<PlayfabExperimentDbObject>(keyField, keyValue),
        "segments"           => DeleteWhere<PlayfabSegmentDbObject>(keyField, keyValue),
        "ad-placements"      => DeleteWhere<PlayfabAdPlacementDbObject>(keyField, keyValue),
        "telemetry-keys"     => DeleteWhere<PlayfabTelemetryKeyDbObject>(keyField, keyValue),
        "data-connections"   => DeleteWhere<PlayfabDataConnectionDbObject>(keyField, keyValue),
        "content"            => DeleteWhere<PlayfabContentDbObject>(keyField, keyValue),
        "policies"           => DeleteWhere<PlayfabPolicyDbObject>(keyField, keyValue),
        "exclusion-groups"   => DeleteWhere<PlayfabExclusionGroupDbObject>(keyField, keyValue),
        "stat-defs-v2"       => DeleteWhere<PlayfabStatisticDefinitionV2DbObject>(keyField, keyValue),
        "stat-defs-v1"       => DeleteWhere<PlayfabStatisticDefinitionDbObject>(keyField, keyValue),
        "leaderboard-defs"   => DeleteWhere<PlayfabLeaderboardDefinitionDbObject>(keyField, keyValue),
        "leaderboard-entries" => DeleteWhere<PlayfabLeaderboardEntryDbObject>(keyField, keyValue),
        "stats-v1"           => DeleteWhere<PlayfabStatisticDbObject>(keyField, keyValue),
        "stats-v2"           => DeleteWhere<PlayfabEntityStatisticDbObject>(keyField, keyValue),
        "matchmaking-queues" => DeleteWhere<PlayfabMatchmakingQueueDbObject>(keyField, keyValue),
        "matchmaking-tickets" => DeleteWhere<PlayfabMatchmakingTicketDbObject>(keyField, keyValue),
        "matches"            => DeleteWhere<PlayfabMatchDbObject>(keyField, keyValue),
        "lobbies"            => DeleteWhere<PlayfabLobbyDbObject>(keyField, keyValue),
        "parties"            => DeleteWhere<PlayfabPartyDbObject>(keyField, keyValue),
        "builds"             => DeleteWhere<PlayfabBuildDbObject>(keyField, keyValue),
        "build-aliases"      => DeleteWhere<PlayfabBuildAliasDbObject>(keyField, keyValue),
        "servers"            => DeleteWhere<PlayfabServerDbObject>(keyField, keyValue),
        "files"              => DeleteWhere<PlayfabFileDbObject>(keyField, keyValue),
        "objects"            => DeleteWhere<PlayfabObjectDbObject>(keyField, keyValue),
        "currencies"         => DeleteWhere<PlayfabVirtualCurrencyDefDbObject>(keyField, keyValue),
        "catalog-config"     => DeleteWhere<PlayfabCatalogConfigDbObject>(keyField, keyValue),
        "title-data"         => DeleteWhere<PlayfabTitleDataDbObject>(keyField, keyValue),
        "title-news"         => DeleteWhere<PlayfabTitleNewsDbObject>(keyField, keyValue),
        "shared-groups"      => DeleteWhere<PlayfabSharedGroupDbObject>(keyField, keyValue),
        "groups"             => DeleteWhere<PlayfabGroupDbObject>(keyField, keyValue),
        "group-memberships"  => DeleteWhere<PlayfabGroupMembershipDbObject>(keyField, keyValue),
        "characters"         => DeleteWhere<PlayfabCharacterDbObject>(keyField, keyValue),
        "cloudscript-files"  => DeleteWhere<PlayfabCloudScriptFileDbObject>(keyField, keyValue),
        "cloudscript-functions" => DeleteWhere<PlayfabCloudScriptFunctionDbObject>(keyField, keyValue),
        "events"             => DeleteWhere<PlayfabEventDbObject>(keyField, keyValue),
        "transactions"       => DeleteWhere<PlayfabTransactionDbObject>(keyField, keyValue),
        _                    => 0,
    };

    private int DeleteWhere<T>(string keyField, string keyValue) where T : AUnrealFeatures.Hosting.Database.AstraDbObject
    {
        var prop = typeof(T).GetProperty(keyField);
        if (prop == null) return 0;
        var coll = Database.GetCollection<T>(true)!;
        var matches = coll.FindAll().Where(x => string.Equals(prop.GetValue(x)?.ToString() ?? "", keyValue, StringComparison.Ordinal)).ToList();
        foreach (var m in matches) coll.Delete(m.Id);
        return matches.Count;
    }

    /// <summary>Returns the full set of secondary records for a player profile in a single call —
    /// events, logins, CloudScript executions, characters, V2 inventory, V2 transactions, entity
    /// objects/files, groups, segments. Used by the dashboard's per-player tab strip so each tab
    /// doesn't have to issue its own request.</summary>
    [HttpPost("/Admin/GetPlayerExtras")]
    public Task<IHttpActionResult> Admin_GetPlayerExtras(IHttpRequest request, IHttpResponse response)
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

        var player = Database.GetCollection<PlayfabPlayerDbObject>(true)!.FindOne(p => p.PlayFabId == pid);
        if (player == null) return Task.FromResult(Err("AccountNotFound", 1001, "Player not found", System.Net.HttpStatusCode.NotFound));

        var entityId = player.EntityId ?? "";

        var events = Database.GetCollection<PlayfabEventDbObject>(true)!
                             .Find(e => e.EntityId == pid)
                             .OrderByDescending(e => e.Timestamp).Take(200).ToList();
        var logins = events.Where(e =>
                          (e.EventName ?? "").IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0
                       || (e.EventName ?? "").IndexOf("logged",  StringComparison.OrdinalIgnoreCase) >= 0)
                          .Take(50).ToList();

        var csLogs = Database.GetCollection<PlayfabCloudScriptExecutionLogDbObject>(true)!
                             .Find(l => l.PlayFabId == pid)
                             .OrderByDescending(l => l.Timestamp).Take(50).ToList();

        var characters    = Database.GetCollection<PlayfabCharacterDbObject>(true)!.Find(c => c.PlayFabId == pid).ToList();
        var invV2         = Database.GetCollection<PlayfabEconomyInventoryDbObject>(true)!.Find(i => i.EntityId == entityId).ToList();
        var transactionsV2 = Database.GetCollection<PlayfabTransactionDbObject>(true)!.Find(t => t.EntityId == entityId)
                                .OrderByDescending(t => t.Timestamp).Take(100).ToList();
        var objects       = Database.GetCollection<PlayfabObjectDbObject>(true)!.Find(o => o.EntityId == entityId).ToList();
        var files         = Database.GetCollection<PlayfabFileDbObject>(true)!.Find(f => f.EntityId == entityId).ToList();
        var memberships   = Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!.Find(m => m.EntityId == entityId).ToList();
        var tags          = Database.GetCollection<PlayfabPlayerTagDbObject>(true)!.Find(t => t.PlayFabId == pid).ToList();
        var pushTokens    = Database.GetCollection<PlayfabPushTokenDbObject>(true)!.Find(p => p.PlayFabId == pid).ToList();
        var statsV2       = Database.GetCollection<PlayfabEntityStatisticDbObject>(true)!.Find(s => s.EntityId == entityId).ToList();

        return Task.FromResult(Ok(new
        {
            PlayFabId = pid,
            EntityId  = entityId,

            PlayStream = events.Select(e => new
            {
                e.EventId, e.EventName, e.EventNamespace, e.PayloadJson, e.Timestamp,
                e.EntityId, e.EntityType,
            }).ToArray(),

            Logins = logins.Select(e => new { e.EventName, e.Timestamp, e.PayloadJson }).ToArray(),

            CloudScript = csLogs.Select(l => new
            {
                l.ExecutionId, l.FunctionName, l.ResultJson, l.LogsJson,
                l.ExecutionTimeSeconds, l.Timestamp, l.Error,
            }).ToArray(),

            Characters = characters.Select(c => new
            {
                c.CharacterId, c.CharacterName, c.CharacterType, c.Created,
            }).ToArray(),

            InventoryV2 = invV2.Select(i => new
            {
                i.CollectionId, i.ItemId, i.StackId, i.Amount,
            }).ToArray(),

            TransactionsV2 = transactionsV2.Select(t => new
            {
                t.TransactionId, t.Operation, t.ItemId, t.Amount, t.Timestamp, t.Details,
            }).ToArray(),

            Objects = objects.Select(o => new { o.ObjectName, o.DataObjectJson, o.EscapedDataObject }).ToArray(),

            Files = files.Select(f => new
            {
                f.FileName, f.Size, f.LastModified, f.Etag, f.ProfileVersion,
            }).ToArray(),

            Groups = memberships.Select(m => new { m.GroupId, m.RoleId, m.Joined }).ToArray(),

            Segments = tags.Select(t => new { t.Namespace, t.Tag }).ToArray(),

            PushTokens = pushTokens.Select(p => new { p.Platform, p.DeviceToken }).ToArray(),

            StatisticsV2 = statsV2.Select(s => new { s.StatisticName, s.Scores, s.Version, s.LastUpdated, s.Metadata }).ToArray(),

            Bans = player.IsBanned
                ? new object[] { new { Active = true, Reason = "Banned via Admin API", Created = player.LastLogin } }
                : Array.Empty<object>(),
        }));
    }

    /// <summary>Returns recent CloudScript execution logs.</summary>
    [HttpPost("/Admin/GetCloudScriptLogs")]
    public Task<IHttpActionResult> Admin_GetCloudScriptLogs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var (skip, take, query) = ReadPage(request, request.Body);
        var rows = Database.GetCollection<PlayfabCloudScriptExecutionLogDbObject>(true)!
                           .FindAll()
                           .OrderByDescending(l => l.Timestamp).ToList();
        if (!string.IsNullOrEmpty(query))
        {
            var q = query.ToLowerInvariant();
            rows = rows.Where(l =>
                (l.FunctionName ?? "").ToLowerInvariant().Contains(q) ||
                (l.PlayFabId    ?? "").ToLowerInvariant().Contains(q)).ToList();
        }
        var total = rows.Count;
        var page  = rows.Skip(skip).Take(take).Select(l => new
        {
            l.ExecutionId, l.FunctionName, l.PlayFabId, l.Timestamp, l.ExecutionTimeSeconds,
            l.Error, ResultPreview = (l.ResultJson ?? "").Length > 200 ? (l.ResultJson ?? "").Substring(0, 200) + "…" : l.ResultJson,
            LogPreview    = (l.LogsJson ?? "").Length > 200 ? (l.LogsJson ?? "").Substring(0, 200) + "…" : l.LogsJson,
        }).ToList();
        return Task.FromResult(Ok(new { Total = total, Logs = page, Skip = skip, Take = take }));
    }

    /// <summary>Returns a single CloudScript execution log entry with full result/log JSON.</summary>
    [HttpPost("/Admin/GetCloudScriptLog")]
    public Task<IHttpActionResult> Admin_GetCloudScriptLog(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        string id;
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            id = doc.RootElement.TryGetProperty("ExecutionId", out var x) ? x.GetString() ?? "" : "";
        }
        catch { id = request.GetQueryParameter("ExecutionId") ?? ""; }
        if (string.IsNullOrEmpty(id)) return Task.FromResult(Err("InvalidParams", 1000, "ExecutionId required"));
        var log = Database.GetCollection<PlayfabCloudScriptExecutionLogDbObject>(true)!.FindOne(l => l.ExecutionId == id);
        if (log == null) return Task.FromResult(Err("NotFound", 1404, "Execution log not found", System.Net.HttpStatusCode.NotFound));
        return Task.FromResult(Ok(log));
    }

    /// <summary>Returns recent Photon webhook calls.</summary>
    [HttpPost("/Admin/GetPhotonWebhookLogs")]
    public Task<IHttpActionResult> Admin_GetPhotonWebhookLogs(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var (skip, take, query) = ReadPage(request, request.Body);
        var rows = Database.GetCollection<PlayfabPhotonWebhookLogDbObject>(true)!
                           .FindAll()
                           .OrderByDescending(l => l.Timestamp).ToList();
        if (!string.IsNullOrEmpty(query))
        {
            var q = query.ToLowerInvariant();
            rows = rows.Where(l =>
                (l.Handler ?? "").ToLowerInvariant().Contains(q) ||
                (l.GameId  ?? "").ToLowerInvariant().Contains(q) ||
                (l.PlayFabId ?? "").ToLowerInvariant().Contains(q)).ToList();
        }
        var total = rows.Count;
        var page  = rows.Skip(skip).Take(take).ToList();
        return Task.FromResult(Ok(new { Total = total, Logs = page, Skip = skip, Take = take }));
    }

    /// <summary>Returns persisted Photon room states.</summary>
    [HttpPost("/Admin/GetPhotonRoomStates")]
    public Task<IHttpActionResult> Admin_GetPhotonRoomStates(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var (skip, take, query) = ReadPage(request, request.Body);
        var rows = Database.GetCollection<PlayfabPhotonRoomStateDbObject>(true)!.FindAll().ToList();
        if (!string.IsNullOrEmpty(query))
        {
            var q = query.ToLowerInvariant();
            rows = rows.Where(r => (r.GameId ?? "").ToLowerInvariant().Contains(q)).ToList();
        }
        var total = rows.Count;
        var page  = rows.Skip(skip).Take(take).Select(r => new
        {
            r.AppId, r.AppVersion, r.Region, r.GameId, r.ActorCount, r.Saved,
            HasState = !string.IsNullOrEmpty(r.StateJson),
            StateLength = (r.StateJson ?? "").Length,
        }).ToList();
        return Task.FromResult(Ok(new { Total = total, Rooms = page, Skip = skip, Take = take }));
    }

    /// <summary>Returns recent analytics events.</summary>
    [HttpPost("/Admin/GetEvents")]
    public Task<IHttpActionResult> Admin_GetEvents(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var (skip, take, query) = ReadPage(request, request.Body);
        var rows = Database.GetCollection<PlayfabEventDbObject>(true)!
                           .FindAll()
                           .OrderByDescending(e => e.Timestamp).ToList();
        if (!string.IsNullOrEmpty(query))
        {
            var q = query.ToLowerInvariant();
            rows = rows.Where(e =>
                (e.EventName      ?? "").ToLowerInvariant().Contains(q) ||
                (e.EventNamespace ?? "").ToLowerInvariant().Contains(q) ||
                (e.EntityId       ?? "").ToLowerInvariant().Contains(q)).ToList();
        }
        var total = rows.Count;
        var page  = rows.Skip(skip).Take(take).ToList();
        return Task.FromResult(Ok(new { Total = total, Events = page, Skip = skip, Take = take }));
    }

    /// <summary>Returns title news items.</summary>
    [HttpPost("/Admin/GetTitleNews")]
    public Task<IHttpActionResult> Admin_GetTitleNewsAdmin(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabTitleNewsDbObject>(true)!
                           .Find(n => n.TitleId == TITLE_ID)
                           .OrderByDescending(n => n.Timestamp).ToList();
        return Task.FromResult(Ok(new { News = rows }));
    }

    /// <summary>Deletes a single news item.</summary>
    [HttpPost("/Admin/DeleteNews")]
    public Task<IHttpActionResult> Admin_DeleteNews(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        string newsId;
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            newsId = doc.RootElement.TryGetProperty("NewsId", out var x) ? x.GetString() ?? "" : "";
        }
        catch { newsId = ""; }
        if (string.IsNullOrEmpty(newsId)) return Task.FromResult(Err("InvalidParams", 1000, "NewsId required"));
        var n = Database.GetCollection<PlayfabTitleNewsDbObject>(true)!.DeleteMany(x => x.TitleId == TITLE_ID && x.NewsId == newsId);
        return Task.FromResult(Ok(new { Deleted = n }));
    }

    /// <summary>Returns groups (entity-based v2).</summary>
    [HttpPost("/Admin/ListGroups")]
    public Task<IHttpActionResult> Admin_ListGroups(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var (skip, take, query) = ReadPage(request, request.Body);
        var groups = Database.GetCollection<PlayfabGroupDbObject>(true)!.FindAll().ToList();
        var members = Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!.FindAll().ToList();
        if (!string.IsNullOrEmpty(query))
        {
            var q = query.ToLowerInvariant();
            groups = groups.Where(g => (g.GroupName ?? "").ToLowerInvariant().Contains(q) || (g.GroupId ?? "").ToLowerInvariant().Contains(q)).ToList();
        }
        var total = groups.Count;
        var page = groups.OrderByDescending(g => g.Created).Skip(skip).Take(take).Select(g => new
        {
            g.GroupId, g.GroupName, g.AdminRoleId, g.MemberRoleId, g.Created,
            MemberCount = members.Count(m => m.GroupId == g.GroupId),
        }).ToList();
        return Task.FromResult(Ok(new { Total = total, Groups = page, Skip = skip, Take = take }));
    }

    /// <summary>Returns leaderboard definitions plus top-N entries for each.</summary>
    [HttpPost("/Admin/ListLeaderboards")]
    public Task<IHttpActionResult> Admin_ListLeaderboards(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var defs    = Database.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true)!.Find(d => d.TitleId == TITLE_ID).ToList();
        var entries = Database.GetCollection<PlayfabLeaderboardEntryDbObject>(true)!.Find(e => e.TitleId == TITLE_ID).ToList();
        var result  = defs.Select(d => new
        {
            d.Name, d.EntityType, d.Version, d.SizeLimit, d.Created,
            EntryCount = entries.Count(e => e.LeaderboardName == d.Name && e.Version == d.Version),
        }).ToList();
        return Task.FromResult(Ok(new { Leaderboards = result }));
    }

    /// <summary>Returns the inventory items (Economy v2) for an entity.</summary>
    [HttpPost("/Admin/GetEntityInventory")]
    public Task<IHttpActionResult> Admin_GetEntityInventory(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        string entityId;
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            entityId = doc.RootElement.TryGetProperty("EntityId", out var x) ? x.GetString() ?? "" : "";
        }
        catch { entityId = ""; }
        if (string.IsNullOrEmpty(entityId)) return Task.FromResult(Err("InvalidParams", 1000, "EntityId required"));
        var rows = Database.GetCollection<PlayfabEconomyInventoryDbObject>(true)!
                           .Find(i => i.EntityId == entityId).ToList();
        return Task.FromResult(Ok(new { Items = rows }));
    }

    /// <summary>Returns Economy v2 catalog items.</summary>
    [HttpPost("/Admin/ListEconomyItems")]
    public Task<IHttpActionResult> Admin_ListEconomyItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var (skip, take, query) = ReadPage(request, request.Body);
        var rows = Database.GetCollection<PlayfabEconomyItemDbObject>(true)!.Find(x => x.TitleId == TITLE_ID).ToList();
        if (!string.IsNullOrEmpty(query))
        {
            var q = query.ToLowerInvariant();
            rows = rows.Where(r => (r.ItemId ?? "").ToLowerInvariant().Contains(q)
                                || (r.Type   ?? "").ToLowerInvariant().Contains(q)
                                || (r.Tags ?? new()).Any(t => t.ToLowerInvariant().Contains(q))).ToList();
        }
        var total = rows.Count;
        var page  = rows.OrderByDescending(r => r.Created).Skip(skip).Take(take).ToList();
        return Task.FromResult(Ok(new { Total = total, Items = page, Skip = skip, Take = take }));
    }

    /// <summary>Returns lobbies + matchmaking ticket counts.</summary>
    [HttpPost("/Admin/GetMultiplayerOverview")]
    public Task<IHttpActionResult> Admin_GetMultiplayerOverview(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var lobbies = Database.GetCollection<PlayfabLobbyDbObject>(true)!.FindAll().ToList();
        var tickets = Database.GetCollection<PlayfabMatchmakingTicketDbObject>(true)!.FindAll().ToList();
        var queues  = Database.GetCollection<PlayfabMatchmakingQueueDbObject>(true)!.FindAll().ToList();
        var builds  = Database.GetCollection<PlayfabBuildDbObject>(true)!.FindAll().ToList();
        var servers = Database.GetCollection<PlayfabServerDbObject>(true)!.FindAll().ToList();
        return Task.FromResult(Ok(new
        {
            LobbyCount   = lobbies.Count,
            ActiveLobbies = lobbies.OrderByDescending(l => l.Created).Take(20).Select(l => new
            {
                l.LobbyId, l.OwnerEntityId, l.MaxPlayers, MemberCount = l.MemberEntityIds?.Count ?? 0,
                l.AccessPolicy, l.Created,
            }).ToList(),
            TicketCount  = tickets.Count,
            QueueCount   = queues.Count,
            Queues       = queues.Select(q => new { q.QueueName, q.MinMatchSize, q.MaxMatchSize }).ToList(),
            BuildCount   = builds.Count,
            Builds       = builds.Select(b => new { b.BuildId, b.BuildName, b.ContainerFlavor, b.Created }).ToList(),
            ServerCount  = servers.Count,
            ActiveServers = servers.Where(s => s.State == "Active").Count(),
        }));
    }

    /// <summary>Recent in-flight Photon auth tickets (debug).</summary>
    [HttpPost("/Admin/GetPhotonTickets")]
    public Task<IHttpActionResult> Admin_GetPhotonTickets(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabPhotonTicketDbObject>(true)!
                           .FindAll()
                           .OrderByDescending(t => t.Issued).Take(100).ToList();
        return Task.FromResult(Ok(new { Tickets = rows }));
    }
}
