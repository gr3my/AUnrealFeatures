using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Playfab.Models;
using PlayFab.LocalizationModels;

namespace AUnrealFeatures.Playfab;

public sealed partial class PlayfabServer
{
    /// <summary>Returns the list of supported language codes (BCP-47).</summary>
    [HttpPost("/Locale/GetLanguageList")]
    public Task<IHttpActionResult> Localization_GetLanguageList(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());

        var coll = Database.GetCollection<PlayfabLocaleDbObject>(true)!;
        var stored = coll.FindAll().Select(l => l.LanguageCode).Where(c => !string.IsNullOrEmpty(c)).ToList();
        if (stored.Count == 0)
            stored = new List<string>
            {
                "ar-SA","cs-CZ","da-DK","de-DE","el-GR","en-GB","en-US","es-ES","es-MX","fi-FI",
                "fr-FR","he-IL","hu-HU","id-ID","it-IT","ja-JP","ko-KR","nb-NO","nl-NL","pl-PL",
                "pt-BR","pt-PT","ro-RO","ru-RU","sv-SE","th-TH","tr-TR","uk-UA","vi-VN","zh-CN","zh-TW"
            };
        return Task.FromResult(Ok(new GetLanguageListResponse { LanguageList = stored }));
    }
}
