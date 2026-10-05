"use client";
import { useEffect, useState } from "react";
import { getModeration, banPlayer, mutePlayer, deleteMute, type Ban, type Mute, type Report } from "@/lib/api";
import { Card, CardHeader, Table, Tr, Td, Btn, Input, Modal, Badge, Select } from "@/components/Card";
import { Plus, RefreshCw, Trash2 } from "lucide-react";

type Tab = "bans" | "mutes" | "reports";

export default function ModerationPage() {
  const [bans, setBans] = useState<Ban[]>([]);
  const [mutes, setMutes] = useState<Mute[]>([]);
  const [reports, setReports] = useState<Report[]>([]);
  const [tab, setTab] = useState<Tab>("bans");
  const [loading, setLoading] = useState(true);
  const [banModal, setBanModal] = useState(false);
  const [muteModal, setMuteModal] = useState(false);
  const [banForm, setBanForm] = useState({ player_id: "", category: "0", reason: "", duration_minutes: "1440" });
  const [muteForm, setMuteForm] = useState({ player_id: "", duration_minutes: "60" });
  const [saving, setSaving] = useState(false);

  const load = () => {
    setLoading(true);
    getModeration()
      .then(r => { setBans(r.bans ?? []); setMutes(r.mutes ?? []); setReports(r.reports ?? []); })
      .finally(() => setLoading(false));
  };

  useEffect(load, []);

  const submitBan = async () => {
    if (!banForm.player_id.trim()) return;
    setSaving(true);
    try {
      await banPlayer({
        player_id: banForm.player_id,
        category: parseInt(banForm.category),
        reason: banForm.reason,
        duration_minutes: parseInt(banForm.duration_minutes),
      });
      setBanModal(false);
      setBanForm({ player_id: "", category: "0", reason: "", duration_minutes: "1440" });
      load();
    } finally { setSaving(false); }
  };

  const submitMute = async () => {
    if (!muteForm.player_id.trim()) return;
    setSaving(true);
    try {
      await mutePlayer(muteForm.player_id, parseInt(muteForm.duration_minutes));
      setMuteModal(false);
      setMuteForm({ player_id: "", duration_minutes: "60" });
      load();
    } finally { setSaving(false); }
  };

  const removeMute = async (mute_id: string) => {
    await deleteMute(mute_id);
    load();
  };

  const tabClass = (t: Tab) =>
    `px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
      tab === t
        ? "border-primary-500 text-primary-500"
        : "border-transparent text-surface-400 hover:text-white"
    }`;

  return (
    <div className="space-y-4">
      {/* Tabs */}
      <div className="flex gap-1 border-b border-surface-600">
        <button className={tabClass("bans")} onClick={() => setTab("bans")}>Bans ({bans.length})</button>
        <button className={tabClass("mutes")} onClick={() => setTab("mutes")}>Mutes ({mutes.length})</button>
        <button className={tabClass("reports")} onClick={() => setTab("reports")}>Reports ({reports.length})</button>
      </div>

      {/* Bans */}
      {tab === "bans" && (
        <Card>
          <CardHeader title="Active Bans" action={
            <div className="flex gap-2">
              <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
              <Btn variant="danger" onClick={() => setBanModal(true)}><Plus size={14} className="mr-1 inline" />Ban Player</Btn>
            </div>
          } />
          {loading ? <p className="text-surface-400 text-sm py-4 text-center">Loading…</p> : (
            <Table headers={["Player ID", "Reason", "Category", "Duration", "Expires", "Org-Wide"]}>
              {bans.map(b => (
                <Tr key={b.BanId}>
                  <Td mono>{b.PlayerId.slice(0, 12)}…</Td>
                  <Td>{b.Reason || "–"}</Td>
                  <Td><Badge label={`Cat ${b.Category}`} color="warning" /></Td>
                  <Td>{b.DurationMinutes}m</Td>
                  <Td><span className="text-xs text-surface-400">{b.ExpiresAt ? new Date(b.ExpiresAt).toLocaleString() : "–"}</span></Td>
                  <Td><Badge label={b.OrgWide ? "Yes" : "No"} color={b.OrgWide ? "error" : "surface"} /></Td>
                </Tr>
              ))}
              {bans.length === 0 && <Tr><Td><span className="text-surface-400">No bans</span></Td><Td>–</Td><Td>–</Td><Td>–</Td><Td>–</Td><Td>–</Td></Tr>}
            </Table>
          )}
        </Card>
      )}

      {/* Mutes */}
      {tab === "mutes" && (
        <Card>
          <CardHeader title="Active Mutes" action={
            <div className="flex gap-2">
              <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
              <Btn variant="secondary" onClick={() => setMuteModal(true)}><Plus size={14} className="mr-1 inline" />Mute Player</Btn>
            </div>
          } />
          {loading ? <p className="text-surface-400 text-sm py-4 text-center">Loading…</p> : (
            <Table headers={["Mute ID", "Player ID", "Duration", "Created", ""]}>
              {mutes.map(m => (
                <Tr key={m.MuteId}>
                  <Td mono>{m.MuteId.slice(0, 8)}…</Td>
                  <Td mono>{m.PlayerId.slice(0, 12)}…</Td>
                  <Td>{m.DurationMinutes}m</Td>
                  <Td><span className="text-xs text-surface-400">{m.CreatedAt ? new Date(m.CreatedAt).toLocaleString() : "–"}</span></Td>
                  <Td>
                    <Btn variant="danger" onClick={() => removeMute(m.MuteId)}>
                      <Trash2 size={12} />
                    </Btn>
                  </Td>
                </Tr>
              ))}
              {mutes.length === 0 && <Tr><Td><span className="text-surface-400">No mutes</span></Td><Td>–</Td><Td>–</Td><Td>–</Td><Td>–</Td></Tr>}
            </Table>
          )}
        </Card>
      )}

      {/* Reports */}
      {tab === "reports" && (
        <Card>
          <CardHeader title="Reports" action={<Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>} />
          {loading ? <p className="text-surface-400 text-sm py-4 text-center">Loading…</p> : (
            <Table headers={["Report ID", "Reported", "Reporter", "Category", "Reason", "Platform", "Created"]}>
              {reports.map(r => (
                <Tr key={r.ReportId}>
                  <Td mono>{r.ReportId.slice(0, 8)}…</Td>
                  <Td mono>{r.ReportedUserId.slice(0, 12)}…</Td>
                  <Td mono>{r.ReporterUserId ? r.ReporterUserId.slice(0, 12) + "…" : "–"}</Td>
                  <Td><Badge label={`Cat ${r.Category}`} color="warning" /></Td>
                  <Td>{r.Reason || "–"}</Td>
                  <Td>{r.Platform || "–"}</Td>
                  <Td><span className="text-xs text-surface-400">{r.CreatedAt ? new Date(r.CreatedAt).toLocaleString() : "–"}</span></Td>
                </Tr>
              ))}
              {reports.length === 0 && <Tr><Td><span className="text-surface-400">No reports</span></Td><Td>–</Td><Td>–</Td><Td>–</Td><Td>–</Td><Td>–</Td><Td>–</Td></Tr>}
            </Table>
          )}
        </Card>
      )}

      {/* Ban modal */}
      <Modal open={banModal} onClose={() => setBanModal(false)} title="Ban Player">
        <div className="space-y-3">
          <Input label="Player ID *" placeholder="UUID" value={banForm.player_id}
            onChange={e => setBanForm(f => ({ ...f, player_id: e.target.value }))} />
          <Input label="Reason" placeholder="Cheating, harassment…" value={banForm.reason}
            onChange={e => setBanForm(f => ({ ...f, reason: e.target.value }))} />
          <Select label="Category" value={banForm.category} onChange={e => setBanForm(f => ({ ...f, category: e.target.value }))}>
            <option value="0">0 — General</option>
            <option value="1">1 — Cheating</option>
            <option value="2">2 — Harassment</option>
            <option value="3">3 — Hate Speech</option>
            <option value="4">4 — Exploit</option>
          </Select>
          <Input label="Duration (minutes)" type="number" value={banForm.duration_minutes}
            onChange={e => setBanForm(f => ({ ...f, duration_minutes: e.target.value }))} />
          <div className="flex justify-end gap-2 pt-2">
            <Btn variant="ghost" onClick={() => setBanModal(false)}>Cancel</Btn>
            <Btn variant="danger" onClick={submitBan} disabled={saving}>{saving ? "Banning…" : "Ban Player"}</Btn>
          </div>
        </div>
      </Modal>

      {/* Mute modal */}
      <Modal open={muteModal} onClose={() => setMuteModal(false)} title="Mute Player">
        <div className="space-y-3">
          <Input label="Player ID *" placeholder="UUID" value={muteForm.player_id}
            onChange={e => setMuteForm(f => ({ ...f, player_id: e.target.value }))} />
          <Input label="Duration (minutes)" type="number" value={muteForm.duration_minutes}
            onChange={e => setMuteForm(f => ({ ...f, duration_minutes: e.target.value }))} />
          <div className="flex justify-end gap-2 pt-2">
            <Btn variant="ghost" onClick={() => setMuteModal(false)}>Cancel</Btn>
            <Btn variant="secondary" onClick={submitMute} disabled={saving}>{saving ? "Muting…" : "Mute Player"}</Btn>
          </div>
        </div>
      </Modal>
    </div>
  );
}
