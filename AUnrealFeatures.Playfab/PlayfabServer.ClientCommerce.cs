using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Database;
using AUnrealFeatures.Playfab.Models;
using PlayFab.ClientModels;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed class PlayfabPendingPurchaseDbObject : AstraDbObject
{
    public string OrderId { get; set; } = "";
    public string PlayFabId { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public string StoreId { get; set; } = "";
    public List<string> ItemIds { get; set; } = new();
    public uint Total { get; set; }
    public string Currency { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Status { get; set; } = "Init"; // Init | Approved | Confirmed | Failed
    public long Created { get; set; }
}

public sealed partial class PlayfabServer
{
    private static CatalogItem CatalogRowToClient(PlayfabCatalogItemDbObject r)
    {
        CatalogItem item = null;
        // Prefer the original full PlayFab JSON if present — that way Bundle / Consumable /
        // Container / DeepLinks etc. roundtrip exactly as the developer uploaded them.
        if (!string.IsNullOrEmpty(r.FullJson))
        {
            try { item = JsonSerializer.Deserialize<CatalogItem>(r.FullJson, JsonOpts); }
            catch { /* fall through to indexed-column reconstruction */ }
        }
        item ??= new CatalogItem
        {
            ItemId                = r.ItemId,
            ItemClass             = r.ItemClass,
            CatalogVersion        = r.CatalogVersion,
            DisplayName           = r.DisplayName,
            Description           = r.Description,
            VirtualCurrencyPrices = r.VirtualCurrencyPrices,
            RealCurrencyPrices    = r.RealCurrencyPrices,
            Tags                  = r.Tags,
            CustomData            = r.CustomData,
            IsLimitedEdition      = r.IsLimitedEdition,
            IsStackable           = r.IsStackable,
            IsTradable            = r.IsTradable,
            ItemImageUrl          = r.ItemImageUrl,
            InitialLimitedEditionCount = r.InitialLimitedEditionCount,
        };

        // PlayFab Unity SDK clients often do `item.VirtualCurrencyPrices.TryGetValue(...)` and
        // `item.Bundle.BundledItems.ToArray()` directly — null containers there throw on the
        // client. Initialize the optional collection fields so the client never sees null.
        item.VirtualCurrencyPrices ??= new Dictionary<string, uint>();
        item.RealCurrencyPrices    ??= new Dictionary<string, uint>();
        item.Tags                  ??= new List<string>();
        if (item.Bundle != null)
        {
            item.Bundle.BundledItems              ??= new List<string>();
            item.Bundle.BundledVirtualCurrencies  ??= new Dictionary<string, uint>();
            item.Bundle.BundledResultTables       ??= new List<string>();
        }
        if (item.Container != null)
        {
            item.Container.ItemContents              ??= new List<string>();
            item.Container.VirtualCurrencyContents   ??= new Dictionary<string, uint>();
            item.Container.ResultTableContents       ??= new List<string>();
        }
        return item;
    }

    /* ─── Catalog & Stores ────────────────────────────────────────────────── */

    /// <summary>Returns the title's catalog (legacy v1).</summary>
    [HttpPost("/Client/GetCatalogItems")]
    public Task<IHttpActionResult> Client_GetCatalogItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetCatalogItemsRequest>(request) ?? new GetCatalogItemsRequest();
        var v = body.CatalogVersion;
        var rows = Database.GetCollection<PlayfabCatalogItemDbObject>(true)!
                           .Find(x => string.IsNullOrEmpty(v) || x.CatalogVersion == v).ToList();
        return Task.FromResult(Ok(new GetCatalogItemsResult { Catalog = rows.Select(CatalogRowToClient).ToList() }));
    }

    /// <summary>Returns store items.</summary>
    [HttpPost("/Client/GetStoreItems")]
    public Task<IHttpActionResult> Client_GetStoreItems(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetStoreItemsRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.StoreId))
            return Task.FromResult(Err("InvalidParams", 1000, "StoreId required"));
        var sid = body.StoreId;
        var rows = Database.GetCollection<PlayfabStoreItemDbObject>(true)!
                           .Find(x => x.StoreId == sid && (string.IsNullOrEmpty(body.CatalogVersion) || x.CatalogVersion == body.CatalogVersion)).ToList();
        return Task.FromResult(Ok(new GetStoreItemsResult
        {
            CatalogVersion = body.CatalogVersion ?? "",
            StoreId        = body.StoreId,
            Store          = rows.Select(r => new StoreItem
            {
                ItemId                = r.ItemId,
                VirtualCurrencyPrices = r.VirtualCurrencyPrices,
                RealCurrencyPrices    = r.RealCurrencyPrices,
            }).ToList()
        }));
    }

    /// <summary>Returns the player's inventory and virtual currencies. The VirtualCurrency dict
    /// always contains every currency the title defined (zero-filled for ones the player has no
    /// row for), matching real PlayFab — clients commonly do `result.VirtualCurrency["GD"]` and
    /// expect a Dictionary lookup, not a TryGetValue.</summary>
    [HttpPost("/Client/GetUserInventory")]
    public Task<IHttpActionResult> Client_GetUserInventory(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());
        var inventory = Database.GetCollection<PlayfabInventoryItemDbObject>(true)!
                                .Find(i => i.PlayFabId == pid && string.IsNullOrEmpty(i.CharacterId))
                                .Select(InventoryRowToInstance).ToList();
        var currencies = AllPlayerCurrencies(pid);
        return Task.FromResult(Ok(new GetUserInventoryResult
        {
            Inventory       = inventory,
            VirtualCurrency = currencies,
            VirtualCurrencyRechargeTimes = new(),
        }));
    }

    /// <summary>Returns the player's full balance map, zero-filled with every defined virtual
    /// currency type for the title. Both the player's actual balances and any title-defined
    /// currency they haven't earned yet are present in the result so client code that does
    /// `result.VirtualCurrency[code]` never throws.</summary>
    private Dictionary<string, int> AllPlayerCurrencies(string playFabId)
    {
        var defined = Database.GetCollection<PlayfabVirtualCurrencyDefDbObject>(true)!
                              .Find(d => d.TitleId == TITLE_ID).ToList();
        var ownedRows = Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!
                                .Find(v => v.PlayFabId == playFabId && string.IsNullOrEmpty(v.CharacterId)).ToList();
        var owned = ownedRows.ToDictionary(v => v.CurrencyCode, v => v.Balance);
        var result = new Dictionary<string, int>();
        foreach (var d in defined)
            result[d.CurrencyCode] = owned.TryGetValue(d.CurrencyCode, out var b1) ? b1 : 0;
        // Walk the catalog so any currency code referenced by item prices (e.g. uploaded DLC
        // items use "SR" before the title formally defined the currency) is also surfaced.
        var catalog = Database.GetCollection<PlayfabCatalogItemDbObject>(true)!.FindAll();
        foreach (var c in catalog)
            foreach (var code in c.VirtualCurrencyPrices.Keys)
                if (!result.ContainsKey(code))
                    result[code] = owned.TryGetValue(code, out var b2) ? b2 : 0;
        // Always include any code the player actually has a row for, even if it was deleted
        // from the catalog or never formally defined.
        foreach (var (code, bal) in owned)
            if (!result.ContainsKey(code)) result[code] = bal;
        return result;
    }

    /* ─── Virtual currencies ──────────────────────────────────────────────── */

    private (int balance, int change) ModifyCurrency(string playFabId, string currencyCode, int delta)
    {
        var coll = Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!;
        var existing = coll.FindOne(v => v.PlayFabId == playFabId && string.IsNullOrEmpty(v.CharacterId) && v.CurrencyCode == currencyCode);
        if (existing == null)
        {
            existing = new PlayfabVirtualCurrencyDbObject
            {
                PlayFabId    = playFabId,
                CurrencyCode = currencyCode,
                Balance      = Math.Max(0, delta),
            };
            coll.Insert(existing);
            return (existing.Balance, existing.Balance);
        }
        var newBalance = Math.Max(0, existing.Balance + delta);
        var realChange = newBalance - existing.Balance;
        existing.Balance = newBalance;
        coll.Update(existing);
        return (newBalance, realChange);
    }

    /// <summary>Adds virtual currency to the player.</summary>
    [HttpPost("/Client/AddUserVirtualCurrency")]
    public Task<IHttpActionResult> Client_AddUserVirtualCurrency(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<AddUserVirtualCurrencyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.VirtualCurrency))
            return Task.FromResult(Err("InvalidParams", 1000, "VirtualCurrency required"));
        var pid = PlayFabIdFromRequest(request);
        var (balance, change) = ModifyCurrency(pid, body.VirtualCurrency, body.Amount);
        return Task.FromResult(Ok(new ModifyUserVirtualCurrencyResult
        {
            PlayFabId       = pid,
            VirtualCurrency = body.VirtualCurrency,
            Balance         = balance,
            BalanceChange   = change,
        }));
    }

    /// <summary>Subtracts virtual currency from the player.</summary>
    [HttpPost("/Client/SubtractUserVirtualCurrency")]
    public Task<IHttpActionResult> Client_SubtractUserVirtualCurrency(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SubtractUserVirtualCurrencyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.VirtualCurrency))
            return Task.FromResult(Err("InvalidParams", 1000, "VirtualCurrency required"));
        var pid = PlayFabIdFromRequest(request);
        var (balance, change) = ModifyCurrency(pid, body.VirtualCurrency, -body.Amount);
        return Task.FromResult(Ok(new ModifyUserVirtualCurrencyResult
        {
            PlayFabId       = pid,
            VirtualCurrency = body.VirtualCurrency,
            Balance         = balance,
            BalanceChange   = change,
        }));
    }

    /* ─── Item purchase / consume ─────────────────────────────────────────── */

    private ItemInstance GrantItemInstance(string playFabId, string? characterId, PlayfabCatalogItemDbObject catalog)
    {
        var inst = new PlayfabInventoryItemDbObject
        {
            PlayFabId      = playFabId,
            CharacterId    = characterId ?? "",
            ItemInstanceId = NewId(),
            ItemId         = catalog.ItemId,
            ItemClass      = catalog.ItemClass,
            CatalogVersion = catalog.CatalogVersion,
            DisplayName    = catalog.DisplayName,
            PurchaseDate   = NowUnix(),
        };
        Database.GetCollection<PlayfabInventoryItemDbObject>(true)!.Insert(inst);
        return InventoryRowToInstance(inst);
    }

    /// <summary>Purchases an item using virtual currency.</summary>
    [HttpPost("/Client/PurchaseItem")]
    public Task<IHttpActionResult> Client_PurchaseItem(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<PurchaseItemRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ItemId))
            return Task.FromResult(Err("InvalidParams", 1000, "ItemId required"));
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var iid = body.ItemId;
        var v = body.CatalogVersion;
        var catalog = Database.GetCollection<PlayfabCatalogItemDbObject>(true)!
                              .FindOne(c => c.ItemId == iid && (string.IsNullOrEmpty(v) || c.CatalogVersion == v));
        if (catalog == null) return Task.FromResult(Err("ItemNotFound", 1430, "Item not found", HttpStatusCode.NotFound));

        // Deduct currency
        if (!string.IsNullOrEmpty(body.VirtualCurrency) && body.Price > 0)
        {
            var (balance, _) = ModifyCurrency(pid, body.VirtualCurrency, -body.Price);
            if (balance < 0) return Task.FromResult(Err("InsufficientFunds", 1059, "Insufficient currency"));
        }
        var inst = GrantItemInstance(pid, body.CharacterId, catalog);
        return Task.FromResult(Ok(new PurchaseItemResult { Items = new List<ItemInstance> { inst } }));
    }

    /// <summary>Consumes uses from an item instance.</summary>
    [HttpPost("/Client/ConsumeItem")]
    public Task<IHttpActionResult> Client_ConsumeItem(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ConsumeItemRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ItemInstanceId))
            return Task.FromResult(Err("InvalidParams", 1000, "ItemInstanceId required"));
        var pid = PlayFabIdFromRequest(request);
        var coll = Database.GetCollection<PlayfabInventoryItemDbObject>(true)!;
        var iid = body.ItemInstanceId;
        var inst = coll.FindOne(i => i.PlayFabId == pid && i.ItemInstanceId == iid);
        if (inst == null) return Task.FromResult(Err("ItemNotOwned", 1431, "Item not owned", HttpStatusCode.NotFound));
        var remaining = (inst.RemainingUses ?? 1) - body.ConsumeCount;
        if (remaining <= 0)
        {
            coll.Delete(inst.Id);
            remaining = 0;
        }
        else
        {
            inst.RemainingUses = remaining;
            coll.Update(inst);
        }
        return Task.FromResult(Ok(new ConsumeItemResult { ItemInstanceId = inst.ItemInstanceId, RemainingUses = remaining }));
    }

    /// <summary>Redeems a coupon code; grants any associated items.</summary>
    [HttpPost("/Client/RedeemCoupon")]
    public Task<IHttpActionResult> Client_RedeemCoupon(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RedeemCouponRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.CouponCode))
            return Task.FromResult(Err("InvalidParams", 1000, "CouponCode required"));
        // No coupon registry — return empty grant successfully
        return Task.FromResult(Ok(new RedeemCouponResult { GrantedItems = new List<ItemInstance>() }));
    }

    /// <summary>Unlocks a container item by item id.</summary>
    [HttpPost("/Client/UnlockContainerItem")]
    public Task<IHttpActionResult> Client_UnlockContainerItem(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UnlockContainerItemRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ContainerItemId))
            return Task.FromResult(Err("InvalidParams", 1000, "ContainerItemId required"));
        return Task.FromResult(Ok(new UnlockContainerItemResult
        {
            UnlockedItemInstanceId = NewId(),
            GrantedItems           = new(),
            VirtualCurrency        = new(),
        }));
    }

    /// <summary>Unlocks a container item by instance id.</summary>
    [HttpPost("/Client/UnlockContainerInstance")]
    public Task<IHttpActionResult> Client_UnlockContainerInstance(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UnlockContainerInstanceRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.ContainerItemInstanceId))
            return Task.FromResult(Err("InvalidParams", 1000, "ContainerItemInstanceId required"));
        return Task.FromResult(Ok(new UnlockContainerItemResult
        {
            UnlockedItemInstanceId      = body.ContainerItemInstanceId,
            UnlockedWithItemInstanceId  = body.KeyItemInstanceId,
            GrantedItems                = new(),
            VirtualCurrency             = new(),
        }));
    }

    /* ─── Real-money purchase flow ────────────────────────────────────────── */

    /// <summary>Initiates a real-money purchase order.</summary>
    [HttpPost("/Client/StartPurchase")]
    public Task<IHttpActionResult> Client_StartPurchase(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<StartPurchaseRequest>(request);
        if (body == null || body.Items == null) return Task.FromResult(Err("InvalidParams", 1000, "Items required"));
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var orderId = NewId();
        var ids = body.Items.Select(i => i.ItemId).Where(id => !string.IsNullOrEmpty(id)).ToList();
        Database.GetCollection<PlayfabPendingPurchaseDbObject>(true)!.Insert(new PlayfabPendingPurchaseDbObject
        {
            OrderId        = orderId,
            PlayFabId      = pid,
            CatalogVersion = body.CatalogVersion ?? "",
            StoreId        = body.StoreId ?? "",
            ItemIds        = ids,
            Status         = "Init",
            Created        = NowUnix(),
        });

        var contents = new List<CartItem>();
        foreach (var pi in body.Items)
        {
            var iid = pi.ItemId;
            var ci = Database.GetCollection<PlayfabCatalogItemDbObject>(true)!.FindOne(c => c.ItemId == iid);
            contents.Add(new CartItem
            {
                ItemId             = pi.ItemId,
                DisplayName        = ci?.DisplayName ?? "",
                Description        = ci?.Description ?? "",
                ItemClass          = ci?.ItemClass ?? "",
                ItemInstanceId     = NewId(),
                VirtualCurrencyPrices = ci?.VirtualCurrencyPrices ?? new(),
                RealCurrencyPrices = ci?.RealCurrencyPrices ?? new(),
            });
        }
        return Task.FromResult(Ok(new StartPurchaseResult
        {
            OrderId        = orderId,
            Contents       = contents,
            PaymentOptions = new List<PaymentOption>
            {
                new() { Currency = "USD", Price = 100, ProviderName = "Test", StoreCredit = 0 }
            },
            VirtualCurrencyBalances = Database.GetCollection<PlayfabVirtualCurrencyDbObject>(true)!
                                              .Find(v => v.PlayFabId == pid && string.IsNullOrEmpty(v.CharacterId))
                                              .ToDictionary(v => v.CurrencyCode, v => v.Balance),
        }));
    }

    /// <summary>Submits payment for an order (records as Approved).</summary>
    [HttpPost("/Client/PayForPurchase")]
    public Task<IHttpActionResult> Client_PayForPurchase(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<PayForPurchaseRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.OrderId))
            return Task.FromResult(Err("InvalidParams", 1000, "OrderId required"));
        var coll = Database.GetCollection<PlayfabPendingPurchaseDbObject>(true)!;
        var oid = body.OrderId;
        var p = coll.FindOne(x => x.OrderId == oid);
        if (p == null) return Task.FromResult(Err("InvalidOrderId", 1432, "Order not found", HttpStatusCode.NotFound));
        p.Status   = "Approved";
        p.Currency = body.Currency ?? "USD";
        p.Provider = body.ProviderName ?? "Test";
        coll.Update(p);
        return Task.FromResult(Ok(new PayForPurchaseResult
        {
            OrderId          = oid,
            ProviderToken    = NewId(),
            Status           = TransactionStatus.Approved,
            PurchaseCurrency = p.Currency,
            PurchasePrice    = p.Total,
        }));
    }

    /// <summary>Confirms a pending purchase, granting the items.</summary>
    [HttpPost("/Client/ConfirmPurchase")]
    public Task<IHttpActionResult> Client_ConfirmPurchase(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ConfirmPurchaseRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.OrderId))
            return Task.FromResult(Err("InvalidParams", 1000, "OrderId required"));
        var coll = Database.GetCollection<PlayfabPendingPurchaseDbObject>(true)!;
        var oid = body.OrderId;
        var p = coll.FindOne(x => x.OrderId == oid);
        if (p == null) return Task.FromResult(Err("InvalidOrderId", 1432, "Order not found", HttpStatusCode.NotFound));

        var items = new List<ItemInstance>();
        foreach (var iid in p.ItemIds)
        {
            var ci = Database.GetCollection<PlayfabCatalogItemDbObject>(true)!.FindOne(c => c.ItemId == iid);
            if (ci != null) items.Add(GrantItemInstance(p.PlayFabId, null, ci));
        }
        p.Status = "Confirmed";
        coll.Update(p);

        // Persist the purchase record
        Database.GetCollection<PlayfabPurchaseDbObject>(true)!.Insert(new PlayfabPurchaseDbObject
        {
            OrderId         = oid,
            PlayFabId       = p.PlayFabId,
            PaymentProvider = p.Provider,
            TransactionId   = NewId(),
            ItemInstanceIds = items.Select(i => i.ItemInstanceId).ToList(),
            Amount          = p.Total,
            Currency        = p.Currency,
            PurchaseDate    = NowUnix(),
        });

        return Task.FromResult(Ok(new ConfirmPurchaseResult
        {
            OrderId      = oid,
            Items        = items,
            PurchaseDate = DateTime.UtcNow,
        }));
    }

    /// <summary>Returns details for a recorded purchase.</summary>
    [HttpPost("/Client/GetPurchase")]
    public Task<IHttpActionResult> Client_GetPurchase(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPurchaseRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.OrderId))
            return Task.FromResult(Err("InvalidParams", 1000, "OrderId required"));
        var oid = body.OrderId;
        var p = Database.GetCollection<PlayfabPurchaseDbObject>(true)!.FindOne(x => x.OrderId == oid);
        if (p == null) return Task.FromResult(Err("OrderNotFound", 1432, "Order not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new GetPurchaseResult
        {
            OrderId           = p.OrderId,
            PaymentProvider   = p.PaymentProvider,
            TransactionId     = p.TransactionId,
            PurchaseDate      = DateTimeOffset.FromUnixTimeSeconds(p.PurchaseDate).UtcDateTime,
            TransactionStatus = "Confirmed",
        }));
    }

    /// <summary>Returns a payment-provider token for the user.</summary>
    [HttpPost("/Client/GetPaymentToken")]
    public Task<IHttpActionResult> Client_GetPaymentToken(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPaymentTokenRequest>(request);
        return Task.FromResult(Ok(new GetPaymentTokenResult
        {
            ProviderToken = NewId(),
            OrderId       = NewId(),
        }));
    }

    /// <summary>Returns a Photon authentication token bound to the player. The token is a JWT signed
    /// with the entity key and is also persisted as a one-shot ticket so /photon/authenticate can
    /// validate it without recomputing the signature.</summary>
    [HttpPost("/Client/GetPhotonAuthenticationToken")]
    public Task<IHttpActionResult> Client_GetPhotonAuthenticationToken(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetPhotonAuthenticationTokenRequest>(request);
        var pid  = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());

        var expSec = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
        var token  = GenerateJwt(new { sub = pid, photon = true, exp = expSec }, ENTITY_KEY);

        // Persist a one-shot ticket so the Photon auth endpoint can match {username, token}
        Database.GetCollection<PlayfabPhotonTicketDbObject>(true)!.Insert(new PlayfabPhotonTicketDbObject
        {
            PlayFabId   = pid,
            Token       = token,
            PhotonAppId = body?.PhotonApplicationId ?? "",
            Issued      = NowUnix(),
            Expires     = expSec,
            Used        = false,
        });

        return Task.FromResult(Ok(new GetPhotonAuthenticationTokenResult { PhotonCustomAuthenticationToken = token }));
    }

    /// <summary>Returns a content download URL.</summary>
    [HttpPost("/Client/GetContentDownloadUrl")]
    public Task<IHttpActionResult> Client_GetContentDownloadUrl(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetContentDownloadUrlRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Key))
            return Task.FromResult(Err("InvalidParams", 1000, "Key required"));
        return Task.FromResult(Ok(new GetContentDownloadUrlResult
        {
            URL = $"/Content/Download/{Uri.EscapeDataString(body.Key)}",
        }));
    }

    /* ─── Trades ──────────────────────────────────────────────────────────── */

    private TradeInfo TradeRowToDto(PlayfabTradeDbObject t) => new()
    {
        TradeId                     = t.TradeId,
        OfferingPlayerId            = t.OfferingPlayerId,
        AllowedPlayerIds            = t.AllowedPlayerIds,
        OfferedInventoryInstanceIds = t.OfferedItems,
        RequestedCatalogItemIds     = t.RequestedCatalogItemIds,
        Status                      = Enum.TryParse<TradeStatus>(t.Status, out var s) ? s : TradeStatus.Open,
        OpenedAt                    = DateTimeOffset.FromUnixTimeSeconds(t.Created).UtcDateTime,
        FilledAt                    = t.Filled.HasValue ? DateTimeOffset.FromUnixTimeSeconds(t.Filled.Value).UtcDateTime : (DateTime?)null,
    };

    /// <summary>Opens a new trade offer.</summary>
    [HttpPost("/Client/OpenTrade")]
    public Task<IHttpActionResult> Client_OpenTrade(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<OpenTradeRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var pid = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(pid)) return Task.FromResult(Unauthorized());
        var t = new PlayfabTradeDbObject
        {
            TradeId                 = NewId(),
            OfferingPlayerId        = pid,
            AllowedPlayerIds        = body.AllowedPlayerIds ?? new(),
            OfferedItems            = body.OfferedInventoryInstanceIds ?? new(),
            RequestedCatalogItemIds = body.RequestedCatalogItemIds ?? new(),
            Status                  = "Open",
            Created                 = NowUnix(),
        };
        Database.GetCollection<PlayfabTradeDbObject>(true)!.Insert(t);
        return Task.FromResult(Ok(new OpenTradeResponse { Trade = TradeRowToDto(t) }));
    }

    /// <summary>Accepts a trade offer.</summary>
    [HttpPost("/Client/AcceptTrade")]
    public Task<IHttpActionResult> Client_AcceptTrade(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<AcceptTradeRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.TradeId))
            return Task.FromResult(Err("InvalidParams", 1000, "TradeId required"));
        var coll = Database.GetCollection<PlayfabTradeDbObject>(true)!;
        var tid = body.TradeId;
        var t = coll.FindOne(x => x.TradeId == tid);
        if (t == null) return Task.FromResult(Err("TradeNotFound", 1495, "Trade not found", HttpStatusCode.NotFound));
        if (t.Status != "Open") return Task.FromResult(Err("TradeNotOpen", 1497, "Trade is not open"));
        t.Status = "Filled";
        t.Filled = NowUnix();
        coll.Update(t);
        return Task.FromResult(Ok(new AcceptTradeResponse { Trade = TradeRowToDto(t) }));
    }

    /// <summary>Cancels an open trade.</summary>
    [HttpPost("/Client/CancelTrade")]
    public Task<IHttpActionResult> Client_CancelTrade(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CancelTradeRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.TradeId))
            return Task.FromResult(Err("InvalidParams", 1000, "TradeId required"));
        var coll = Database.GetCollection<PlayfabTradeDbObject>(true)!;
        var tid = body.TradeId;
        var t = coll.FindOne(x => x.TradeId == tid);
        if (t == null) return Task.FromResult(Err("TradeNotFound", 1495, "Trade not found", HttpStatusCode.NotFound));
        t.Status = "Cancelled";
        coll.Update(t);
        return Task.FromResult(Ok(new CancelTradeResponse { Trade = TradeRowToDto(t) }));
    }

    /// <summary>Returns the player's trades (open & accepted).</summary>
    [HttpPost("/Client/GetPlayerTrades")]
    public Task<IHttpActionResult> Client_GetPlayerTrades(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<GetPlayerTradesRequest>(request);
        var pid = PlayFabIdFromRequest(request);
        var rows = Database.GetCollection<PlayfabTradeDbObject>(true)!.FindAll().ToList();
        var open = rows.Where(t => t.OfferingPlayerId == pid && t.Status == "Open").Select(TradeRowToDto).ToList();
        var accepted = rows.Where(t => t.Status == "Filled" && (t.OfferingPlayerId == pid || (t.AllowedPlayerIds?.Contains(pid) ?? false))).Select(TradeRowToDto).ToList();
        return Task.FromResult(Ok(new GetPlayerTradesResponse { OpenedTrades = open, AcceptedTrades = accepted }));
    }

    /// <summary>Returns the status of a single trade.</summary>
    [HttpPost("/Client/GetTradeStatus")]
    public Task<IHttpActionResult> Client_GetTradeStatus(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetTradeStatusRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.TradeId))
            return Task.FromResult(Err("InvalidParams", 1000, "TradeId required"));
        var tid = body.TradeId;
        var t = Database.GetCollection<PlayfabTradeDbObject>(true)!.FindOne(x => x.TradeId == tid);
        if (t == null) return Task.FromResult(Err("TradeNotFound", 1495, "Trade not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new GetTradeStatusResponse { Trade = TradeRowToDto(t) }));
    }

    /* ─── Marketplace receipt validation ──────────────────────────────────── */

    private static List<PurchaseReceiptFulfillment> EmptyFulfillment() => new() { new PurchaseReceiptFulfillment { FulfilledItems = new() } };

    /// <summary>Validates an Amazon IAP receipt.</summary>
    [HttpPost("/Client/ValidateAmazonIAPReceipt")]
    public Task<IHttpActionResult> Client_ValidateAmazonIAPReceipt(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<ValidateAmazonReceiptRequest>(request);
        return Task.FromResult(Ok(new ValidateAmazonReceiptResult { Fulfillments = EmptyFulfillment() }));
    }

    /// <summary>Validates a Google Play purchase token.</summary>
    [HttpPost("/Client/ValidateGooglePlayPurchase")]
    public Task<IHttpActionResult> Client_ValidateGooglePlayPurchase(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<ValidateGooglePlayPurchaseRequest>(request);
        return Task.FromResult(Ok(new ValidateGooglePlayPurchaseResult { Fulfillments = EmptyFulfillment() }));
    }

    /// <summary>Validates an iOS receipt.</summary>
    [HttpPost("/Client/ValidateIOSReceipt")]
    public Task<IHttpActionResult> Client_ValidateIOSReceipt(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<ValidateIOSReceiptRequest>(request);
        return Task.FromResult(Ok(new ValidateIOSReceiptResult { Fulfillments = EmptyFulfillment() }));
    }

    /// <summary>Validates a Windows Store receipt.</summary>
    [HttpPost("/Client/ValidateWindowsStoreReceipt")]
    public Task<IHttpActionResult> Client_ValidateWindowsStoreReceipt(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<ValidateWindowsReceiptRequest>(request);
        return Task.FromResult(Ok(new ValidateWindowsReceiptResult { Fulfillments = EmptyFulfillment() }));
    }

    /// <summary>Restores iOS purchases.</summary>
    [HttpPost("/Client/RestoreIOSPurchases")]
    public Task<IHttpActionResult> Client_RestoreIOSPurchases(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<RestoreIOSPurchasesRequest>(request);
        return Task.FromResult(Ok(new RestoreIOSPurchasesResult { Fulfillments = EmptyFulfillment() }));
    }

    /// <summary>Consumes PSN entitlements.</summary>
    [HttpPost("/Client/ConsumePSNEntitlements")]
    public Task<IHttpActionResult> Client_ConsumePSNEntitlements(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<ConsumePSNEntitlementsRequest>(request);
        return Task.FromResult(Ok(new ConsumePSNEntitlementsResult { ItemsGranted = new() }));
    }

    /// <summary>Consumes PS5 entitlements.</summary>
    [HttpPost("/Client/ConsumePS5Entitlements")]
    public Task<IHttpActionResult> Client_ConsumePS5Entitlements(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<ConsumePS5EntitlementsRequest>(request);
        return Task.FromResult(Ok(new ConsumePS5EntitlementsResult { Items = new() }));
    }

    /// <summary>Consumes Xbox entitlements.</summary>
    [HttpPost("/Client/ConsumeXboxEntitlements")]
    public Task<IHttpActionResult> Client_ConsumeXboxEntitlements(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<ConsumeXboxEntitlementsRequest>(request);
        return Task.FromResult(Ok(new ConsumeXboxEntitlementsResult { Items = new() }));
    }

    /// <summary>Consumes Microsoft Store entitlements.</summary>
    [HttpPost("/Client/ConsumeMicrosoftStoreEntitlements")]
    public Task<IHttpActionResult> Client_ConsumeMicrosoftStoreEntitlements(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        ParseRequest<ConsumeMicrosoftStoreEntitlementsRequest>(request);
        return Task.FromResult(Ok(new ConsumeMicrosoftStoreEntitlementsResponse { Items = new() }));
    }

    /* ─── Ad placements ───────────────────────────────────────────────────── */

    /// <summary>Returns available ad placements for the player.</summary>
    [HttpPost("/Client/GetAdPlacements")]
    public Task<IHttpActionResult> Client_GetAdPlacements(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var rows = Database.GetCollection<PlayfabAdPlacementDbObject>(true)!
                           .Find(x => x.TitleId == TITLE_ID).ToList();
        return Task.FromResult(Ok(new GetAdPlacementsResult
        {
            AdPlacements = rows.Select(r => new AdPlacementDetails
            {
                PlacementId                = r.PlacementId,
                PlacementName              = r.PlacementName,
                PlacementViewsRemaining    = (int)r.PlacementViewsRemaining,
                PlacementViewsResetMinutes = r.PlacementViewsResetMinutes,
            }).ToList(),
        }));
    }

    /// <summary>Records an ad activity event.</summary>
    [HttpPost("/Client/ReportAdActivity")]
    public Task<IHttpActionResult> Client_ReportAdActivity(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ReportAdActivityRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        Database.GetCollection<PlayfabEventDbObject>(true)!.Insert(new PlayfabEventDbObject
        {
            EventId        = NewId(),
            EventName      = $"AdActivity_{body.Activity}",
            EventNamespace = "title.ads",
            EntityId       = PlayFabIdFromRequest(request),
            PayloadJson    = $"{{\"PlacementId\":\"{body.PlacementId}\"}}",
            Timestamp      = NowUnixMs(),
        });
        return Task.FromResult(Ok(new ReportAdActivityResult()));
    }

    /// <summary>Records that the player viewed a rewarded ad and grants the placement reward.</summary>
    [HttpPost("/Client/RewardAdActivity")]
    public Task<IHttpActionResult> Client_RewardAdActivity(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.LoginSession)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RewardAdActivityRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.PlacementId))
            return Task.FromResult(Err("InvalidParams", 1000, "PlacementId required"));

        var placement = Database.GetCollection<PlayfabAdPlacementDbObject>(true)!
                                .FindOne(x => x.TitleId == TITLE_ID && x.PlacementId == body.PlacementId);
        return Task.FromResult(Ok(new RewardAdActivityResult
        {
            AdActivityEventId          = NewId(),
            PlacementId                = body.PlacementId,
            PlacementName              = placement?.PlacementName ?? "",
            PlacementViewsRemaining    = placement != null ? (int?)placement.PlacementViewsRemaining : null,
            PlacementViewsResetMinutes = placement?.PlacementViewsResetMinutes,
            DebugResults               = new(),
        }));
    }
}
