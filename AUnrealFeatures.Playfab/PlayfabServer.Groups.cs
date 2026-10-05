using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Playfab.Models;
using PlayFab.GroupsModels;
using System.Net;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    /* ─── Helpers ─────────────────────────────────────────────────────────── */

    private static EntityKey ToGroupKey(string id, string type = "group") => new() { Id = id, Type = type };

    private static EntityWithLineage ToLineage(string entityId, string entityType) => new()
    {
        Key = new EntityKey { Id = entityId, Type = entityType },
        Lineage = new Dictionary<string, EntityKey>
        {
            ["title_player_account"] = new EntityKey { Id = entityId, Type = entityType },
        }
    };

    private static int ProfileVersionFor(PlayfabGroupDbObject g) => (int)(g.Created & int.MaxValue);

    /* ─── Group CRUD ──────────────────────────────────────────────────────── */

    /// <summary>Creates a new group with the calling entity as admin.</summary>
    [HttpPost("/Group/CreateGroup")]
    public Task<IHttpActionResult> Groups_CreateGroup(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateGroupRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.GroupName))
            return Task.FromResult(Err("InvalidParams", 1000, "GroupName is required"));

        var coll = Database.GetCollection<PlayfabGroupDbObject>(true)!;
        var name = body.GroupName;
        if (coll.FindOne(g => g.GroupName == name) != null)
            return Task.FromResult(Err("GroupNameNotAvailable", 1612, "Group name already taken"));

        var group = new PlayfabGroupDbObject
        {
            GroupId      = NewId(),
            GroupName    = body.GroupName,
            AdminRoleId  = "admins",
            MemberRoleId = "members",
            Created      = NowUnix(),
        };
        coll.Insert(group);

        var roleColl = Database.GetCollection<PlayfabGroupRoleDbObject>(true)!;
        roleColl.Insert(new PlayfabGroupRoleDbObject { GroupId = group.GroupId, RoleId = "admins",  RoleName = "Admins" });
        roleColl.Insert(new PlayfabGroupRoleDbObject { GroupId = group.GroupId, RoleId = "members", RoleName = "Members" });

        // Add the calling entity as admin
        var memColl = Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!;
        var entityId   = body.Entity?.Id   ?? "";
        var entityType = body.Entity?.Type ?? "title_player_account";
        if (!string.IsNullOrEmpty(entityId))
        {
            memColl.Insert(new PlayfabGroupMembershipDbObject
            {
                GroupId    = group.GroupId,
                EntityId   = entityId,
                EntityType = entityType,
                RoleId     = "admins",
                Joined     = NowUnix(),
            });
        }

        return Task.FromResult(Ok(new CreateGroupResponse
        {
            Group          = ToGroupKey(group.GroupId),
            GroupName      = group.GroupName,
            AdminRoleId    = group.AdminRoleId,
            MemberRoleId   = group.MemberRoleId,
            Created        = DateTimeOffset.FromUnixTimeSeconds(group.Created).UtcDateTime,
            ProfileVersion = ProfileVersionFor(group),
            Roles          = new Dictionary<string, string> { ["admins"] = "Admins", ["members"] = "Members" }
        }));
    }

    /// <summary>Returns details about a group.</summary>
    [HttpPost("/Group/GetGroup")]
    public Task<IHttpActionResult> Groups_GetGroup(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetGroupRequest>(request) ?? new GetGroupRequest();
        var coll = Database.GetCollection<PlayfabGroupDbObject>(true)!;
        PlayfabGroupDbObject? g = null;
        if (!string.IsNullOrEmpty(body.Group?.Id))
            g = coll.FindOne(x => x.GroupId == body.Group.Id);
        if (g == null && !string.IsNullOrEmpty(body.GroupName))
        {
            var gname = body.GroupName;
            g = coll.FindOne(x => x.GroupName == gname);
        }
        if (g == null) return Task.FromResult(Err("GroupNotFound", 1610, "Group not found", HttpStatusCode.NotFound));

        var roles = Database.GetCollection<PlayfabGroupRoleDbObject>(true)!.Find(r => r.GroupId == g.GroupId)
                            .ToDictionary(r => r.RoleId, r => r.RoleName);
        return Task.FromResult(Ok(new GetGroupResponse
        {
            Group          = ToGroupKey(g.GroupId),
            GroupName      = g.GroupName,
            AdminRoleId    = g.AdminRoleId,
            MemberRoleId   = g.MemberRoleId,
            Created        = DateTimeOffset.FromUnixTimeSeconds(g.Created).UtcDateTime,
            ProfileVersion = ProfileVersionFor(g),
            Roles          = roles,
        }));
    }

    /// <summary>Updates group name or admin/member role ids.</summary>
    [HttpPost("/Group/UpdateGroup")]
    public Task<IHttpActionResult> Groups_UpdateGroup(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateGroupRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group is required"));

        var coll = Database.GetCollection<PlayfabGroupDbObject>(true)!;
        var gid = body.Group.Id;
        var g = coll.FindOne(x => x.GroupId == gid);
        if (g == null) return Task.FromResult(Err("GroupNotFound", 1610, "Group not found", HttpStatusCode.NotFound));

        if (!string.IsNullOrEmpty(body.GroupName))    g.GroupName    = body.GroupName;
        if (!string.IsNullOrEmpty(body.AdminRoleId))  g.AdminRoleId  = body.AdminRoleId;
        if (!string.IsNullOrEmpty(body.MemberRoleId)) g.MemberRoleId = body.MemberRoleId;
        coll.Update(g);
        return Task.FromResult(Ok(new UpdateGroupResponse
        {
            ProfileVersion = ProfileVersionFor(g),
            SetResult      = OperationTypes.Updated,
        }));
    }

    /// <summary>Deletes a group along with its roles, memberships, applications, invitations, and blocks.</summary>
    [HttpPost("/Group/DeleteGroup")]
    public Task<IHttpActionResult> Groups_DeleteGroup(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteGroupRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group is required"));

        var gid = body.Group.Id;
        Database.GetCollection<PlayfabGroupDbObject>(true)!.DeleteMany(g => g.GroupId == gid);
        Database.GetCollection<PlayfabGroupRoleDbObject>(true)!.DeleteMany(r => r.GroupId == gid);
        Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!.DeleteMany(m => m.GroupId == gid);
        Database.GetCollection<PlayfabGroupApplicationDbObject>(true)!.DeleteMany(a => a.GroupId == gid);
        Database.GetCollection<PlayfabGroupInvitationDbObject>(true)!.DeleteMany(i => i.GroupId == gid);
        Database.GetCollection<PlayfabGroupBlockDbObject>(true)!.DeleteMany(b => b.GroupId == gid);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /* ─── Roles ───────────────────────────────────────────────────────────── */

    /// <summary>Creates a new role on a group.</summary>
    [HttpPost("/Group/CreateRole")]
    public Task<IHttpActionResult> Groups_CreateRole(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateGroupRoleRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || string.IsNullOrEmpty(body.RoleId) || string.IsNullOrEmpty(body.RoleName))
            return Task.FromResult(Err("InvalidParams", 1000, "Group, RoleId, and RoleName are required"));

        var coll = Database.GetCollection<PlayfabGroupRoleDbObject>(true)!;
        var gid = body.Group.Id;
        var rid = body.RoleId;
        if (coll.FindOne(r => r.GroupId == gid && r.RoleId == rid) != null)
            return Task.FromResult(Err("RoleNameNotAvailable", 1614, "Role already exists"));

        coll.Insert(new PlayfabGroupRoleDbObject { GroupId = gid, RoleId = body.RoleId, RoleName = body.RoleName });
        var g = Database.GetCollection<PlayfabGroupDbObject>(true)!.FindOne(x => x.GroupId == gid);
        return Task.FromResult(Ok(new CreateGroupRoleResponse
        {
            RoleId         = body.RoleId,
            RoleName       = body.RoleName,
            ProfileVersion = g != null ? ProfileVersionFor(g) : 0,
        }));
    }

    /// <summary>Renames an existing role.</summary>
    [HttpPost("/Group/UpdateRole")]
    public Task<IHttpActionResult> Groups_UpdateRole(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UpdateGroupRoleRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || string.IsNullOrEmpty(body.RoleId))
            return Task.FromResult(Err("InvalidParams", 1000, "Group and RoleId are required"));

        var coll = Database.GetCollection<PlayfabGroupRoleDbObject>(true)!;
        var gid = body.Group.Id;
        var rid = body.RoleId;
        var r = coll.FindOne(x => x.GroupId == gid && x.RoleId == rid);
        if (r == null) return Task.FromResult(Err("RoleNotFound", 1613, "Role not found", HttpStatusCode.NotFound));
        if (!string.IsNullOrEmpty(body.RoleName)) r.RoleName = body.RoleName;
        coll.Update(r);
        var g = Database.GetCollection<PlayfabGroupDbObject>(true)!.FindOne(x => x.GroupId == gid);
        return Task.FromResult(Ok(new UpdateGroupRoleResponse
        {
            SetResult      = OperationTypes.Updated,
            ProfileVersion = g != null ? ProfileVersionFor(g) : 0,
        }));
    }

    /// <summary>Deletes a role; default admin/member roles cannot be removed.</summary>
    [HttpPost("/Group/DeleteRole")]
    public Task<IHttpActionResult> Groups_DeleteRole(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteRoleRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || string.IsNullOrEmpty(body.RoleId))
            return Task.FromResult(Err("InvalidParams", 1000, "Group and RoleId are required"));

        var gid = body.Group.Id;
        var g = Database.GetCollection<PlayfabGroupDbObject>(true)!.FindOne(x => x.GroupId == gid);
        if (g != null && (body.RoleId == g.AdminRoleId || body.RoleId == g.MemberRoleId))
            return Task.FromResult(Err("InvalidParams", 1000, "Cannot delete the admin or member role"));

        var rid = body.RoleId;
        Database.GetCollection<PlayfabGroupRoleDbObject>(true)!.DeleteMany(r => r.GroupId == gid && r.RoleId == rid);
        // Reassign affected memberships back to the member role
        var memColl = Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!;
        foreach (var m in memColl.Find(m => m.GroupId == gid && m.RoleId == rid).ToList())
        {
            m.RoleId = g?.MemberRoleId ?? "members";
            memColl.Update(m);
        }
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /* ─── Members ─────────────────────────────────────────────────────────── */

    /// <summary>Adds members to a group with an optional role.</summary>
    [HttpPost("/Group/AddMembers")]
    public Task<IHttpActionResult> Groups_AddMembers(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<AddMembersRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || body.Members == null || body.Members.Count == 0)
            return Task.FromResult(Err("InvalidParams", 1000, "Group and Members are required"));

        var coll = Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!;
        var gid = body.Group.Id;
        var roleId = string.IsNullOrEmpty(body.RoleId) ? "members" : body.RoleId;
        foreach (var m in body.Members)
        {
            var entityId = m.Id;
            var existing = coll.FindOne(x => x.GroupId == gid && x.EntityId == entityId);
            if (existing != null) continue;
            coll.Insert(new PlayfabGroupMembershipDbObject
            {
                GroupId    = gid,
                EntityId   = m.Id,
                EntityType = m.Type ?? "title_player_account",
                RoleId     = roleId,
                Joined     = NowUnix(),
            });
        }
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Removes members from a group.</summary>
    [HttpPost("/Group/RemoveMembers")]
    public Task<IHttpActionResult> Groups_RemoveMembers(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RemoveMembersRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || body.Members == null)
            return Task.FromResult(Err("InvalidParams", 1000, "Group and Members are required"));

        var coll = Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!;
        var gid = body.Group.Id;
        foreach (var m in body.Members)
        {
            var entityId = m.Id;
            coll.DeleteMany(x => x.GroupId == gid && x.EntityId == entityId);
        }
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Reassigns members from one role to another within a group.</summary>
    [HttpPost("/Group/ChangeMemberRole")]
    public Task<IHttpActionResult> Groups_ChangeMemberRole(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ChangeMemberRoleRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || string.IsNullOrEmpty(body.DestinationRoleId) || body.Members == null)
            return Task.FromResult(Err("InvalidParams", 1000, "Group, DestinationRoleId, and Members are required"));

        var coll = Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!;
        var gid = body.Group.Id;
        foreach (var m in body.Members)
        {
            var entityId = m.Id;
            var roleFilter = body.OriginRoleId;
            var membership = coll.FindOne(x => x.GroupId == gid && x.EntityId == entityId
                                            && (string.IsNullOrEmpty(roleFilter) || x.RoleId == roleFilter));
            if (membership == null) continue;
            membership.RoleId = body.DestinationRoleId;
            coll.Update(membership);
        }
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Returns whether an entity is a member of a group.</summary>
    [HttpPost("/Group/IsMember")]
    public Task<IHttpActionResult> Groups_IsMember(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<IsMemberRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || string.IsNullOrEmpty(body.Entity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group and Entity are required"));

        var gid = body.Group.Id;
        var eid = body.Entity.Id;
        var role = body.RoleId;
        var m = Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!
                        .FindOne(x => x.GroupId == gid && x.EntityId == eid
                                   && (string.IsNullOrEmpty(role) || x.RoleId == role));
        return Task.FromResult(Ok(new IsMemberResponse { IsMember = m != null }));
    }

    /// <summary>Returns all members of a group, grouped by role.</summary>
    [HttpPost("/Group/ListGroupMembers")]
    public Task<IHttpActionResult> Groups_ListGroupMembers(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ListGroupMembersRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group is required"));

        var gid = body.Group.Id;
        var memberships = Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!
                                  .Find(m => m.GroupId == gid).ToList();
        var roles = Database.GetCollection<PlayfabGroupRoleDbObject>(true)!
                            .Find(r => r.GroupId == gid)
                            .ToDictionary(r => r.RoleId, r => r.RoleName);
        var grouped = memberships.GroupBy(m => m.RoleId).Select(grp => new EntityMemberRole
        {
            RoleId   = grp.Key,
            RoleName = roles.TryGetValue(grp.Key, out var n) ? n : grp.Key,
            Members  = grp.Select(m => ToLineage(m.EntityId, m.EntityType)).ToList(),
        }).ToList();
        return Task.FromResult(Ok(new ListGroupMembersResponse { Members = grouped }));
    }

    /* ─── Applications & Invitations ─────────────────────────────────────── */

    /// <summary>Creates an application for an entity to join a group.</summary>
    [HttpPost("/Group/ApplyToGroup")]
    public Task<IHttpActionResult> Groups_ApplyToGroup(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ApplyToGroupRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || string.IsNullOrEmpty(body.Entity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group and Entity are required"));

        var gid = body.Group.Id;
        var eid = body.Entity.Id;

        // Auto-accept outstanding invitation if requested
        if (body.AutoAcceptOutstandingInvite == true)
        {
            var invColl = Database.GetCollection<PlayfabGroupInvitationDbObject>(true)!;
            var inv = invColl.FindOne(i => i.GroupId == gid && i.EntityId == eid);
            if (inv != null)
            {
                Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!.Insert(new PlayfabGroupMembershipDbObject
                {
                    GroupId = gid, EntityId = eid, EntityType = body.Entity.Type ?? "title_player_account",
                    RoleId = inv.RoleId, Joined = NowUnix(),
                });
                invColl.Delete(inv.Id);
                return Task.FromResult(Ok(new ApplyToGroupResponse
                {
                    Group   = ToGroupKey(gid),
                    Entity  = ToLineage(eid, body.Entity.Type ?? "title_player_account"),
                    Expires = DateTimeOffset.UtcNow.AddDays(30).UtcDateTime,
                }));
            }
        }

        var appColl = Database.GetCollection<PlayfabGroupApplicationDbObject>(true)!;
        if (appColl.FindOne(a => a.GroupId == gid && a.EntityId == eid) == null)
        {
            appColl.Insert(new PlayfabGroupApplicationDbObject
            {
                GroupId    = gid,
                EntityId   = eid,
                EntityType = body.Entity.Type ?? "title_player_account",
                Created    = NowUnix(),
                Expires    = NowUnix() + 30 * 24 * 60 * 60,
            });
        }
        return Task.FromResult(Ok(new ApplyToGroupResponse
        {
            Group   = ToGroupKey(gid),
            Entity  = ToLineage(eid, body.Entity.Type ?? "title_player_account"),
            Expires = DateTimeOffset.UtcNow.AddDays(30).UtcDateTime,
        }));
    }

    /// <summary>Approves a pending application; the entity becomes a member.</summary>
    [HttpPost("/Group/AcceptGroupApplication")]
    public Task<IHttpActionResult> Groups_AcceptGroupApplication(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<AcceptGroupApplicationRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || string.IsNullOrEmpty(body.Entity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group and Entity are required"));

        var gid = body.Group.Id;
        var eid = body.Entity.Id;
        var appColl = Database.GetCollection<PlayfabGroupApplicationDbObject>(true)!;
        var app = appColl.FindOne(a => a.GroupId == gid && a.EntityId == eid);
        if (app == null) return Task.FromResult(Err("EntityNotInvitedToGroup", 1623, "Application not found", HttpStatusCode.NotFound));

        var g = Database.GetCollection<PlayfabGroupDbObject>(true)!.FindOne(x => x.GroupId == gid);
        Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!.Insert(new PlayfabGroupMembershipDbObject
        {
            GroupId    = gid,
            EntityId   = eid,
            EntityType = app.EntityType,
            RoleId     = g?.MemberRoleId ?? "members",
            Joined     = NowUnix(),
        });
        appColl.Delete(app.Id);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Removes a pending application (rejection).</summary>
    [HttpPost("/Group/RemoveGroupApplication")]
    public Task<IHttpActionResult> Groups_RemoveGroupApplication(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RemoveGroupApplicationRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || string.IsNullOrEmpty(body.Entity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group and Entity are required"));

        var gid = body.Group.Id;
        var eid = body.Entity.Id;
        Database.GetCollection<PlayfabGroupApplicationDbObject>(true)!
                .DeleteMany(a => a.GroupId == gid && a.EntityId == eid);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Returns all pending applications for a group.</summary>
    [HttpPost("/Group/ListGroupApplications")]
    public Task<IHttpActionResult> Groups_ListGroupApplications(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ListGroupApplicationsRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group is required"));

        var gid = body.Group.Id;
        var apps = Database.GetCollection<PlayfabGroupApplicationDbObject>(true)!
                           .Find(a => a.GroupId == gid)
                           .Select(a => new GroupApplication
                           {
                               Group  = ToGroupKey(gid),
                               Entity = ToLineage(a.EntityId, a.EntityType),
                               Expires = a.Expires.HasValue
                                   ? DateTimeOffset.FromUnixTimeSeconds(a.Expires.Value).UtcDateTime
                                   : DateTime.UtcNow.AddDays(30),
                           }).ToList();
        return Task.FromResult(Ok(new ListGroupApplicationsResponse { Applications = apps }));
    }

    /// <summary>Sends an invitation to an entity to join a group.</summary>
    [HttpPost("/Group/InviteToGroup")]
    public Task<IHttpActionResult> Groups_InviteToGroup(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<InviteToGroupRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || string.IsNullOrEmpty(body.Entity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group and Entity are required"));

        var gid = body.Group.Id;
        var eid = body.Entity.Id;
        var roleId = string.IsNullOrEmpty(body.RoleId) ? "members" : body.RoleId;

        // Auto-accept outstanding application
        if (body.AutoAcceptOutstandingApplication == true)
        {
            var appColl = Database.GetCollection<PlayfabGroupApplicationDbObject>(true)!;
            var app = appColl.FindOne(a => a.GroupId == gid && a.EntityId == eid);
            if (app != null)
            {
                Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!.Insert(new PlayfabGroupMembershipDbObject
                {
                    GroupId = gid, EntityId = eid, EntityType = app.EntityType, RoleId = roleId, Joined = NowUnix(),
                });
                appColl.Delete(app.Id);
            }
        }

        var invColl = Database.GetCollection<PlayfabGroupInvitationDbObject>(true)!;
        if (invColl.FindOne(i => i.GroupId == gid && i.EntityId == eid) == null)
        {
            invColl.Insert(new PlayfabGroupInvitationDbObject
            {
                GroupId           = gid,
                EntityId          = eid,
                EntityType        = body.Entity.Type ?? "title_player_account",
                RoleId            = roleId,
                InvitedByEntityId = PlayFabIdFromRequest(request),
                Created           = NowUnix(),
                Expires           = NowUnix() + 30 * 24 * 60 * 60,
            });
        }
        var inviter = PlayFabIdFromRequest(request);
        return Task.FromResult(Ok(new InviteToGroupResponse
        {
            Group           = ToGroupKey(gid),
            InvitedEntity   = ToLineage(eid, body.Entity.Type ?? "title_player_account"),
            InvitedByEntity = ToLineage(string.IsNullOrEmpty(inviter) ? "title" : inviter, "title_player_account"),
            RoleId          = roleId,
            Expires         = DateTimeOffset.UtcNow.AddDays(30).UtcDateTime,
        }));
    }

    /// <summary>Accepts a group invitation; entity becomes a member.</summary>
    [HttpPost("/Group/AcceptGroupInvitation")]
    public Task<IHttpActionResult> Groups_AcceptGroupInvitation(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<AcceptGroupInvitationRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || string.IsNullOrEmpty(body.Entity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group and Entity are required"));

        var gid = body.Group.Id;
        var eid = body.Entity.Id;
        var invColl = Database.GetCollection<PlayfabGroupInvitationDbObject>(true)!;
        var inv = invColl.FindOne(i => i.GroupId == gid && i.EntityId == eid);
        if (inv == null) return Task.FromResult(Err("EntityNotInvitedToGroup", 1623, "Invitation not found", HttpStatusCode.NotFound));

        Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!.Insert(new PlayfabGroupMembershipDbObject
        {
            GroupId    = gid,
            EntityId   = eid,
            EntityType = inv.EntityType,
            RoleId     = inv.RoleId,
            Joined     = NowUnix(),
        });
        invColl.Delete(inv.Id);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Removes a pending invitation (decline).</summary>
    [HttpPost("/Group/RemoveGroupInvitation")]
    public Task<IHttpActionResult> Groups_RemoveGroupInvitation(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<RemoveGroupInvitationRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || string.IsNullOrEmpty(body.Entity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group and Entity are required"));

        var gid = body.Group.Id;
        var eid = body.Entity.Id;
        Database.GetCollection<PlayfabGroupInvitationDbObject>(true)!
                .DeleteMany(i => i.GroupId == gid && i.EntityId == eid);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Returns all pending invitations for a group.</summary>
    [HttpPost("/Group/ListGroupInvitations")]
    public Task<IHttpActionResult> Groups_ListGroupInvitations(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ListGroupInvitationsRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group is required"));

        var gid = body.Group.Id;
        var invs = Database.GetCollection<PlayfabGroupInvitationDbObject>(true)!
                           .Find(i => i.GroupId == gid)
                           .Select(i => new GroupInvitation
                           {
                               Group           = ToGroupKey(gid),
                               InvitedEntity   = ToLineage(i.EntityId, i.EntityType),
                               InvitedByEntity = ToLineage(string.IsNullOrEmpty(i.InvitedByEntityId) ? "title" : i.InvitedByEntityId, "title_player_account"),
                               RoleId          = i.RoleId,
                               Expires         = i.Expires.HasValue
                                   ? DateTimeOffset.FromUnixTimeSeconds(i.Expires.Value).UtcDateTime
                                   : DateTime.UtcNow.AddDays(30),
                           }).ToList();
        return Task.FromResult(Ok(new ListGroupInvitationsResponse { Invitations = invs }));
    }

    /* ─── Blocks ──────────────────────────────────────────────────────────── */

    /// <summary>Blocks an entity from joining a group.</summary>
    [HttpPost("/Group/BlockEntity")]
    public Task<IHttpActionResult> Groups_BlockEntity(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<BlockEntityRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || string.IsNullOrEmpty(body.Entity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group and Entity are required"));

        var coll = Database.GetCollection<PlayfabGroupBlockDbObject>(true)!;
        var gid = body.Group.Id;
        var eid = body.Entity.Id;
        if (coll.FindOne(b => b.GroupId == gid && b.EntityId == eid) == null)
        {
            coll.Insert(new PlayfabGroupBlockDbObject
            {
                GroupId    = gid,
                EntityId   = eid,
                EntityType = body.Entity.Type ?? "title_player_account",
                Created    = NowUnix(),
            });
        }
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Removes a block on an entity.</summary>
    [HttpPost("/Group/UnblockEntity")]
    public Task<IHttpActionResult> Groups_UnblockEntity(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<UnblockEntityRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id) || string.IsNullOrEmpty(body.Entity?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group and Entity are required"));

        var gid = body.Group.Id;
        var eid = body.Entity.Id;
        Database.GetCollection<PlayfabGroupBlockDbObject>(true)!
                .DeleteMany(b => b.GroupId == gid && b.EntityId == eid);
        return Task.FromResult(Ok(new EmptyResponse()));
    }

    /// <summary>Returns all entities blocked from a group.</summary>
    [HttpPost("/Group/ListGroupBlocks")]
    public Task<IHttpActionResult> Groups_ListGroupBlocks(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ListGroupBlocksRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Group?.Id))
            return Task.FromResult(Err("InvalidParams", 1000, "Group is required"));

        var gid = body.Group.Id;
        var blocks = Database.GetCollection<PlayfabGroupBlockDbObject>(true)!
                             .Find(b => b.GroupId == gid)
                             .Select(b => new GroupBlock
                             {
                                 Group  = ToGroupKey(gid),
                                 Entity = ToLineage(b.EntityId, b.EntityType),
                             }).ToList();
        return Task.FromResult(Ok(new ListGroupBlocksResponse { BlockedEntities = blocks }));
    }

    /* ─── Membership lookup for an entity ────────────────────────────────── */

    /// <summary>Returns all groups the calling entity is a member of.</summary>
    [HttpPost("/Group/ListMembership")]
    public Task<IHttpActionResult> Groups_ListMembership(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ListMembershipRequest>(request) ?? new ListMembershipRequest();
        var entityId = body.Entity?.Id;
        if (string.IsNullOrEmpty(entityId)) entityId = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(entityId)) return Task.FromResult(Unauthorized());

        var memberships = Database.GetCollection<PlayfabGroupMembershipDbObject>(true)!
                                  .Find(m => m.EntityId == entityId).ToList();
        var groupColl = Database.GetCollection<PlayfabGroupDbObject>(true)!;
        var roleColl  = Database.GetCollection<PlayfabGroupRoleDbObject>(true)!;
        var groups = memberships.Select(m =>
        {
            var mgid = m.GroupId;
            var g = groupColl.FindOne(x => x.GroupId == mgid);
            if (g == null) return null;
            var roles = roleColl.Find(r => r.GroupId == mgid)
                                .Select(r => new GroupRole { RoleId = r.RoleId, RoleName = r.RoleName }).ToList();
            return new GroupWithRoles
            {
                Group          = ToGroupKey(g.GroupId),
                GroupName      = g.GroupName,
                ProfileVersion = ProfileVersionFor(g),
                Roles          = roles,
            };
        }).Where(x => x != null).Cast<GroupWithRoles>().ToList();
        return Task.FromResult(Ok(new ListMembershipResponse { Groups = groups }));
    }

    /// <summary>Returns all pending applications and invitations for the calling entity.</summary>
    [HttpPost("/Group/ListMembershipOpportunities")]
    public Task<IHttpActionResult> Groups_ListMembershipOpportunities(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<ListMembershipOpportunitiesRequest>(request) ?? new ListMembershipOpportunitiesRequest();
        var entityId = body.Entity?.Id;
        if (string.IsNullOrEmpty(entityId)) entityId = PlayFabIdFromRequest(request);
        if (string.IsNullOrEmpty(entityId)) return Task.FromResult(Unauthorized());

        var apps = Database.GetCollection<PlayfabGroupApplicationDbObject>(true)!
                           .Find(a => a.EntityId == entityId)
                           .Select(a => new GroupApplication
                           {
                               Group   = ToGroupKey(a.GroupId),
                               Entity  = ToLineage(a.EntityId, a.EntityType),
                               Expires = a.Expires.HasValue
                                   ? DateTimeOffset.FromUnixTimeSeconds(a.Expires.Value).UtcDateTime
                                   : DateTime.UtcNow.AddDays(30),
                           }).ToList();
        var invs = Database.GetCollection<PlayfabGroupInvitationDbObject>(true)!
                           .Find(i => i.EntityId == entityId)
                           .Select(i => new GroupInvitation
                           {
                               Group           = ToGroupKey(i.GroupId),
                               InvitedEntity   = ToLineage(i.EntityId, i.EntityType),
                               InvitedByEntity = ToLineage(string.IsNullOrEmpty(i.InvitedByEntityId) ? "title" : i.InvitedByEntityId, "title_player_account"),
                               RoleId          = i.RoleId,
                               Expires         = i.Expires.HasValue
                                   ? DateTimeOffset.FromUnixTimeSeconds(i.Expires.Value).UtcDateTime
                                   : DateTime.UtcNow.AddDays(30),
                           }).ToList();
        return Task.FromResult(Ok(new ListMembershipOpportunitiesResponse
        {
            Applications = apps,
            Invitations  = invs,
        }));
    }
}
