using AUnrealFeatures.Hosting.Configuration;
using AUnrealFeatures.Playfab.Models;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    // Format: item-id|Display Name;item-id-2|Display Name 2
    private static readonly (string ItemId, string DisplayName)[] DefaultAccessTokenItems =
        ParseDefaultItems(EnvironmentSettings.Get("PLAYFAB_DEFAULT_ACCESS_ITEMS"));

    private static (string ItemId, string DisplayName)[] ParseDefaultItems(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<(string, string)>();

        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Split('|', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            .Select(parts => (parts[0], string.IsNullOrWhiteSpace(parts[1]) ? parts[0] : parts[1]))
            .ToArray();
    }

    private void SeedDefaultCatalogItems()
    {
        if (Database == null || DefaultAccessTokenItems.Length == 0) return;
        var collection = Database.GetCollection<PlayfabEconomyItemDbObject>(true)!;

        foreach (var (itemId, displayName) in DefaultAccessTokenItems)
        {
            if (collection.FindOne(x => x.TitleId == TITLE_ID && x.ItemId == itemId) != null)
                continue;

            collection.Insert(new PlayfabEconomyItemDbObject
            {
                TitleId = TITLE_ID,
                ItemId = itemId,
                ContentType = "AccessToken",
                DefaultStackId = "Default",
                Type = "catalogItem",
                DisplayVersion = "1",
                Title = new() { ["NEUTRAL"] = displayName },
                Description = new() { ["NEUTRAL"] = $"Grants access to {displayName}." },
                Tags = new() { "AccessToken" },
                Status = "Published",
                Created = NowUnix(),
            });
        }
    }

    private void EnsureDefaultInventoryItems(string entityId)
    {
        if (Database == null || string.IsNullOrWhiteSpace(entityId) || DefaultAccessTokenItems.Length == 0)
            return;

        var collection = Database.GetCollection<PlayfabEconomyInventoryDbObject>(true)!;
        foreach (var (itemId, _) in DefaultAccessTokenItems)
        {
            if (collection.FindOne(x => x.EntityId == entityId && x.CollectionId == "Default" && x.ItemId == itemId && x.StackId == "Default") != null)
                continue;

            collection.Insert(new PlayfabEconomyInventoryDbObject
            {
                EntityId = entityId,
                EntityType = "title_player_account",
                CollectionId = "Default",
                ItemId = itemId,
                StackId = "Default",
                Amount = 1,
            });
        }
    }
}
