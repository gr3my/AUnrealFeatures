"use client";
import { useEffect, useState } from "react";
import {
  getProgressionTracks, createProgressionTrack, deleteProgressionTrack,
  getProgressionTrees, createProgressionTree, deleteProgressionTree,
  type ProgressionTrack, type ProgressionTree,
} from "@/lib/api";
import { Card, CardHeader, Table, Tr, Td, Btn, Input, Modal } from "@/components/Card";
import { Plus, RefreshCw, Trash2 } from "lucide-react";

type Tab = "tracks" | "trees";

export default function ProgressionPage() {
  const [tab, setTab] = useState<Tab>("tracks");
  const [tracks, setTracks] = useState<ProgressionTrack[]>([]);
  const [trees, setTrees] = useState<ProgressionTree[]>([]);
  const [loading, setLoading] = useState(true);
  const [modal, setModal] = useState(false);
  const [name, setName] = useState("");
  const [saving, setSaving] = useState(false);

  const load = () => {
    setLoading(true);
    Promise.all([
      getProgressionTracks().then(r => setTracks(r.Results ?? [])),
      getProgressionTrees().then(r => setTrees(r.Results ?? [])),
    ]).finally(() => setLoading(false));
  };

  useEffect(load, []);

  const save = async () => {
    if (!name.trim()) return;
    setSaving(true);
    try {
      if (tab === "tracks") await createProgressionTrack(name);
      else await createProgressionTree(name);
      setModal(false);
      setName("");
      load();
    } finally { setSaving(false); }
  };

  const remove = async (id: string) => {
    if (!confirm("Delete this progression item?")) return;
    if (tab === "tracks") await deleteProgressionTrack(id);
    else await deleteProgressionTree(id);
    load();
  };

  const tabClass = (t: Tab) =>
    `px-4 py-2 text-sm font-medium border-b-2 transition-colors ${tab === t ? "border-primary-500 text-primary-500" : "border-transparent text-surface-400 hover:text-white"}`;

  return (
    <div className="space-y-4">
      <div className="flex gap-1 border-b border-surface-600">
        <button className={tabClass("tracks")} onClick={() => setTab("tracks")}>Tracks ({tracks.length})</button>
        <button className={tabClass("trees")} onClick={() => setTab("trees")}>Trees ({trees.length})</button>
      </div>

      <Card>
        <CardHeader
          title={tab === "tracks" ? "Progression Tracks" : "Progression Trees"}
          action={
            <div className="flex gap-2">
              <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
              <Btn onClick={() => setModal(true)}><Plus size={14} className="mr-1 inline" />New {tab === "tracks" ? "Track" : "Tree"}</Btn>
            </div>
          }
        />

        {loading ? (
          <p className="text-surface-400 text-sm py-4 text-center">Loading…</p>
        ) : tab === "tracks" ? (
          <Table headers={["Track ID", "Name", ""]}>
            {tracks.map(t => (
              <Tr key={t.track_id}>
                <Td mono>{t.track_id}</Td>
                <Td>{t.name}</Td>
                <Td><Btn variant="danger" onClick={() => remove(t.track_id)}><Trash2 size={12} /></Btn></Td>
              </Tr>
            ))}
            {tracks.length === 0 && <Tr><Td><span className="text-surface-400">No tracks</span></Td><Td>–</Td><Td>–</Td></Tr>}
          </Table>
        ) : (
          <Table headers={["Tree ID", "Name", ""]}>
            {trees.map(t => (
              <Tr key={t.tree_id}>
                <Td mono>{t.tree_id}</Td>
                <Td>{t.name}</Td>
                <Td><Btn variant="danger" onClick={() => remove(t.tree_id)}><Trash2 size={12} /></Btn></Td>
              </Tr>
            ))}
            {trees.length === 0 && <Tr><Td><span className="text-surface-400">No trees</span></Td><Td>–</Td><Td>–</Td></Tr>}
          </Table>
        )}
      </Card>

      <Modal open={modal} onClose={() => setModal(false)} title={`New Progression ${tab === "tracks" ? "Track" : "Tree"}`}>
        <div className="space-y-3">
          <Input label="Name *" placeholder={tab === "tracks" ? "Season Pass Track" : "Skill Tree"}
            value={name} onChange={e => setName(e.target.value)}
            onKeyDown={e => e.key === "Enter" && save()} />
          <div className="flex justify-end gap-2 pt-2">
            <Btn variant="ghost" onClick={() => setModal(false)}>Cancel</Btn>
            <Btn onClick={save} disabled={saving}>{saving ? "Saving…" : "Create"}</Btn>
          </div>
        </div>
      </Modal>
    </div>
  );
}
