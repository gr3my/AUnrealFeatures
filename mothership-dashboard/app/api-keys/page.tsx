"use client";
import { useEffect, useState } from "react";
import { getApiKeys, createApiKey, type ApiKey } from "@/lib/api";
import { Card, CardHeader, Table, Tr, Td, Btn, Input, Modal, Badge } from "@/components/Card";
import { Plus, RefreshCw, Copy, Eye, EyeOff } from "lucide-react";

export default function ApiKeysPage() {
  const [keys, setKeys] = useState<ApiKey[]>([]);
  const [loading, setLoading] = useState(true);
  const [modal, setModal] = useState(false);
  const [name, setName] = useState("");
  const [saving, setSaving] = useState(false);
  const [revealed, setRevealed] = useState<Set<string>>(new Set());
  const [copied, setCopied] = useState<string | null>(null);

  const load = () => {
    setLoading(true);
    getApiKeys()
      .then(r => setKeys(r.Results ?? []))
      .finally(() => setLoading(false));
  };

  useEffect(load, []);

  const save = async () => {
    if (!name.trim()) return;
    setSaving(true);
    try {
      await createApiKey(name);
      setModal(false);
      setName("");
      load();
    } finally { setSaving(false); }
  };

  const copy = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopied(id);
    setTimeout(() => setCopied(null), 2000);
  };

  const toggleReveal = (id: string) => {
    setRevealed(s => {
      const n = new Set(s);
      n.has(id) ? n.delete(id) : n.add(id);
      return n;
    });
  };

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader title={`API Keys (${keys.length})`} action={
          <div className="flex gap-2">
            <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
            <Btn onClick={() => setModal(true)}><Plus size={14} className="mr-1 inline" />New Key</Btn>
          </div>
        } />

        {loading ? (
          <p className="text-surface-400 text-sm py-4 text-center">Loading…</p>
        ) : (
          <Table headers={["Key ID", "Name", "API Key", "Created", ""]}>
            {keys.map(k => (
              <Tr key={k.key_id}>
                <Td mono>{k.key_id.slice(0, 12)}…</Td>
                <Td>{k.name || <span className="text-surface-400 italic">unnamed</span>}</Td>
                <Td>
                  <span className="font-mono text-xs">
                    {revealed.has(k.key_id) ? k.api_key : "••••••••••••••••••••••••••••••••"}
                  </span>
                </Td>
                <Td><span className="text-xs text-surface-400">{k.created_at ? new Date(k.created_at).toLocaleString() : "–"}</span></Td>
                <Td>
                  <div className="flex gap-1">
                    <Btn variant="ghost" onClick={() => toggleReveal(k.key_id)}>
                      {revealed.has(k.key_id) ? <EyeOff size={12} /> : <Eye size={12} />}
                    </Btn>
                    <Btn variant="ghost" onClick={() => copy(k.api_key, k.key_id)}>
                      {copied === k.key_id
                        ? <Badge label="Copied" color="success" />
                        : <Copy size={12} />}
                    </Btn>
                  </div>
                </Td>
              </Tr>
            ))}
            {keys.length === 0 && <Tr><Td><span className="text-surface-400">No API keys</span></Td><Td>–</Td><Td>–</Td><Td>–</Td><Td>–</Td></Tr>}
          </Table>
        )}
      </Card>

      <Modal open={modal} onClose={() => setModal(false)} title="New API Key">
        <div className="space-y-3">
          <Input label="Key Name *" placeholder="Game Server Key" value={name}
            onChange={e => setName(e.target.value)}
            onKeyDown={e => e.key === "Enter" && save()} />
          <div className="flex justify-end gap-2 pt-2">
            <Btn variant="ghost" onClick={() => setModal(false)}>Cancel</Btn>
            <Btn onClick={save} disabled={saving}>{saving ? "Creating…" : "Create Key"}</Btn>
          </div>
        </div>
      </Modal>
    </div>
  );
}
