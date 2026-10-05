"use client";
import { useEffect, useState } from "react";
import { getDeployments, createDeployment, getTitles, getEnvironments, type Deployment, type Title, type Environment } from "@/lib/api";
import { Card, CardHeader, Table, Tr, Td, Btn, Input, Modal, Select, Badge } from "@/components/Card";
import { Plus, RefreshCw } from "lucide-react";

export default function DeploymentsPage() {
  const [deployments, setDeployments] = useState<Deployment[]>([]);
  const [titles, setTitles] = useState<Title[]>([]);
  const [envs, setEnvs] = useState<Environment[]>([]);
  const [loading, setLoading] = useState(true);
  const [modal, setModal] = useState(false);
  const [form, setForm] = useState({ title_id: "", env_id: "", name: "" });
  const [saving, setSaving] = useState(false);

  const load = () => {
    setLoading(true);
    Promise.all([
      getDeployments().then(r => setDeployments(r.Results ?? [])),
      getTitles().then(r => setTitles(r.Results ?? [])),
      getEnvironments().then(r => setEnvs(r.Results ?? [])),
    ]).finally(() => setLoading(false));
  };

  useEffect(load, []);

  const save = async () => {
    if (!form.name.trim()) return;
    setSaving(true);
    try {
      await createDeployment(form);
      setModal(false);
      setForm({ title_id: "", env_id: "", name: "" });
      load();
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader title={`Deployments (${deployments.length})`} action={
          <div className="flex gap-2">
            <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
            <Btn onClick={() => setModal(true)}><Plus size={14} className="mr-1 inline" />New Deployment</Btn>
          </div>
        } />

        {loading ? (
          <p className="text-surface-400 text-sm py-4 text-center">Loading…</p>
        ) : (
          <Table headers={["Deployment ID", "Name", "Title ID", "Env ID", "Tags"]}>
            {deployments.map(d => (
              <Tr key={d.deployment_id}>
                <Td mono>{d.deployment_id.slice(0, 12)}…</Td>
                <Td>{d.name || "–"}</Td>
                <Td mono>{d.title_id || "–"}</Td>
                <Td mono>{d.env_id || "–"}</Td>
                <Td>
                  <div className="flex flex-wrap gap-1">
                    {(d.requiredTags ?? []).map(t => <Badge key={t} label={t} color="secondary" />)}
                    {!d.requiredTags?.length && <span className="text-surface-500 text-xs">none</span>}
                  </div>
                </Td>
              </Tr>
            ))}
            {deployments.length === 0 && (
              <Tr><Td><span className="text-surface-400">No deployments yet</span></Td>
                <Td>–</Td><Td>–</Td><Td>–</Td><Td>–</Td>
              </Tr>
            )}
          </Table>
        )}
      </Card>

      <Modal open={modal} onClose={() => setModal(false)} title="New Deployment">
        <div className="space-y-3">
          <Input label="Deployment Name *" placeholder="Server Group A" value={form.name}
            onChange={e => setForm(f => ({ ...f, name: e.target.value }))} />
          <Select label="Title" value={form.title_id} onChange={e => setForm(f => ({ ...f, title_id: e.target.value }))}>
            <option value="">— Select title —</option>
            {titles.map(t => <option key={t.title_id} value={t.title_id}>{t.title_name}</option>)}
          </Select>
          <Select label="Environment" value={form.env_id} onChange={e => setForm(f => ({ ...f, env_id: e.target.value }))}>
            <option value="">— Select environment —</option>
            {envs.map(e => <option key={e.envId} value={e.envId}>{e.envName}</option>)}
          </Select>
          <div className="flex justify-end gap-2 pt-2">
            <Btn variant="ghost" onClick={() => setModal(false)}>Cancel</Btn>
            <Btn onClick={save} disabled={saving}>{saving ? "Saving…" : "Create"}</Btn>
          </div>
        </div>
      </Modal>
    </div>
  );
}
