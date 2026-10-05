using AUnrealFeatures.Hosting.Database;

namespace AUnrealFeatures.Playfab.Models;

public sealed class PlayfabPlayerDbObject : AstraDbObject
{
    public string PlayFabId { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string TitleId { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string CustomId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string AndroidDeviceId { get; set; } = "";
    public string IosDeviceId { get; set; } = "";
    public string FacebookId { get; set; } = "";
    public string GoogleId { get; set; } = "";
    public string GoogleAccountId { get; set; } = "";
    public string SteamId { get; set; } = "";
    public string XboxId { get; set; } = "";
    public string PsnId { get; set; } = "";
    public string NintendoId { get; set; } = "";
    public string TwitchId { get; set; } = "";
    public string KongregateId { get; set; } = "";
    public string GameCenterId { get; set; } = "";
    public string OpenIdSubject { get; set; } = "";
    public long Created { get; set; }
    public long LastLogin { get; set; }
    public string AvatarUrl { get; set; } = "";
    public string OriginPlatform { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public bool IsBanned { get; set; }
    public string SessionTicket { get; set; } = "";
    public long SessionExpiration { get; set; }
}
