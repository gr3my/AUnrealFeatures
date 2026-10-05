using System.Text.Json.Serialization;

namespace AUnrealFeatures.Ares.Models;

// legacy client build GET /v1/region response. The client geo-picks its server region from this.
// Matches SDK FRegionResponse { FString continent_code; FString country_code }.
public sealed class RegionResponse
{
    [JsonPropertyName("continent_code")]
    public string ContinentCode { get; set; } = "NA";

    [JsonPropertyName("country_code")]
    public string CountryCode { get; set; } = "US";
}
