using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Database;
using AUnrealFeatures.Playfab.Models;
using PlayFab.EventsModels;
using System.Net;
using System.Text.Json;

namespace AUnrealFeatures.Playfab;

public sealed class PlayfabTelemetryKeyDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string KeyValue { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public long CreateTime { get; set; }
    public long LastUpdateTime { get; set; }
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
}

public sealed class PlayfabDataConnectionDbObject : AstraDbObject
{
    public string TitleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";  // AzureBlobStorage, AzureDataExplorer, FabricKQL
    public bool IsActive { get; set; } = true;
    public string SettingsJson { get; set; } = "";
    public string Error { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public long? MostRecentErrorTime { get; set; }
    public string ErrorState { get; set; } = "OK";
    public long Created { get; set; }
}

public sealed partial class PlayfabServer
{
    /* ─── Helpers ─────────────────────────────────────────────────────────── */

    private static TelemetryKeyDetails KeyToDetails(PlayfabTelemetryKeyDbObject k) => new()
    {
        Name           = k.Name,
        KeyValue       = k.KeyValue,
        IsActive       = k.IsActive,
        CreateTime     = DateTimeOffset.FromUnixTimeSeconds(k.CreateTime).UtcDateTime,
        LastUpdateTime = DateTimeOffset.FromUnixTimeSeconds(k.LastUpdateTime).UtcDateTime,
    };

    private static DataConnectionDetails ConnToDetails(PlayfabDataConnectionDbObject c)
    {
        DataConnectionSettings settings = new();
        try
        {
            if (!string.IsNullOrEmpty(c.SettingsJson))
                settings = JsonSerializer.Deserialize<DataConnectionSettings>(c.SettingsJson, JsonOpts) ?? new();
        }
        catch { settings = new(); }
        var type = Enum.TryParse<DataConnectionType>(c.Type, out var t) ? t : DataConnectionType.AzureBlobStorage;
        return new DataConnectionDetails
        {
            Name               = c.Name,
            Type               = type,
            IsActive           = c.IsActive,
            ConnectionSettings = settings,
            Status = new DataConnectionStatusDetails
            {
                Error               = c.Error,
                ErrorMessage        = c.ErrorMessage,
                State               = c.ErrorState == "Error" ? DataConnectionErrorState.Error : DataConnectionErrorState.OK,
                MostRecentErrorTime = c.MostRecentErrorTime.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(c.MostRecentErrorTime.Value).UtcDateTime
                    : null,
            }
        };
    }

    /* ─── Telemetry Keys ──────────────────────────────────────────────────── */

    /// <summary>Creates a new telemetry key.</summary>
    [HttpPost("/Event/CreateTelemetryKey")]
    public Task<IHttpActionResult> Events_CreateTelemetryKey(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<CreateTelemetryKeyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.KeyName))
            return Task.FromResult(Err("InvalidParams", 1000, "KeyName is required"));

        var coll = Database.GetCollection<PlayfabTelemetryKeyDbObject>(true)!;
        var name = body.KeyName;
        var existing = coll.FindOne(k => k.TitleId == TITLE_ID && k.Name == name);
        if (existing != null)
            return Task.FromResult(Err("TelemetryKeyAlreadyExists", 1632, "Telemetry key already exists"));

        var k = new PlayfabTelemetryKeyDbObject
        {
            TitleId        = TITLE_ID,
            Name           = body.KeyName,
            KeyValue       = NewId(),
            IsActive       = true,
            CreateTime     = NowUnix(),
            LastUpdateTime = NowUnix(),
            EntityId       = body.Entity?.Id   ?? "",
            EntityType     = body.Entity?.Type ?? "",
        };
        coll.Insert(k);
        return Task.FromResult(Ok(new CreateTelemetryKeyResponse { NewKeyDetails = KeyToDetails(k) }));
    }

    /// <summary>Deletes an existing telemetry key.</summary>
    [HttpPost("/Event/DeleteTelemetryKey")]
    public Task<IHttpActionResult> Events_DeleteTelemetryKey(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteTelemetryKeyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.KeyName))
            return Task.FromResult(Err("InvalidParams", 1000, "KeyName is required"));

        var coll = Database.GetCollection<PlayfabTelemetryKeyDbObject>(true)!;
        var name = body.KeyName;
        var k = coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (k == null) return Task.FromResult(Ok(new DeleteTelemetryKeyResponse { WasKeyDeleted = false }));
        coll.Delete(k.Id);
        return Task.FromResult(Ok(new DeleteTelemetryKeyResponse { WasKeyDeleted = true }));
    }

    /// <summary>Returns a single telemetry key by name.</summary>
    [HttpPost("/Event/GetTelemetryKey")]
    public Task<IHttpActionResult> Events_GetTelemetryKey(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetTelemetryKeyRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.KeyName))
            return Task.FromResult(Err("InvalidParams", 1000, "KeyName is required"));

        var name = body.KeyName;
        var k = Database.GetCollection<PlayfabTelemetryKeyDbObject>(true)!
                        .FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (k == null) return Task.FromResult(Err("TelemetryKeyNotFound", 1631, "Telemetry key not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new GetTelemetryKeyResponse { KeyDetails = KeyToDetails(k) }));
    }

    /// <summary>Returns all telemetry keys for the title.</summary>
    [HttpPost("/Event/ListTelemetryKeys")]
    public Task<IHttpActionResult> Events_ListTelemetryKeys(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var keys = Database.GetCollection<PlayfabTelemetryKeyDbObject>(true)!
                           .Find(x => x.TitleId == TITLE_ID).Select(KeyToDetails).ToList();
        return Task.FromResult(Ok(new ListTelemetryKeysResponse { KeyDetails = keys }));
    }

    /// <summary>Activates or deactivates a telemetry key.</summary>
    [HttpPost("/Event/SetTelemetryKeyActive")]
    public Task<IHttpActionResult> Events_SetTelemetryKeyActive(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SetTelemetryKeyActiveRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.KeyName))
            return Task.FromResult(Err("InvalidParams", 1000, "KeyName is required"));

        var coll = Database.GetCollection<PlayfabTelemetryKeyDbObject>(true)!;
        var name = body.KeyName;
        var k = coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (k == null) return Task.FromResult(Err("TelemetryKeyNotFound", 1631, "Telemetry key not found", HttpStatusCode.NotFound));

        var changed = k.IsActive != body.Active;
        k.IsActive       = body.Active;
        k.LastUpdateTime = NowUnix();
        coll.Update(k);
        return Task.FromResult(Ok(new SetTelemetryKeyActiveResponse
        {
            KeyDetails    = KeyToDetails(k),
            WasKeyUpdated = changed,
        }));
    }

    /* ─── Data Connections ────────────────────────────────────────────────── */

    /// <summary>Deletes a data connection.</summary>
    [HttpPost("/Event/DeleteDataConnection")]
    public Task<IHttpActionResult> Events_DeleteDataConnection(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<DeleteDataConnectionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var coll = Database.GetCollection<PlayfabDataConnectionDbObject>(true)!;
        var name = body.Name;
        var c = coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (c == null) return Task.FromResult(Ok(new DeleteDataConnectionResponse { WasDeleted = false }));
        coll.Delete(c.Id);
        return Task.FromResult(Ok(new DeleteDataConnectionResponse { WasDeleted = true }));
    }

    /// <summary>Returns a data connection by name.</summary>
    [HttpPost("/Event/GetDataConnection")]
    public Task<IHttpActionResult> Events_GetDataConnection(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<GetDataConnectionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var name = body.Name;
        var c = Database.GetCollection<PlayfabDataConnectionDbObject>(true)!
                        .FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (c == null) return Task.FromResult(Err("DataConnectionNotFound", 1657, "Data connection not found", HttpStatusCode.NotFound));
        return Task.FromResult(Ok(new GetDataConnectionResponse { DataConnection = ConnToDetails(c) }));
    }

    /// <summary>Returns all data connections for the title.</summary>
    [HttpPost("/Event/ListDataConnections")]
    public Task<IHttpActionResult> Events_ListDataConnections(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var conns = Database.GetCollection<PlayfabDataConnectionDbObject>(true)!
                            .Find(x => x.TitleId == TITLE_ID).Select(ConnToDetails).ToList();
        return Task.FromResult(Ok(new ListDataConnectionsResponse { DataConnections = conns }));
    }

    /// <summary>Creates or updates a data connection.</summary>
    [HttpPost("/Event/SetDataConnection")]
    public Task<IHttpActionResult> Events_SetDataConnection(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SetDataConnectionRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var coll = Database.GetCollection<PlayfabDataConnectionDbObject>(true)!;
        var name = body.Name;
        var c = coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        var settingsJson = body.ConnectionSettings != null
            ? JsonSerializer.Serialize(body.ConnectionSettings, JsonOpts) : "";

        if (c == null)
        {
            c = new PlayfabDataConnectionDbObject
            {
                TitleId      = TITLE_ID,
                Name         = body.Name,
                Type         = body.Type.ToString(),
                IsActive     = body.IsActive,
                SettingsJson = settingsJson,
                ErrorState   = "OK",
                Created      = NowUnix(),
            };
            coll.Insert(c);
        }
        else
        {
            c.Type         = body.Type.ToString();
            c.IsActive     = body.IsActive;
            c.SettingsJson = settingsJson;
            coll.Update(c);
        }
        return Task.FromResult(Ok(new SetDataConnectionResponse { DataConnection = ConnToDetails(c) }));
    }

    /// <summary>Activates or deactivates a data connection.</summary>
    [HttpPost("/Event/SetDataConnectionActive")]
    public Task<IHttpActionResult> Events_SetDataConnectionActive(IHttpRequest request, IHttpResponse response)
    {
        if (!CheckAuth(request, AuthType.EntityToken)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<SetDataConnectionActiveRequest>(request);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return Task.FromResult(Err("InvalidParams", 1000, "Name is required"));

        var coll = Database.GetCollection<PlayfabDataConnectionDbObject>(true)!;
        var name = body.Name;
        var c = coll.FindOne(x => x.TitleId == TITLE_ID && x.Name == name);
        if (c == null) return Task.FromResult(Err("DataConnectionNotFound", 1657, "Data connection not found", HttpStatusCode.NotFound));

        var changed = c.IsActive != body.Active;
        c.IsActive = body.Active;
        coll.Update(c);
        return Task.FromResult(Ok(new SetDataConnectionActiveResponse
        {
            DataConnection = ConnToDetails(c),
            WasUpdated     = changed,
        }));
    }

    /* ─── Event ingestion ─────────────────────────────────────────────────── */

    private Task<IHttpActionResult> WriteEventsCommon(IHttpRequest request, AuthType authType, string defaultNamespace)
    {
        if (!CheckAuth(request, authType)) return Task.FromResult(Unauthorized());
        var body = ParseRequest<WriteEventsRequest>(request);
        if (body == null || body.Events == null || body.Events.Count == 0)
            return Task.FromResult(Ok(new WriteEventsResponse { AssignedEventIds = new List<string>() }));

        var coll = Database.GetCollection<PlayfabEventDbObject>(true)!;
        var assigned = new List<string>(body.Events.Count);
        foreach (var e in body.Events)
        {
            var id = NewId();
            assigned.Add(id);
            string payloadJson = "";
            try
            {
                if (!string.IsNullOrEmpty(e.PayloadJSON)) payloadJson = e.PayloadJSON;
                else if (e.Payload != null) payloadJson = JsonSerializer.Serialize(e.Payload, JsonOpts);
            }
            catch { payloadJson = ""; }
            coll.Insert(new PlayfabEventDbObject
            {
                EventId        = id,
                EventName      = e.Name ?? "",
                EventNamespace = string.IsNullOrEmpty(e.EventNamespace) ? defaultNamespace : e.EventNamespace,
                EntityId       = e.Entity?.Id   ?? "",
                EntityType     = e.Entity?.Type ?? "",
                PayloadJson    = payloadJson,
                OriginalId     = e.OriginalId ?? "",
                Timestamp      = e.OriginalTimestamp.HasValue
                                    ? new DateTimeOffset(e.OriginalTimestamp.Value, TimeSpan.Zero).ToUnixTimeMilliseconds()
                                    : NowUnixMs(),
            });
        }
        return Task.FromResult(Ok(new WriteEventsResponse { AssignedEventIds = assigned }));
    }

    /// <summary>Writes one or more events to the title's event stream.</summary>
    [HttpPost("/Event/WriteEvents")]
    public Task<IHttpActionResult> Events_WriteEvents(IHttpRequest request, IHttpResponse response) =>
        WriteEventsCommon(request, AuthType.EntityToken, "title.events");

    /// <summary>Writes telemetry events using either an entity token or a telemetry key.</summary>
    [HttpPost("/Event/WriteTelemetryEvents")]
    public Task<IHttpActionResult> Events_WriteTelemetryEvents(IHttpRequest request, IHttpResponse response)
    {
        // Accept either entity-token or telemetry-key auth
        var hasEntity = !string.IsNullOrEmpty(Header(request, "X-EntityToken"));
        var hasKey    = !string.IsNullOrEmpty(Header(request, "X-TelemetryKey"));
        if (!hasEntity && !hasKey) return Task.FromResult(Unauthorized());

        if (hasKey)
        {
            var keyHeader = Header(request, "X-TelemetryKey")!;
            var coll = Database.GetCollection<PlayfabTelemetryKeyDbObject>(true)!;
            var k = coll.FindOne(x => x.KeyValue == keyHeader && x.IsActive);
            if (k == null) return Task.FromResult(Err("InvalidTelemetryKey", 1633, "Telemetry key invalid or inactive", HttpStatusCode.Unauthorized));
        }
        return WriteEventsCommon(request, hasEntity ? AuthType.EntityToken : AuthType.TelemetryKey, "title.telemetry");
    }
}
