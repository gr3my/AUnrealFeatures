using AUnrealFeatures.Hosting.Http;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AUnrealFeatures.Virtex;

public interface IVirtexServer { }

public sealed partial class VirtexServer : AstraHttpServer, IVirtexServer
{
    const string HOSTNAME = "localhost";
    const ushort PORT = 9092;

    // Wire ports for the raw FVirtexConnection socket protocol (length-prefixed
    // {"Type":int,"Payload":{...}} JSON), matching discovery.json's
    // CommunicationsServicePort / LiveDataDistributorPort (DataStreamPort shares 8997).
    const ushort COMMUNICATIONS_PORT = 43614;
    const ushort LIVE_DATA_DISTRIBUTOR_PORT = 8997;

    static readonly JsonElement DiscoveryPayload;
    static readonly JsonDocument _discoveryDoc;

    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    static VirtexServer()
    {
        var asm = Assembly.GetExecutingAssembly();
        _discoveryDoc = JsonDocument.Parse(ReadResource(asm, "discovery.json"));
        DiscoveryPayload = _discoveryDoc.RootElement;
    }

    static string ReadResource(Assembly asm, string filename)
    {
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(filename, StringComparison.OrdinalIgnoreCase));
        if (name == null) return "{}";
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    readonly VirtexSocketServer _communications;

    public VirtexServer() : base(HOSTNAME, PORT)
    {
        _communications = new VirtexSocketServer("Communications", COMMUNICATIONS_PORT, Logger);

        // Log messages that are not handled by a higher-level protocol.
        _communications.OnMessage += (conn, type, payload) =>
            Logger.Information("[Communications] Unhandled message Type={Type} Payload={Payload} from {Remote}",
                type, payload.ValueKind == JsonValueKind.Undefined ? "(none)" : payload.GetRawText(), conn.RemoteEndPoint);

        // LiveDataCommunications: the per-game live pipeline. The old mock
        // FlatBuffers MatchChunk streamer on this port is replaced by the VTX
        // router — an injected native reader streams a raw VTX byte stream into
        // the ingest port tagged with its source gameId; the router routes it by
        // the game->stadium table and fans it out (raw, no re-encode) to Unity
        // stadium clients subscribed on the distribute port (still 8997, the
        // discovery LiveDataDistributorPort). Frame decoding happens only on the
        // consumer (Unity VTX.LiveData), never here.
        //_vtxRouter = new VtxLiveRouter(
           // registry: VtxGameRegistry.CreateDefault(),
         //   distributePort: LIVE_DATA_DISTRIBUTOR_PORT,
          //  log: msg => Logger.Information("[LiveDataComms] {Message}", msg));

        _communications.Start();
      //  _vtxRouter.Start();
    }

    
    private sealed class Envelope<T>
    {
        [JsonPropertyName("result")]
        public T? Result { get; set; }
    }

    private static IHttpActionResult VirtexOk<T>(T result) =>
        Results.Configurable(HttpStatusCode.OK, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new Envelope<T> { Result = result }, JsonOpts));

    private static T? ParseBody<T>(IHttpRequest request) where T : class
    {
        try
        {
            if (request.Body == null || request.Body.Length == 0) return null;
            return JsonSerializer.Deserialize<T>(request.Body, JsonOpts);
        }
        catch { return null; }
    }

    [HttpGet("/")]
    public async Task<IHttpActionResult> Discovery(IHttpRequest request, IHttpResponse response)
    {
        Logger.Information("Discovery requested");
        return Results.Ok(DiscoveryPayload);
    }

    [HttpGet("/@")]
    public async Task<IHttpActionResult> DiscoveryAt(IHttpRequest request, IHttpResponse response)
    {
        Logger.Information("Discovery (@) requested");
        return Results.Ok(DiscoveryPayload);
    }
}
