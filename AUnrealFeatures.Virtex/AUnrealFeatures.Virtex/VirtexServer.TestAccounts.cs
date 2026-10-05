using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using System.Text.Json.Serialization;

namespace AUnrealFeatures.Virtex;

/* ─── PersistentTestAccountManagerURL ────────────────────────────────────────
 * UAccountManagerRequestTask::Activate POSTs {"Num": N} to
 * "{PersistentTestAccountManagerURL}/{Endpoint}" and on failure calls
 * FOnAccountRequestMade_DelegateWrapper(success=false, empty
 * TArray<FEntityTestData>, errorText) — confirming the success path returns
 * an array of FEntityTestData. The exact field layout of that struct wasn't
 * pulled from the Dumper-7 SDK dump, so this is a best-effort shape
 * (id/username/password) rather than a confirmed contract.
 *
 * NOTE: discovery.json's PersistentTestAccountManagerURL is currently just
 * "127.0.0.1" with no scheme/port, which the client would concatenate as
 * "127.0.0.1/{endpoint}" — not a valid absolute URL for its HTTP request. It
 * needs to be something like "http://localhost:9092/test-accounts" for this
 * endpoint to actually be reachable. */
public sealed partial class VirtexServer
{
    private sealed class BulkAccountRequest
    {
        [JsonPropertyName("Num")] public int Num { get; set; }
    }

    private sealed class EntityTestData
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = "";
        [JsonPropertyName("Username")] public string Username { get; set; } = "";
        [JsonPropertyName("Password")] public string Password { get; set; } = "";
    }

    [HttpPost("/test-accounts/{endpoint}")]
    public async Task<IHttpActionResult> TestAccounts(IHttpRequest request, IHttpResponse response, string endpoint)
    {
        var body = ParseBody<BulkAccountRequest>(request);
        var num = Math.Clamp(body?.Num ?? 0, 0, 1000);
        Logger.Information("Test account bulk request '{Endpoint}' for {Num} accounts", endpoint, num);

        var accounts = new List<EntityTestData>(num);
        for (int i = 0; i < num; i++)
        {
            var id = Guid.NewGuid().ToString("N");
            accounts.Add(new EntityTestData
            {
                Id = id,
                Username = $"test_{id[..8]}",
                Password = Guid.NewGuid().ToString("N"),
            });
        }

        return VirtexOk(accounts);
    }
}
