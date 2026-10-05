using AUnrealFeatures.Hosting.Database;

namespace AUnrealFeatures.Playfab.Models;

public sealed class PlayfabEconomyItemDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string DefaultStackId { get; set; } = "";
    public List<string> AlternateIds { get; set; } = new();
    public string Type { get; set; } = "";
    public string DisplayVersion { get; set; } = "";
    public Dictionary<string, string> Title { get; set; } = new();
    public Dictionary<string, string> Description { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public long Created { get; set; }
    public long? StartDate { get; set; }
    public long? EndDate { get; set; }
    public bool Hidden { get; set; }
    public string CreatorEntityId { get; set; } = "";
    public string Status { get; set; } = "Active";
    public string PlatformsJson { get; set; } = "";
    public string PriceOptionsJson { get; set; } = "";
    public string ImagesJson { get; set; } = "";
    public string ContentsJson { get; set; } = "";
    public string ItemReferencesJson { get; set; } = "";
}

public sealed class PlayfabEconomyInventoryDbObject : AstraDbObject
{
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string CollectionId { get; set; } = "Default";
    public string ItemId { get; set; } = "";
    public string StackId { get; set; } = "Default";
    public int Amount { get; set; }
    public string DisplayProperties { get; set; } = ""; // JSON
}

public sealed class PlayfabTransactionDbObject : AstraDbObject
{
    public string TransactionId { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Operation { get; set; } = ""; // Add, Subtract, Update, Delete, Purchase
    public string ItemId { get; set; } = "";
    public int Amount { get; set; }
    public long Timestamp { get; set; }
    public string Details { get; set; } = ""; // JSON
}

public sealed class PlayfabReviewDbObject : AstraDbObject
{
    public string ReviewId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string ReviewerEntityId { get; set; } = "";
    public string ReviewerName { get; set; } = "";
    public int Rating { get; set; }
    public string ReviewText { get; set; } = "";
    public string ReviewTitle { get; set; } = "";
    public int HelpfulPositive { get; set; }
    public int HelpfulNegative { get; set; }
    public string Locale { get; set; } = "";
    public bool IsInstalled { get; set; }
    public long Submitted { get; set; }
}

public sealed class PlayfabFileDbObject : AstraDbObject
{
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ProfileVersion { get; set; } = "";
    public string Etag { get; set; } = "";
    public long Size { get; set; }
    public string Checksum { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public long LastModified { get; set; }
}

public sealed class PlayfabObjectDbObject : AstraDbObject
{
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string ObjectName { get; set; } = "";
    public string DataObjectJson { get; set; } = "";
    public string EscapedDataObject { get; set; } = "";
}

public sealed class PlayfabProfileDbObject : AstraDbObject
{
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public string Language { get; set; } = "";
    public List<string> Lineage { get; set; } = new();
    public string PolicyJson { get; set; } = "";
    public List<string> Permissions { get; set; } = new();
    public long Created { get; set; }
    public long LastUpdated { get; set; }
}
