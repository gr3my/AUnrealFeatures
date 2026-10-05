using AUnrealFeatures.Hosting.Configuration;
using AUnrealFeatures.Playfab.Models;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    /// <summary>
    /// Adds a small, game-neutral set of title-data defaults. Existing values are never overwritten.
    /// Game-specific title data should be configured through the Admin API or your own bootstrap code.
    /// </summary>
    private static void SeedDefaultTitleData()
    {
        if (Database == null) return;

        var collection = Database.GetCollection<PlayfabTitleDataDbObject>(true)!;

        void SeedIfMissing(string key, string value)
        {
            if (collection.FindOne(x => x.TitleId == TITLE_ID && x.Scope == "Title" && x.Key == key) != null)
                return;

            collection.Insert(new PlayfabTitleDataDbObject
            {
                TitleId = TITLE_ID,
                Key = key,
                Value = value,
                Scope = "Title",
                LastUpdated = NowUnix(),
            });
        }

        SeedIfMissing("access_enabled", "true");
        SeedIfMissing("feature_flags", "{}");

        var defaultMap = EnvironmentSettings.Get("GAME_DEFAULT_MAP");
        if (!string.IsNullOrWhiteSpace(defaultMap))
            SeedIfMissing("coordinator_map", defaultMap);

        var defaultStream = EnvironmentSettings.Get("GAME_DEFAULT_STREAM_URL");
        if (!string.IsNullOrWhiteSpace(defaultStream))
            SeedIfMissing("config_stream_default", defaultStream);
    }
}
