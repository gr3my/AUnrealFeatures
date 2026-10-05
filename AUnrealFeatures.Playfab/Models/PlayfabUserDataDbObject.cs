using AUnrealFeatures.Hosting.Database;

namespace AUnrealFeatures.Playfab.Models;

public sealed class PlayfabUserDataDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Permission { get; set; } = "Private"; // Public | Private
    public string Scope { get; set; } = "Title";        // Title | Publisher | ReadOnly | InternalReadOnly | Internal
    public long LastUpdated { get; set; }
}

public sealed class PlayfabTitleDataDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Scope { get; set; } = "Title"; // Title | TitleInternal
    public long LastUpdated { get; set; }
}

public sealed class PlayfabSharedGroupDbObject : AstraDbObject
{
    public string SharedGroupId { get; set; } = "";
    public List<string> Members { get; set; } = new();
    public Dictionary<string, PlayfabSharedGroupValue> Data { get; set; } = new();
    public long Created { get; set; }
}

public sealed class PlayfabSharedGroupValue
{
    public string Value { get; set; } = "";
    public string LastUpdatedBy { get; set; } = "";
    public long LastUpdated { get; set; }
    public string Permission { get; set; } = "Private";
}
