"use client";
import { useEffect, useState } from "react";
import { getTitles, createTitle, type Title } from "@/lib/api";
import { Card, CardHeader, Table, Tr, Td, Btn, Input, Modal } from "@/components/Card";
import { Plus, RefreshCw } from "lucide-react";

export default function TitlesPage() {
  const [titles, setTitles] = useState<Title[]>([]);
  const [loading, setLoading] = useState(true);
  const [modal, setModal] = useState(false);
  const [form, setForm] = useState({ title_id: "", title_name: "" });
  const [saving, setSaving] = useState(false);

  const load = () => {
    setLoading(true);
    getTitles()
      .then(r => setTitles(r.Results ?? []))
      .finally(() => setLoading(false));
  };

  useEffect(load, []);

  const save = async () => {
    if (!form.title_name.trim()) return;
    setSaving(true);
    try {
      await createTitle(form.title_id || crypto.randomUUID(), form.title_name);
      setModal(false);
      setForm({ title_id: "", title_name: "" });
      load();
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader title={`Titles (${titles.length})`} action={
          <div className="flex gap-2">
            <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
            <Btn onClick={() => setModal(true)}><Plus size={14} className="mr-1 inline" />New Title</Btn>
          </div>
        } />

        {loading ? (
          <p className="text-surface-400 text-sm py-4 text-center">Loading…</p>
        ) : (
          <Table headers={["Title ID", "Name"]}>
            {titles.map(t => (
              <Tr key={t.title_id}>
                <Td mono>{t.title_id}</Td>
                <Td>{t.title_name}</Td>
              </Tr>
            ))}
            {titles.length === 0 && (
              <Tr><Td><span className="text-surface-400">No titles created yet</span></Td><Td>–</Td></Tr>
            )}
          </Table>
        )}
      </Card>

      <Modal open={modal} onClose={() => setModal(false)} title="New Title">
        <div className="space-y-3">
          <Input label="Title Name *" placeholder="My Game" value={form.title_name}
            onChange={e => setForm(f => ({ ...f, title_name: e.target.value }))} />
          <Input label="Title ID (optional — auto-generated if blank)" placeholder="my-game-id"
            value={form.title_id} onChange={e => setForm(f => ({ ...f, title_id: e.target.value }))} />
          <div className="flex justify-end gap-2 pt-2">
            <Btn variant="ghost" onClick={() => setModal(false)}>Cancel</Btn>
            <Btn onClick={save} disabled={saving}>{saving ? "Saving…" : "Create"}</Btn>
          </div>
        </div>
      </Modal>
    </div>
  );
}
