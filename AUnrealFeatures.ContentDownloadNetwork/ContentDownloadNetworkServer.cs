using AUnrealFeatures.Hosting.Configuration;
using AUnrealFeatures.Hosting.Http;
using AUnrealFeatures.Hosting.Http.Preprocessors.Default;

namespace AUnrealFeatures.ContentDownloadNetwork;

/// <summary>Serves game-owned static media from a configurable content directory.</summary>
public sealed class ContentDownloadNetworkServer : AstraHttpServer, IContentDownloadNetworkServer
{
    private const string DefaultHostname = "localhost";
    private static readonly ushort ContentPort = EnvironmentSettings.GetUInt16("CONTENT_PORT", 9094);

    public ContentDownloadNetworkServer() : base(DefaultHostname, ContentPort)
    {
        this.AddStaticFiles(ResolveContentRoot(), allowExtensionlessLookups: true);
    }

    private static string ResolveContentRoot()
    {
        var configured = EnvironmentSettings.Get("CONTENT_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "AUnrealFeatures.ContentDownloadNetwork", "Content"),
            Path.Combine(Environment.CurrentDirectory, "..", "AUnrealFeatures.ContentDownloadNetwork", "Content"),
            Path.Combine(Environment.CurrentDirectory, "Content"),
            Path.Combine(AppContext.BaseDirectory, "Content"),
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }
}

public interface IContentDownloadNetworkServer { }
