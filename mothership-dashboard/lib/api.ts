const BASE = "/ms";

type Auth = "automation" | "server" | "none";

function authHeaders(auth: Auth): Record<string, string> {
  const headers: Record<string, string> = { "Content-Type": "application/json" };

  if (auth !== "none") {
    headers["X-Dashboard-Auth"] = auth;
  }

  return headers;
}

async function get<T>(path: string, auth: Auth = "automation"): Promise<T> {
  const headers = authHeaders(auth);
  const res = await fetch(`${BASE}${path}`, { headers, cache: "no-store" });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.json();
}

async function post<T>(path: string, body?: unknown, auth: Auth = "automation"): Promise<T> {
  const headers = authHeaders(auth);
  const res = await fetch(`${BASE}${path}`, {
    method: "POST",
    headers,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.json();
}

async function del<T>(path: string, auth: Auth = "automation"): Promise<T> {
  const headers = authHeaders(auth);
  const res = await fetch(`${BASE}${path}`, { method: "DELETE", headers });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.json();
}

async function put<T>(path: string, body?: unknown, auth: Auth = "automation"): Promise<T> {
  const headers = authHeaders(auth);
  const res = await fetch(`${BASE}${path}`, {
    method: "PUT",
    headers,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.json();
}

// ── Health ────────────────────────────────────────────────────────────────────
export const health = () => get<{ status: string; timestamp: string }>("/health", "none");
export const debugStorage = () => get<Record<string, number>>("/debug/storage");

// ── Players ───────────────────────────────────────────────────────────────────
export const getPlayers = () => get<{ Results: Player[] }>("/v1/automation/player/auth/players");
export const updatePlayerTags = (player_id: string, tags: string[]) =>
  post("/v1/automation/player/auth/players/tags", { player_id, tags });

// ── Titles ────────────────────────────────────────────────────────────────────
export const getTitles = () => get<{ Results: Title[] }>("/v1/title/automation");
export const createTitle = (title_id: string, title_name: string) =>
  post<Title>("/v1/title/automation", { title_id, title_name });

// ── Environments ──────────────────────────────────────────────────────────────
export const getEnvironments = () => get<{ Results: Environment[] }>("/v1/env/automation");
export const createEnvironment = (envId: string, titleId: string, envName: string) =>
  post<{ env: Environment }>("/v1/env/automation", { envId, titleId, envName });

// ── Deployments ───────────────────────────────────────────────────────────────
export const getDeployments = () => get<{ Results: Deployment[] }>("/v1/deployment/automation");
export const createDeployment = (body: { title_id: string; env_id: string; name: string }) =>
  post<{ deployment: Deployment }>("/v1/deployment/automation", body);

// ── Moderation ────────────────────────────────────────────────────────────────
export const getModeration = () => get<{ bans: Ban[]; mutes: Mute[]; reports: Report[] }>("/v1/moderation/automation");
export const banPlayer = (body: BanRequest) => post<{ Results: Ban[] }>("/v1/moderation/server/ban", body, "server");
export const deleteMute = (mute_id: string) => del(`/v1/moderation/automation?mute_id=${mute_id}`);
export const mutePlayer = (player_id: string, duration_minutes: number) =>
  post("/v1/moderation/automation", { action: "mute", player_id, duration_minutes });

// ── Title Data ────────────────────────────────────────────────────────────────
export const getTitleData = () => get<{ Results: TitleData[] }>("/v1/title-data/automation");
export const setTitleData = (key: string, value: string) => post<TitleData>("/v1/title-data/automation", { key, value });
export const deleteTitleData = (key: string) => del(`/v1/title-data/automation?key=${encodeURIComponent(key)}`);

// ── User Data ─────────────────────────────────────────────────────────────────
export const getUserData = (user_id: string) =>
  get<{ Results: UserData[] }>(`/v1/userdata?user_id=${encodeURIComponent(user_id)}`);
export const setUserData = (user_id: string, key_name: string, value: string) =>
  post<UserData>("/v1/userdata", { user_id, key_name, value });
export const deleteUserData = (user_id: string, key_name: string) =>
  del(`/v1/userdata?user_id=${encodeURIComponent(user_id)}&key_name=${encodeURIComponent(key_name)}`);

// ── Offers ────────────────────────────────────────────────────────────────────
export const getOffers = () => get<{ Results: Offer[] }>("/v1/offers/automation");
export const createOffer = (body: Partial<Offer>) => post<Offer>("/v1/offers/automation", body);
export const getOfferDisplays = () => get<{ Results: OfferDisplay[] }>("/v1/offerdisplays/automation");
export const createOfferDisplay = (name: string, description: string) =>
  post<OfferDisplay>("/v1/offerdisplays/automation", { name, description });
export const getOfferBindings = () => get<{ Results: OfferBinding[] }>("/v1/offerbindings/automation");

// ── Entitlements ──────────────────────────────────────────────────────────────
export const getEntitlements = () => get<{ Results: Entitlement[] }>("/v1/entitlement-catalog/automation");
export const createEntitlement = (name: string, type: string, item_class: string) =>
  post<Entitlement>("/v1/entitlement-catalog/automation", { name, type, item_class });

// ── Transactions ──────────────────────────────────────────────────────────────
export const getTransactions = () => get<{ Results: Transaction[] }>("/v1/transaction-catalog/automation");
export const createTransaction = (name: string) => post<Transaction>("/v1/transaction-catalog/automation", { name });

// ── Inventory ─────────────────────────────────────────────────────────────────
export const getInventory = (player_id: string) =>
  get<{ Results: Record<string, unknown> }>(`/v1/inventory?player_id=${encodeURIComponent(player_id)}`, "server");

// ── Progression ───────────────────────────────────────────────────────────────
export const getProgressionTracks = () => get<{ Results: ProgressionTrack[] }>("/v1/progression/automation");
export const createProgressionTrack = (name: string) =>
  post<ProgressionTrack>("/v1/progression/automation", { action: "create_track", name });
export const deleteProgressionTrack = (track_id: string) =>
  del(`/v1/progression/automation?track_id=${track_id}`);
export const getProgressionTrees = () => get<{ Results: ProgressionTree[] }>("/v1/progression-tree/automation");
export const createProgressionTree = (name: string) =>
  post<ProgressionTree>("/v1/progression-tree/automation", { action: "create_tree", name });
export const deleteProgressionTree = (tree_id: string) =>
  del(`/v1/progression-tree/automation?tree_id=${tree_id}`);

// ── API Keys ──────────────────────────────────────────────────────────────────
export const getApiKeys = () => get<{ Results: ApiKey[] }>("/v1/server/automation/api_key");
export const createApiKey = (name: string) => post<ApiKey>("/v1/server/automation/api_key", { name });

// ── Friends ───────────────────────────────────────────────────────────────────
export const getFriends = (PlayFabId: string) =>
  post<{ Result: { Friends: Friend[]; MyPrivacyState: number }; StatusCode: number }>("/api/GetFriendsV2", { PlayFabId }, "none");

// ── Analytics ─────────────────────────────────────────────────────────────────
export const getAnalyticsEvents = () =>
  get<AnalyticsEvent[]>("/storage/analytics_events", "none");

// ── Shared groups ─────────────────────────────────────────────────────────────
export const getSharedGroups = () => get<unknown[]>("/storage/shared_groups", "none");

// ── Storage raw ───────────────────────────────────────────────────────────────
export const getStorage = (key: string) => get<unknown>(`/storage/${key}`, "none");

// ── Types ─────────────────────────────────────────────────────────────────────
export interface Player {
  MothershipPlayerId: string;
  ExternalAccountNickname: string;
  ExternalAccountId: string;
  ExpirationTime: number;
  Tags: string[];
  Token: string;
}

export interface Title {
  title_id: string;
  title_name: string;
}

export interface Environment {
  envId: string;
  titleId: string;
  envName: string;
  requiredTags: string[];
}

export interface Deployment {
  deployment_id: string;
  title_id: string;
  env_id: string;
  name: string;
  requiredTags: string[];
}

export interface Ban {
  BanId: string;
  PlayerId: string;
  Category: number;
  Reason: string;
  DurationMinutes: number;
  OrgWide: boolean;
  CreatedAt: string;
  ExpiresAt: string;
  TitleId?: string;
  EnvId?: string;
}

export interface BanRequest {
  player_id: string;
  category: number;
  reason: string;
  duration_minutes: number;
  org_wide?: boolean;
}

export interface Mute {
  MuteId: string;
  PlayerId: string;
  DurationMinutes: number;
  CreatedAt: string;
}

export interface Report {
  ReportId: string;
  ReportedUserId: string;
  ReporterUserId: string;
  Category: number;
  Reason: string;
  Platform: string;
  ModdedClient: boolean;
  Metadata: string;
  CreatedAt: string;
}

export interface TitleData {
  key: string;
  value: string;
  updated_at: string;
}

export interface UserData {
  id: string;
  user_id: string;
  key_name: string;
  value: string;
  generation: number;
}

export interface Offer {
  offer_id: string;
  name: string;
  titleId: string;
  envId: string;
  transaction_id: string;
  discount_percent: number;
}

export interface OfferDisplay {
  offer_display_id: string;
  name: string;
  description: string;
}

export interface OfferBinding {
  offer_binding_id: string;
  offer_id: string;
  offer_display_id: string;
  deployment_id: string;
  committed: boolean;
  display_index: number;
}

export interface Entitlement {
  entitlement_id: string;
  name: string;
  type: string;
  item_class: string;
}

export interface Transaction {
  transaction_id: string;
  name: string;
}

export interface ProgressionTrack {
  track_id: string;
  name: string;
}

export interface ProgressionTree {
  tree_id: string;
  name: string;
}

export interface ApiKey {
  key_id: string;
  api_key: string;
  name: string;
  created_at: string;
}

export interface Friend {
  Presence: {
    FriendLinkId: string;
    UserName: string;
    RoomId: string;
    Zone: string;
    Region: string;
    IsPublic: boolean;
  };
  Created: string;
}

export interface AnalyticsEvent {
  event_id: string;
  platform: string;
  timestamp: string;
  data: string;
}
