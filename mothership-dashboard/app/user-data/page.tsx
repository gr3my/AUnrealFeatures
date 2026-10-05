"use client";
import { useState } from "react";
import { getUserData, setUserData, deleteUserData, type UserData } from "@/lib/api";
import { Card, CardHeader, Table, Tr, Td, Btn, Input, Modal, Textarea } from "@/components/Card";
import { Plus, RefreshCw, Trash2, Search } from "lucide-react";

export default function UserDataPage() {
  const [userId, setUserId] = useState("");
  const [data, setData] = useState<UserData[]>([]);
  const [loading, setLoading] = useState(false);
  const [fetched, setFetched] = useState(false);
  const [modal, setModal] = useState(false);
  const [form, setForm] = useState({ key_name: "", value: "" });
  const [saving, setSaving] = useState(false);

  const load = async () => {
    if (!userId.trim()) return;
    setLoading(true);
    setFetched(true);
    getUserData(userId.trim())
      .then(r => setData(r.Results ?? []))
      .catch(() => setData([]))
      .finally(() => setLoading(false));
  };

  const save = async () => {
    if (!form.key_name.trim()) return;
    setSaving(true);
    try {
      await setUserData(userId, form.key_name, form.value);
      setModal(false);
      setForm({ key_name: "", value: "" });
      load();
    } finally { setSaving(false); }
  };

  const remove = async (key_name: string) => {
    if (!confirm(`Delete user data key "${key_name}"?`)) return;
    await deleteUserData(userId, key_name);
    load();
  };

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader title="User Data Lookup" />
        <div className="flex gap-2">
          <Input className="flex-1" placeholder="Player / User ID (UUID)…" value={userId}
            onChange={e => setUserId(e.target.value)}
            onKeyDown={e => e.key === "Enter" && load()} />
          <Btn onClick={load} disabled={!userId.trim()}><Search size={14} className="mr-1 inline" />Lookup</Btn>
        </div>
      </Card>

      {fetched && (
        <Card>
          <CardHeader title={`User Data${userId ? ` — ${userId.slice(0, 16)}…` : ""} (${data.length})`} action={
            <div className="flex gap-2">
              <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
              <Btn onClick={() => { setForm({ key_name: "", value: "" }); setModal(true); }} disabled={!userId.trim()}>
                <Plus size={14} className="mr-1 inline" />New Key
              </Btn>
            </div>
          } />

          {loading ? (
            <p className="text-surface-400 text-sm py-4 text-center">Loading…</p>
          ) : (
            <Table headers={["Key", "Value", "Generation", ""]}>
              {data.map(d => (
                <Tr key={d.key_name}>
                  <Td mono>{d.key_name}</Td>
                  <Td>
                    <span className="font-mono text-xs break-all">
                      {d.value.length > 80 ? d.value.slice(0, 80) + "…" : d.value}
                    </span>
                  </Td>
                  <Td><span className="text-xs text-surface-400">v{d.generation}</span></Td>
                  <Td>
                    <Btn variant="danger" onClick={() => remove(d.key_name)}><Trash2 size={12} /></Btn>
                  </Td>
                </Tr>
              ))}
              {data.length === 0 && <Tr><Td><span className="text-surface-400">No user data for this player</span></Td><Td>–</Td><Td>–</Td><Td>–</Td></Tr>}
            </Table>
          )}
        </Card>
      )}

      <Modal open={modal} onClose={() => setModal(false)} title="Set User Data">
        <div className="space-y-3">
          <Input label="Key *" placeholder="MyKey" value={form.key_name}
            onChange={e => setForm(f => ({ ...f, key_name: e.target.value }))} />
          <Textarea label="Value" rows={4} placeholder="value…" value={form.value}
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
