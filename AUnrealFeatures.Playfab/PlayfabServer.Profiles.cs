using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Playfab.Models;
using PlayFab.DataModels;
using PlayFab.ProfilesModels;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    /* ─── Profiles API ────────────────────────────────────────────────────── */

    private PlayfabProfileDbObject GetOrCreateProfile(string entityId, string entityType)
    {
        var coll = Database.GetCollection<PlayfabProfileDbObject>(true)!;
        var p = coll.FindOne(x => x.EntityId == entityId && x.EntityType == entityType);
        if (p != null) return p;
        p = new PlayfabProfileDbObject
        {
            EntityId    = entityId,
            EntityType  = entityType,
            Created     = NowUnix(),
            LastUpdated = NowUnix(),
        };
        coll.Insert(p);
        return p;
    }

    private static EntityProfileBody ProfileToBody(PlayfabProfileDbObject p) => new()
    {
        Entity        = new PlayFab.ProfilesModels.EntityKey { Id = p.EntityId, Type = p.EntityType },
        DisplayName   = p.DisplayName,
        AvatarUrl     = p.AvatarUrl,
        Language      = p.Language,
        Created       = DateTimeOffset.FromUnixTimeSeconds(p.Created).UtcDateTime,
        VersionNumber = (int)(p.LastUpdated & int.MaxValue),
    };

    /// <summary>Returns an entity profile.</summary>
    [HttpPost("/Profile/GetProfile")]
    public Task<IHttpActionResult> Profile_GetProfile(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetEntityProfileRequest>(request) ?? new GetEntityProfileRequest();
        var entityId   = body.Entity?.Id   ?? "";
        var entityType = body.Entity?.Type ?? "title_player_account";
        if (string.IsNullOrEmpty(entityId))
            return Task.FromResult(Err("InvalidParams", 1000, "Entity is required"));

        var profile = GetOrCreateProfile(entityId, entityType);
        return Task.FromResult(Ok(new GetEntityProfileResponse { Profile = ProfileToBody(profile) }));
    }

    /// <summary>Returns a batch of entity profiles.</summary>
    [HttpPost("/Profile/GetProfiles")]
    public Task<IHttpActionResult> Profile_GetProfiles(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetEntityProfilesRequest>(request) ?? new GetEntityProfilesRequest();
        var profiles = new List<EntityProfileBody>();
        if (body.Entities != null)
            foreach (var e in body.Entities)
                profiles.Add(ProfileToBody(GetOrCreateProfile(e.Id ?? "", e.Type ?? "title_player_account")));
        return Task.FromResult(Ok(new GetEntityProfilesResponse { Profiles = profiles }));
    }

    /// <summary>Sets an entity's preferred language.</summary>
    [HttpPost("/Profile/SetProfileLanguage")]
    public Task<IHttpActionResult> Profile_SetProfileLanguage(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SetProfileLanguageRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId   = body.Entity?.Id   ?? "";
        var entityType = body.Entity?.Type ?? "title_player_account";
        var coll       = Database.GetCollection<PlayfabProfileDbObject>(true)!;
        var profile    = GetOrCreateProfile(entityId, entityType);
        profile.Language    = body.Language ?? "";
        profile.LastUpdated = NowUnix();
        coll.Update(profile);
        return Task.FromResult(Ok(new SetProfileLanguageResponse
        {
            OperationResult = PlayFab.ProfilesModels.OperationTypes.Updated,
            VersionNumber   = (int)(profile.LastUpdated & int.MaxValue),
        }));
    }

    /// <summary>Sets an entity's display name.</summary>
    [HttpPost("/Profile/SetDisplayName")]
    public Task<IHttpActionResult> Profile_SetDisplayName(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SetDisplayNameRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId   = body.Entity?.Id   ?? "";
        var entityType = body.Entity?.Type ?? "title_player_account";
        var coll       = Database.GetCollection<PlayfabProfileDbObject>(true)!;
        var profile    = GetOrCreateProfile(entityId, entityType);
        profile.DisplayName = body.DisplayName ?? "";
        profile.LastUpdated = NowUnix();
        coll.Update(profile);
        return Task.FromResult(Ok(new SetDisplayNameResponse
        {
            OperationResult = PlayFab.ProfilesModels.OperationTypes.Updated,
            VersionNumber   = (int)(profile.LastUpdated & int.MaxValue),
        }));
    }

    /// <summary>Resolves master player account ids → title player account entities.</summary>
    [HttpPost("/Profile/GetTitlePlayersFromMasterPlayerAccountIds")]
    public Task<IHttpActionResult> Profile_GetTitlePlayersFromMaster(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetTitlePlayersFromMasterPlayerAccountIdsRequest>(request)
                    ?? new GetTitlePlayersFromMasterPlayerAccountIdsRequest();
        var pcoll = Database.GetCollection<PlayfabPlayerDbObject>(true)!;
        var map = new Dictionary<string, PlayFab.ProfilesModels.EntityKey>();
        if (body.MasterPlayerAccountIds != null)
            foreach (var mid in body.MasterPlayerAccountIds)
            {
                var p = pcoll.FindOne(x => x.PlayFabId == mid);
                if (p != null) map[mid] = new PlayFab.ProfilesModels.EntityKey { Id = p.EntityId, Type = "title_player_account" };
            }
        return Task.FromResult(Ok(new GetTitlePlayersFromMasterPlayerAccountIdsResponse
        {
            TitleId = TITLE_ID,
            TitlePlayerAccounts = map,
        }));
    }

    /// <summary>Resolves Xbox Live account ids → title player account entities (stub).</summary>
    [HttpPost("/Profile/GetTitlePlayersFromXboxLiveIDs")]
    public Task<IHttpActionResult> Profile_GetTitlePlayersFromXbox(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        return Task.FromResult(Ok(new GetTitlePlayersFromProviderIDsResponse
        {
            TitlePlayerAccounts = new Dictionary<string, EntityLineage>()
        }));
    }

    /// <summary>Resolves provider ids → title player account entities (stub).</summary>
    [HttpPost("/Profile/GetTitlePlayersFromProviderIDs")]
    public Task<IHttpActionResult> Profile_GetTitlePlayersFromProvider(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        return Task.FromResult(Ok(new GetTitlePlayersFromProviderIDsResponse
        {
            TitlePlayerAccounts = new Dictionary<string, EntityLineage>()
        }));
    }

    /// <summary>Updates the title's global access policy.</summary>
    [HttpPost("/Profile/SetGlobalPolicy")]
    public Task<IHttpActionResult> Profile_SetGlobalPolicy(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SetGlobalPolicyRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var coll = Database.GetCollection<PlayfabPolicyDbObject>(true)!;
        var existing = coll.FindOne(p => p.TitleId == TITLE_ID && p.PolicyName == "GlobalPolicy");
        var json = JsonSerializer.Serialize(body.Permissions ?? new(), JsonOpts);
        if (existing == null)
            coll.Insert(new PlayfabPolicyDbObject { TitleId = TITLE_ID, PolicyName = "GlobalPolicy", StatementsJson = json });
        else
        { existing.StatementsJson = json; coll.Update(existing); }
        return Task.FromResult(Ok(new SetGlobalPolicyResponse()));
    }

    /// <summary>Updates an entity's profile-scoped policy.</summary>
    [HttpPost("/Profile/SetProfilePolicy")]
    public Task<IHttpActionResult> Profile_SetProfilePolicy(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SetEntityProfilePolicyRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId   = body.Entity?.Id   ?? "";
        var entityType = body.Entity?.Type ?? "title_player_account";
        var profile = GetOrCreateProfile(entityId, entityType);
        profile.PolicyJson  = JsonSerializer.Serialize(body.Statements ?? new(), JsonOpts);
        profile.LastUpdated = NowUnix();
        Database.GetCollection<PlayfabProfileDbObject>(true)!.Update(profile);
        return Task.FromResult(Ok(new SetEntityProfilePolicyResponse { Permissions = body.Statements }));
    }

    /* ─── Object data API ─────────────────────────────────────────────────── */

    /// <summary>Returns all custom-data objects on an entity.</summary>
    [HttpPost("/Object/GetObjects")]
    public Task<IHttpActionResult> Object_GetObjects(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetObjectsRequest>(request) ?? new GetObjectsRequest();
        var entityId   = body.Entity?.Id   ?? "";
        var entityType = body.Entity?.Type ?? "title_player_account";
        if (string.IsNullOrEmpty(entityId))
            return Task.FromResult(Err("InvalidParams", 1000, "Entity is required"));

        var coll = Database.GetCollection<PlayfabObjectDbObject>(true)!;
        var rows = coll.Find(x => x.EntityId == entityId && x.EntityType == entityType).ToList();
        var dict = rows.ToDictionary(r => r.ObjectName, r => new ObjectResult
        {
            ObjectName        = r.ObjectName,
            DataObject        = body.EscapeObject == true ? null : (object?)JsonSerializer.Deserialize<JsonElement>(r.DataObjectJson),
            EscapedDataObject = body.EscapeObject == true ? r.EscapedDataObject : null,
        });
        return Task.FromResult(Ok(new GetObjectsResponse
        {
            Entity         = new PlayFab.DataModels.EntityKey { Id = entityId, Type = entityType },
            Objects        = dict,
            ProfileVersion = rows.Count,
        }));
    }

    /// <summary>Sets or deletes custom-data objects on an entity.</summary>
    [HttpPost("/Object/SetObjects")]
    public Task<IHttpActionResult> Object_SetObjects(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SetObjectsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId   = body.Entity?.Id   ?? "";
        var entityType = body.Entity?.Type ?? "title_player_account";
        if (string.IsNullOrEmpty(entityId))
            return Task.FromResult(Err("InvalidParams", 1000, "Entity is required"));

        var coll    = Database.GetCollection<PlayfabObjectDbObject>(true)!;
        var results = new List<SetObjectInfo>();
        if (body.Objects != null)
            foreach (var obj in body.Objects)
            {
                var name     = obj.ObjectName ?? "";
                var existing = coll.FindOne(x => x.EntityId == entityId && x.EntityType == entityType && x.ObjectName == name);
                if (obj.DeleteObject == true)
                {
                    if (existing != null) coll.Delete(existing.Id);
                    results.Add(new SetObjectInfo { ObjectName = name, OperationReason = "Deleted" });
                    continue;
                }
                var json = obj.DataObject != null ? JsonSerializer.Serialize(obj.DataObject, JsonOpts) : "";
                if (existing == null)
                {
                    coll.Insert(new PlayfabObjectDbObject
                    {
                        EntityId          = entityId,
                        EntityType        = entityType,
                        ObjectName        = name,
                        DataObjectJson    = json,
                        EscapedDataObject = obj.EscapedDataObject ?? "",
                    });
                    results.Add(new SetObjectInfo { ObjectName = name, OperationReason = "Created" });
                }
                else
                {
                    existing.DataObjectJson    = json;
                    existing.EscapedDataObject = obj.EscapedDataObject ?? "";
                    coll.Update(existing);
                    results.Add(new SetObjectInfo { ObjectName = name, OperationReason = "Updated" });
                }
            }
        return Task.FromResult(Ok(new SetObjectsResponse
        {
            ProfileVersion = coll.Count(x => x.EntityId == entityId && x.EntityType == entityType),
            SetResults     = results,
        }));
    }

    /* ─── File data API ───────────────────────────────────────────────────── */

    /// <summary>Returns metadata for all files attached to an entity.</summary>
    [HttpPost("/File/GetFiles")]
    public Task<IHttpActionResult> File_GetFiles(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetFilesRequest>(request) ?? new GetFilesRequest();
        var entityId   = body.Entity?.Id   ?? "";
        var entityType = body.Entity?.Type ?? "title_player_account";

        var coll = Database.GetCollection<PlayfabFileDbObject>(true)!;
        var rows = coll.Find(f => f.EntityId == entityId && f.EntityType == entityType).ToList();
        var meta = rows.ToDictionary(r => r.FileName, r => new GetFileMetadata
        {
            FileName     = r.FileName,
            DownloadUrl  = $"/File/Download/{entityType}/{entityId}/{Uri.EscapeDataString(r.FileName)}",
            Size         = (int)r.Size,
            Checksum     = r.Checksum,
            LastModified = DateTimeOffset.FromUnixTimeSeconds(r.LastModified).UtcDateTime,
        });
        return Task.FromResult(Ok(new GetFilesResponse
        {
            Entity         = new PlayFab.DataModels.EntityKey { Id = entityId, Type = entityType },
            Metadata       = meta,
            ProfileVersion = rows.Count,
        }));
    }

    /// <summary>Initiates uploads — returns presigned URLs (here: server-relative POST URLs).</summary>
    [HttpPost("/File/InitiateFileUploads")]
    public Task<IHttpActionResult> File_InitiateFileUploads(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<InitiateFileUploadsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId   = body.Entity?.Id   ?? "";
        var entityType = body.Entity?.Type ?? "title_player_account";

        var uploads = (body.FileNames ?? new List<string>()).Select(name => new InitiateFileUploadMetadata
        {
            FileName  = name,
            UploadUrl = $"/File/Upload/{entityType}/{entityId}/{Uri.EscapeDataString(name)}",
        }).ToList();
        return Task.FromResult(Ok(new InitiateFileUploadsResponse
        {
            Entity         = new PlayFab.DataModels.EntityKey { Id = entityId, Type = entityType },
            UploadDetails  = uploads,
            ProfileVersion = 0,
        }));
    }

    /// <summary>Aborts an in-progress upload (no-op since we treat uploads as atomic).</summary>
    [HttpPost("/File/AbortFileUploads")]
    public Task<IHttpActionResult> File_AbortFileUploads(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<AbortFileUploadsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        return Task.FromResult(Ok(new AbortFileUploadsResponse
        {
            Entity         = body.Entity == null ? null! : new PlayFab.DataModels.EntityKey { Id = body.Entity.Id, Type = body.Entity.Type },
            ProfileVersion = 0,
        }));
    }

    /// <summary>Finalizes uploads.</summary>
    [HttpPost("/File/FinalizeFileUploads")]
    public Task<IHttpActionResult> File_FinalizeFileUploads(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<FinalizeFileUploadsRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId   = body.Entity?.Id   ?? "";
        var entityType = body.Entity?.Type ?? "title_player_account";
        var coll       = Database.GetCollection<PlayfabFileDbObject>(true)!;
        var meta       = new Dictionary<string, GetFileMetadata>();
        if (body.FileNames != null)
            foreach (var name in body.FileNames)
            {
                var n = name;
                var f = coll.FindOne(x => x.EntityId == entityId && x.EntityType == entityType && x.FileName == n);
                if (f != null)
                {
                    meta[n] = new GetFileMetadata
                    {
                        FileName     = f.FileName,
                        DownloadUrl  = $"/File/Download/{entityType}/{entityId}/{Uri.EscapeDataString(f.FileName)}",
                        Size         = (int)f.Size,
                        Checksum     = f.Checksum,
                        LastModified = DateTimeOffset.FromUnixTimeSeconds(f.LastModified).UtcDateTime,
                    };
                }
            }
        return Task.FromResult(Ok(new FinalizeFileUploadsResponse
        {
            Entity         = new PlayFab.DataModels.EntityKey { Id = entityId, Type = entityType },
            Metadata       = meta,
            ProfileVersion = coll.Count(x => x.EntityId == entityId && x.EntityType == entityType),
        }));
    }

    /// <summary>Deletes files attached to an entity.</summary>
    [HttpPost("/File/DeleteFiles")]
    public Task<IHttpActionResult> File_DeleteFiles(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteFilesRequest>(request);
        if (body == null) return Task.FromResult(Err("InvalidParams", 1000, "Body required"));
        var entityId   = body.Entity?.Id   ?? "";
        var entityType = body.Entity?.Type ?? "title_player_account";
        var coll       = Database.GetCollection<PlayfabFileDbObject>(true)!;
        if (body.FileNames != null)
            foreach (var name in body.FileNames)
            {
                var n = name;
                coll.DeleteMany(x => x.EntityId == entityId && x.EntityType == entityType && x.FileName == n);
            }
        return Task.FromResult(Ok(new DeleteFilesResponse
        {
            Entity         = new PlayFab.DataModels.EntityKey { Id = entityId, Type = entityType },
            ProfileVersion = coll.Count(x => x.EntityId == entityId && x.EntityType == entityType),
        }));
    }
}
