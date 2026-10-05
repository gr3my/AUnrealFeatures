using AUnrealFeatures.Hosting.Configuration;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AUnrealFeatures.Virtex;

public sealed partial class VirtexServer
{
    // Bypasses ParseBody<T>/JsonSerializer.Deserialize entirely for this one
    // field — PartnerId arrives as either a bare JSON number ({"PartnerId":3})
    // or a quoted string depending on caller, and binding that directly to a
    // typed DTO property (string, or even JsonElement) kept silently failing
    // deserialization, with ParseBody's catch-all swallowing the exception
    // and returning null. Parsing the raw body with JsonDocument and reading
    // whichever ValueKind is actually present sidesteps that entirely.
    private string ExtractPartnerId(IHttpRequest request)
    {
        try
        {
            if (request.Body == null || request.Body.Length == 0) return "";
            using var doc = JsonDocument.Parse(request.Body);
            if (!doc.RootElement.TryGetProperty("PartnerId", out var value)) return "";
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Number => value.GetRawText(),
                _ => "",
            };
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to extract PartnerId from request body");
            return "";
        }
    }

    // (FBannerMedia::ParseJson, FStadiumMedia::ParseJson, FStadiumType::ParseJson)
    // -- every PartnerPortal contract uses snake_case wire keys EXCEPT two
    // specific oddities that are genuinely PascalCase on the wire:
    // FBannerMedia's own "StadiumTypeName" and FStadiumType's "Name". Do not
    private sealed class MediaData
    {
        [JsonPropertyName("id")] public int ID { get; set; }
        [JsonPropertyName("media_type_id")] public int MediaTypeId { get; set; }
        [JsonPropertyName("partner_id")] public string PartnerId { get; set; } = "";
        [JsonPropertyName("image_url")] public string ImageUrl { get; set; } = "";
        [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = "";
        [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";
    }

    // FBannerMedia::ParseJson zeroes out the ENTIRE entry (silently, no error)
    // unless id, stadium_config_id, media_id, media_location_id, AND display
    // set all five to a non-zero value or the client discards it.
    private sealed class BannerMedia
    {
        [JsonPropertyName("id")] public int ID { get; set; }
        [JsonPropertyName("stadium_config_id")] public int StadiumConfigId { get; set; }
        [JsonPropertyName("media_id")] public int MediaId { get; set; }
        [JsonPropertyName("media_location_id")] public int MediaLocationId { get; set; }
        [JsonPropertyName("media_location_name")] public string MediaLocationName { get; set; } = "";
        [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = "";
        [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";
        [JsonPropertyName("display")] public int Display { get; set; } = 1;
        [JsonPropertyName("media")] public MediaData Media { get; set; } = new();
        public string StadiumTypeName { get; set; } = ""; // PascalCase on the wire, confirmed
    }

    private sealed class StadiumType
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        public string Name { get; set; } = ""; // PascalCase on the wire, confirmed
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("map_name")] public string MapName { get; set; } = "";
        [JsonPropertyName("banner_image")] public string BannerImage { get; set; } = "";
        [JsonPropertyName("is_private")] public int IsPrivate { get; set; }
        [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = "";
        [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";
    }

    // REAL shape of each element of GetPartnerMedia's top-level "data" array.
    // Critically, "stream_url" here (not anything in the "media" array) is
    // what FPartnerPortalModule::GetPartnerStadium(PartnerId, StadiumTypeName)
    // returns to UGetDefaultStreamUrls, which is what actually resolves the
    // "default stream url" a stadium screen without its own hardcoded default
    // ends up playing. Our previous {"data":[{"media":[...]}]} response had
    // no stream_url at all, so that lookup always got an empty FStadiumMedia.
    private sealed class StadiumMedia
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("partner_id")] public string PartnerId { get; set; } = "";
        [JsonPropertyName("stadium_type_id")] public int StadiumTypeId { get; set; }
        [JsonPropertyName("stadium_type_name")] public string StadiumTypeName { get; set; } = "";
        [JsonPropertyName("stream_url")] public string StreamUrl { get; set; } = "";
        [JsonPropertyName("default_language")] public string DefaultLanguage { get; set; } = "en";
        [JsonPropertyName("advanced_configuration")] public int AdvancedConfiguration { get; set; }
        [JsonPropertyName("data_key")] public string DataKey { get; set; } = "";
        [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = "";
        [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";
        [JsonPropertyName("stadium_type")] public StadiumType StadiumType { get; set; } = new();
        [JsonPropertyName("media")] public List<BannerMedia> Media { get; set; } = new();
    }

    // PartnerId (the DT_Portals/access_gate_lobby_<N> numeric scheme, see
    // header comment) -> which (StadiumType, MediaLocationName, DefaultTexture)
    // placements it has banner media for. StadiumType matches the
    // E_Level-associated map name; MediaLocationName is the exact LocationID
    // string a placed BP_Billboard*/BP_WP_*/BP_StoreLiminal_C instance filters
    // GetPartnerMedia's response by.
    //
    // Lobby.json/Lobby_*.json/Plaza_*.json/Lounge_*.json and grepping every
    // can't show this, since these are per-instance level overrides, not
    // it unset too) is the actual Texture2D asset name the placed instance
    // references as its own default/preview texture (e.g. AVlcScreenActor's
    // "Texture"/"Texture_0" property), used by ResolveImageUrl below to pick
    // the dimensionally-correct template art before falling back to a debug
    // placehold.co image.
    //
    // Reception's BP_Billboard_MediumBanner_C/SmallBanner_C instances were
    // checked too and have NO LocationID property at all -- they use a
    // separate self-contained "Images: Sign" array system, unrelated to
    // GetPartnerMedia. Nothing to add for PID "0".
    // Keyed by STADIUM TYPE (not PartnerId) -- every PartnerId that resolves
    // to a given stadium type gets this FULL location list, not just whatever
    // one PID happened to be hardcoded. See ResolveStadiumType below for how
    // an arbitrary/unmapped PartnerId (e.g. one hijacked from a customisation
    // title-data value) still gets a real location set instead of a single
    // generic "Default" fallback entry.
    // Example media placement sets. Extend these names to match your game's maps and media slots.
    private static readonly Dictionary<string, (string Location, string? DefaultTexture)[]> StadiumTypePlacements = new()
    {
        ["Default"] = new[] { ("Default", (string?)"placeholder") },
        ["Lobby"] = new[] { ("Default", (string?)"placeholder") },
        ["Arena"] = new[] { ("Default", (string?)"placeholder") },
    };

    // Any PartnerId gets the full location set for a real stadium type, not
    // just the 6 numeric PIDs (0-5) DT_Portals/coordinator_map_<PID> use.
    // Known small PIDs map via PartnerDefaultStadiums (defined below); any
    // other value (e.g. one hijacked from a config_customisation_bsc_<PID>
    // back to "Lobby" -- the same default GetPartnerData/GetPartners already
    // use for an unrecognized PartnerId.
    private static string ResolveStadiumType(string partnerId) =>
        int.TryParse(partnerId, out var pidInt) && PartnerDefaultStadiums.TryGetValue(pidInt, out var stadiumType)
            ? stadiumType
            : "Default";

    // ContentDownloadNetworkServer (AUnrealFeatures.ContentDownloadNetwork,
    // port 9094) serves whatever's dropped under its own Content/ folder.
    // Since Ares hosts every server in-process off the same CWD, that folder
    // resolves to <wherever Ares is launched from>/Content/ -- same
    // CWD-relative-path gotcha as the LiteDB files (see the /coordinator
    //   1. Content/partners/<PartnerId>_<location>.png -- per-partner override,
    //      flat "PID_location" naming so a specific sponsor's art can override
    //      one slot without touching the shared stadium-type template
    //      itself references for this exact placed instance (ground truth,
    //      shared across every location that reuses the same texture)
    //   3. Content/templates/<location>.png -- manual per-location override
    //   4. placehold.co -- only until a real template/override exists
    private static readonly string ContentBaseUrl =
        EnvironmentSettings.Get("CONTENT_PUBLIC_BASE_URL", "http://localhost:9094").TrimEnd('/');

    private static string ResolveImageUrl(string partnerId, string location, string? defaultTexture)
    {
        var partnerFileName = $"{partnerId}_{location}.png";
        var partnerPath = Path.Combine(Environment.CurrentDirectory, "Content", "partners", partnerFileName);
        if (File.Exists(partnerPath))
            return $"{ContentBaseUrl}/partners/{Uri.EscapeDataString(partnerFileName)}";

        if (!string.IsNullOrEmpty(defaultTexture))
        {
            var texturePath = Path.Combine(Environment.CurrentDirectory, "Content", "templates", defaultTexture + ".png");
            if (File.Exists(texturePath))
                return $"{ContentBaseUrl}/templates/{Uri.EscapeDataString(defaultTexture)}.png";
        }

        var templatePath = Path.Combine(Environment.CurrentDirectory, "Content", "templates", location + ".png");
        if (File.Exists(templatePath))
            return $"{ContentBaseUrl}/templates/{Uri.EscapeDataString(location)}.png";

        var placeholderUrl = EnvironmentSettings.Get("CONTENT_PLACEHOLDER_URL", "http://localhost:9094/templates/placeholder.png");
        return placeholderUrl;
    }

    private static List<BannerMedia> BuildPlaceholderMedia(string partnerId)
    {
        // Every PartnerId gets the FULL location set for its resolved stadium
        // type -- not just the numeric PIDs we happen to have hardcoded.
        var stadiumType = ResolveStadiumType(partnerId);
        var placements = StadiumTypePlacements.TryGetValue(stadiumType, out var found)
            ? found
            : new (string, string?)[] { ("Default", null) };

        var now = DateTimeOffset.UtcNow.ToString("o");
        var list = new List<BannerMedia>();
        var id = 1;
        foreach (var (location, defaultTexture) in placements)
        {
            list.Add(new BannerMedia
            {
                // All five of these must stay non-zero -- FBannerMedia::ParseJson
                // silently zeroes the whole entry otherwise (see class comment).
                ID = id,
                StadiumConfigId = id,
                MediaId = id,
                MediaLocationId = id,
                MediaLocationName = location,
                CreatedAt = now,
                UpdatedAt = now,
                Display = 1,
                StadiumTypeName = stadiumType,
                Media = new MediaData
                {
                    ID = id,
                    MediaTypeId = 1,
                    PartnerId = partnerId,
                    ImageUrl = ResolveImageUrl(partnerId, location, defaultTexture),
                    CreatedAt = now,
                    UpdatedAt = now,
                },
            });
            id++;
        }
        return list;
    }

    // Same numeric-PID scheme as PartnerDefaultStadiums/config_stream_<PID>
    // title data (see PlayfabServer.Seed.cs) -- reused here so StreamUrl stays
    // in sync with whatever's been configured via /Admin/SetTitleData instead
    // of hardcoding a second, independent default.
    private string ResolveConfigStreamUrl(string partnerId)
    {
        var coll = AUnrealFeatures.Playfab.PlayfabServer.Database
            .GetCollection<AUnrealFeatures.Playfab.Models.PlayfabTitleDataDbObject>(true)!;
        // LiteDB's expression visitor can't resolve string interpolation inline
        // inside the predicate (throws NotImplementedException in StringResolver),
        // so the key must be a plain local variable, not built in the lambda.
        var titleId = AUnrealFeatures.Playfab.PlayfabServer.TITLE_ID;
        var key = "config_stream_" + partnerId;
        var entry = coll.FindOne(x => x.TitleId == titleId && x.Scope == "Title" && x.Key == key);
        return string.IsNullOrEmpty(entry?.Value) ? EnvironmentSettings.Get("GAME_DEFAULT_STREAM_URL", "https://example.invalid/stream") : entry!.Value;
    }

    // behind FPartnerPortalModule::QueryPartnerStadiumMedia, which backs BOTH
    // UGetPartnerMediaTask and UGetDefaultStreamUrls): the top-level "data"
    // array holds per-(PartnerId, StadiumType) FStadiumMedia objects, each
    // with its own "stream_url" field alongside a nested "media" array. The
    // stream_url here -- not anything in "media" -- is what
    // FPartnerPortalModule::GetPartnerStadium ultimately returns to
    // UGetDefaultStreamUrls as "the" default stream url for a stadium screen
    // that has no hardcoded class-default (for example, a title-specific arena map).
    [HttpPost("/partner-portal/PartnerInterface/GetPartnerMedia")]
    public async Task<IHttpActionResult> GetPartnerMedia(IHttpRequest request, IHttpResponse response)
    {
        var partnerId = ExtractPartnerId(request);
        Logger.Information("Partner stadium media requested for PartnerId={PartnerId}", partnerId);

        var now = DateTimeOffset.UtcNow.ToString("o");
        var stadiumTypeName = ResolveStadiumType(partnerId);

        var data = new[]
        {
            new StadiumMedia
            {
                Id = int.TryParse(partnerId, out var pidInt) ? pidInt : 0,
                PartnerId = partnerId,
                StadiumTypeId = int.TryParse(partnerId, out var stidInt) ? stidInt : 0,
                StadiumTypeName = stadiumTypeName,
                StreamUrl = ResolveConfigStreamUrl(partnerId),
                DefaultLanguage = "en",
                CreatedAt = now,
                UpdatedAt = now,
                StadiumType = new StadiumType
                {
                    Id = int.TryParse(partnerId, out var typeId) ? typeId : 0,
                    Name = stadiumTypeName,
                    Key = stadiumTypeName,
                    MapName = stadiumTypeName,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                Media = BuildPlaceholderMedia(partnerId),
            },
        };
        return Results.Configurable(HttpStatusCode.OK, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new { data }, JsonOpts));
    }

    // ONLY endpoint the native partner/gate resolution path actually calls (GET,
    // no body). UGetPartnerDataTask::QueryPartnerData(PID) does NOT hit a
    // GetPartnerData path at all: it fetches this full list, caches it in
    // FPartnerPortalModule::StoredPartnerData, then linearly scans for the
    // entry whose "id" matches the requested PartnerId. Response MUST be a
    // {"data": [...]} wrapper (FJsonObject::GetArrayField(obj, "data")), and
    // each element is parsed by FPartnerData::ParseJson, which reads:
    //   "id" (JSON number, stringified via %i for the FString PartnerId)
    //   "default_stadium_name" (string) -> FPartnerData.Map
    //     falling back to nested "default_stadium": { "map_name": "..." }
    //     if "default_stadium_name" is absent.
    // FPartnerData.Map is then FName'd and passed to GetLevelFromName ->
    // Portal->LoadPortalLocation(Level), which is what actually resolves the
    // DT_Portals row (keyed by its own Map/E_Level field, NOT by PartnerId)
    // whose GateCubemap gets loaded onto the gate's display panel. Our old
    // response here (a bare array of PID strings) meant Map always came back
    // empty, GetLevelFromName failed, and the cubemap LoadAsset never fired
    // thumbnails rendering as a blank/untextured panel.
    // Example partner-to-map mappings. Replace or populate these for your title.
    private static readonly Dictionary<int, string> PartnerDefaultStadiums = new()
    {
        [0] = "Default",
        [1] = "Lobby",
        [2] = "Arena",
    };

    [HttpGet("/partner-portal/PartnerInterface/GetPartners")]
    public async Task<IHttpActionResult> GetPartners(IHttpRequest request, IHttpResponse response)
    {
        Logger.Information("Partners list requested ({Count} known partners)", PartnerDefaultStadiums.Count);
        var now = DateTimeOffset.UtcNow.ToString("o");
        // of UGetPartnerDataTask::Activate) has many more fields than what the
        // "default_stadium_name" feed anything any Blueprint content uses —
        // GetLevelFromName off DefaultStadiumName is the only real consumer).
        // Padding the rest with harmless defaults costs nothing and removes
        // any doubt about a stricter-than-expected parser choking on missing
        var data = PartnerDefaultStadiums.Select(kv => new
        {
            id = kv.Key,
            name = kv.Value,
            owner_id = 0,
            country_id = 0,
            timezone = "UTC",
            default_stadium_id = kv.Key,
            default_stadium_name = kv.Value,
            website_url = "",
            facebook_url = "",
            twitter_url = "",
            discord_url = "",
            instagram_url = "",
            tiktok_url = "",
            created_at = now,
            updated_at = now,
        }).ToArray();
        // NOT VirtexOk — that wraps in {"result": ...}, but
        // FPartnerData::ParseJson reads "data" as a TOP-LEVEL array field.
        return Results.Configurable(HttpStatusCode.OK, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new { data }));
    }

    [HttpPost("/partner-portal/PartnerInterface/GetPartnerData")]
    public async Task<IHttpActionResult> GetPartnerData(IHttpRequest request, IHttpResponse response)
    {
        var partnerId = ExtractPartnerId(request);
        Logger.Information("Single partner data requested for PartnerId={PartnerId}", partnerId);

        return VirtexOk(BuildPlaceholderMedia(partnerId));
    }
}
