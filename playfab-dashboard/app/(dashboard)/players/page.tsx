"use client";
import { useEffect, useState } from "react";
import { Card, CardHeader, Table, Tr, Td, Badge, Btn, Input, Modal } from "@/components/Card";
import {
  listPlayers, getPlayer, banUser, unbanUser, updatePlayer, deletePlayer,
  grantItemsToUser, revokeInventoryItem, addUserVirtualCurrency, subtractUserVirtualCurrency, setUserVirtualCurrency,
  PlayerRow, PlayerPatch,
} from "@/lib/api";

interface InvState {
  PlayFabId: string;
  Player: any;
}

export default function PlayersPage() {
  const [rows, setRows] = useState<PlayerRow[]>([]);
  const [total, setTotal] = useState(0);
  const [skip, setSkip] = useState(0);
  const take = 50;
  const [query, setQuery] = useState("");
  const [selected, setSelected] = useState<any>(null);
  const [editing, setEditing] = useState<PlayerPatch | null>(null);
  const [inv, setInv] = useState<InvState | null>(null);
  const [grantInput, setGrantInput] = useState("");
  const [grantVersion, setGrantVersion] = useState("");
  const [newCurrencyCode, setNewCurrencyCode] = useState("");
  const [newCurrencyAmount, setNewCurrencyAmount] = useState("0");
  const [err, setErr] = useState("");
  const [busy, setBusy] = useState(false);

  const refresh = () => {
    listPlayers(skip, take, query)
      .then((r) => { setRows(r.Players); setTotal(r.Total); setErr(""); })
      .catch((e) => setErr(e.message ?? String(e)));
  };

  useEffect(refresh, [skip, query]);

  const open = (id: string) =>
    getPlayer(id).then(setSelected).catch((e) => setErr(e.message));

  const startEdit = (p: PlayerRow) =>
    setEditing({
      PlayFabId:      p.PlayFabId,
      DisplayName:    p.DisplayName ?? "",
      Username:       p.Username ?? "",
      Email:          p.Email ?? "",
      CustomId:       p.CustomId ?? "",
      OriginPlatform: p.OriginPlatform ?? "",
      IsBanned:       p.IsBanned,
      Tags:           p.Tags ?? [],
    });

  const saveEdit = async () => {
    if (!editing) return;
    try { await updatePlayer(editing); setEditing(null); refresh(); }
    catch (e: any) { setErr(e.message ?? String(e)); }
  };

  const toggleBan = async (p: PlayerRow) => {
    if (p.IsBanned) await unbanUser(p.PlayFabId);
    else            await banUser(p.PlayFabId, "Dashboard ban");
    refresh();
  };

  const remove = async (p: PlayerRow) => {
    const id = p.DisplayName || p.Username || p.PlayFabId;
    if (!confirm(`Permanently delete player '${id}' and all of their data? This cannot be undone.`)) return;
    try { await deletePlayer(p.PlayFabId); refresh(); }
    catch (e: any) { setErr(e.message ?? String(e)); }
  };

  /* ─── Inventory modal ─────────────────────────────────────────────── */
  const openInventory = async (id: string) => {
    setGrantInput("");
    setGrantVersion("");
    setNewCurrencyCode("");
    setNewCurrencyAmount("0");
    try {
      const data: any = await getPlayer(id);
      setInv({ PlayFabId: id, Player: data });
    } catch (e: any) { setErr(e.message ?? String(e)); }
  };

  const reloadInventory = async () => {
    if (!inv) return;
    try {
      const data: any = await getPlayer(inv.PlayFabId);
      setInv({ PlayFabId: inv.PlayFabId, Player: data });
    } catch (e: any) { setErr(e.message ?? String(e)); }
  };

  const grant = async () => {
    if (!inv) return;
    const ids = grantInput.split(/[\s,]+/).map(s => s.trim()).filter(Boolean);
    if (ids.length === 0) return;
    setBusy(true);
    try {
      const r = await grantItemsToUser(inv.PlayFabId, ids, grantVersion);
      if (r.Skipped?.length) setErr(`Granted ${r.Granted.length}, skipped ${r.Skipped.length} (not in catalog): ${r.Skipped.join(", ")}`);
      else setErr("");
      setGrantInput("");
      await reloadInventory();
    } catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };

  const revoke = async (instanceId: string) => {
    if (!inv || !confirm(`Revoke item instance ${instanceId}?`)) return;
    setBusy(true);
    try { await revokeInventoryItem(inv.PlayFabId, instanceId); await reloadInventory(); }
    catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };

  const adjustCurrency = async (code: string, delta: number) => {
    if (!inv) return;
    setBusy(true);
    try {
      if (delta >= 0) await addUserVirtualCurrency(inv.PlayFabId, code, delta);
      else            await subtractUserVirtualCurrency(inv.PlayFabId, code, -delta);
      await reloadInventory();
    } catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };

  const setCurrencyExact = async (code: string, value: number) => {
    if (!inv) return;
    setBusy(true);
    try { await setUserVirtualCurrency(inv.PlayFabId, code, value); await reloadInventory(); }
    catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };

  const addNewCurrency = async () => {
    if (!inv || !newCurrencyCode) return;
    const n = parseInt(newCurrencyAmount, 10);
    if (Number.isNaN(n)) return;
    await setCurrencyExact(newCurrencyCode.trim().toUpperCase(), n);
    setNewCurrencyCode("");
    setNewCurrencyAmount("0");
  };

  return (
    <div className="space-y-4">
      {err && <div style={{ background: "rgb(var(--color-error-500) / 0.1)" }} className="rounded p-3 text-sm">{err}</div>}

      <Card>
        <div className="flex items-center gap-3 mb-3">
          <Input placeholder="Search by id, username, email…" value={query} onChange={(e) => { setSkip(0); setQuery(e.target.value); }} className="flex-1" />
          <Badge label={`${total} total`} color="primary" />
        </div>

        <Table headers={["PlayFabId", "Display / Username", "Origin", "Last Login", "Status", ""]}>
          {rows.map((p) => (
            <Tr key={p.PlayFabId}>
              <Td>
                <a href={`/players/${p.PlayFabId}`} className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{p.PlayFabId}</a>
              </Td>
              <Td>
                <div>{p.DisplayName || p.Username || "—"}</div>
                <div className="text-xs" style={{ color: "rgb(var(--color-surface-600))" }}>{p.Email || p.CustomId || ""}</div>
              </Td>
              <Td><Badge label={p.OriginPlatform || "?"} color="surface" /></Td>
              <Td>{p.LastLoginUnix ? new Date(p.LastLoginUnix * 1000).toLocaleString() : "—"}</Td>
              <Td>{p.IsBanned ? <Badge label="banned" color="error" /> : <Badge label="active" color="success" />}</Td>
              <Td>
                <div className="flex gap-2 flex-wrap">
                  <Btn variant="ghost" onClick={() => open(p.PlayFabId)}>View</Btn>
                  <Btn variant="ghost" onClick={() => startEdit(p)}>Edit</Btn>
                  <Btn variant="ghost" onClick={() => openInventory(p.PlayFabId)}>Inventory</Btn>
                  <Btn variant={p.IsBanned ? "secondary" : "danger"} onClick={() => toggleBan(p)}>{p.IsBanned ? "Unban" : "Ban"}</Btn>
                  <Btn variant="danger" onClick={() => remove(p)}>Delete</Btn>
                </div>
              </Td>
            </Tr>
          ))}
          {rows.length === 0 && <Tr><Td><span style={{ color: "rgb(var(--color-surface-600))" }}>No players yet</span></Td><Td>—</Td><Td>—</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
        </Table>

        <div className="flex justify-between items-center mt-3">
          <Btn variant="ghost" onClick={() => setSkip(Math.max(0, skip - take))} disabled={skip === 0}>← Prev</Btn>
          <span className="text-xs" style={{ color: "rgb(var(--color-surface-600))" }}>{skip + 1}–{Math.min(skip + take, total)} of {total}</span>
          <Btn variant="ghost" onClick={() => setSkip(skip + take)} disabled={skip + take >= total}>Next →</Btn>
        </div>
      </Card>

      <Modal open={selected != null} onClose={() => setSelected(null)} title={selected?.Player?.PlayFabId ?? ""}>
        {selected && (
          <pre className="text-xs overflow-auto max-h-[70vh]" style={{ color: "rgb(var(--color-surface-900))" }}>
            {JSON.stringify(selected, null, 2)}
          </pre>
        )}
      </Modal>

      <Modal open={editing != null} onClose={() => setEditing(null)} title={editing ? `Edit ${editing.PlayFabId}` : ""}>
        {editing && (
          <div className="space-y-3">
            <Input label="Display Name"   value={editing.DisplayName ?? ""}    onChange={(e) => setEditing({ ...editing, DisplayName: e.target.value })} />
            <Input label="Username"       value={editing.Username ?? ""}       onChange={(e) => setEditing({ ...editing, Username: e.target.value })} />
            <Input label="Email"          value={editing.Email ?? ""}          onChange={(e) => setEditing({ ...editing, Email: e.target.value })} />
            <Input label="Custom Id"      value={editing.CustomId ?? ""}       onChange={(e) => setEditing({ ...editing, CustomId: e.target.value })} />
            <Input label="Origin Platform" value={editing.OriginPlatform ?? ""} onChange={(e) => setEditing({ ...editing, OriginPlatform: e.target.value })} />
            <Input
              label="Tags (comma-separated)"
              value={(editing.Tags ?? []).join(", ")}
              onChange={(e) => setEditing({ ...editing, Tags: e.target.value.split(",").map(s => s.trim()).filter(Boolean) })}
            />
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={!!editing.IsBanned}
                onChange={(e) => setEditing({ ...editing, IsBanned: e.target.checked })}
              />
              <span style={{ color: "rgb(var(--color-surface-900))" }}>Banned</span>
            </label>
            <div className="flex justify-end gap-2">
              <Btn variant="ghost" onClick={() => setEditing(null)}>Cancel</Btn>
              <Btn variant="primary" onClick={saveEdit}>Save</Btn>
            </div>
          </div>
        )}
      </Modal>

      <Modal open={inv != null} onClose={() => setInv(null)} title={inv ? `Inventory · ${inv.PlayFabId}` : ""}>
        {inv && (
          <div className="space-y-5">
            {/* ─── Virtual currency ───────────────────────────────────── */}
            <section>
              <h3 className="text-sm font-semibold mb-2" style={{ color: "rgb(var(--color-surface-900))" }}>Virtual currency</h3>
              <Table headers={["Code", "Balance", ""]}>
                {Object.entries((inv.Player?.VirtualCurrency ?? {}) as Record<string, number>).map(([code, balance]) => (
                  <Tr key={code}>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{code}</span></Td>
                    <Td>{balance}</Td>
                    <Td>
                      <div className="flex gap-1 flex-wrap items-center">
                        <Btn variant="ghost"     onClick={() => adjustCurrency(code,  10)}>+10</Btn>
                        <Btn variant="ghost"     onClick={() => adjustCurrency(code, 100)}>+100</Btn>
                        <Btn variant="ghost"     onClick={() => adjustCurrency(code, -10)}>-10</Btn>
                        <Btn variant="ghost"     onClick={() => adjustCurrency(code, -100)}>-100</Btn>
                        <Btn variant="secondary" onClick={() => {
                          const v = prompt(`Set '${code}' balance to:`, String(balance));
                          if (v != null && !Number.isNaN(parseInt(v, 10))) setCurrencyExact(code, parseInt(v, 10));
                        }}>Set…</Btn>
                      </div>
                    </Td>
                  </Tr>
                ))}
                {Object.keys(inv.Player?.VirtualCurrency ?? {}).length === 0 &&
                  <Tr><Td><span style={{ color: "rgb(var(--color-surface-600))" }}>No currencies</span></Td><Td>—</Td><Td>—</Td></Tr>}
              </Table>
              <div className="flex gap-2 mt-2 items-end">
                <Input label="New code"  value={newCurrencyCode}    onChange={(e) => setNewCurrencyCode(e.target.value)} className="w-24" />
                <Input label="Balance"   value={newCurrencyAmount}  onChange={(e) => setNewCurrencyAmount(e.target.value)} className="w-32" />
                <Btn variant="primary" onClick={addNewCurrency} disabled={busy || !newCurrencyCode}>Add currency</Btn>
              </div>
            </section>

            {/* ─── Inventory items ────────────────────────────────────── */}
            <section>
              <h3 className="text-sm font-semibold mb-2" style={{ color: "rgb(var(--color-surface-900))" }}>Inventory</h3>
              <Table headers={["ItemId", "Display", "InstanceId", "Acquired", ""]}>
                {((inv.Player?.Inventory ?? []) as any[]).map((it) => (
                  <Tr key={it.ItemInstanceId}>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{it.ItemId}</span></Td>
                    <Td>{it.DisplayName || "—"}</Td>
                    <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-surface-600))" }}>{it.ItemInstanceId}</span></Td>
                    <Td>{it.PurchaseDate ? new Date(it.PurchaseDate * 1000).toLocaleString() : "—"}</Td>
                    <Td><Btn variant="danger" onClick={() => revoke(it.ItemInstanceId)} disabled={busy}>Revoke</Btn></Td>
                  </Tr>
                ))}
                {(inv.Player?.Inventory ?? []).length === 0 &&
                  <Tr><Td><span style={{ color: "rgb(var(--color-surface-600))" }}>No items</span></Td><Td>—</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
              </Table>
              <div className="flex gap-2 mt-2 items-end flex-wrap">
                <Input label="Item Ids (comma- or space-separated)" value={grantInput} onChange={(e) => setGrantInput(e.target.value)} className="flex-1 min-w-[240px]" />
                <Input label="Catalog Version (optional)" value={grantVersion} onChange={(e) => setGrantVersion(e.target.value)} className="w-44" />
                <Btn variant="primary" onClick={grant} disabled={busy || !grantInput.trim()}>Grant items</Btn>
              </div>
            </section>

            <div className="flex justify-end">
              <Btn variant="ghost" onClick={() => setInv(null)}>Close</Btn>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
}
