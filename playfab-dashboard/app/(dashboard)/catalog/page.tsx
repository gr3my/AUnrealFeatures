"use client";
import { useEffect, useRef, useState } from "react";
import { Card, CardHeader, Table, Tr, Td, Badge, Btn, Input, Textarea, Modal } from "@/components/Card";
import { getCatalogItems, addCatalogItems, deleteCatalogItem, CatalogItem } from "@/lib/api";
import { Upload } from "lucide-react";

export default function CatalogPage() {
  const [items, setItems] = useState<CatalogItem[]>([]);
  const [versions, setVersions] = useState<Record<string, number>>({});
  const [versionFilter, setVersionFilter] = useState<string>("");
  const [editing, setEditing] = useState<CatalogItem | null>(null);
  const [json, setJson] = useState("");
  const [err, setErr] = useState("");
  const [importStatus, setImportStatus] = useState("");
  const fileRef = useRef<HTMLInputElement>(null);

  const refresh = () => getCatalogItems().then((r: any) => {
    const arr = (r.Catalog ?? []) as CatalogItem[];
    setItems(arr);
    setVersions(r.Versions ?? {});
  }).catch((e) => setErr(e.message));
  useEffect(() => { refresh(); }, []);

  const startEdit = (i: CatalogItem | null) => {
    setEditing(i ?? { ItemId: "", ItemClass: "", DisplayName: "", Description: "" });
    setJson(JSON.stringify(i ?? {
      ItemId: "", ItemClass: "", DisplayName: "", Description: "",
      VirtualCurrencyPrices: { GD: 100 }, IsStackable: true,
    }, null, 2));
  };

  const save = async () => {
    try {
      const item: CatalogItem = JSON.parse(json);
      if (!item.ItemId) throw new Error("ItemId required");
      await addCatalogItems([item]);
      setEditing(null);
      refresh();
    } catch (e: any) {
      alert(e.message ?? String(e));
    }
  };

  const remove = async (id: string) => {
    if (!confirm(`Delete catalog item '${id}'?`)) return;
    await deleteCatalogItem(id);
    refresh();
  };

  const importFile = async (file: File) => {
    setImportStatus(`Reading ${file.name}…`);
    setErr("");
    try {
      const text = await file.text();
      const parsed = JSON.parse(text);
      if (!parsed.Catalog || !Array.isArray(parsed.Catalog))
        throw new Error("File must have a top-level 'Catalog' array (PlayFab export format)");

      // POST the raw JSON straight through — server accepts the full PlayFab shape
      setImportStatus(`Uploading ${parsed.Catalog.length} items (catalog "${parsed.CatalogVersion ?? "main"}")…`);
      const res = await fetch(`/pf/Admin/AddCatalogItems`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Dashboard-Auth": "admin",
          "ngrok-skip-browser-warning": "true",
        },
        body: text,
      });
      const respText = await res.text();
      if (!res.ok) throw new Error(respText || res.statusText);
      const respJson = JSON.parse(respText);
      const r = respJson.data ?? respJson;
      setImportStatus(`✓ Imported ${r.Added} item(s) into "${r.CatalogVersion}"${r.Skipped ? `, skipped ${r.Skipped}` : ""}`);
      refresh();
    } catch (e: any) {
      setErr(`Import failed: ${e.message ?? String(e)}`);
      setImportStatus("");
    } finally {
      if (fileRef.current) fileRef.current.value = "";
    }
  };

  const filtered = versionFilter ? items.filter((i: any) => i.CatalogVersion === versionFilter) : items;

  return (
    <div className="space-y-4">
      {err && <div className="rounded p-3 text-sm" style={{ background: "rgb(var(--color-error-500) / 0.1)" }}>{err}</div>}
      {importStatus && (
        <div className="rounded p-3 text-sm font-mono" style={{ background: "rgb(var(--color-primary-500) / 0.1)", color: "rgb(var(--color-primary-300))" }}>
          {importStatus}
        </div>
      )}

      <Card>
        <CardHeader title="Catalog v1 items" action={
          <div className="flex items-center gap-2">
            <input
              ref={fileRef}
              type="file"
              accept=".json,application/json"
              style={{ display: "none" }}
              onChange={(e) => { const f = e.target.files?.[0]; if (f) importFile(f); }}
            />
            <Btn variant="ghost" onClick={() => fileRef.current?.click()}>
              <Upload size={14} style={{ display: "inline", marginRight: 6 }} />
              Import JSON
            </Btn>
            <Btn variant="primary" onClick={() => startEdit(null)}>+ New item</Btn>
          </div>
        } />

        {Object.keys(versions).length > 0 && (
          <div className="flex items-center gap-2 mb-3 flex-wrap">
            <span className="text-xs" style={{ color: "rgb(var(--color-surface-700))" }}>Catalog version:</span>
            <button
              onClick={() => setVersionFilter("")}
              className="px-2 py-0.5 text-xs rounded"
              style={!versionFilter
                ? { background: "rgb(var(--color-primary-500) / 0.15)", color: "rgb(var(--color-primary-500))", border: "1px solid rgb(var(--color-primary-500) / 0.3)" }
                : { background: "rgb(var(--color-surface-100))", color: "rgb(var(--color-surface-700))" }}
            >
              All ({items.length})
            </button>
            {Object.entries(versions).map(([v, c]) => (
              <button
                key={v}
                onClick={() => setVersionFilter(v)}
                className="px-2 py-0.5 text-xs rounded font-mono"
                style={versionFilter === v
                  ? { background: "rgb(var(--color-primary-500) / 0.15)", color: "rgb(var(--color-primary-500))", border: "1px solid rgb(var(--color-primary-500) / 0.3)" }
                  : { background: "rgb(var(--color-surface-100))", color: "rgb(var(--color-surface-700))" }}
              >
                {v} ({c})
              </button>
            ))}
          </div>
        )}
        <Table headers={["ItemId", "Class", "Display", "Prices", "Flags", ""]}>
          {filtered.map((i) => (
            <Tr key={i.ItemId}>
              <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{i.ItemId}</span></Td>
              <Td>{i.ItemClass || "—"}</Td>
              <Td>{i.DisplayName || "—"}</Td>
              <Td>
                <div className="flex flex-wrap gap-1">
                  {Object.entries(i.VirtualCurrencyPrices ?? {}).map(([c, p]) => <Badge key={c} label={`${c}: ${p}`} color="primary" />)}
                  {Object.entries(i.RealCurrencyPrices ?? {}).map(([c, p]) => <Badge key={c} label={`${c}: ${p}`} color="secondary" />)}
                </div>
              </Td>
              <Td>
                <div className="flex flex-wrap gap-1">
                  {i.IsStackable && <Badge label="stack" color="surface" />}
                  {i.IsTradable && <Badge label="trade" color="surface" />}
                  {i.IsLimitedEdition && <Badge label="LE" color="warning" />}
                </div>
              </Td>
              <Td>
                <div className="flex gap-2">
                  <Btn variant="ghost" onClick={() => startEdit(i)}>Edit</Btn>
                  <Btn variant="danger" onClick={() => remove(i.ItemId)}>Delete</Btn>
                </div>
              </Td>
            </Tr>
          ))}
          {filtered.length === 0 && <Tr><Td>—</Td><Td><span style={{ color: "rgb(var(--color-surface-600))" }}>No catalog items{versionFilter ? ` in version "${versionFilter}"` : ""} — try "Import JSON" with a PlayFab catalog export</span></Td><Td>—</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
        </Table>
      </Card>

      <Modal open={editing != null} onClose={() => setEditing(null)} title={editing?.ItemId ? `Edit ${editing.ItemId}` : "New catalog item"}>
        {editing && (
          <div className="space-y-3">
            <Textarea label="Item JSON" rows={18} value={json} onChange={(e) => setJson(e.target.value)} />
            <div className="flex justify-end gap-2">
              <Btn variant="ghost" onClick={() => setEditing(null)}>Cancel</Btn>
              <Btn variant="primary" onClick={save}>Save</Btn>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
}
