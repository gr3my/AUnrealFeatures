"use client";
import { useEffect, useState } from "react";
import { Card, Table, Tr, Td, Badge, Btn, Input, Modal } from "@/components/Card";
import {
  listSharedGroups, getSharedGroup, setSharedGroupKey, deleteSharedGroupKey, deleteSharedGroup,
  SharedGroupRow,
} from "@/lib/api";

interface SharedGroupDetail {
  SharedGroupId: string;
  CreatedUnix:   number;
  Members:       string[];
  Data:          Record<string, { Value: string; Permission: string; LastUpdatedBy: string; LastUpdated: number }>;
}

export default function SharedGroupsPage() {
  const [rows, setRows] = useState<SharedGroupRow[]>([]);
  const [total, setTotal] = useState(0);
  const [skip, setSkip] = useState(0);
  const take = 50;
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState<SharedGroupDetail | null>(null);
  const [newKey, setNewKey] = useState("");
  const [newValue, setNewValue] = useState("");
  const [newPermission, setNewPermission] = useState("Private");
  const [err, setErr] = useState("");
  const [busy, setBusy] = useState(false);

  const refresh = () => {
    listSharedGroups(skip, take, query)
      .then((r) => { setRows(r.Groups); setTotal(r.Total); setErr(""); })
      .catch((e) => setErr(e.message ?? String(e)));
  };

  useEffect(refresh, [skip, query]);

  const view = async (id: string) => {
    try { setOpen(await getSharedGroup(id) as SharedGroupDetail); }
    catch (e: any) { setErr(e.message ?? String(e)); }
  };

  const reload = async () => {
    if (!open) return;
    try { setOpen(await getSharedGroup(open.SharedGroupId) as SharedGroupDetail); }
    catch (e: any) { setErr(e.message ?? String(e)); }
  };

  const remove = async (id: string) => {
    if (!confirm(`Delete shared group '${id}'? This drops all keys and members.`)) return;
    try { await deleteSharedGroup(id); refresh(); }
    catch (e: any) { setErr(e.message ?? String(e)); }
  };

  const setKey = async () => {
    if (!open || !newKey) return;
    setBusy(true);
    try { await setSharedGroupKey(open.SharedGroupId, newKey, newValue, newPermission); setNewKey(""); setNewValue(""); await reload(); }
    catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };

  const editKey = async (key: string, currentValue: string, currentPermission: string) => {
    if (!open) return;
    const v = prompt(`Edit value for '${key}':`, currentValue);
    if (v == null) return;
    setBusy(true);
    try { await setSharedGroupKey(open.SharedGroupId, key, v, currentPermission); await reload(); }
    catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };

  const removeKey = async (key: string) => {
    if (!open || !confirm(`Remove key '${key}' from group '${open.SharedGroupId}'?`)) return;
    setBusy(true);
    try { await deleteSharedGroupKey(open.SharedGroupId, key); await reload(); }
    catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };

  return (
    <div className="space-y-4">
      {err && <div style={{ background: "rgb(var(--color-error-500) / 0.1)" }} className="rounded p-3 text-sm">{err}</div>}

      <Card>
        <div className="flex items-center gap-3 mb-3">
          <Input
            placeholder="Search by SharedGroupId…"
            value={query}
            onChange={(e) => { setSkip(0); setQuery(e.target.value); }}
            className="flex-1"
          />
          <Badge label={`${total} total`} color="primary" />
        </div>

        <Table headers={["SharedGroupId", "Members", "Keys", "Created", ""]}>
          {rows.map((g) => (
            <Tr key={g.SharedGroupId}>
              <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{g.SharedGroupId}</span></Td>
              <Td>{g.MemberCount}</Td>
              <Td>{g.KeyCount}</Td>
              <Td>{g.CreatedUnix ? new Date(g.CreatedUnix * 1000).toLocaleString() : "—"}</Td>
              <Td>
                <div className="flex gap-2">
                  <Btn variant="ghost" onClick={() => view(g.SharedGroupId)}>View</Btn>
                  <Btn variant="danger" onClick={() => remove(g.SharedGroupId)}>Delete</Btn>
                </div>
              </Td>
            </Tr>
          ))}
          {rows.length === 0 &&
            <Tr><Td><span style={{ color: "rgb(var(--color-surface-600))" }}>No shared groups</span></Td><Td>—</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
        </Table>

        <div className="flex justify-between items-center mt-3">
          <Btn variant="ghost" onClick={() => setSkip(Math.max(0, skip - take))} disabled={skip === 0}>← Prev</Btn>
          <span className="text-xs" style={{ color: "rgb(var(--color-surface-600))" }}>{skip + 1}–{Math.min(skip + take, total)} of {total}</span>
          <Btn variant="ghost" onClick={() => setSkip(skip + take)} disabled={skip + take >= total}>Next →</Btn>
        </div>
      </Card>

      <Modal open={open != null} onClose={() => setOpen(null)} title={open ? `Shared group · ${open.SharedGroupId}` : ""}>
        {open && (
          <div className="space-y-5">
            {/* ─── Members ────────────────────────────────────────────── */}
            <section>
              <h3 className="text-sm font-semibold mb-2" style={{ color: "rgb(var(--color-surface-900))" }}>Members ({open.Members.length})</h3>
              {open.Members.length === 0
                ? <div className="text-xs" style={{ color: "rgb(var(--color-surface-600))" }}>No members</div>
                : <div className="flex flex-wrap gap-1">
                    {open.Members.map((m) => (
                      <span key={m} className="font-mono text-xs px-2 py-1 rounded" style={{ background: "rgb(var(--color-surface-100))", color: "rgb(var(--color-primary-500))" }}>
                        {m}
                      </span>
                    ))}
                  </div>}
            </section>

            {/* ─── Data keys ──────────────────────────────────────────── */}
            <section>
              <h3 className="text-sm font-semibold mb-2" style={{ color: "rgb(var(--color-surface-900))" }}>
                Data ({Object.keys(open.Data ?? {}).length} keys)
              </h3>
              <Table headers={["Key", "Value", "Permission", "Updated", ""]}>
                {Object.entries(open.Data ?? {}).map(([key, v]) => {
                  const value = v?.Value ?? "";
                  const permission = v?.Permission ?? "Private";
                  const lastUpdated = v?.LastUpdated ?? 0;
                  return (
                    <Tr key={key}>
                      <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{key}</span></Td>
                      <Td>
                        <span className="font-mono text-xs break-all" style={{ color: "rgb(var(--color-surface-900))" }}>
                          {value.length > 200 ? value.slice(0, 200) + "…" : (value || <em style={{ color: "rgb(var(--color-surface-600))" }}>(empty)</em>)}
                        </span>
                      </Td>
                      <Td><Badge label={permission} color="surface" /></Td>
                      <Td>{lastUpdated ? new Date(lastUpdated * 1000).toLocaleString() : "—"}</Td>
                      <Td>
                        <div className="flex gap-1">
                          <Btn variant="ghost"  onClick={() => editKey(key, value, permission)} disabled={busy}>Edit</Btn>
                          <Btn variant="danger" onClick={() => removeKey(key)} disabled={busy}>×</Btn>
                        </div>
                      </Td>
                    </Tr>
                  );
                })}
                {Object.keys(open.Data ?? {}).length === 0 &&
                  <Tr><Td><span style={{ color: "rgb(var(--color-surface-600))" }}>No keys</span></Td><Td>—</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
              </Table>

              <div className="flex gap-2 mt-2 items-end flex-wrap">
                <Input label="New key"   value={newKey}   onChange={(e) => setNewKey(e.target.value)}   className="w-40" />
                <Input label="Value"     value={newValue} onChange={(e) => setNewValue(e.target.value)} className="flex-1 min-w-[200px]" />
                <div className="flex flex-col gap-1">
                  <label className="text-xs font-medium" style={{ color: "rgb(var(--color-surface-700))" }}>Permission</label>
                  <select
                    value={newPermission}
                    onChange={(e) => setNewPermission(e.target.value)}
                    className="input"
                  >
                    <option value="Private">Private</option>
                    <option value="Public">Public</option>
                  </select>
                </div>
                <Btn variant="primary" onClick={setKey} disabled={busy || !newKey}>Set key</Btn>
              </div>
            </section>

            <div className="flex justify-end">
              <Btn variant="ghost" onClick={() => setOpen(null)}>Close</Btn>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
}
