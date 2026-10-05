"use client";
import { useEffect, useState } from "react";
import { Card, CardHeader, Table, Tr, Td, Badge, Btn, Input, Textarea, Modal } from "@/components/Card";
import { getTitleData, setTitleData, setTitleInternalData } from "@/lib/api";

type Scope = "Title" | "TitleInternal";

export default function TitleDataPage() {
  const [data, setData] = useState<{ Title: Record<string,string>; TitleInternal: Record<string,string> }>({ Title: {}, TitleInternal: {} });
  const [scope, setScope] = useState<Scope>("Title");
  const [editing, setEditing] = useState<{ key: string; value: string } | null>(null);
  const [err, setErr] = useState("");

  const refresh = () => getTitleData().then(setData).catch((e) => setErr(e.message));
  useEffect(() => { refresh(); }, []);

  const save = async () => {
    if (!editing) return;
    const kv = { [editing.key]: editing.value };
    if (scope === "Title")          await setTitleData(kv);
    else                            await setTitleInternalData(kv);
    setEditing(null);
    refresh();
  };

  const tabClass = (t: Scope) =>
    `px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
      scope === t ? "border-primary-500 text-primary-500" : "border-transparent text-surface-400 hover:text-white"
    }`;

  const rows = Object.entries(data[scope] || {});

  return (
    <div className="space-y-4">
      {err && <div className="rounded p-3 text-sm" style={{ background: "rgb(var(--color-error-500) / 0.1)" }}>{err}</div>}

      <div className="flex gap-1" style={{ borderBottom: "1px solid rgb(var(--color-surface-600))" }}>
        <button className={tabClass("Title")}         onClick={() => setScope("Title")}>Title ({Object.keys(data.Title).length})</button>
        <button className={tabClass("TitleInternal")} onClick={() => setScope("TitleInternal")}>TitleInternal ({Object.keys(data.TitleInternal).length})</button>
      </div>

      <Card>
        <CardHeader title={scope === "Title" ? "Public title data" : "Internal title data (server-only)"} action={
          <Btn variant="primary" onClick={() => setEditing({ key: "", value: "" })}>+ New key</Btn>
        } />
        <Table headers={["Key", "Value", ""]}>
          {rows.map(([k, v]) => (
            <Tr key={k}>
              <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{k}</span></Td>
              <Td><span className="font-mono text-xs">{v.length > 80 ? v.substring(0, 80) + "…" : v}</span></Td>
              <Td><Btn variant="ghost" onClick={() => setEditing({ key: k, value: v })}>Edit</Btn></Td>
            </Tr>
          ))}
          {rows.length === 0 && <Tr><Td>—</Td><Td><span style={{ color: "rgb(var(--color-surface-600))" }}>Nothing set in this scope</span></Td><Td>—</Td></Tr>}
        </Table>
      </Card>

      <Modal open={editing != null} onClose={() => setEditing(null)} title={editing?.key ? `Edit "${editing.key}"` : "New key"}>
        {editing && (
          <div className="space-y-3">
            <Input label="Key" value={editing.key} onChange={(e) => setEditing({ ...editing, key: e.target.value })} disabled={!!editing.key && data[scope][editing.key] != null} />
            <Textarea label="Value" rows={6} value={editing.value} onChange={(e) => setEditing({ ...editing, value: e.target.value })} />
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
