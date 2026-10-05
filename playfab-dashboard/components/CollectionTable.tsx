"use client";
import { useEffect, useState } from "react";
import { Card, Btn, Input, Badge, Textarea, Table, Tr, Td, Modal } from "@/components/Card";
import { PageChrome } from "@/components/PageChrome";
import { listCollection, upsertCollection, deleteCollectionRow } from "@/lib/api";

export interface ColumnSpec<T = any> {
  label: string;
  key?: keyof T & string;
  width?: number | string;
  render?: (row: T) => React.ReactNode;
  mono?: boolean;
}

/**
 * Standard list-table page for the secondary nav routes. Wraps /Admin/ListCollection +
 * /Admin/UpsertCollection + /Admin/DeleteCollectionRow so every page gets full CRUD.
 *
 * - `name` is the collection name on the server side.
 * - `keyField` is the property used to identify a row for delete/upsert (e.g. "AddonName").
 *   When omitted, edits are still possible but the server falls back to the default key per
 *   collection (the first sensible field — e.g. PlayfabAddonConfigDbObject.AddonName).
 * - `template` is the JSON shown when "Add row" is clicked (a sensible blank record).
 */
export function CollectionPage<T extends Record<string, any> = any>({
  title, subtitle, name, columns, emptyMessage, headerActions, intro,
  keyField, template, readOnly,
}: {
  title:    string;
  subtitle?: string;
  name:     string;
  columns:  ColumnSpec<T>[];
  emptyMessage?: string;
  headerActions?: React.ReactNode;
  intro?:   React.ReactNode;
  keyField?: string;
  template?: T | (() => T);
  readOnly?: boolean;
}) {
  const take = 50;
  const [rows, setRows]   = useState<T[]>([]);
  const [total, setTotal] = useState(0);
  const [skip, setSkip]   = useState(0);
  const [query, setQuery] = useState("");
  const [err, setErr]     = useState("");
  const [busy, setBusy]   = useState(false);
  const [editing, setEditing] = useState<{ original: T | null; json: string } | null>(null);
  const [editErr, setEditErr] = useState("");

  const refresh = async () => {
    setBusy(true);
    try {
      const r = await listCollection<T>(name, skip, take, query);
      setRows(r.Rows ?? []);
      setTotal(r.Total ?? 0);
      setErr("");
    } catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };

  useEffect(() => { refresh(); }, [name, skip]);

  const openAdd = () => {
    const t = typeof template === "function" ? (template as () => T)() : template;
    setEditErr("");
    setEditing({ original: null, json: JSON.stringify(t ?? {}, null, 2) });
  };
  const openEdit = (row: T) => {
    setEditErr("");
    // Strip LiteDB Id from the editable JSON so the dashboard user doesn't have to think about it
    const { Id, ...rest } = row as any;
    setEditing({ original: row, json: JSON.stringify(rest, null, 2) });
  };

  const save = async () => {
    if (!editing) return;
    let parsed: any;
    try { parsed = JSON.parse(editing.json); }
    catch (e: any) { setEditErr(`JSON parse failed: ${e.message}`); return; }
    try {
      await upsertCollection(name, parsed, keyField ?? "");
      setEditing(null);
      await refresh();
    } catch (e: any) { setEditErr(e.message ?? String(e)); }
  };

  const remove = async (row: T) => {
    const kf = keyField ?? Object.keys(row)[0];
    const kv = (row as any)[kf];
    if (!kv) { alert(`Cannot delete: row has no '${kf}' value`); return; }
    if (!confirm(`Delete row where ${kf}="${kv}"?`)) return;
    setBusy(true);
    try { await deleteCollectionRow(name, kf, String(kv)); await refresh(); }
    catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };

  const filtered = rows.filter(r => !query || JSON.stringify(r).toLowerCase().includes(query.toLowerCase()));
  const allColumns = readOnly ? columns : [...columns, { label: "", render: (r: T) => (
    <div className="flex gap-1">
      <Btn variant="ghost"  onClick={() => openEdit(r)}>Edit</Btn>
      <Btn variant="danger" onClick={() => remove(r)}>Delete</Btn>
    </div>
  )} as ColumnSpec<T>];

  return (
    <PageChrome
      title={title}
      subtitle={subtitle}
      actions={
        <div className="flex gap-2">
          {headerActions}
          {!readOnly && <Btn variant="primary" onClick={openAdd}>+ Add row</Btn>}
        </div>
      }
    >
      {intro && <div className="pf-message-bar" style={{ marginBottom: 16 }}>{intro}</div>}

      {err && (
        <div style={{ background: "#fde8e8", border: "1px solid rgb(var(--color-error-400))", color: "rgb(var(--color-error-700))", padding: 10, borderRadius: 2, marginBottom: 12, fontSize: 13 }}>
          {err}
        </div>
      )}

      <Card>
        <div className="flex items-center gap-3 mb-3">
          <Input
            placeholder="Filter…"
            value={query}
            onChange={(e) => { setSkip(0); setQuery(e.target.value); }}
            onKeyDown={(e) => { if (e.key === "Enter") refresh(); }}
            className="flex-1"
          />
          <Btn variant="secondary" onClick={refresh} disabled={busy}>{busy ? "Loading…" : "Refresh"}</Btn>
          <Badge label={`${total} rows`} color="primary" />
        </div>

        <Table headers={allColumns.map(c => c.label)}>
          {filtered.map((r, i) => (
            <Tr key={i}>
              {allColumns.map((c, j) => {
                const value = c.render ? c.render(r) : (c.key ? (r as any)[c.key] : "");
                return (
                  <Td key={j} mono={c.mono}>
                    {value == null || value === "" ? <span style={{ color: "rgb(var(--color-surface-700))" }}>—</span> : value}
                  </Td>
                );
              })}
            </Tr>
          ))}
          {filtered.length === 0 && (
            <Tr>
              <Td>
                <span style={{ color: "rgb(var(--color-surface-700))" }}>{emptyMessage ?? "No rows"}</span>
              </Td>
              {Array.from({ length: allColumns.length - 1 }).map((_, i) => <Td key={i}>—</Td>)}
            </Tr>
          )}
        </Table>

        <div className="flex justify-between items-center mt-3">
          <Btn variant="ghost" onClick={() => setSkip(Math.max(0, skip - take))} disabled={skip === 0}>← Prev</Btn>
          <span className="text-xs" style={{ color: "rgb(var(--color-surface-700))" }}>
            {total === 0 ? "0 of 0" : `${skip + 1}–${Math.min(skip + take, total)} of ${total}`}
          </span>
          <Btn variant="ghost" onClick={() => setSkip(skip + take)} disabled={skip + take >= total}>Next →</Btn>
        </div>
      </Card>

      <Modal
        open={editing != null}
        onClose={() => setEditing(null)}
        title={editing?.original ? `Edit row` : `Add row to ${name}`}
      >
        {editing && (
          <div className="space-y-3">
            <p style={{ fontSize: 12, color: "rgb(var(--color-surface-700))", margin: 0 }}>
              Edit the JSON below. The row is matched on <code>{keyField || "the default key"}</code>;
              an existing match is updated, otherwise a new row is inserted.
            </p>
            {editErr && (
              <div style={{ background: "#fde8e8", border: "1px solid rgb(var(--color-error-400))", color: "rgb(var(--color-error-700))", padding: 8, borderRadius: 2, fontSize: 13 }}>
                {editErr}
              </div>
            )}
            <Textarea
              value={editing.json}
              onChange={(e) => setEditing({ ...editing, json: e.target.value })}
              rows={20}
              style={{ minHeight: 360, fontFamily: "var(--font-mono)", fontSize: 12 }}
            />
            <div className="flex justify-end gap-2">
              <Btn variant="ghost" onClick={() => setEditing(null)}>Cancel</Btn>
              <Btn variant="primary" onClick={save}>Save</Btn>
            </div>
          </div>
        )}
      </Modal>
    </PageChrome>
  );
}
