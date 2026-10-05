"use client";
import { useEffect, useState } from "react";
import { getPlayers, updatePlayerTags, type Player } from "@/lib/api";
import { Card, CardHeader, Table, Tr, Td, Btn, Badge, Input, Modal } from "@/components/Card";
import { RefreshCw, Tag } from "lucide-react";

export default function PlayersPage() {
  const [players, setPlayers] = useState<Player[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [tagsModal, setTagsModal] = useState<Player | null>(null);
  const [newTag, setNewTag] = useState("");

  const load = () => {
    setLoading(true);
    getPlayers()
      .then(r => setPlayers(r.Results ?? []))
      .finally(() => setLoading(false));
  };

  useEffect(load, []);

  const filtered = players.filter(p =>
    p.MothershipPlayerId.includes(search) ||
    p.ExternalAccountNickname.toLowerCase().includes(search.toLowerCase()) ||
    p.ExternalAccountId.toLowerCase().includes(search.toLowerCase())
  );

  const addTag = async () => {
    if (!tagsModal || !newTag.trim()) return;
    const tags = [...(tagsModal.Tags ?? []), newTag.trim()];
    await updatePlayerTags(tagsModal.MothershipPlayerId, tags);
    setTagsModal({ ...tagsModal, Tags: tags });
    setNewTag("");
    load();
  };

  const removeTag = async (tag: string) => {
    if (!tagsModal) return;
    const tags = tagsModal.Tags.filter(t => t !== tag);
    await updatePlayerTags(tagsModal.MothershipPlayerId, tags);
    setTagsModal({ ...tagsModal, Tags: tags });
    load();
  };

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader title={`Players (${filtered.length})`} action={
          <div className="flex items-center gap-2">
            <Input placeholder="Search ID / name…" value={search} onChange={e => setSearch(e.target.value)} />
            <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
          </div>
        } />

        {loading ? (
          <p className="text-surface-400 text-sm py-4 text-center">Loading…</p>
        ) : (
          <Table headers={["Player ID", "Nickname", "External ID", "Expires", "Tags", ""]}>
            {filtered.map(p => (
              <Tr key={p.MothershipPlayerId}>
                <Td mono>{p.MothershipPlayerId.slice(0, 8)}…</Td>
                <Td>{p.ExternalAccountNickname || <span className="text-surface-400 italic">anon</span>}</Td>
                <Td mono>{p.ExternalAccountId ? p.ExternalAccountId.slice(0, 12) + "…" : "–"}</Td>
                <Td>
                  <span className="text-xs text-surface-400">
                    {p.ExpirationTime ? new Date(p.ExpirationTime * 1000).toLocaleString() : "–"}
                  </span>
                </Td>
                <Td>
                  <div className="flex flex-wrap gap-1">
                    {(p.Tags ?? []).map(t => <Badge key={t} label={t} color="secondary" />)}
                    {!p.Tags?.length && <span className="text-surface-500 text-xs">none</span>}
                  </div>
                </Td>
                <Td>
                  <Btn variant="ghost" onClick={() => setTagsModal(p)}>
                    <Tag size={12} />
                  </Btn>
                </Td>
              </Tr>
            ))}
            {filtered.length === 0 && (
              <Tr><Td><span className="text-surface-400">No players found</span></Td>
                <Td>–</Td><Td>–</Td><Td>–</Td><Td>–</Td><Td>–</Td>
              </Tr>
            )}
          </Table>
        )}
      </Card>

      <Modal open={!!tagsModal} onClose={() => setTagsModal(null)} title="Manage Player Tags">
        {tagsModal && (
          <div className="space-y-4">
            <p className="text-xs text-surface-400 font-mono">{tagsModal.MothershipPlayerId}</p>
            <div className="flex flex-wrap gap-2">
              {(tagsModal.Tags ?? []).map(t => (
                <span key={t} className="flex items-center gap-1 bg-secondary-500/10 border border-secondary-500/30 text-secondary-500 text-xs px-2 py-0.5 rounded">
                  {t}
                  <button onClick={() => removeTag(t)} className="ml-1 text-error-400 hover:text-error-500">×</button>
                </span>
              ))}
              {!tagsModal.Tags?.length && <span className="text-surface-400 text-xs">No tags</span>}
            </div>
            <div className="flex gap-2">
              <Input placeholder="New tag…" value={newTag} onChange={e => setNewTag(e.target.value)}
                onKeyDown={e => e.key === "Enter" && addTag()} className="flex-1" />
              <Btn onClick={addTag}>Add</Btn>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
}
