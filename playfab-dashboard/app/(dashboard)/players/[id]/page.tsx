"use client";
import { useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { Card, Btn, Badge, Table, Tr, Td } from "@/components/Card";
import { PageChrome } from "@/components/PageChrome";
import {
  getPlayer, getPlayerExtras, banUser, unbanUser, deletePlayer,
  grantItemsToUser, revokeInventoryItem,
  addUserVirtualCurrency, subtractUserVirtualCurrency, setUserVirtualCurrency,
} from "@/lib/api";

type TabKey =
  | "Overview" | "Cloud Script" | "PlayStream" | "Purchases (Legacy)" | "Statistics (Legacy)"
  | "Friends" | "Logins" | "Virtual Currency (Legacy)" | "Bans" | "Data Explorer"
  | "Characters" | "Inventory (Legacy)" | "Inventory (V2)" | "Transaction History (V2)"
  | "Player Data" | "Segments" | "Objects" | "Files" | "Policy" | "Groups" | "Statistics" | "Game Saves";

const TABS: TabKey[] = [
  "Overview", "Cloud Script", "PlayStream", "Purchases (Legacy)", "Statistics (Legacy)",
  "Friends", "Logins", "Virtual Currency (Legacy)", "Bans", "Data Explorer",
  "Characters", "Inventory (Legacy)", "Inventory (V2)", "Transaction History (V2)",
  "Player Data", "Segments", "Objects", "Files", "Policy", "Groups", "Statistics", "Game Saves",
];

export default function PlayerProfilePage() {
  const params = useParams<{ id: string }>();
  const playerId = params?.id ?? "";
  const [data, setData]     = useState<any>(null);
  const [extras, setExtras] = useState<any>(null);
  const [tab, setTab]       = useState<TabKey>("Overview");
  const [err, setErr]       = useState("");
  const [busy, setBusy]     = useState(false);

  const refresh = async () => {
    try {
      const [d, e] = await Promise.all([getPlayer(playerId), getPlayerExtras(playerId)]);
      setData(d); setExtras(e); setErr("");
    } catch (ex: any) { setErr(ex.message ?? String(ex)); }
  };

  useEffect(() => { if (playerId) refresh(); }, [playerId]);

  const ban    = async () => { setBusy(true); try { await banUser(playerId, "Dashboard ban"); await refresh(); } finally { setBusy(false); } };
  const unban  = async () => { setBusy(true); try { await unbanUser(playerId);                await refresh(); } finally { setBusy(false); } };
  const purge  = async () => {
    if (!confirm(`Permanently delete '${playerId}' and all of their data?`)) return;
    setBusy(true);
    try { await deletePlayer(playerId); window.location.href = "/players"; } finally { setBusy(false); }
  };

  const grantItem = async () => {
    const ids = prompt("ItemIds to grant (comma- or space-separated):");
    if (!ids) return;
    const list = ids.split(/[\s,]+/).map(s => s.trim()).filter(Boolean);
    setBusy(true);
    try { await grantItemsToUser(playerId, list); await refresh(); }
    catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };
  const revoke = async (instanceId: string) => {
    if (!confirm(`Revoke item instance ${instanceId}?`)) return;
    setBusy(true);
    try { await revokeInventoryItem(playerId, instanceId); await refresh(); }
    catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };
  const adjustVC = async (code: string, delta: number) => {
    setBusy(true);
    try {
      if (delta >= 0) await addUserVirtualCurrency(playerId, code,  delta);
      else            await subtractUserVirtualCurrency(playerId, code, -delta);
      await refresh();
    } catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };
  const setVC = async (code: string, current: number) => {
    const v = prompt(`Set '${code}' balance to:`, String(current));
    if (v == null || Number.isNaN(parseInt(v, 10))) return;
    setBusy(true);
    try { await setUserVirtualCurrency(playerId, code, parseInt(v, 10)); await refresh(); }
    catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };

  const player = data?.Player;
  const isBanned = !!player?.IsBanned;
  const ts = (n: number) => n ? new Date(n * 1000).toLocaleString() : "—";
  const tsMs = (n: number) => n ? new Date(n).toLocaleString() : "—";

  return (
    <PageChrome
      title={playerId}
      subtitle={player?.DisplayName || player?.Username || undefined}
      breadcrumbs={[
        { label: "Players", href: "/players" },
        { label: playerId },
      ]}
      tabs={TABS.map((t) => ({ label: t, active: t === tab, onClick: () => setTab(t) }))}
      actions={
        <div className="flex gap-2">
          <Btn variant="secondary" onClick={isBanned ? unban : ban} disabled={busy}>{isBanned ? "Unban" : "Ban"}</Btn>
          <Btn variant="danger"    onClick={purge} disabled={busy}>Delete player</Btn>
        </div>
      }
    >
      {err && (
        <div style={{ background: "#fde8e8", border: "1px solid rgb(var(--color-error-400))", color: "rgb(var(--color-error-700))", padding: 10, borderRadius: 2, marginBottom: 16, fontSize: 13 }}>
          {err}
        </div>
      )}

      {!data ? <p style={{ color: "rgb(var(--color-surface-700))" }}>Loading…</p> : (
        <>
          {/* ─── OVERVIEW ─────────────────────────────────────────────── */}
          {tab === "Overview" && (
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
              <Card>
                <h3 className="h3" style={{ margin: "0 0 12px" }}>Player profile</h3>
                <Detail label="PlayFabId"     value={player?.PlayFabId} mono />
                <Detail label="EntityId"      value={player?.EntityId}  mono />
                <Detail label="Username"      value={player?.Username} />
                <Detail label="Display Name"  value={player?.DisplayName} />
                <Detail label="Email"         value={player?.Email} />
                <Detail label="Custom Id"     value={player?.CustomId} mono />
                <Detail label="Origination"   value={player?.OriginPlatform} />
                <Detail label="Created"       value={ts(player?.CreatedUnix)} />
                <Detail label="Last Login"    value={ts(player?.LastLoginUnix)} />
                <Detail label="Status"        value={isBanned ? <Badge label="banned" color="error" /> : <Badge label="active" color="success" />} />
                <Detail label="Tags"          value={(player?.Tags ?? []).join(", ") || "—"} />
              </Card>

              <Card>
                <h3 className="h3" style={{ margin: "0 0 12px" }}>Linked accounts</h3>
                {Object.entries((player?.Linked ?? {}) as Record<string, string>).filter(([, v]) => !!v).map(([k, v]) => (
                  <Detail key={k} label={k} value={v} mono />
                ))}
                {Object.values(player?.Linked ?? {}).filter(Boolean).length === 0 &&
                  <p style={{ color: "rgb(var(--color-surface-700))", fontSize: 13 }}>No linked accounts.</p>}
              </Card>

              <Card>
                <h3 className="h3" style={{ margin: "0 0 12px" }}>Virtual currency (Legacy)</h3>
                <Table headers={["Code", "Balance", ""]}>
                  {Object.entries((data.VirtualCurrency ?? {}) as Record<string, number>).map(([code, bal]) => (
                    <Tr key={code}>
                      <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{code}</span></Td>
                      <Td>{bal}</Td>
                      <Td>
                        <div className="flex gap-1">
                          <Btn variant="ghost" onClick={() => adjustVC(code,  100)}>+100</Btn>
                          <Btn variant="ghost" onClick={() => adjustVC(code, -100)}>-100</Btn>
                          <Btn variant="secondary" onClick={() => setVC(code, bal)}>Set…</Btn>
                        </div>
                      </Td>
                    </Tr>
                  ))}
                  {Object.keys(data.VirtualCurrency ?? {}).length === 0 &&
                    <Tr><Td><span style={{ color: "rgb(var(--color-surface-700))" }}>No currencies</span></Td><Td>—</Td><Td>—</Td></Tr>}
                </Table>
              </Card>

              <Card>
                <div className="flex items-center justify-between mb-3">
                  <h3 className="h3" style={{ margin: 0 }}>Inventory (Legacy)</h3>
                  <Btn variant="primary" onClick={grantItem}>Grant items</Btn>
                </div>
                <Table headers={["ItemId", "Display", "Acquired", ""]}>
                  {((data.Inventory ?? []) as any[]).map((it) => (
                    <Tr key={it.ItemInstanceId}>
                      <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{it.ItemId}</span></Td>
                      <Td>{it.DisplayName || "—"}</Td>
                      <Td>{ts(it.PurchaseDate)}</Td>
                      <Td><Btn variant="danger" onClick={() => revoke(it.ItemInstanceId)} disabled={busy}>Revoke</Btn></Td>
                    </Tr>
                  ))}
                  {(data.Inventory ?? []).length === 0 &&
                    <Tr><Td><span style={{ color: "rgb(var(--color-surface-700))" }}>No items</span></Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
                </Table>
              </Card>
            </div>
          )}

          {/* ─── CLOUD SCRIPT ─────────────────────────────────────────── */}
          {tab === "Cloud Script" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Recent CloudScript executions ({extras?.CloudScript?.length ?? 0})</h3>
              <Table headers={["Function", "Time", "Duration", "Status", "Result"]}>
                {(extras?.CloudScript ?? []).map((l: any) => (
                  <Tr key={l.ExecutionId}>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{l.FunctionName}</span></Td>
                    <Td>{ts(l.Timestamp)}</Td>
                    <Td>{l.ExecutionTimeSeconds}s</Td>
                    <Td>{l.Error ? <Badge label="error" color="error" /> : <Badge label="ok" color="success" />}</Td>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-surface-700))" }}>{l.ResultJson?.slice(0, 80) || "—"}</span></Td>
                  </Tr>
                ))}
                {!(extras?.CloudScript ?? []).length && <EmptyRow cols={5} />}
              </Table>
            </Card>
          )}

          {/* ─── PLAYSTREAM ───────────────────────────────────────────── */}
          {tab === "PlayStream" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Event timeline ({extras?.PlayStream?.length ?? 0})</h3>
              <Table headers={["Event", "Namespace", "Time", "Payload"]}>
                {(extras?.PlayStream ?? []).map((e: any) => (
                  <Tr key={e.EventId}>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{e.EventName}</span></Td>
                    <Td>{e.EventNamespace}</Td>
                    <Td>{tsMs(e.Timestamp)}</Td>
                    <Td><span className="font-mono text-xs">{(e.PayloadJson || "").slice(0, 100)}</span></Td>
                  </Tr>
                ))}
                {!(extras?.PlayStream ?? []).length && <EmptyRow cols={4} />}
              </Table>
            </Card>
          )}

          {/* ─── PURCHASES (LEGACY) ──────────────────────────────────── */}
          {tab === "Purchases (Legacy)" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Purchases (Legacy)</h3>
              <p style={{ color: "rgb(var(--color-surface-700))", fontSize: 13 }}>
                Legacy v1 PurchaseDate stamps are visible in the Inventory tab. Standalone purchase records are part of the V2 transaction history.
              </p>
            </Card>
          )}

          {/* ─── STATISTICS (LEGACY) ─────────────────────────────────── */}
          {tab === "Statistics (Legacy)" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Statistics (Legacy v1)</h3>
              <Table headers={["Name", "Value", "Version"]}>
                {((data.Statistics ?? []) as any[]).map((st) => (
                  <Tr key={st.StatisticName}>
                    <Td><span className="font-mono text-xs">{st.StatisticName}</span></Td>
                    <Td>{st.Value}</Td>
                    <Td>{st.Version}</Td>
                  </Tr>
                ))}
                {!(data.Statistics ?? []).length && <EmptyRow cols={3} />}
              </Table>
            </Card>
          )}

          {/* ─── FRIENDS ─────────────────────────────────────────────── */}
          {tab === "Friends" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Friends</h3>
              <Table headers={["FriendId", "Username", "Title display name", "Tags", "Added"]}>
                {((data.Friends ?? []) as any[]).map((f) => (
                  <Tr key={f.FriendPlayFabId}>
                    <Td><a href={`/players/${f.FriendPlayFabId}`} className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{f.FriendPlayFabId}</a></Td>
                    <Td>{f.Username || "—"}</Td>
                    <Td>{f.TitleDisplayName || "—"}</Td>
                    <Td>{(f.Tags ?? []).join(", ")}</Td>
                    <Td>{ts(f.Created)}</Td>
                  </Tr>
                ))}
                {!(data.Friends ?? []).length && <EmptyRow cols={5} />}
              </Table>
            </Card>
          )}

          {/* ─── LOGINS ──────────────────────────────────────────────── */}
          {tab === "Logins" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Login history ({extras?.Logins?.length ?? 0})</h3>
              <Table headers={["Event", "Time", "Payload"]}>
                {(extras?.Logins ?? []).map((l: any, i: number) => (
                  <Tr key={i}>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{l.EventName}</span></Td>
                    <Td>{tsMs(l.Timestamp)}</Td>
                    <Td><span className="font-mono text-xs">{(l.PayloadJson || "").slice(0, 120)}</span></Td>
                  </Tr>
                ))}
                {!(extras?.Logins ?? []).length && <EmptyRow cols={3} />}
              </Table>
            </Card>
          )}

          {/* ─── VIRTUAL CURRENCY (LEGACY) ───────────────────────────── */}
          {tab === "Virtual Currency (Legacy)" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Virtual currency (Legacy v1)</h3>
              <Table headers={["Code", "Balance", ""]}>
                {Object.entries((data.VirtualCurrency ?? {}) as Record<string, number>).map(([code, bal]) => (
                  <Tr key={code}>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{code}</span></Td>
                    <Td>{bal}</Td>
                    <Td>
                      <div className="flex gap-1">
                        <Btn variant="ghost" onClick={() => adjustVC(code,  100)}>+100</Btn>
                        <Btn variant="ghost" onClick={() => adjustVC(code, -100)}>-100</Btn>
                        <Btn variant="secondary" onClick={() => setVC(code, bal)}>Set…</Btn>
                      </div>
                    </Td>
                  </Tr>
                ))}
                {!Object.keys(data.VirtualCurrency ?? {}).length && <EmptyRow cols={3} />}
              </Table>
            </Card>
          )}

          {/* ─── BANS ────────────────────────────────────────────────── */}
          {tab === "Bans" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Bans</h3>
              <Table headers={["Status", "Reason", "Created"]}>
                {(extras?.Bans ?? []).map((b: any, i: number) => (
                  <Tr key={i}>
                    <Td><Badge label={b.Active ? "active" : "expired"} color={b.Active ? "error" : "surface"} /></Td>
                    <Td>{b.Reason}</Td>
                    <Td>{ts(b.Created)}</Td>
                  </Tr>
                ))}
                {!(extras?.Bans ?? []).length && <EmptyRow cols={3} />}
              </Table>
            </Card>
          )}

          {/* ─── DATA EXPLORER ───────────────────────────────────────── */}
          {tab === "Data Explorer" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Data Explorer</h3>
              <p className="text-xs" style={{ color: "rgb(var(--color-surface-700))", marginBottom: 12 }}>Raw JSON dump of every persisted record we have for this player.</p>
              <pre className="text-xs" style={{ background: "rgb(var(--color-surface-100))", padding: 12, overflow: "auto", maxHeight: "60vh", borderRadius: 2, color: "rgb(var(--color-surface-900))" }}>
                {JSON.stringify({ Player: data, Extras: extras }, null, 2)}
              </pre>
            </Card>
          )}

          {/* ─── CHARACTERS ──────────────────────────────────────────── */}
          {tab === "Characters" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Characters</h3>
              <Table headers={["CharacterId", "Name", "Type", "Created"]}>
                {(extras?.Characters ?? []).map((c: any) => (
                  <Tr key={c.CharacterId}>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{c.CharacterId}</span></Td>
                    <Td>{c.CharacterName}</Td>
                    <Td>{c.CharacterType}</Td>
                    <Td>{ts(c.Created)}</Td>
                  </Tr>
                ))}
                {!(extras?.Characters ?? []).length && <EmptyRow cols={4} />}
              </Table>
            </Card>
          )}

          {/* ─── INVENTORY (LEGACY) ──────────────────────────────────── */}
          {tab === "Inventory (Legacy)" && (
            <Card>
              <div className="flex items-center justify-between mb-3">
                <h3 className="h3" style={{ margin: 0 }}>Inventory (Legacy v1)</h3>
                <Btn variant="primary" onClick={grantItem}>Grant items</Btn>
              </div>
              <Table headers={["ItemId", "InstanceId", "Display", "Acquired", ""]}>
                {((data.Inventory ?? []) as any[]).map((it) => (
                  <Tr key={it.ItemInstanceId}>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{it.ItemId}</span></Td>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-surface-700))" }}>{it.ItemInstanceId}</span></Td>
                    <Td>{it.DisplayName || "—"}</Td>
                    <Td>{ts(it.PurchaseDate)}</Td>
                    <Td><Btn variant="danger" onClick={() => revoke(it.ItemInstanceId)} disabled={busy}>Revoke</Btn></Td>
                  </Tr>
                ))}
                {!(data.Inventory ?? []).length && <EmptyRow cols={5} />}
              </Table>
            </Card>
          )}

          {/* ─── INVENTORY V2 ────────────────────────────────────────── */}
          {tab === "Inventory (V2)" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Economy V2 inventory</h3>
              <Table headers={["Collection", "ItemId", "StackId", "Amount"]}>
                {(extras?.InventoryV2 ?? []).map((i: any, idx: number) => (
                  <Tr key={idx}>
                    <Td>{i.CollectionId}</Td>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{i.ItemId}</span></Td>
                    <Td>{i.StackId}</Td>
                    <Td>{i.Amount}</Td>
                  </Tr>
                ))}
                {!(extras?.InventoryV2 ?? []).length && <EmptyRow cols={4} />}
              </Table>
            </Card>
          )}

          {/* ─── TRANSACTIONS V2 ─────────────────────────────────────── */}
          {tab === "Transaction History (V2)" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Transaction history (V2)</h3>
              <Table headers={["TransactionId", "Operation", "ItemId", "Amount", "Time"]}>
                {(extras?.TransactionsV2 ?? []).map((t: any) => (
                  <Tr key={t.TransactionId}>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{t.TransactionId}</span></Td>
                    <Td><Badge label={t.Operation} color="surface" /></Td>
                    <Td>{t.ItemId}</Td>
                    <Td>{t.Amount}</Td>
                    <Td>{ts(t.Timestamp)}</Td>
                  </Tr>
                ))}
                {!(extras?.TransactionsV2 ?? []).length && <EmptyRow cols={5} />}
              </Table>
            </Card>
          )}

          {/* ─── PLAYER DATA ─────────────────────────────────────────── */}
          {tab === "Player Data" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>User Data</h3>
              <Table headers={["Scope", "Key", "Value", "Permission", "Updated"]}>
                {((data.UserData ?? []) as any[]).map((d, i) => (
                  <Tr key={i}>
                    <Td><Badge label={d.Scope} color="surface" /></Td>
                    <Td><span className="font-mono text-xs">{d.Key}</span></Td>
                    <Td><span className="font-mono text-xs">{d.Value}</span></Td>
                    <Td>{d.Permission}</Td>
                    <Td>{ts(d.LastUpdated)}</Td>
                  </Tr>
                ))}
                {!(data.UserData ?? []).length && <EmptyRow cols={5} />}
              </Table>
            </Card>
          )}

          {/* ─── SEGMENTS ────────────────────────────────────────────── */}
          {tab === "Segments" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Service tags / segments</h3>
              <Table headers={["Namespace", "Tag"]}>
                {(extras?.Segments ?? []).map((s: any, i: number) => (
                  <Tr key={i}>
                    <Td>{s.Namespace}</Td>
                    <Td><Badge label={s.Tag} color="primary" /></Td>
                  </Tr>
                ))}
                {!(extras?.Segments ?? []).length && <EmptyRow cols={2} />}
              </Table>
            </Card>
          )}

          {/* ─── OBJECTS ─────────────────────────────────────────────── */}
          {tab === "Objects" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Entity objects</h3>
              <Table headers={["ObjectName", "Data (JSON)"]}>
                {(extras?.Objects ?? []).map((o: any, i: number) => (
                  <Tr key={i}>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{o.ObjectName}</span></Td>
                    <Td><span className="font-mono text-xs">{(o.DataObjectJson || "").slice(0, 200)}</span></Td>
                  </Tr>
                ))}
                {!(extras?.Objects ?? []).length && <EmptyRow cols={2} />}
              </Table>
            </Card>
          )}

          {/* ─── FILES ──────────────────────────────────────────────── */}
          {tab === "Files" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Entity files</h3>
              <Table headers={["FileName", "Size", "ProfileVersion", "Last modified"]}>
                {(extras?.Files ?? []).map((f: any, i: number) => (
                  <Tr key={i}>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{f.FileName}</span></Td>
                    <Td>{f.Size} B</Td>
                    <Td>{f.ProfileVersion}</Td>
                    <Td>{ts(f.LastModified)}</Td>
                  </Tr>
                ))}
                {!(extras?.Files ?? []).length && <EmptyRow cols={4} />}
              </Table>
            </Card>
          )}

          {/* ─── POLICY ─────────────────────────────────────────────── */}
          {tab === "Policy" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Policy</h3>
              <p style={{ color: "rgb(var(--color-surface-700))", fontSize: 13 }}>
                Title-level entity policies are managed at <a href="/openid">/openid</a>. No per-player policy overrides are persisted.
              </p>
            </Card>
          )}

          {/* ─── GROUPS ─────────────────────────────────────────────── */}
          {tab === "Groups" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Group memberships</h3>
              <Table headers={["GroupId", "Role", "Joined"]}>
                {(extras?.Groups ?? []).map((g: any, i: number) => (
                  <Tr key={i}>
                    <Td><a href={`/groups/${g.GroupId}`} className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{g.GroupId}</a></Td>
                    <Td>{g.RoleId}</Td>
                    <Td>{ts(g.Joined)}</Td>
                  </Tr>
                ))}
                {!(extras?.Groups ?? []).length && <EmptyRow cols={3} />}
              </Table>
            </Card>
          )}

          {/* ─── STATISTICS V2 ──────────────────────────────────────── */}
          {tab === "Statistics" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Statistics (V2)</h3>
              <Table headers={["Name", "Scores", "Version", "Updated"]}>
                {(extras?.StatisticsV2 ?? []).map((s: any, i: number) => (
                  <Tr key={i}>
                    <Td><span className="font-mono text-xs">{s.StatisticName}</span></Td>
                    <Td>{(s.Scores ?? []).join(", ")}</Td>
                    <Td>{s.Version}</Td>
                    <Td>{ts(s.LastUpdated)}</Td>
                  </Tr>
                ))}
                {!(extras?.StatisticsV2 ?? []).length && <EmptyRow cols={4} />}
              </Table>
            </Card>
          )}

          {/* ─── GAME SAVES ─────────────────────────────────────────── */}
          {tab === "Game Saves" && (
            <Card>
              <h3 className="h3" style={{ margin: "0 0 12px" }}>Game saves</h3>
              <p style={{ color: "rgb(var(--color-surface-700))", fontSize: 13 }}>
                Game saves are stored as entity files. See the <strong>Files</strong> tab.
              </p>
              <Table headers={["FileName", "Size", "Last modified"]}>
                {(extras?.Files ?? []).map((f: any, i: number) => (
                  <Tr key={i}>
                    <Td><span className="font-mono text-xs">{f.FileName}</span></Td>
                    <Td>{f.Size} B</Td>
                    <Td>{ts(f.LastModified)}</Td>
                  </Tr>
                ))}
                {!(extras?.Files ?? []).length && <EmptyRow cols={3} />}
              </Table>
            </Card>
          )}
        </>
      )}
    </PageChrome>
  );
}

function Detail({ label, value, mono }: { label: string; value: any; mono?: boolean }) {
  return (
    <div style={{ display: "flex", padding: "6px 0", borderBottom: "1px solid rgb(var(--color-surface-200))", fontSize: 13 }}>
      <span style={{ width: 140, color: "rgb(var(--color-surface-700))", flexShrink: 0 }}>{label}</span>
      <span style={{ flex: 1, color: "rgb(var(--color-surface-900))", fontFamily: mono ? "var(--font-mono)" : undefined, wordBreak: "break-word" }}>
        {value ?? "—"}
      </span>
    </div>
  );
}

function EmptyRow({ cols }: { cols: number }) {
  return (
    <Tr>
      <Td><span style={{ color: "rgb(var(--color-surface-700))" }}>No data</span></Td>
      {Array.from({ length: cols - 1 }).map((_, i) => <Td key={i}>—</Td>)}
    </Tr>
  );
}
