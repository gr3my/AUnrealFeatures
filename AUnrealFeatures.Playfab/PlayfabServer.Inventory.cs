using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Playfab.Models;
using PlayFab.EconomyModels;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    /* ─── Inventory helpers ───────────────────────────────────────────────── */

    private static InventoryItem RowToInventoryItem(PlayfabEconomyInventoryDbObject r)
    {
        var item = new InventoryItem
        {
            Id      = r.ItemId,
            StackId = r.StackId,
            Amount  = r.Amount,
            Type    = "item",
        };
        try
        {
            if (!string.IsNullOrEmpty(r.DisplayProperties))
                item.DisplayProperties = JsonSerializer.Deserialize<JsonElement>(r.DisplayProperties);
        }
        catch { }
        return item;
    }

    private static string ResolveItemId(InventoryItemReference? reference) =>
        reference?.Id ?? reference?.AlternateId?.Value ?? "";

    private string AddInventoryInternal(string entityId, string entityType, string collectionId, InventoryItemReference itemRef, int amount, InitialValues? initial)
    {
        var coll = Database.GetCollection<PlayfabEconomyInventoryDbObject>(true)!;
        var itemId  = ResolveItemId(itemRef);
        var stackId = string.IsNullOrEmpty(itemRef.StackId) ? "Default" : itemRef.StackId;
        var existing = coll.FindOne(x => x.EntityId == entityId && x.CollectionId == collectionId && x.ItemId == itemId && x.StackId == stackId);
        if (existing != null)
        {
            existing.Amount += amount;
            coll.Update(existing);
        }
        else
        {
            coll.Insert(new PlayfabEconomyInventoryDbObject
            {
                EntityId          = entityId,
                EntityType        = entityType,
                CollectionId      = collectionId,
                ItemId            = itemId,
                StackId           = stackId,
                Amount            = amount,
                DisplayProperties = initial?.DisplayProperties != null ? JsonSerializer.Serialize(initial.DisplayProperties, JsonOpts) : "",
            });
        }
        return RecordTransaction(entityId, "Add", itemId, amount);
    }

    private string SubtractInventoryInternal(string entityId, string entityType, string collectionId, InventoryItemReference itemRef, int amount, bool deleteEmpty)
    {
        var coll = Database.GetCollection<PlayfabEconomyInventoryDbObject>(true)!;
        var itemId  = ResolveItemId(itemRef);
        var stackId = string.IsNullOrEmpty(itemRef.StackId) ? "Default" : itemRef.StackId;
        var existing = coll.FindOne(x => x.EntityId == entityId && x.CollectionId == collectionId && x.ItemId == itemId && x.StackId == stackId);
        if (existing == null)
            throw new PlayfabFault("InventoryItemNotFound", 1762, "Inventory item not found");

        existing.Amount = Math.Max(0, existing.Amount - amount);
        if (existing.Amount == 0 && deleteEmpty) coll.Delete(existing.Id);
        else coll.Update(existing);
        return RecordTransaction(entityId, "Subtract", itemId, amount);
    }

    private string DeleteInventoryInternal(string entityId, string collectionId, InventoryItemReference itemRef)
    {
        var coll = Database.GetCollection<PlayfabEconomyInventoryDbObject>(true)!;
        var itemId  = ResolveItemId(itemRef);
        var stackId = string.IsNullOrEmpty(itemRef.StackId) ? "Default" : itemRef.StackId;
        coll.DeleteMany(x => x.EntityId == entityId && x.CollectionId == collectionId && x.ItemId == itemId && x.StackId == stackId);
        return RecordTransaction(entityId, "Delete", itemId, 0);
    }

    private string UpdateInventoryInternal(string entityId, string entityType, string collectionId, InventoryItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.Id)) return "";
        var coll = Database.GetCollection<PlayfabEconomyInventoryDbObject>(true)!;
        var itemId  = item.Id;
        var stackId = string.IsNullOrEmpty(item.StackId) ? "Default" : item.StackId;
        var existing = coll.FindOne(x => x.EntityId == entityId && x.CollectionId == collectionId && x.ItemId == itemId && x.StackId == stackId);
        if (existing == null)
        {
            coll.Insert(new PlayfabEconomyInventoryDbObject
            {
                EntityId          = entityId,
                EntityType        = entityType,
                CollectionId      = collectionId,
                ItemId            = itemId,
                StackId           = stackId,
                Amount            = item.Amount ?? 0,
                DisplayProperties = item.DisplayProperties != null ? JsonSerializer.Serialize(item.DisplayProperties, JsonOpts) : "",
            });
        }
        else
        {
            if (item.Amount.HasValue) existing.Amount = item.Amount.Value;
            if (item.DisplayProperties != null)
                existing.DisplayProperties = JsonSerializer.Serialize(item.DisplayProperties, JsonOpts);
            coll.Update(existing);
        }
        return RecordTransaction(entityId, "Update", itemId, item.Amount ?? 0);
    }

    private string RecordTransaction(string entityId, string operation, string itemId, int amount)
    {
        var tx = new PlayfabTransactionDbObject
        {
            TransactionId = NewId(),
            EntityId      = entityId,
            Operation     = operation,
            ItemId        = itemId,
            Amount        = amount,
            Timestamp     = NowUnixMs(),
        };
        Database.GetCollection<PlayfabTransactionDbObject>(true)!.Insert(tx);
        return tx.TransactionId;
    }

    /* ─── Inventory: Items ────────────────────────────────────────────────── */

    /// <summary>Adds inventory items to an entity's collection.</summary>
    [HttpPost("/Inventory/AddInventoryItems")]
    public Task<IHttpActionResult> Economy_AddInventoryItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<AddInventoryItemsRequest>(request);
        if (body == null || body.Item == null) return Task.FromResult(Err("InvalidParams", 1000, "Item required"));
        var entityId = body.Entity?.Id ?? PlayFabIdFromRequest(request);
        var entityType = body.Entity?.Type ?? "title_player_account";
        if (string.IsNullOrEmpty(entityId)) return Task.FromResult(Unauthorized());
        var coll = string.IsNullOrEmpty(body.CollectionId) ? "Default" : body.CollectionId;
        var tx = AddInventoryInternal(entityId, entityType, coll, body.Item, body.Amount ?? 1, body.NewStackValues);
        return Task.FromResult(Ok(new AddInventoryItemsResponse
        {
            ETag           = tx,
            IdempotencyId  = body.IdempotencyId ?? "",
            TransactionIds = new List<string> { tx },
        }));
    }

    /// <summary>Subtracts items from an entity's inventory.</summary>
    [HttpPost("/Inventory/SubtractInventoryItems")]
    public Task<IHttpActionResult> Economy_SubtractInventoryItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SubtractInventoryItemsRequest>(request);
        if (body == null || body.Item == null) return Task.FromResult(Err("InvalidParams", 1000, "Item required"));
        var entityId = body.Entity?.Id ?? PlayFabIdFromRequest(request);
        var entityType = body.Entity?.Type ?? "title_player_account";
        var coll = string.IsNullOrEmpty(body.CollectionId) ? "Default" : body.CollectionId;
        try
        {
            var tx = SubtractInventoryInternal(entityId, entityType, coll, body.Item, body.Amount ?? 1, body.DeleteEmptyStacks);
            return Task.FromResult(Ok(new SubtractInventoryItemsResponse
            {
                ETag           = tx,
                IdempotencyId  = body.IdempotencyId ?? "",
                TransactionIds = new List<string> { tx },
            }));
        }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
    }

    /// <summary>Updates a specific stack of items for an entity.</summary>
    [HttpPost("/Inventory/UpdateInventoryItems")]
    public Task<IHttpActionResult> Economy_UpdateInventoryItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateInventoryItemsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId = body.Entity?.Id ?? PlayFabIdFromRequest(request);
        var entityType = body.Entity?.Type ?? "title_player_account";
        var coll = string.IsNullOrEmpty(body.CollectionId) ? "Default" : body.CollectionId;
        var tx = UpdateInventoryInternal(entityId, entityType, coll, body.Item);
        return Task.FromResult(Ok(new UpdateInventoryItemsResponse
        {
            ETag           = tx,
            IdempotencyId  = body.IdempotencyId ?? "",
            TransactionIds = new List<string> { tx },
        }));
    }

    /// <summary>Deletes a specific stack of items from an entity's inventory.</summary>
    [HttpPost("/Inventory/DeleteInventoryItems")]
    public Task<IHttpActionResult> Economy_DeleteInventoryItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteInventoryItemsRequest>(request);
        if (body == null || body.Item == null) return Task.FromResult(Err("InvalidParams", 1000, "Item required"));
        var entityId = body.Entity?.Id ?? PlayFabIdFromRequest(request);
        var coll = string.IsNullOrEmpty(body.CollectionId) ? "Default" : body.CollectionId;
        var tx = DeleteInventoryInternal(entityId, coll, body.Item);
        return Task.FromResult(Ok(new DeleteInventoryItemsResponse
        {
            ETag           = tx,
            IdempotencyId  = body.IdempotencyId ?? "",
            TransactionIds = new List<string> { tx },
        }));
    }

    /// <summary>Returns paged inventory items for an entity collection.</summary>
    [HttpPost("/Inventory/GetInventoryItems")]
    public Task<IHttpActionResult> Economy_GetInventoryItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetInventoryItemsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId = body.Entity?.Id ?? PlayFabIdFromRequest(request);
        var coll = string.IsNullOrEmpty(body.CollectionId) ? "Default" : body.CollectionId;
        var rows = Database.GetCollection<PlayfabEconomyInventoryDbObject>(true)!
                           .Find(x => x.EntityId == entityId && x.CollectionId == coll).ToList();
        var skip = 0;
        if (!string.IsNullOrEmpty(body.ContinuationToken)) int.TryParse(body.ContinuationToken, out skip);
        var take = body.Count > 0 ? body.Count : 100;
        var page = rows.Skip(skip).Take(take).ToList();
        var nextToken = skip + page.Count < rows.Count ? (skip + page.Count).ToString() : "";
        return Task.FromResult(Ok(new GetInventoryItemsResponse
        {
            Items             = page.Select(RowToInventoryItem).ToList(),
            ContinuationToken = nextToken,
            ETag              = NewId(),
        }));
    }

    /// <summary>Returns the list of collection ids for an entity.</summary>
    [HttpPost("/Inventory/GetInventoryCollectionIds")]
    public Task<IHttpActionResult> Economy_GetInventoryCollectionIds(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetInventoryCollectionIdsRequest>(request);
        var entityId = body?.Entity?.Id ?? PlayFabIdFromRequest(request);
        var ids = Database.GetCollection<PlayfabEconomyInventoryDbObject>(true)!
                          .Find(x => x.EntityId == entityId).Select(x => x.CollectionId).Distinct().ToList();
        return Task.FromResult(Ok(new GetInventoryCollectionIdsResponse
        {
            CollectionIds     = ids,
            ContinuationToken = "",
        }));
    }

    /// <summary>Deletes all items in a collection.</summary>
    [HttpPost("/Inventory/DeleteInventoryCollection")]
    public Task<IHttpActionResult> Economy_DeleteInventoryCollection(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteInventoryCollectionRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId = body.Entity?.Id ?? PlayFabIdFromRequest(request);
        var coll = string.IsNullOrEmpty(body.CollectionId) ? "Default" : body.CollectionId;
        Database.GetCollection<PlayfabEconomyInventoryDbObject>(true)!
                .DeleteMany(x => x.EntityId == entityId && x.CollectionId == coll);
        return Task.FromResult(Ok(new DeleteInventoryCollectionResponse()));
    }

    /// <summary>Executes a batch of inventory operations atomically.</summary>
    [HttpPost("/Inventory/ExecuteInventoryOperations")]
    public Task<IHttpActionResult> Economy_ExecuteInventoryOperations(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ExecuteInventoryOperationsRequest>(request);
        if (body == null || body.Operations == null) return Task.FromResult(Err("InvalidParams", 1000, "Operations required"));
        var entityId = body.Entity?.Id ?? PlayFabIdFromRequest(request);
        var entityType = body.Entity?.Type ?? "title_player_account";
        var coll = string.IsNullOrEmpty(body.CollectionId) ? "Default" : body.CollectionId;

        var txIds = new List<string>();
        foreach (var op in body.Operations)
        {
            try
            {
                if (op.Add != null && op.Add.Item != null)
                    txIds.Add(AddInventoryInternal(entityId, entityType, coll, op.Add.Item, op.Add.Amount ?? 1, op.Add.NewStackValues));
                else if (op.Subtract != null && op.Subtract.Item != null)
                    txIds.Add(SubtractInventoryInternal(entityId, entityType, coll, op.Subtract.Item, op.Subtract.Amount ?? 1, op.Subtract.DeleteEmptyStacks));
                else if (op.Delete != null && op.Delete.Item != null)
                    txIds.Add(DeleteInventoryInternal(entityId, coll, op.Delete.Item));
                else if (op.Update != null && op.Update.Item != null)
                    txIds.Add(UpdateInventoryInternal(entityId, entityType, coll, op.Update.Item));
                else if (op.Purchase != null && op.Purchase.Item != null)
                    txIds.Add(AddInventoryInternal(entityId, entityType, coll, op.Purchase.Item, op.Purchase.Amount ?? 1, op.Purchase.NewStackValues));
            }
            catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
        }
        return Task.FromResult(Ok(new ExecuteInventoryOperationsResponse
        {
            ETag           = NewId(),
            IdempotencyId  = body.IdempotencyId ?? "",
            TransactionIds = txIds,
        }));
    }

    /// <summary>Executes a batch of transfer operations between two entities.</summary>
    [HttpPost("/Inventory/ExecuteTransferOperations")]
    public Task<IHttpActionResult> Economy_ExecuteTransferOperations(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ExecuteTransferOperationsRequest>(request);
        if (body == null || body.Operations == null) return Task.FromResult(Err("InvalidParams", 1000, "Operations required"));
        var givEid = body.GivingEntity?.Id ?? PlayFabIdFromRequest(request);
        var givEt  = body.GivingEntity?.Type ?? "title_player_account";
        var givCol = string.IsNullOrEmpty(body.GivingCollectionId) ? "Default" : body.GivingCollectionId;
        var recEid = body.ReceivingEntity?.Id ?? "";
        var recEt  = body.ReceivingEntity?.Type ?? "title_player_account";
        var recCol = string.IsNullOrEmpty(body.ReceivingCollectionId) ? "Default" : body.ReceivingCollectionId;

        var giveTx = new List<string>();
        var recvTx = new List<string>();
        foreach (var op in body.Operations)
        {
            if (op.GivingItem == null || op.ReceivingItem == null) continue;
            try
            {
                giveTx.Add(SubtractInventoryInternal(givEid, givEt, givCol, op.GivingItem, op.Amount ?? 1, op.DeleteEmptyStacks));
                recvTx.Add(AddInventoryInternal(recEid, recEt, recCol, op.ReceivingItem, op.Amount ?? 1, op.NewStackValues));
            }
            catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
        }
        return Task.FromResult(Ok(new ExecuteTransferOperationsResponse
        {
            GivingETag              = NewId(),
            ReceivingETag           = NewId(),
            IdempotencyId           = body.IdempotencyId ?? "",
            GivingTransactionIds    = giveTx,
            ReceivingTransactionIds = recvTx,
            OperationStatus         = "Completed",
            OperationToken          = NewId(),
        }));
    }

    /// <summary>Returns the status of an asynchronous transfer operation.</summary>
    [HttpPost("/Inventory/GetInventoryOperationStatus")]
    public Task<IHttpActionResult> Economy_GetInventoryOperationStatus(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        // We execute synchronously, so all operations are always completed by the time this is called
        return Task.FromResult(Ok(new GetInventoryOperationStatusResponse { OperationStatus = "Completed" }));
    }

    /// <summary>Purchases items from a store using virtual currency or other items as payment.</summary>
    [HttpPost("/Inventory/PurchaseInventoryItems")]
    public Task<IHttpActionResult> Economy_PurchaseInventoryItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<PurchaseInventoryItemsRequest>(request);
        if (body == null || body.Item == null) return Task.FromResult(Err("InvalidParams", 1000, "Item required"));
        var entityId = body.Entity?.Id ?? PlayFabIdFromRequest(request);
        var entityType = body.Entity?.Type ?? "title_player_account";
        var coll = string.IsNullOrEmpty(body.CollectionId) ? "Default" : body.CollectionId;
        var amount = body.Amount ?? 1;

        // Subtract price amounts from inventory (currencies are inventory items)
        try
        {
            if (body.PriceAmounts != null)
            {
                foreach (var price in body.PriceAmounts)
                {
                    SubtractInventoryInternal(entityId, entityType, coll,
                        new InventoryItemReference { Id = price.ItemId, StackId = string.IsNullOrEmpty(price.StackId) ? "Default" : price.StackId },
                        price.Amount, body.DeleteEmptyStacks);
                }
            }
        }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }

        var tx = AddInventoryInternal(entityId, entityType, coll, body.Item, amount, body.NewStackValues);
        return Task.FromResult(Ok(new PurchaseInventoryItemsResponse
        {
            ETag           = tx,
            IdempotencyId  = body.IdempotencyId ?? "",
            TransactionIds = new List<string> { tx },
        }));
    }

    /// <summary>Returns the entity's inventory transaction history.</summary>
    [HttpPost("/Inventory/GetTransactionHistory")]
    public Task<IHttpActionResult> Economy_GetTransactionHistory(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetTransactionHistoryRequest>(request);
        if (body == null) body = new GetTransactionHistoryRequest();
        var entityId = body.Entity?.Id ?? PlayFabIdFromRequest(request);
        var rows = Database.GetCollection<PlayfabTransactionDbObject>(true)!
                           .Find(t => t.EntityId == entityId)
                           .OrderByDescending(t => t.Timestamp).ToList();
        var skip = 0;
        if (!string.IsNullOrEmpty(body.ContinuationToken)) int.TryParse(body.ContinuationToken, out skip);
        var take = body.Count > 0 ? body.Count : 100;
        var page = rows.Skip(skip).Take(take).Select(t => new Transaction
        {
            TransactionId = t.TransactionId,
            ApiName       = "Inventory",
            OperationType = t.Operation,
            ItemType      = "Item",
            Timestamp     = DateTimeOffset.FromUnixTimeMilliseconds(t.Timestamp).UtcDateTime,
            Operations    = new List<TransactionOperation>
            {
                new() { ItemId = t.ItemId, Amount = t.Amount, Type = t.Operation }
            }
        }).ToList();
        var nextToken = skip + page.Count < rows.Count ? (skip + page.Count).ToString() : "";
        return Task.FromResult(Ok(new GetTransactionHistoryResponse { Transactions = page, ContinuationToken = nextToken }));
    }

    /* ─── Marketplace redemptions ─────────────────────────────────────────── */

    private RedemptionSuccess StubRedeem(string marketplaceId)
    {
        return new RedemptionSuccess
        {
            MarketplaceTransactionId = marketplaceId,
            MarketplaceAlternateId   = marketplaceId,
            SuccessTimestamp         = DateTime.UtcNow,
        };
    }

    /// <summary>Redeems Apple App Store receipts (treated as a credit-only operation here).</summary>
    [HttpPost("/Inventory/RedeemAppleAppStoreInventoryItems")]
    public Task<IHttpActionResult> Economy_RedeemAppleAppStore(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RedeemAppleAppStoreInventoryItemsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var txId = NewId();
        return Task.FromResult(Ok(new RedeemAppleAppStoreInventoryItemsResponse
        {
            Succeeded      = string.IsNullOrEmpty(body.Receipt) ? new() : new() { StubRedeem(body.Receipt) },
            Failed         = new(),
            TransactionIds = new List<string> { txId },
        }));
    }

    /// <summary>Redeems Apple App Store JWS-formatted receipts.</summary>
    [HttpPost("/Inventory/RedeemAppleAppStoreWithJwsInventoryItems")]
    public Task<IHttpActionResult> Economy_RedeemAppleAppStoreJws(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RedeemAppleAppStoreWithJwsInventoryItemsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var succeeded = (body.JWSTransactions ?? new()).Select(StubRedeem).ToList();
        return Task.FromResult(Ok(new RedeemAppleAppStoreWithJwsInventoryItemsResponse
        {
            Succeeded      = succeeded,
            Failed         = new(),
            TransactionIds = succeeded.Select(_ => NewId()).ToList(),
        }));
    }

    /// <summary>Redeems Google Play purchase tokens.</summary>
    [HttpPost("/Inventory/RedeemGooglePlayInventoryItems")]
    public Task<IHttpActionResult> Economy_RedeemGooglePlay(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RedeemGooglePlayInventoryItemsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var succeeded = (body.Purchases ?? new()).Select(p => StubRedeem(p?.ProductId ?? "")).ToList();
        return Task.FromResult(Ok(new RedeemGooglePlayInventoryItemsResponse
        {
            Succeeded      = succeeded,
            Failed         = new(),
            TransactionIds = succeeded.Select(_ => NewId()).ToList(),
        }));
    }

    /// <summary>Redeems Microsoft Store entitlements.</summary>
    [HttpPost("/Inventory/RedeemMicrosoftStoreInventoryItems")]
    public Task<IHttpActionResult> Economy_RedeemMicrosoftStore(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RedeemMicrosoftStoreInventoryItemsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var txId = NewId();
        return Task.FromResult(Ok(new RedeemMicrosoftStoreInventoryItemsResponse
        {
            Succeeded      = string.IsNullOrEmpty(body.XboxToken) ? new() : new() { StubRedeem(body.XboxToken) },
            Failed         = new(),
            TransactionIds = new List<string> { txId },
        }));
    }

    /// <summary>Redeems Nintendo eShop entitlements.</summary>
    [HttpPost("/Inventory/RedeemNintendoEShopInventoryItems")]
    public Task<IHttpActionResult> Economy_RedeemNintendoEShop(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RedeemNintendoEShopInventoryItemsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var txId = NewId();
        return Task.FromResult(Ok(new RedeemNintendoEShopInventoryItemsResponse
        {
            Succeeded      = string.IsNullOrEmpty(body.NintendoServiceAccountIdToken) ? new() : new() { StubRedeem(body.NintendoServiceAccountIdToken) },
            Failed         = new(),
            TransactionIds = new List<string> { txId },
        }));
    }

    /// <summary>Redeems PlayStation Store entitlements.</summary>
    [HttpPost("/Inventory/RedeemPlayStationStoreInventoryItems")]
    public Task<IHttpActionResult> Economy_RedeemPlayStationStore(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RedeemPlayStationStoreInventoryItemsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var txId = NewId();
        return Task.FromResult(Ok(new RedeemPlayStationStoreInventoryItemsResponse
        {
            Succeeded      = string.IsNullOrEmpty(body.AuthorizationCode) ? new() : new() { StubRedeem(body.AuthorizationCode) },
            Failed         = new(),
            TransactionIds = new List<string> { txId },
        }));
    }

    /// <summary>Redeems Steam wallet items.</summary>
    [HttpPost("/Inventory/RedeemSteamInventoryItems")]
    public Task<IHttpActionResult> Economy_RedeemSteam(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RedeemSteamInventoryItemsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        return Task.FromResult(Ok(new RedeemSteamInventoryItemsResponse
        {
            Succeeded      = new(),
            Failed         = new(),
            TransactionIds = new List<string>(),
        }));
    }

    /// <summary>Transfers items between two entities/collections.</summary>
    [HttpPost("/Inventory/TransferInventoryItems")]
    public Task<IHttpActionResult> Economy_TransferInventoryItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<TransferInventoryItemsRequest>(request);
        if (body == null || body.GivingItem == null || body.ReceivingItem == null)
            return Task.FromResult(Err("InvalidParams", 1000, "GivingItem and ReceivingItem are required"));
        var givEid = body.GivingEntity?.Id ?? PlayFabIdFromRequest(request);
        var givEt  = body.GivingEntity?.Type ?? "title_player_account";
        var givCol = string.IsNullOrEmpty(body.GivingCollectionId) ? "Default" : body.GivingCollectionId;
        var recEid = body.ReceivingEntity?.Id ?? "";
        var recEt  = body.ReceivingEntity?.Type ?? "title_player_account";
        var recCol = string.IsNullOrEmpty(body.ReceivingCollectionId) ? "Default" : body.ReceivingCollectionId;
        try
        {
            var giveTx = SubtractInventoryInternal(givEid, givEt, givCol, body.GivingItem, body.Amount ?? 1, body.DeleteEmptyStacks);
            var recvTx = AddInventoryInternal(recEid, recEt, recCol, body.ReceivingItem, body.Amount ?? 1, body.NewStackValues);
            return Task.FromResult(Ok(new TransferInventoryItemsResponse
            {
                GivingETag              = NewId(),
                IdempotencyId           = body.IdempotencyId ?? "",
                GivingTransactionIds    = new List<string> { giveTx },
                ReceivingTransactionIds = new List<string> { recvTx },
                OperationStatus         = "Completed",
                OperationToken          = NewId(),
            }));
        }
        catch (PlayfabFault f) { return Task.FromResult(Err(f.Error, f.ErrorCode, f.Message, f.HttpStatus)); }
    }
}
