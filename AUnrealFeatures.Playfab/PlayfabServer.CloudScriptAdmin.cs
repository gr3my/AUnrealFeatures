using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Playfab.Models;
using PlayFab.CloudScriptModels;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    /* ─── CloudScript revision management (admin) ─────────────────────────── */

    /// <summary>Upload (or replace) a single CloudScript file for the title. The file becomes part
    /// of a new revision number unless an existing revision with the same files is found.</summary>
    [HttpPost("/Admin/UpdateCloudScript")]
    public Task<IHttpActionResult> Admin_UpdateCloudScript(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());

        UpdateCloudScriptBody body;
        try { body = JsonSerializer.Deserialize<UpdateCloudScriptBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (body == null || body.Files == null || body.Files.Count == 0)
            return Task.FromResult(Err("InvalidParams", 1000, "Files array required (each item: { Filename, FileContents })"));

        var coll  = Database.GetCollection<PlayfabCloudScriptFileDbObject>(true)!;
        var maxRev = coll.Find(f => f.TitleId == TITLE_ID).Select(f => (int?)f.Version).DefaultIfEmpty(0).Max() ?? 0;
        var newRev = (uint)(maxRev + 1);

        // If `Publish == true`, mark the new revision as published and demote earlier ones
        if (body.Publish)
            foreach (var prev in coll.Find(f => f.TitleId == TITLE_ID && f.Published))
            { prev.Published = false; coll.Update(prev); }

        foreach (var f in body.Files)
        {
            if (string.IsNullOrEmpty(f.Filename)) continue;
            coll.Insert(new PlayfabCloudScriptFileDbObject
            {
                TitleId      = TITLE_ID,
                FileName     = f.Filename,
                FileContents = f.FileContents ?? "",
                Version      = newRev,
                Published    = body.Publish,
                Uploaded     = NowUnix(),
            });
        }

        return Task.FromResult(Ok(new UpdateCloudScriptResponse
        {
            Revision  = (int)newRev,
            Version   = (int)newRev,
            Published = body.Publish,
        }));
    }

    /// <summary>List all CloudScript revisions and their files for the title.</summary>
    [HttpPost("/Admin/GetCloudScriptRevision")]
    public Task<IHttpActionResult> Admin_GetCloudScriptRevision(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());

        GetCloudScriptRevisionBody body;
        try { body = JsonSerializer.Deserialize<GetCloudScriptRevisionBody>(request.Body, JsonOpts) ?? new(); }
        catch { body = new(); }

        var coll = Database.GetCollection<PlayfabCloudScriptFileDbObject>(true)!;
        IEnumerable<PlayfabCloudScriptFileDbObject> files;
        if (body.Version.HasValue)
        {
            var v = (uint)body.Version.Value;
            files = coll.Find(f => f.TitleId == TITLE_ID && f.Version == v);
        }
        else if (body.Revision.HasValue)
        {
            var r = (uint)body.Revision.Value;
            files = coll.Find(f => f.TitleId == TITLE_ID && f.Version == r);
        }
        else
        {
            // Live revision = latest published
            var live = coll.Find(f => f.TitleId == TITLE_ID && f.Published)
                           .OrderByDescending(f => f.Version).FirstOrDefault();
            files = live == null ? Array.Empty<PlayfabCloudScriptFileDbObject>()
                                 : coll.Find(f => f.TitleId == TITLE_ID && f.Version == live.Version);
        }
        var fileList = files.OrderBy(f => f.FileName).ToList();
        return Task.FromResult(Ok(new GetCloudScriptRevisionResponse
        {
            Files = fileList.Select(f => new CloudScriptFile
            {
                Filename     = f.FileName,
                FileContents = f.FileContents,
            }).ToList(),
            Version    = fileList.Count > 0 ? (int)fileList[0].Version : 0,
            Revision   = fileList.Count > 0 ? (int)fileList[0].Version : 0,
            CreatedAt  = fileList.Count > 0 ? DateTimeOffset.FromUnixTimeSeconds(fileList[0].Uploaded).UtcDateTime : (DateTime?)null,
            IsPublished = fileList.Any(f => f.Published),
        }));
    }

    /// <summary>List all CloudScript versions for the title (one entry per revision number).</summary>
    [HttpPost("/Admin/GetCloudScriptVersions")]
    public Task<IHttpActionResult> Admin_GetCloudScriptVersions(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.DevSecretKey)) return Task.FromResult(Unauthorized());

        var coll = Database.GetCollection<PlayfabCloudScriptFileDbObject>(true)!;
        var versions = coll.Find(f => f.TitleId == TITLE_ID)
                           .GroupBy(f => f.Version)
                           .Select(grp => new CloudScriptVersionStatus
                           {
                               Version       = (int)grp.Key,
                               Revision      = (int)grp.Key,
                               PublishedRevision = grp.Any(f => f.Published) ? (int)grp.Key : 0,
                           })
                           .OrderByDescending(v => v.Version)
                           .ToList();
        return Task.FromResult(Ok(new GetCloudScriptVersionsResponse { Versions = versions }));
    }

    /// <summary>Set which revision is currently published/live.</summary>
    [HttpPost("/Admin/SetPublishedRevision")]
    public Task<IHttpActionResult> Admin_SetPublishedRevision(IHttpRequest request, IHttpResponse response)
    {
        SetPublishedRevisionBody body;
        try { body = JsonSerializer.Deserialize<SetPublishedRevisionBody>(request.Body, JsonOpts); }
        catch (Exception e) { return Task.FromResult(Err("InvalidParams", 1000, $"Body JSON invalid: {e.Message}")); }
        if (body == null || !body.Revision.HasValue)
            return Task.FromResult(Err("InvalidParams", 1000, "Revision required"));

        var coll = Database.GetCollection<PlayfabCloudScriptFileDbObject>(true)!;
        var rev  = (uint)body.Revision.Value;
        if (!coll.FindAll().Any(f => f.TitleId == TITLE_ID && f.Version == rev))
            return Task.FromResult(Err("CloudScriptRevisionNotFound", 1621, $"Revision {body.Revision} not found", HttpStatusCode.NotFound));

        foreach (var f in coll.Find(f => f.TitleId == TITLE_ID).ToList())
        {
            var shouldPublish = f.Version == rev;
            if (f.Published != shouldPublish) { f.Published = shouldPublish; coll.Update(f); }
        }
        return Task.FromResult(Ok(new SetPublishedRevisionResponse { Revision = body.Revision.Value }));
    }
}

/* ─── Admin payload types (private to this file) ──────────────────────────── */

internal sealed class UpdateCloudScriptBody
{
    public List<CloudScriptFile> Files { get; set; }
    public bool Publish { get; set; }
    public string DeveloperPlayFabId { get; set; }
}

internal sealed class GetCloudScriptRevisionBody
{
    public int? Revision { get; set; }
    public int? Version  { get; set; }
}

internal sealed class SetPublishedRevisionBody
{
    public int? Revision { get; set; }
    public int? Version  { get; set; }
}

public sealed class CloudScriptFile
{
    public string Filename     { get; set; } = "";
    public string FileContents { get; set; } = "";
}

public sealed class UpdateCloudScriptResponse
{
    public int Version { get; set; }
    public int Revision { get; set; }
    public bool Published { get; set; }
}

public sealed class GetCloudScriptRevisionResponse
{
    public List<CloudScriptFile> Files { get; set; } = new();
    public int Version { get; set; }
    public int Revision { get; set; }
    public DateTime? CreatedAt { get; set; }
    public bool IsPublished { get; set; }
}

public sealed class GetCloudScriptVersionsResponse
{
    public List<CloudScriptVersionStatus> Versions { get; set; } = new();
}

public sealed class CloudScriptVersionStatus
{
    public int Version { get; set; }
    public int Revision { get; set; }
    public int PublishedRevision { get; set; }
}

public sealed class SetPublishedRevisionResponse
{
    public int Revision { get; set; }
}
