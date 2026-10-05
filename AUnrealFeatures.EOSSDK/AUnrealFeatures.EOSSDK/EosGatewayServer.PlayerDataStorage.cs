using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace AUnrealFeatures.EOSSDK;


public sealed partial class EosGatewayServer
{
    static string PlayerDataRoot => Path.Combine(AppContext.BaseDirectory, "playerdata");

    static bool IsSafeStorageSegment(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value is not "." and not ".."
           && string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal)
           && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    static string? PlayerDataFilePath(string deploymentId, string productUserId, string filename, bool createDirectory)
    {
        if (!IsSafeStorageSegment(deploymentId) || !IsSafeStorageSegment(productUserId) || !IsSafeStorageSegment(filename))
            return null;

        var root = Path.GetFullPath(PlayerDataRoot);
        var dir = Path.GetFullPath(Path.Combine(root, deploymentId, productUserId));
        var file = Path.GetFullPath(Path.Combine(dir, filename));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!file.StartsWith(rootPrefix, comparison)) return null;
        if (createDirectory) Directory.CreateDirectory(dir);
        return file;
    }

    [HttpGet("/datastorage/v2/access/deployment/{deployment_id}/private/{product_user_id}/{filename}")]
    public async Task<IHttpActionResult> PlayerDataStorageAccess(IHttpRequest request, IHttpResponse response, string deployment_id, string product_user_id, string filename)
    {
        var path = PlayerDataFilePath(deployment_id, product_user_id, filename, createDirectory: false);
        if (path == null) return Results.BadRequest(new { error = "Invalid storage path." });
        var exists = File.Exists(path);

        var baseUrl = $"http://{Hostname}:{Port}/datastorage/v2/data/deployment/{deployment_id}/private/{product_user_id}/{filename}";

        object? fileMeta = null;
        if (exists)
        {
            var info = new FileInfo(path);
            var md5 = Convert.ToHexString(MD5.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            fileMeta = new
            {
                filename,
                md5Hash = md5,
                lastModified = info.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            };
        }

        var result = new Dictionary<string, object?>
        {
            ["accountId"] = product_user_id,
            ["deploymentId"] = deployment_id,
            ["writeRequests"] = new[] { new { id = Guid.NewGuid().ToString("N"), uri = baseUrl } },
        };
        if (exists)
        {
            result["readRequests"] = new[] { new { id = Guid.NewGuid().ToString("N"), deviceId = (string?)null, uri = baseUrl } };
            result["file"] = fileMeta;
        }
        else
        {
            result["readRequests"] = Array.Empty<object>();
        }

        return Results.Configurable(HttpStatusCode.OK, "application/json", JsonSerializer.SerializeToUtf8Bytes(result));
    }

    [HttpGet("/datastorage/v2/data/deployment/{deployment_id}/private/{product_user_id}/{filename}")]
    public async Task<IHttpActionResult> PlayerDataStorageGet(IHttpRequest request, IHttpResponse response, string deployment_id, string product_user_id, string filename)
    {
        var path = PlayerDataFilePath(deployment_id, product_user_id, filename, createDirectory: false);
        if (path == null) return Results.BadRequest(new { error = "Invalid storage path." });
        if (!File.Exists(path)) return Results.NotFound();
        return Results.Configurable(HttpStatusCode.OK, "application/octet-stream", await File.ReadAllBytesAsync(path));
    }

    [HttpPut("/datastorage/v2/data/deployment/{deployment_id}/private/{product_user_id}/{filename}")]
    public async Task<IHttpActionResult> PlayerDataStoragePut(IHttpRequest request, IHttpResponse response, string deployment_id, string product_user_id, string filename)
    {
        var path = PlayerDataFilePath(deployment_id, product_user_id, filename, createDirectory: true);
        if (path == null) return Results.BadRequest(new { error = "Invalid storage path." });
        await File.WriteAllBytesAsync(path, request.Body ?? Array.Empty<byte>());
        return Results.Ok();
    }
}
