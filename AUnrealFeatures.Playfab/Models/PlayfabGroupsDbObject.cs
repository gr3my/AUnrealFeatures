using AUnrealFeatures.Hosting.Database;

namespace AUnrealFeatures.Playfab.Models;

public sealed class PlayfabGroupDbObject : AstraDbObject
{
    public string GroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string AdminRoleId { get; set; } = "admins";
    public string MemberRoleId { get; set; } = "members";
    public long Created { get; set; }
}

public sealed class PlayfabSharedGroupDataEntry
{
    public string Value { get; set; } = "";
    public string Permission { get; set; } = "Private";
}

public sealed class PlayfabGroupRoleDbObject : AstraDbObject
{
    public string GroupId { get; set; } = "";
    public string RoleId { get; set; } = "";
    public string RoleName { get; set; } = "";
}

public sealed class PlayfabGroupMembershipDbObject : AstraDbObject
{
    public string GroupId { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string RoleId { get; set; } = "";
    public long Joined { get; set; }
}

public sealed class PlayfabGroupInvitationDbObject : AstraDbObject
{
    public string GroupId { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string RoleId { get; set; } = "";
    public string InvitedByEntityId { get; set; } = "";
    public long Created { get; set; }
    public long? Expires { get; set; }
}

public sealed class PlayfabGroupApplicationDbObject : AstraDbObject
{
    public string GroupId { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public long Created { get; set; }
    public long? Expires { get; set; }
}

public sealed class PlayfabGroupBlockDbObject : AstraDbObject
{
    public string GroupId { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public long Created { get; set; }
}
