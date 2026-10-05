using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Database;
using AUnrealFeatures.Playfab.Models;
using PlayFab.EconomyModels;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed class PlayfabCatalogConfigDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string ConfigJson { get; set; } = "";
    public long LastUpdated { get; set; }
}

public sealed partial class PlayfabServer
{
    /* ─── Catalog item helpers ────────────────────────────────────────────── */

    private static CatalogItem RowToItem(PlayfabEconomyItemDbObject r)
    {
        var item = new CatalogItem
        {
            Id              = r.ItemId,
            ContentType     = r.ContentType,
            DefaultStackId  = r.DefaultStackId,
            Type            = r.Type,
            DisplayVersion  = r.DisplayVersion,
            Title           = r.Title ?? new(),
            Description     = r.Description ?? new(),
            Tags            = r.Tags ?? new(),
            Platforms       = new List<string>(),
            CreationDate    = DateTimeOffset.FromUnixTimeSeconds(r.Created).UtcDateTime,
            LastModifiedDate = DateTimeOffset.FromUnixTimeSeconds(r.Created).UtcDateTime,
            StartDate       = r.StartDate.HasValue ? DateTimeOffset.FromUnixTimeSeconds(r.StartDate.Value).UtcDateTime : (DateTime?)null,
            EndDate         = r.EndDate.HasValue ? DateTimeOffset.FromUnixTimeSeconds(r.EndDate.Value).UtcDateTime : (DateTime?)null,
            IsHidden        = r.Hidden,
            CreatorEntity   = !string.IsNullOrEmpty(r.CreatorEntityId) ? new EntityKey { Id = r.CreatorEntityId } : null,
            ETag            = r.Id.ToString(),
            AlternateIds    = (r.AlternateIds ?? new()).Select(a =>
            {
                var parts = a.Split('|', 2);
                return new CatalogAlternateId { Type = parts[0], Value = parts.Length > 1 ? parts[1] : "" };
            }).ToList(),
        };
        try
        {
            if (!string.IsNullOrEmpty(r.PriceOptionsJson))
                item.PriceOptions = JsonSerializer.Deserialize<CatalogPriceOptions>(r.PriceOptionsJson, JsonOpts);
            if (!string.IsNullOrEmpty(r.ImagesJson))
                item.Images       = JsonSerializer.Deserialize<List<Image>>(r.ImagesJson, JsonOpts);
            if (!string.IsNullOrEmpty(r.ContentsJson))
                item.Contents     = JsonSerializer.Deserialize<List<Content>>(r.ContentsJson, JsonOpts);
            if (!string.IsNullOrEmpty(r.ItemReferencesJson))
                item.ItemReferences = JsonSerializer.Deserialize<List<CatalogItemReference>>(r.ItemReferencesJson, JsonOpts);
        }
        catch { /* ignore deserialization issues */ }
        return item;
    }

    private static PlayfabEconomyItemDbObject ItemToRow(CatalogItem it, string status, string titleId)
    {
        return new PlayfabEconomyItemDbObject
        {
            TitleId          = titleId,
            ItemId           = string.IsNullOrEmpty(it.Id) ? Guid.NewGuid().ToString("N") : it.Id,
            ContentType      = it.ContentType ?? "",
            DefaultStackId   = it.DefaultStackId ?? "Default",
            Type             = it.Type ?? "ugc",
            DisplayVersion   = it.DisplayVersion ?? "",
            Title            = it.Title ?? new(),
            Description      = it.Description ?? new(),
            Tags             = it.Tags ?? new(),
            CreatorEntityId  = it.CreatorEntity?.Id ?? "",
            Status           = status,
            Hidden           = it.IsHidden ?? false,
            Created          = NowUnix(),
            StartDate        = it.StartDate.HasValue ? new DateTimeOffset(it.StartDate.Value, TimeSpan.Zero).ToUnixTimeSeconds() : (long?)null,
            EndDate          = it.EndDate.HasValue ? new DateTimeOffset(it.EndDate.Value, TimeSpan.Zero).ToUnixTimeSeconds() : (long?)null,
            AlternateIds     = (it.AlternateIds ?? new()).Select(a => $"{a.Type}|{a.Value}").ToList(),
            PriceOptionsJson = it.PriceOptions != null ? JsonSerializer.Serialize(it.PriceOptions, JsonOpts) : "",
            ImagesJson       = it.Images != null ? JsonSerializer.Serialize(it.Images, JsonOpts) : "",
            ContentsJson     = it.Contents != null ? JsonSerializer.Serialize(it.Contents, JsonOpts) : "",
            ItemReferencesJson = it.ItemReferences != null ? JsonSerializer.Serialize(it.ItemReferences, JsonOpts) : "",
        };
    }

    private PlayfabEconomyItemDbObject? FindCatalogItem(string id, CatalogAlternateId? alt)
    {
        var coll = Database.GetCollection<PlayfabEconomyItemDbObject>(true)!;
        if (!string.IsNullOrEmpty(id))
            return coll.FindOne(x => x.TitleId == TITLE_ID && x.ItemId == id);
        if (alt != null && !string.IsNullOrEmpty(alt.Type))
        {
            var key = $"{alt.Type}|{alt.Value}";
            return coll.FindAll().FirstOrDefault(x => x.TitleId == TITLE_ID && x.AlternateIds.Contains(key));
        }
        return null;
    }

    /* ─── Catalog: Drafts and Items ───────────────────────────────────────── */

    /// <summary>Creates a draft catalog item.</summary>
    [HttpPost("/Catalog/CreateDraftItem")]
    public Task<IHttpActionResult> Economy_CreateDraftItem(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateDraftItemRequest>(request);
        if (body == null || body.Item == null)
            return Task.FromResult(Err("InvalidParams", 1000, "Item is required"));

        var coll = Database.GetCollection<PlayfabEconomyItemDbObject>(true)!;
        var row = ItemToRow(body.Item, body.Publish ? "Published" : "Draft", TITLE_ID);
        coll.Insert(row);
        body.Item.Id = row.ItemId;
        return Task.FromResult(Ok(new CreateDraftItemResponse { Item = RowToItem(row) }));
    }

    /// <summary>Updates an existing draft catalog item.</summary>
    [HttpPost("/Catalog/UpdateDraftItem")]
    public Task<IHttpActionResult> Economy_UpdateDraftItem(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateDraftItemRequest>(request);
        if (body == null || body.Item == null || string.IsNullOrEmpty(body.Item.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Item with Id is required"));

        var coll = Database.GetCollection<PlayfabEconomyItemDbObject>(true)!;
        var iid = body.Item.Id;
        var existing = coll.FindOne(x => x.TitleId == TITLE_ID && x.ItemId == iid);
        if (existing == null) return Task.FromResult(Err("ItemNotFound", 1701, "Item not found", HttpStatusCode.NotFound));

        var updated = ItemToRow(body.Item, body.Publish ? "Published" : existing.Status, TITLE_ID);
        updated.Id      = existing.Id;
        updated.ItemId  = existing.ItemId;
        updated.Created = existing.Created;
        coll.Update(updated);
        return Task.FromResult(Ok(new UpdateDraftItemResponse { Item = RowToItem(updated) }));
    }

    /// <summary>Deletes a catalog item (draft or published).</summary>
    [HttpPost("/Catalog/DeleteItem")]
    public Task<IHttpActionResult> Economy_DeleteItem(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteItemRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var existing = FindCatalogItem(body.Id, body.AlternateId);
        if (existing == null) return Task.FromResult(Err("ItemNotFound", 1701, "Item not found", HttpStatusCode.NotFound));
        Database.GetCollection<PlayfabEconomyItemDbObject>(true)!.Delete(existing.Id);
        return Task.FromResult(Ok(new DeleteItemResponse()));
    }

    /// <summary>Publishes a draft item (transitions Status to Published).</summary>
    [HttpPost("/Catalog/PublishDraftItem")]
    public Task<IHttpActionResult> Economy_PublishDraftItem(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<PublishDraftItemRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var existing = FindCatalogItem(body.Id, body.AlternateId);
        if (existing == null) return Task.FromResult(Err("ItemNotFound", 1701, "Item not found", HttpStatusCode.NotFound));
        existing.Status = "Published";
        Database.GetCollection<PlayfabEconomyItemDbObject>(true)!.Update(existing);
        return Task.FromResult(Ok(new PublishDraftItemResponse()));
    }

    /// <summary>Returns the publish status of an item.</summary>
    [HttpPost("/Catalog/GetItemPublishStatus")]
    public Task<IHttpActionResult> Economy_GetItemPublishStatus(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetItemPublishStatusRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var existing = FindCatalogItem(body.Id, body.AlternateId);
        if (existing == null) return Task.FromResult(Err("ItemNotFound", 1701, "Item not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new GetItemPublishStatusResponse
        {
            Result        = existing.Status == "Published" ? PublishResult.Succeeded : PublishResult.Pending,
            StatusMessage = $"Status: {existing.Status}",
        }));
    }

    /// <summary>Returns a single catalog item.</summary>
    [HttpPost("/Catalog/GetItem")]
    public Task<IHttpActionResult> Economy_GetItem(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetItemRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var existing = FindCatalogItem(body.Id, body.AlternateId);
        if (existing == null) return Task.FromResult(Err("ItemNotFound", 1701, "Item not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new GetItemResponse { Item = RowToItem(existing) }));
    }

    /// <summary>Returns a draft catalog item by id (any status).</summary>
    [HttpPost("/Catalog/GetDraftItem")]
    public Task<IHttpActionResult> Economy_GetDraftItem(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetDraftItemRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var existing = FindCatalogItem(body.Id, body.AlternateId);
        if (existing == null) return Task.FromResult(Err("ItemNotFound", 1701, "Item not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new GetDraftItemResponse { Item = RowToItem(existing) }));
    }

    /// <summary>Returns multiple catalog items by ids/alternate ids.</summary>
    [HttpPost("/Catalog/GetItems")]
    public Task<IHttpActionResult> Economy_GetItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetItemsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var coll = Database.GetCollection<PlayfabEconomyItemDbObject>(true)!;
        var rows = new List<PlayfabEconomyItemDbObject>();
        if (body.Ids != null)
            foreach (var id in body.Ids)
            {
                var iid = id;
                var r = coll.FindOne(x => x.TitleId == TITLE_ID && x.ItemId == iid);
                if (r != null) rows.Add(r);
            }
        if (body.AlternateIds != null)
            foreach (var alt in body.AlternateIds)
            {
                var key = $"{alt.Type}|{alt.Value}";
                var r = coll.FindAll().FirstOrDefault(x => x.TitleId == TITLE_ID && x.AlternateIds.Contains(key));
                if (r != null && !rows.Any(rr => rr.ItemId == r.ItemId)) rows.Add(r);
            }
        return Task.FromResult(Ok(new GetItemsResponse { Items = rows.Select(RowToItem).ToList() }));
    }

    /// <summary>Returns multiple drafts by id/alternate ids.</summary>
    [HttpPost("/Catalog/GetDraftItems")]
    public Task<IHttpActionResult> Economy_GetDraftItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetDraftItemsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var coll = Database.GetCollection<PlayfabEconomyItemDbObject>(true)!;
        var rows = new List<PlayfabEconomyItemDbObject>();
        if (body.Ids != null)
            foreach (var id in body.Ids)
            {
                var iid = id;
                var r = coll.FindOne(x => x.TitleId == TITLE_ID && x.ItemId == iid);
                if (r != null) rows.Add(r);
            }
        return Task.FromResult(Ok(new GetDraftItemsResponse
        {
            Items             = rows.Select(RowToItem).ToList(),
            ContinuationToken = "",
        }));
    }

    /// <summary>Returns drafts owned by a specific entity.</summary>
    [HttpPost("/Catalog/GetEntityDraftItems")]
    public Task<IHttpActionResult> Economy_GetEntityDraftItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetEntityDraftItemsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId = body.Entity?.Id ?? "";
        var rows = Database.GetCollection<PlayfabEconomyItemDbObject>(true)!
                           .Find(x => x.TitleId == TITLE_ID && x.CreatorEntityId == entityId).ToList();
        return Task.FromResult(Ok(new GetEntityDraftItemsResponse
        {
            Items             = rows.Select(RowToItem).ToList(),
            ContinuationToken = "",
        }));
    }

    /// <summary>Returns containers (parent items) that include the specified item.</summary>
    [HttpPost("/Catalog/GetItemContainers")]
    public Task<IHttpActionResult> Economy_GetItemContainers(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetItemContainersRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var target = FindCatalogItem(body.Id, body.AlternateId);
        var coll = Database.GetCollection<PlayfabEconomyItemDbObject>(true)!;
        var containers = new List<CatalogItem>();
        if (target != null)
        {
            foreach (var r in coll.Find(x => x.TitleId == TITLE_ID && x.Type == "bundle"))
            {
                if (string.IsNullOrEmpty(r.ContentsJson)) continue;
                if (r.ContentsJson.Contains(target.ItemId)) containers.Add(RowToItem(r));
            }
        }
        return Task.FromResult(Ok(new GetItemContainersResponse
        {
            Containers        = containers,
            ContinuationToken = "",
        }));
    }

    /// <summary>Searches the catalog with simple string matching.</summary>
    [HttpPost("/Catalog/SearchItems")]
    public Task<IHttpActionResult> Economy_SearchItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SearchItemsRequest>(request);
        if (body == null) body = new SearchItemsRequest();
        var coll = Database.GetCollection<PlayfabEconomyItemDbObject>(true)!;
        var rows = coll.Find(x => x.TitleId == TITLE_ID && x.Status == "Published").ToList();
        if (!string.IsNullOrEmpty(body.Search))
        {
            var s = body.Search.ToLowerInvariant();
            rows = rows.Where(r =>
                r.ItemId.ToLowerInvariant().Contains(s) ||
                (r.Title != null && r.Title.Values.Any(t => t != null && t.ToLowerInvariant().Contains(s))) ||
                (r.Description != null && r.Description.Values.Any(d => d != null && d.ToLowerInvariant().Contains(s))) ||
                (r.Tags ?? new()).Any(tg => tg.ToLowerInvariant().Contains(s))
            ).ToList();
        }
        var skip = 0;
        if (!string.IsNullOrEmpty(body.ContinuationToken)) int.TryParse(body.ContinuationToken, out skip);
        var count = body.Count > 0 ? body.Count : 50;
        var page = rows.Skip(skip).Take(count).ToList();
        var nextToken = skip + page.Count < rows.Count ? (skip + page.Count).ToString() : "";
        return Task.FromResult(Ok(new SearchItemsResponse
        {
            Items             = page.Select(RowToItem).ToList(),
            ContinuationToken = nextToken,
        }));
    }

    /// <summary>Returns presigned upload URLs (here: server-relative).</summary>
    [HttpPost("/Catalog/CreateUploadUrls")]
    public Task<IHttpActionResult> Economy_CreateUploadUrls(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateUploadUrlsRequest>(request);
        if (body == null || body.Files == null) return Task.FromResult(Err("InvalidParams", 1000, "Files are required"));
        var urls = body.Files.Select(f => new UploadUrlMetadata
        {
            FileName = f.FileName ?? "",
            Id       = NewId(),
            Url      = $"/Catalog/Upload/{Uri.EscapeDataString(f.FileName ?? "")}",
        }).ToList();
        return Task.FromResult(Ok(new CreateUploadUrlsResponse { UploadUrls = urls }));
    }

    /* ─── Catalog config ──────────────────────────────────────────────────── */

    /// <summary>Returns the title's catalog configuration.</summary>
    [HttpPost("/Catalog/GetCatalogConfig")]
    public Task<IHttpActionResult> Economy_GetCatalogConfig(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var coll = Database.GetCollection<PlayfabCatalogConfigDbObject>(true)!;
        var row = coll.FindOne(x => x.TitleId == TITLE_ID);
        CatalogConfig cfg;
        if (row == null || string.IsNullOrEmpty(row.ConfigJson))
        {
            cfg = new CatalogConfig
            {
                IsCatalogEnabled = true,
                Catalog          = new CatalogSpecificConfig { ContentTypes = new List<string> { "ugc" }, Tags = new List<string>() },
                Platforms        = new List<string>(),
                AdminEntities    = new List<EntityKey>(),
                ReviewerEntities = new List<EntityKey>(),
                File             = new FileConfig(),
                Image            = new ImageConfig(),
            };
        }
        else
        {
            try { cfg = JsonSerializer.Deserialize<CatalogConfig>(row.ConfigJson, JsonOpts) ?? new CatalogConfig(); }
            catch { cfg = new CatalogConfig(); }
        }
        return Task.FromResult(Ok(new GetCatalogConfigResponse { Config = cfg }));
    }

    /// <summary>Updates the title's catalog configuration.</summary>
    [HttpPost("/Catalog/UpdateCatalogConfig")]
    public Task<IHttpActionResult> Economy_UpdateCatalogConfig(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateCatalogConfigRequest>(request);
        if (body == null || body.Config == null) return Task.FromResult(Err("InvalidParams", 1000, "Config required"));

        var coll = Database.GetCollection<PlayfabCatalogConfigDbObject>(true)!;
        var json = JsonSerializer.Serialize(body.Config, JsonOpts);
        var row = coll.FindOne(x => x.TitleId == TITLE_ID);
        if (row == null)
            coll.Insert(new PlayfabCatalogConfigDbObject { TitleId = TITLE_ID, ConfigJson = json, LastUpdated = NowUnix() });
        else
        { row.ConfigJson = json; row.LastUpdated = NowUnix(); coll.Update(row); }
        return Task.FromResult(Ok(new UpdateCatalogConfigResponse()));
    }

    /* ─── Catalog moderation ──────────────────────────────────────────────── */

    /// <summary>Returns the moderation state of an item.</summary>
    [HttpPost("/Catalog/GetItemModerationState")]
    public Task<IHttpActionResult> Economy_GetItemModerationState(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetItemModerationStateRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var existing = FindCatalogItem(body.Id, body.AlternateId);
        if (existing == null) return Task.FromResult(Err("ItemNotFound", 1701, "Item not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new GetItemModerationStateResponse
        {
            State = new ModerationState
            {
                Status           = Enum.TryParse<ModerationStatus>(existing.Status, out var ms) ? ms : ModerationStatus.Approved,
                Reason           = "",
                LastModifiedDate = DateTimeOffset.FromUnixTimeSeconds(existing.Created).UtcDateTime,
            }
        }));
    }

    /// <summary>Sets the moderation state of an item.</summary>
    [HttpPost("/Catalog/SetItemModerationState")]
    public Task<IHttpActionResult> Economy_SetItemModerationState(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SetItemModerationStateRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var existing = FindCatalogItem(body.Id, body.AlternateId);
        if (existing == null) return Task.FromResult(Err("ItemNotFound", 1701, "Item not found", HttpStatusCode.NotFound));
        existing.Status = (body.Status ?? ModerationStatus.Approved).ToString();
        Database.GetCollection<PlayfabEconomyItemDbObject>(true)!.Update(existing);
        return Task.FromResult(Ok(new SetItemModerationStateResponse()));
    }

    /// <summary>Reports an item for moderation review.</summary>
    [HttpPost("/Catalog/ReportItem")]
    public Task<IHttpActionResult> Economy_ReportItem(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ReportItemRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        // Just record the report — no rejection needed.
        return Task.FromResult(Ok(new ReportItemResponse()));
    }

    /* ─── Reviews ─────────────────────────────────────────────────────────── */

    /// <summary>Submits or replaces an entity's review of an item.</summary>
    [HttpPost("/Catalog/ReviewItem")]
    public Task<IHttpActionResult> Economy_ReviewItem(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ReviewItemRequest>(request);
        if (body == null || body.Review == null) return Task.FromResult(Err("InvalidParams", 1000, "Review required"));
        var item = FindCatalogItem(body.Id, body.AlternateId);
        if (item == null) return Task.FromResult(Err("ItemNotFound", 1701, "Item not found", HttpStatusCode.NotFound));

        var coll = Database.GetCollection<PlayfabReviewDbObject>(true)!;
        var entityId = body.Entity?.Id ?? PlayFabIdFromRequest(request);
        var iid = item.ItemId;
        var existing = coll.FindOne(r => r.ItemId == iid && r.ReviewerEntityId == entityId);
        if (existing == null)
        {
            coll.Insert(new PlayfabReviewDbObject
            {
                ReviewId         = NewId(),
                ItemId           = item.ItemId,
                ReviewerEntityId = entityId,
                Rating           = body.Review.Rating,
                ReviewText       = body.Review.ReviewText ?? "",
                ReviewTitle      = body.Review.Title ?? "",
                Locale           = body.Review.Locale ?? "",
                IsInstalled      = body.Review.IsInstalled,
                Submitted        = NowUnix(),
            });
        }
        else
        {
            existing.Rating       = body.Review.Rating;
            existing.ReviewText   = body.Review.ReviewText ?? "";
            existing.ReviewTitle  = body.Review.Title ?? "";
            existing.Locale       = body.Review.Locale ?? "";
            existing.IsInstalled  = body.Review.IsInstalled;
            existing.Submitted    = NowUnix();
            coll.Update(existing);
        }
        return Task.FromResult(Ok(new ReviewItemResponse()));
    }

    /// <summary>Reports a review for moderation.</summary>
    [HttpPost("/Catalog/ReportItemReview")]
    public Task<IHttpActionResult> Economy_ReportItemReview(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        ParseRequest<ReportItemReviewRequest>(request);
        return Task.FromResult(Ok(new ReportItemReviewResponse()));
    }

    /// <summary>Submits a helpful/unhelpful vote on a review.</summary>
    [HttpPost("/Catalog/SubmitItemReviewVote")]
    public Task<IHttpActionResult> Economy_SubmitItemReviewVote(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SubmitItemReviewVoteRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ReviewId))
            return Task.FromResult(Err("InvalidParams", 1000, "ReviewId required"));

        var coll = Database.GetCollection<PlayfabReviewDbObject>(true)!;
        var rid = body.ReviewId;
        var r = coll.FindOne(x => x.ReviewId == rid);
        if (r == null) return Task.FromResult(Err("ReviewNotFound", 1741, "Review not found", HttpStatusCode.NotFound));
        if (body.Vote == HelpfulnessVote.Helpful) r.HelpfulPositive++;
        else if (body.Vote == HelpfulnessVote.UnHelpful) r.HelpfulNegative++;
        coll.Update(r);
        return Task.FromResult(Ok(new SubmitItemReviewVoteResponse()));
    }

    /// <summary>Removes one or more reviews.</summary>
    [HttpPost("/Catalog/TakedownItemReviews")]
    public Task<IHttpActionResult> Economy_TakedownItemReviews(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<TakedownItemReviewsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var coll = Database.GetCollection<PlayfabReviewDbObject>(true)!;
        if (body.Reviews != null)
            foreach (var t in body.Reviews)
            {
                var rid = t.ReviewId;
                if (!string.IsNullOrEmpty(rid)) coll.DeleteMany(r => r.ReviewId == rid);
            }
        return Task.FromResult(Ok(new TakedownItemReviewsResponse()));
    }

    /// <summary>Returns paged reviews for an item.</summary>
    [HttpPost("/Catalog/GetItemReviews")]
    public Task<IHttpActionResult> Economy_GetItemReviews(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetItemReviewsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var item = FindCatalogItem(body.Id, body.AlternateId);
        if (item == null) return Task.FromResult(Err("ItemNotFound", 1701, "Item not found", HttpStatusCode.NotFound));

        var iid = item.ItemId;
        var rows = Database.GetCollection<PlayfabReviewDbObject>(true)!.Find(r => r.ItemId == iid).ToList();
        var skip = 0;
        if (!string.IsNullOrEmpty(body.ContinuationToken)) int.TryParse(body.ContinuationToken, out skip);
        var count = body.Count > 0 ? body.Count : 50;
        var page = rows.Skip(skip).Take(count).Select(r => new Review
        {
            ReviewId        = r.ReviewId,
            ItemId          = r.ItemId,
            ReviewerEntity  = new EntityKey { Id = r.ReviewerEntityId, Type = "title_player_account" },
            Rating          = r.Rating,
            Title           = r.ReviewTitle,
            ReviewText      = r.ReviewText,
            Locale          = r.Locale,
            IsInstalled     = r.IsInstalled,
            HelpfulPositive = r.HelpfulPositive,
            HelpfulNegative = r.HelpfulNegative,
            Submitted       = DateTimeOffset.FromUnixTimeSeconds(r.Submitted).UtcDateTime,
        }).ToList();
        var nextToken = skip + page.Count < rows.Count ? (skip + page.Count).ToString() : "";
        return Task.FromResult(Ok(new GetItemReviewsResponse { Reviews = page, ContinuationToken = nextToken }));
    }

    /// <summary>Returns the calling entity's review for an item, if any.</summary>
    [HttpPost("/Catalog/GetEntityItemReview")]
    public Task<IHttpActionResult> Economy_GetEntityItemReview(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetEntityItemReviewRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var item = FindCatalogItem(body.Id, body.AlternateId);
        if (item == null) return Task.FromResult(Err("ItemNotFound", 1701, "Item not found", HttpStatusCode.NotFound));
        var entityId = body.Entity?.Id ?? PlayFabIdFromRequest(request);
        var iid = item.ItemId;
        var r = Database.GetCollection<PlayfabReviewDbObject>(true)!.FindOne(x => x.ItemId == iid && x.ReviewerEntityId == entityId);
        if (r == null) return Task.FromResult(Ok(new GetEntityItemReviewResponse { Review = null }));
        return Task.FromResult(Ok(new GetEntityItemReviewResponse
        {
            Review = new Review
            {
                ReviewId        = r.ReviewId,
                ItemId          = r.ItemId,
                ReviewerEntity  = new EntityKey { Id = r.ReviewerEntityId, Type = "title_player_account" },
                Rating          = r.Rating,
                Title           = r.ReviewTitle,
                ReviewText      = r.ReviewText,
                Locale          = r.Locale,
                IsInstalled     = r.IsInstalled,
                HelpfulPositive = r.HelpfulPositive,
                HelpfulNegative = r.HelpfulNegative,
                Submitted       = DateTimeOffset.FromUnixTimeSeconds(r.Submitted).UtcDateTime,
            }
        }));
    }

    /// <summary>Returns aggregate review stats for an item.</summary>
    [HttpPost("/Catalog/GetItemReviewSummary")]
    public Task<IHttpActionResult> Economy_GetItemReviewSummary(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetItemReviewSummaryRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var item = FindCatalogItem(body.Id, body.AlternateId);
        if (item == null) return Task.FromResult(Err("ItemNotFound", 1701, "Item not found", HttpStatusCode.NotFound));

        var iid = item.ItemId;
        var rows = Database.GetCollection<PlayfabReviewDbObject>(true)!.Find(r => r.ItemId == iid).ToList();
        if (rows.Count == 0)
            return Task.FromResult(Ok(new GetItemReviewSummaryResponse { Rating = new Rating { TotalCount = 0 }, ReviewsCount = 0 }));

        return Task.FromResult(Ok(new GetItemReviewSummaryResponse
        {
            ReviewsCount         = rows.Count,
            MostFavorableReview  = rows.OrderByDescending(r => r.Rating).First() is var hi ? new Review { ReviewId = hi.ReviewId, Rating = hi.Rating, ReviewText = hi.ReviewText, Title = hi.ReviewTitle, ItemId = hi.ItemId } : null,
            LeastFavorableReview = rows.OrderBy(r => r.Rating).First() is var lo ? new Review { ReviewId = lo.ReviewId, Rating = lo.Rating, ReviewText = lo.ReviewText, Title = lo.ReviewTitle, ItemId = lo.ItemId } : null,
            Rating = new Rating
            {
                Average    = (float)rows.Average(r => r.Rating),
                TotalCount = rows.Count,
                Count1Star = rows.Count(r => r.Rating == 1),
                Count2Star = rows.Count(r => r.Rating == 2),
                Count3Star = rows.Count(r => r.Rating == 3),
                Count4Star = rows.Count(r => r.Rating == 4),
                Count5Star = rows.Count(r => r.Rating == 5),
            }
        }));
    }

    /// <summary>Deletes all reviews for an entity.</summary>
    [HttpPost("/Catalog/DeleteEntityItemReviews")]
    public Task<IHttpActionResult> Economy_DeleteEntityItemReviews(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteEntityItemReviewsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId = body.Entity?.Id ?? "";
        if (string.IsNullOrEmpty(entityId)) return Task.FromResult(Err("InvalidParams", 1000, "Entity required"));
        Database.GetCollection<PlayfabReviewDbObject>(true)!.DeleteMany(r => r.ReviewerEntityId == entityId);
        return Task.FromResult(Ok(new DeleteEntityItemReviewsResponse()));
    }
}
