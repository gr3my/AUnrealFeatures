using AUnrealFeatures.Hosting.Database;

namespace AUnrealFeatures.Playfab.Models;

public sealed class PlayfabFriendDbObject : AstraDbObject
{
    public string OwnerPlayFabId { get; set; } = "";
    public string FriendPlayFabId { get; set; } = "";
    public string Username { get; set; } = "";
    public string TitleDisplayName { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public long Created { get; set; }
    public string Status { get; set; } = "Confirmed"; // Requested | Confirmed | Denied
}

public sealed class PlayfabStatisticDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string StatisticName { get; set; } = "";
    public int Value { get; set; }
    public uint Version { get; set; }
}

public sealed class PlayfabStatisticDefinitionDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string StatisticName { get; set; } = "";
    public string AggregationMethod { get; set; } = "Last"; // Last | Min | Max | Sum
    public string ResetInterval { get; set; } = "Never"; // Never | Hour | Day | Week | Month
    public uint CurrentVersion { get; set; }
}

public sealed class PlayfabCharacterDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string CharacterType { get; set; } = "";
    public long Created { get; set; }
}

public sealed class PlayfabCharacterDataDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Permission { get; set; } = "Private";
    public bool ReadOnly { get; set; }
    public long LastUpdated { get; set; }
}

public sealed class PlayfabSegmentDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string SegmentId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Definition { get; set; } = ""; // JSON
}

public sealed class PlayfabPlayerTagDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Tag { get; set; } = "";
}

public sealed class PlayfabSessionDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string SessionTicket { get; set; } = "";
    public string EntityToken { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public long Created { get; set; }
    public long Expires { get; set; }
}

public sealed class PlayfabPushTokenDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string Platform { get; set; } = ""; // ios | android
    public string DeviceToken { get; set; } = "";
    public bool SendPushNotificationConfirmation { get; set; }
    public string ConfirmationMessage { get; set; } = "";
}

public sealed class PlayfabTradeDbObject : AstraDbObject
{
    public string TradeId { get; set; } = "";
    public string OfferingPlayerId { get; set; } = "";
    public List<string> AllowedPlayerIds { get; set; } = new();
    public List<string> OfferedItems { get; set; } = new();
    public List<string> RequestedCatalogItemIds { get; set; } = new();
    public string Status { get; set; } = "Open"; // Invalid | Open | Accepting | Accepted | Filled | Cancelled
    public long Created { get; set; }
    public long? Filled { get; set; }
}

public sealed class PlayfabPurchaseDbObject : AstraDbObject
{
    public string OrderId { get; set; } = "";
    public string PlayFabId { get; set; } = "";
    public string PaymentProvider { get; set; } = "";
    public string TransactionId { get; set; } = "";
    public List<string> ItemInstanceIds { get; set; } = new();
    public uint Amount { get; set; }
    public string Currency { get; set; } = "";
    public long PurchaseDate { get; set; }
}

public sealed class PlayfabContentDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string Key { get; set; } = "";
    public string Url { get; set; } = "";
    public long Size { get; set; }
}
