// PlayFab dashboard API client. Requests are proxied through `/pf/*` to the
// configured PlayFab-compatible backend. Admin requests use X-SecretKey.

// Admin credentials are injected by the server-side /pf proxy. They are never bundled into browser JavaScript.

const SESSION_TICKET = () =>
  (typeof window !== "undefined" && window.localStorage.getItem("pf-session-ticket")) || "";

const ENTITY_TOKEN = () =>
  (typeof window !== "undefined" && window.localStorage.getItem("pf-entity-token")) || "";

type Auth = "admin" | "session" | "entity" | "none";

async function call<T = any>(
  path: string,
  body: any = null,
  auth: Auth = "admin",
  method: "POST" | "GET" = "POST",
): Promise<T> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    // ngrok free tier serves an interstitial unless this header is present
    "ngrok-skip-browser-warning": "true",
  };
  if (auth === "admin")   headers["X-Dashboard-Auth"] = "admin";
  if (auth === "session") headers["X-Authorization"] = SESSION_TICKET();
  if (auth === "entity")  headers["X-EntityToken"]   = ENTITY_TOKEN();

  const res = await fetch(`/pf${path}`, {
    method,
    headers,
    body: method === "GET" || body == null ? undefined : JSON.stringify(body ?? {}),
  });
  const text = await res.text();
  let parsed: any = null;
  try { parsed = JSON.parse(text); } catch { parsed = text; }
  if (!res.ok) throw new ApiError(parsed?.errorMessage ?? parsed?.Message ?? res.statusText, res.status, parsed);
  // Most PlayFab responses wrap real payload in { code, status, data }
  return (parsed?.data ?? parsed) as T;
}

export class ApiError extends Error {
  constructor(public msg: string, public status: number, public body: any) {
    super(`${status} ${msg}`);
  }
}

/* ─── Title settings ───────────────────────────────────────────────────── */

export const getTitleSettings = () => call("/Admin/GetTitleSettings");

export const getTitleData = () => call("/Admin/GetTitleData");

export const setTitleData = (kv: Record<string, string>) =>
  call("/Admin/SetTitleData", { KeyValues: kv });

export const setTitleInternalData = (kv: Record<string, string>) =>
  call("/Admin/SetTitleInternalData", { KeyValues: kv });

/* ─── Players ──────────────────────────────────────────────────────────── */

export interface PlayerRow {
  PlayFabId: string;
  EntityId: string;
  Username: string;
  DisplayName: string;
  Email: string;
  CustomId: string;
  OriginPlatform: string;
  IsBanned: boolean;
  CreatedUnix: number;
  LastLoginUnix: number;
  Tags: string[];
}

export const listPlayers = (skip = 0, take = 50, query = "") =>
  call<{ Total: number; Players: PlayerRow[]; Skip: number; Take: number }>(
    "/Admin/ListPlayers", { Skip: skip, Take: take, Query: query }
  );

export const getPlayer = (playFabId: string) =>
  call("/Admin/GetPlayer", { PlayFabId: playFabId });

/* ─── Generic collection lister (used by stub-replacement pages) ───────── */

export const listCollection = <T = any>(name: string, skip = 0, take = 100, query = "") =>
  call<{ Total: number; Rows: T[]; Skip: number; Take: number; Name: string }>(
    "/Admin/ListCollection", { Name: name, Skip: skip, Take: take, Query: query }
  );

export const upsertCollection = (name: string, row: any, keyField = "") =>
  call<{ Inserted: number; Updated: number; Name: string }>(
    "/Admin/UpsertCollection", { Name: name, Row: row, KeyField: keyField }
  );

export const deleteCollectionRow = (name: string, keyField: string, keyValue: string) =>
  call<{ Deleted: number; Name: string }>(
    "/Admin/DeleteCollectionRow", { Name: name, KeyField: keyField, KeyValue: keyValue }
  );

export const getPlayerExtras = (playFabId: string) =>
  call<{
    PlayFabId: string; EntityId: string;
    PlayStream:    Array<{ EventId: string; EventName: string; EventNamespace: string; PayloadJson: string; Timestamp: number; EntityId: string; EntityType: string }>;
    Logins:        Array<{ EventName: string; Timestamp: number; PayloadJson: string }>;
    CloudScript:   Array<{ ExecutionId: string; FunctionName: string; ResultJson: string; LogsJson: string; ExecutionTimeSeconds: number; Timestamp: number; Error: boolean }>;
    Characters:    Array<{ CharacterId: string; CharacterName: string; CharacterType: string; Created: number }>;
    InventoryV2:   Array<{ CollectionId: string; ItemId: string; StackId: string; Amount: number }>;
    TransactionsV2: Array<{ TransactionId: string; Operation: string; ItemId: string; Amount: number; Timestamp: number; Details: string }>;
    Objects:       Array<{ ObjectName: string; DataObjectJson: string; EscapedDataObject: string }>;
    Files:         Array<{ FileName: string; Size: number; LastModified: number; Etag: string; ProfileVersion: string }>;
    Groups:        Array<{ GroupId: string; RoleId: string; Joined: number }>;
    Segments:      Array<{ Namespace: string; Tag: string }>;
    PushTokens:    Array<{ Platform: string; DeviceToken: string }>;
    StatisticsV2:  Array<{ StatisticName: string; Scores: string[]; Version: number; LastUpdated: number; Metadata: string }>;
    Bans:          Array<{ Active: boolean; Reason: string; Created: number }>;
  }>("/Admin/GetPlayerExtras", { PlayFabId: playFabId });

export const banUser   = (playFabId: string, reason = "") =>
  call("/Admin/BanUser",   { PlayFabId: playFabId, Reason: reason });

export const unbanUser = (playFabId: string) =>
  call("/Admin/UnbanUser", { PlayFabId: playFabId });

export interface PlayerPatch {
  PlayFabId: string;
  DisplayName?: string;
  Username?: string;
  Email?: string;
  ContactEmail?: string;
  CustomId?: string;
  AvatarUrl?: string;
  OriginPlatform?: string;
  IsBanned?: boolean;
  Tags?: string[];
}

export const updatePlayer = (patch: PlayerPatch) =>
  call("/Admin/UpdatePlayer", patch);

export const deletePlayer = (playFabId: string) =>
  call("/Admin/DeletePlayer", { PlayFabId: playFabId });

/* ─── Player inventory & currency ──────────────────────────────────────── */

export const grantItemsToUser = (
  playFabId: string,
  itemIds: string[],
  catalogVersion = "",
  annotation = "",
) =>
  call<{ Granted: { ItemId: string; ItemInstanceId: string; DisplayName: string; CatalogVersion: string }[]; Skipped: string[] }>(
    "/Admin/GrantItemsToUser",
    { PlayFabId: playFabId, ItemIds: itemIds, CatalogVersion: catalogVersion, Annotation: annotation }
  );

export const revokeInventoryItem = (playFabId: string, itemInstanceId: string) =>
  call<{ Revoked: number }>("/Admin/RevokeInventoryItem", { PlayFabId: playFabId, ItemInstanceId: itemInstanceId });

export const addUserVirtualCurrency = (playFabId: string, virtualCurrency: string, amount: number) =>
  call<{ Balance: number; BalanceChange: number }>("/Admin/AddUserVirtualCurrency",
    { PlayFabId: playFabId, VirtualCurrency: virtualCurrency, Amount: amount });

export const subtractUserVirtualCurrency = (playFabId: string, virtualCurrency: string, amount: number) =>
  call<{ Balance: number; BalanceChange: number }>("/Admin/SubtractUserVirtualCurrency",
    { PlayFabId: playFabId, VirtualCurrency: virtualCurrency, Amount: amount });

export const setUserVirtualCurrency = (playFabId: string, virtualCurrency: string, amount: number) =>
  call<{ Balance: number }>("/Admin/SetUserVirtualCurrency",
    { PlayFabId: playFabId, VirtualCurrency: virtualCurrency, Amount: amount });

/* ─── Catalog (v1) ─────────────────────────────────────────────────────── */

export interface CatalogItem {
  ItemId: string;
  ItemClass: string;
  DisplayName: string;
  Description: string;
  VirtualCurrencyPrices?: Record<string, number>;
  RealCurrencyPrices?: Record<string, number>;
  Tags?: string[];
  CustomData?: string;
  IsLimitedEdition?: boolean;
  InitialLimitedEditionCount?: number;
  IsStackable?: boolean;
  IsTradable?: boolean;
  ItemImageUrl?: string;
}

// Empty string = return every catalog item across every version (the dashboard
// shows a version filter UI on top, so it always wants the full set first).
export const getCatalogItems = (catalogVersion = "") =>
  call<{ Catalog: any[]; Versions?: Record<string, number>; Count?: number }>(
    "/Admin/GetCatalogItems",
    catalogVersion ? { CatalogVersion: catalogVersion } : {}
  );

export const addCatalogItems = (items: CatalogItem[], catalogVersion = "main") =>
  call("/Admin/AddCatalogItems", { CatalogVersion: catalogVersion, Catalog: items });

export const deleteCatalogItem = (itemId: string) =>
  call("/Admin/DeleteCatalogItem", { ItemId: itemId });

/* ─── Economy v2 ───────────────────────────────────────────────────────── */

export const listEconomyItems = (skip = 0, take = 50, query = "") =>
  call<{ Total: number; Items: any[]; Skip: number; Take: number }>(
    "/Admin/ListEconomyItems", { Skip: skip, Take: take, Query: query }
  );

/* ─── Virtual currencies ───────────────────────────────────────────────── */

export interface VirtualCurrency {
  CurrencyCode: string;
  DisplayName: string;
  InitialDeposit: number;
  RechargeRate: number;
  RechargeMax: number;
}

export const listVirtualCurrencies = () =>
  call<{ VirtualCurrencies: VirtualCurrency[] }>("/Admin/ListVirtualCurrencyTypes");

export const addVirtualCurrencies = (vcs: VirtualCurrency[]) =>
  call("/Admin/AddVirtualCurrencyTypes", { VirtualCurrencies: vcs });

/* ─── Statistics ───────────────────────────────────────────────────────── */

export const listStatDefinitions = () =>
  call<{ Statistics: { StatisticName: string; AggregationMethod: string; VersionChangeInterval: string; CurrentVersion: number }[] }>(
    "/Admin/GetPlayerStatisticDefinitions"
  );

export const createStatDefinition = (name: string, aggregation = "Last", reset = "Never") =>
  call("/Admin/CreatePlayerStatisticDefinition", {
    StatisticName: name,
    AggregationMethod: aggregation,
    VersionChangeInterval: reset,
  });

/* ─── Leaderboards ─────────────────────────────────────────────────────── */

export const listLeaderboards = () =>
  call<{ Leaderboards: any[] }>("/Admin/ListLeaderboards");

/* ─── CloudScript ──────────────────────────────────────────────────────── */

export interface CloudScriptFile { Filename: string; FileContents: string }

export const getCloudScriptRevision = (revision?: number) =>
  call<{ Files: CloudScriptFile[]; Version: number; IsPublished: boolean; CreatedAt: string | null }>(
    "/Admin/GetCloudScriptRevision", revision != null ? { Revision: revision } : {}
  );

export const getCloudScriptVersions = () =>
  call<{ Versions: { Version: number; Revision: number; PublishedRevision: number }[] }>("/Admin/GetCloudScriptVersions");

export const updateCloudScript = (files: CloudScriptFile[], publish = true) =>
  call("/Admin/UpdateCloudScript", { Files: files, Publish: publish });

export const setPublishedRevision = (revision: number) =>
  call("/Admin/SetPublishedRevision", { Revision: revision });

export const getCloudScriptLogs = (skip = 0, take = 50, query = "") =>
  call<{ Total: number; Logs: any[] }>("/Admin/GetCloudScriptLogs", { Skip: skip, Take: take, Query: query });

export const getCloudScriptLog = (executionId: string) =>
  call<any>("/Admin/GetCloudScriptLog", { ExecutionId: executionId });

/* ─── Photon ───────────────────────────────────────────────────────────── */

export const getPhotonWebhookLogs = (skip = 0, take = 50, query = "") =>
  call<{ Total: number; Logs: any[] }>("/Admin/GetPhotonWebhookLogs", { Skip: skip, Take: take, Query: query });

export const getPhotonRoomStates = (skip = 0, take = 50, query = "") =>
  call<{ Total: number; Rooms: any[] }>("/Admin/GetPhotonRoomStates", { Skip: skip, Take: take, Query: query });

export const getPhotonTickets = () =>
  call<{ Tickets: any[] }>("/Admin/GetPhotonTickets");

/* ─── Multiplayer ──────────────────────────────────────────────────────── */

export const getMultiplayerOverview = () =>
  call<any>("/Admin/GetMultiplayerOverview");

/* ─── Groups ───────────────────────────────────────────────────────────── */

export const listGroups = (skip = 0, take = 50, query = "") =>
  call<{ Total: number; Groups: any[] }>("/Admin/ListGroups", { Skip: skip, Take: take, Query: query });

/* ─── Shared groups (cosmetic / data sync) ─────────────────────────────── */

export interface SharedGroupRow {
  SharedGroupId: string;
  MemberCount:   number;
  KeyCount:      number;
  CreatedUnix:   number;
}

export const listSharedGroups = (skip = 0, take = 50, query = "") =>
  call<{ Total: number; Groups: SharedGroupRow[] }>(
    "/Admin/ListSharedGroups", { Skip: skip, Take: take, Query: query }
  );

export const getSharedGroup = (sharedGroupId: string) =>
  call<{
    SharedGroupId: string;
    CreatedUnix:   number;
    Members:       string[];
    Data:          Record<string, { Value: string; Permission: string; LastUpdatedBy: string; LastUpdated: number }>;
  }>("/Admin/GetSharedGroup", { SharedGroupId: sharedGroupId });

export const setSharedGroupKey = (sharedGroupId: string, key: string, value: string, permission = "Private") =>
  call("/Admin/SetSharedGroupKey", { SharedGroupId: sharedGroupId, Key: key, Value: value, Permission: permission });

export const deleteSharedGroupKey = (sharedGroupId: string, key: string) =>
  call<{ Removed: boolean }>("/Admin/DeleteSharedGroupKey", { SharedGroupId: sharedGroupId, Key: key });

export const deleteSharedGroup = (sharedGroupId: string) =>
  call<{ Deleted: number }>("/Admin/DeleteSharedGroup", { SharedGroupId: sharedGroupId });

/* ─── News ─────────────────────────────────────────────────────────────── */

export const getNews = () =>
  call<{ News: any[] }>("/Admin/GetTitleNews");

export const addNews = (title: string, body: string) =>
  call("/Admin/AddNews", { Title: title, Body: body });

export const deleteNews = (newsId: string) =>
  call("/Admin/DeleteNews", { NewsId: newsId });

/* ─── Events ───────────────────────────────────────────────────────────── */

export const getEvents = (skip = 0, take = 50, query = "") =>
  call<{ Total: number; Events: any[] }>("/Admin/GetEvents", { Skip: skip, Take: take, Query: query });

/* ─── Reset ────────────────────────────────────────────────────────────── */

export const resetTitle = async (titleId: string) => {
  const res = await fetch(`/pf/Admin/ResetTitle`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Dashboard-Auth": "admin",
      "X-Confirm-Title-Reset": titleId,
      "ngrok-skip-browser-warning": "true",
    },
    body: "{}",
  });
  return res.ok;
};
