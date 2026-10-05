"use client";
import { useEffect, useState } from "react";
import { getEnvironments, createEnvironment, getTitles, type Environment, type Title } from "@/lib/api";
import { Card, CardHeader, Table, Tr, Td, Btn, Input, Modal, Select, Badge } from "@/components/Card";
import { Plus, RefreshCw } from "lucide-react";

export default function EnvironmentsPage() {
  const [envs, setEnvs] = useState<Environment[]>([]);
  const [titles, setTitles] = useState<Title[]>([]);
  const [loading, setLoading] = useState(true);
  const [modal, setModal] = useState(false);
  const [form, setForm] = useState({ envId: "", titleId: "", envName: "" });
  const [saving, setSaving] = useState(false);

  const load = () => {
    setLoading(true);
    Promise.all([
      getEnvironments().then(r => setEnvs(r.Results ?? [])),
      getTitles().then(r => setTitles(r.Results ?? [])),
    ]).finally(() => setLoading(false));
  };

  useEffect(load, []);

  const save = async () => {
    if (!form.envName.trim()) return;
    setSaving(true);
    try {
      await createEnvironment(form.envId || crypto.randomUUID(), form.titleId, form.envName);
      setModal(false);
      setForm({ envId: "", titleId: "", envName: "" });
      load();
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader title={`Environments (${envs.length})`} action={
          <div className="flex gap-2">
            <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
            <Btn onClick={() => setModal(true)}><Plus size={14} className="mr-1 inline" />New Environment</Btn>
          </div>
        } />

        {loading ? (
          <p className="text-surface-400 text-sm py-4 text-center">Loading…</p>
        ) : (
          <Table headers={["Env ID", "Name", "Title ID", "Tags"]}>
            {envs.map(e => (
              <Tr key={e.envId}>
                <Td mono>{e.envId}</Td>
                <Td>{e.envName}</Td>
                <Td mono>{e.titleId || "–"}</Td>
                <Td>
                  <div className="flex flex-wrap gap-1">
                    {(e.requiredTags ?? []).map(t => <Badge key={t} label={t} color="secondary" />)}
                    {!e.requiredTags?.length && <span className="text-surface-500 text-xs">none</span>}
                  </div>
                </Td>
              </Tr>
            ))}
            {envs.length === 0 && (
              <Tr><Td><span className="text-surface-400">No environments yet</span></Td><Td>–</Td><Td>–</Td><Td>–</Td></Tr>
            )}
          </Table>
        )}
      </Card>

      <Modal open={modal} onClose={() => setModal(false)} title="New Environment">
        <div className="space-y-3">
          <Input label="Environment Name *" placeholder="Production" value={form.envName}
            onChange={e => setForm(f => ({ ...f, envName: e.target.value }))} />
          <Select label="Title" value={form.titleId} onChange={e => setForm(f => ({ ...f, titleId: e.target.value }))}>
            <option value="">— Select title —</option>
            {titles.map(t => <option key={t.title_id} value={t.title_id}>{t.title_name}</option>)}
          </Select>
          <Input label="Env ID (optional)" placeholder="auto-generated" value={form.envId}
            onChange={e => setForm(f => ({ ...f, envId: e.target.value }))} />
          <div className="flex justify-end gap-2 pt-2">
            <Btn variant="ghost" onClick={() => setModal(false)}>Cancel</Btn>
            <Btn onClick={save} disabled={saving}>{saving ? "Saving…" : "Create"}</Btn>
          </div>
        </div>
      </Modal>
    </div>
  );
}
