"use client";
import { useEffect, useState } from "react";
import { getTitleData, setTitleData, deleteTitleData, type TitleData } from "@/lib/api";
import { Card, CardHeader, Table, Tr, Td, Btn, Input, Modal, Textarea } from "@/components/Card";
import { Plus, RefreshCw, Trash2, Pencil } from "lucide-react";

export default function TitleDataPage() {
  const [data, setData] = useState<TitleData[]>([]);
  const [loading, setLoading] = useState(true);
  const [modal, setModal] = useState(false);
  const [form, setForm] = useState({ key: "", value: "" });
  const [saving, setSaving] = useState(false);

  const load = () => {
    setLoading(true);
    getTitleData()
      .then(r => setData(r.Results ?? []))
      .finally(() => setLoading(false));
  };

  useEffect(load, []);

  const openEdit = (item?: TitleData) => {
    setForm(item ? { key: item.key, value: item.value } : { key: "", value: "" });
    setModal(true);
  };

  const save = async () => {
    if (!form.key.trim()) return;
    setSaving(true);
    try {
      await setTitleData(form.key, form.value);
      setModal(false);
      load();
    } finally { setSaving(false); }
  };

  const remove = async (key: string) => {
    if (!confirm(`Delete title data key "${key}"?`)) return;
    await deleteTitleData(key);
    load();
  };

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader title={`Title Data (${data.length})`} action={
          <div className="flex gap-2">
            <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
            <Btn onClick={() => openEdit()}><Plus size={14} className="mr-1 inline" />New Key</Btn>
          </div>
        } />

        {loading ? (
          <p className="text-surface-400 text-sm py-4 text-center">Loading…</p>
        ) : (
          <Table headers={["Key", "Value", "Updated", ""]}>
            {data.map(d => (
              <Tr key={d.key}>
                <Td mono>{d.key}</Td>
                <Td>
                  <span className="font-mono text-xs text-surface-200 break-all">
                    {d.value.length > 80 ? d.value.slice(0, 80) + "…" : d.value}
                  </span>
                </Td>
                <Td><span className="text-xs text-surface-400">{d.updated_at ? new Date(d.updated_at).toLocaleString() : "–"}</span></Td>
                <Td>
                  <div className="flex gap-1">
                    <Btn variant="ghost" onClick={() => openEdit(d)}><Pencil size={12} /></Btn>
                    <Btn variant="danger" onClick={() => remove(d.key)}><Trash2 size={12} /></Btn>
                  </div>
                </Td>
              </Tr>
            ))}
            {data.length === 0 && <Tr><Td><span className="text-surface-400">No title data</span></Td><Td>–</Td><Td>–</Td><Td>–</Td></Tr>}
          </Table>
        )}
      </Card>

      <Modal open={modal} onClose={() => setModal(false)} title={form.key ? "Edit Title Data" : "New Title Data Key"}>
        <div className="space-y-3">
          <Input label="Key *" placeholder="MyKey" value={form.key}
            onChange={e => setForm(f => ({ ...f, key: e.target.value }))}
            disabled={!!(data.find(d => d.key === form.key) && form.key)} />
          <Textarea label="Value" rows={5} placeholder="JSON or plain string…" value={form.value}
            onChange={e => setForm(f => ({ ...f, value: e.target.value }))} />
          <div className="flex justify-end gap-2 pt-2">
            <Btn variant="ghost" onClick={() => setModal(false)}>Cancel</Btn>
            <Btn onClick={save} disabled={saving}>{saving ? "Saving…" : "Save"}</Btn>
          </div>
        </div>
      </Modal>
    </div>
  );
}
