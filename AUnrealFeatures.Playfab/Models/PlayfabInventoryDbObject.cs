using AUnrealFeatures.Hosting.Database;

namespace AUnrealFeatures.Playfab.Models;

public sealed class PlayfabInventoryItemDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string ItemInstanceId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string ItemClass { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Annotation { get; set; } = "";
    public long PurchaseDate { get; set; }
    public long? Expiration { get; set; }
    public int? RemainingUses { get; set; }
    public int? UsesIncrementedBy { get; set; }
    public string UnitCurrency { get; set; } = "";
    public uint UnitPrice { get; set; }
    public List<string> BundleContents { get; set; } = new();
    public string BundleParent { get; set; } = "";
    public Dictionary<string, string> CustomData { get; set; } = new();
}

public sealed class PlayfabVirtualCurrencyDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public int Balance { get; set; }
    public long? RechargeTime { get; set; }
    public int? RechargeMax { get; set; }
}

public sealed class PlayfabCatalogItemDbObject : AstraDbObject
{
    public string CatalogVersion { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string ItemClass { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, uint> VirtualCurrencyPrices { get; set; } = new();
    public Dictionary<string, uint> RealCurrencyPrices { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string CustomData { get; set; } = "";
    public bool IsLimitedEdition { get; set; }
    public int InitialLimitedEditionCount { get; set; }
    public bool IsStackable { get; set; }
    public bool IsTradable { get; set; }
    public string ItemImageUrl { get; set; } = "";

    // The full PlayFab catalog item as uploaded (preserves Bundle / Consumable / Container /
    // ActivatedMembership / DeepLinks / etc. that aren't in the indexed columns above).
    public string FullJson { get; set; } = "";
}

public sealed class PlayfabStoreItemDbObject : AstraDbObject
{
    public string CatalogVersion { get; set; } = "";
    public string StoreId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public Dictionary<string, uint> VirtualCurrencyPrices { get; set; } = new();
    public Dictionary<string, uint> RealCurrencyPrices { get; set; } = new();
}
