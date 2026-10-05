using AUnrealFeatures.Hosting.Database;

namespace AUnrealFeatures.Playfab.Models;

public sealed class PlayfabEventDbObject : AstraDbObject
{
    public string EventId { get; set; } = "";
    public string EventName { get; set; } = "";
    public string EventNamespace { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string OriginalId { get; set; } = "";
    public long Timestamp { get; set; }
}

public sealed class PlayfabExperimentDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string ExperimentId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string SegmentId { get; set; } = "";
    public uint Duration { get; set; }
    public long? StartDate { get; set; }
    public string State { get; set; } = "New";
    public string ExperimentType { get; set; } = "Active";
    public List<string> Variants { get; set; } = new(); // JSON
    public string Exclusion { get; set; } = "";
}

public sealed class PlayfabExperimentTreatmentDbObject : AstraDbObject
{
    public string EntityId { get; set; } = "";
    public string ExperimentId { get; set; } = "";
    public string VariantId { get; set; } = "";
    public string VariantName { get; set; } = "";
}

public sealed class PlayfabLocaleDbObject : AstraDbObject
{
    public string LanguageCode { get; set; } = "";
    public string LanguageName { get; set; } = "";
}

public sealed class PlayfabProgressionDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string DefinitionJson { get; set; } = ""; // versions, columns, scoring details
    public uint Version { get; set; }
    public bool SizeLimited { get; set; }
    public uint? SizeLimit { get; set; }
    public string ResetInterval { get; set; } = "Manual";
    public long Created { get; set; }
    public long LastReset { get; set; }
}

public sealed class PlayfabLeaderboardEntryDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string LeaderboardName { get; set; } = "";
    public uint Version { get; set; }
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<double> Scores { get; set; } = new();
    public string Metadata { get; set; } = "";
    public uint Rank { get; set; }
    public long LastUpdated { get; set; }
}

public sealed class PlayfabAddonConfigDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string AddonName { get; set; } = "";  // Apple, Facebook, Google, Steam, etc.
    public string ConfigJson { get; set; } = "";
    public long LastUpdated { get; set; }
}

public sealed class PlayfabCloudScriptFileDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FileContents { get; set; } = "";
    public uint Version { get; set; }
    public bool Published { get; set; }
    public long Uploaded { get; set; }
}

public sealed class PlayfabCloudScriptFunctionDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string FunctionName { get; set; } = "";
    public string FunctionUrl { get; set; } = "";
    public string TriggerType { get; set; } = "HTTP"; // HTTP | EventGrid | Queue
    public string ConnectionString { get; set; } = "";
    public string QueueName { get; set; } = "";
}

public sealed class PlayfabCloudScriptExecutionLogDbObject : AstraDbObject
{
    public string ExecutionId { get; set; } = "";
    public string PlayFabId { get; set; } = "";
    public string FunctionName { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public string LogsJson { get; set; } = "";
    public long ExecutionTimeSeconds { get; set; }
    public long Timestamp { get; set; }
    public bool Error { get; set; }
}

public sealed class PlayfabContactEmailDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string Name { get; set; } = "";
    public string EmailAddress { get; set; } = "";
    public string VerificationStatus { get; set; } = "Unverified"; // Unverified | Pending | Confirmed
}

public sealed class PlayfabAdPlacementDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string PlacementId { get; set; } = "";
    public string PlacementName { get; set; } = "";
    public string RewardJson { get; set; } = "";
    public uint PlacementViewsRemaining { get; set; }
    public double PlacementViewsResetMinutes { get; set; }
}

public sealed class PlayfabPolicyDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string PolicyName { get; set; } = "ApiPolicy";
    public string StatementsJson { get; set; } = "";
}
