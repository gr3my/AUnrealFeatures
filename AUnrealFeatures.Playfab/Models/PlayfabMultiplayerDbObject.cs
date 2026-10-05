using AUnrealFeatures.Hosting.Database;

namespace AUnrealFeatures.Playfab.Models;

public sealed class PlayfabLobbyDbObject : AstraDbObject
{
    public string LobbyId { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string OwnerEntityId { get; set; } = "";
    public string OwnerMigrationPolicy { get; set; } = "Automatic";
    public uint MaxPlayers { get; set; } = 16;
    public string AccessPolicy { get; set; } = "Public";
    public bool UseConnections { get; set; }
    public string SearchData { get; set; } = "";
    public string LobbyData { get; set; } = "";
    public List<string> MemberEntityIds { get; set; } = new();
    public long Created { get; set; }
}

public sealed class PlayfabMatchmakingTicketDbObject : AstraDbObject
{
    public string TicketId { get; set; } = "";
    public string QueueName { get; set; } = "";
    public string CreatorEntityId { get; set; } = "";
    public List<string> MembersEntityIds { get; set; } = new();
    public string Status { get; set; } = "WaitingForMatch";
    public uint GiveUpAfterSeconds { get; set; }
    public string MatchId { get; set; } = "";
    public string AttributesJson { get; set; } = "";
    public long Created { get; set; }
}

public sealed class PlayfabMatchmakingQueueDbObject : AstraDbObject
{
    public string QueueName { get; set; } = "";
    public uint MinMatchSize { get; set; }
    public uint MaxMatchSize { get; set; }
    public string ConfigJson { get; set; } = "";
    public long Created { get; set; }
}

public sealed class PlayfabMatchDbObject : AstraDbObject
{
    public string MatchId { get; set; } = "";
    public string QueueName { get; set; } = "";
    public List<string> MembersEntityIds { get; set; } = new();
    public string ServerDetailsJson { get; set; } = "";
    public string ArrangementString { get; set; } = "";
    public long Created { get; set; }
}

public sealed class PlayfabBuildDbObject : AstraDbObject
{
    public string BuildId { get; set; } = "";
    public string BuildName { get; set; } = "";
    public string GameAssetReferenceJson { get; set; } = "";
    public string ContainerFlavor { get; set; } = "";
    public uint MultiplayerServerCountPerVm { get; set; }
    public List<string> RegionConfigurations { get; set; } = new();
    public List<string> Ports { get; set; } = new();
    public long Created { get; set; }
}

public sealed class PlayfabServerDbObject : AstraDbObject
{
    public string ServerId { get; set; } = "";
    public string BuildId { get; set; } = "";
    public string Region { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string IPV4Address { get; set; } = "";
    public List<string> Ports { get; set; } = new();
    public string State { get; set; } = "";
    public long LastStateTransitionTime { get; set; }
    public string VmId { get; set; } = "";
    public List<string> ConnectedPlayers { get; set; } = new();
}

public sealed class PlayfabPartyDbObject : AstraDbObject
{
    public string PartyId { get; set; } = "";
    public List<string> MemberEntityIds { get; set; } = new();
    public string LeaderEntityId { get; set; } = "";
    public long Created { get; set; }
}
