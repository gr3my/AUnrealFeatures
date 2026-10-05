"use client";
import { useEffect, useState } from "react";
import { Card, Btn, Select } from "@/components/Card";
import { PageChrome } from "@/components/PageChrome";
import {
  listPlayers, getEvents, getCloudScriptLogs,
  getPhotonWebhookLogs, getPhotonRoomStates,
} from "@/lib/api";

const SOURCES = [
  { id: "players",    label: "Players",        fetch: () => listPlayers(0, 100, "") },
  { id: "events",     label: "Events",         fetch: () => getEvents(0, 100, "") },
  { id: "cloudscript", label: "CloudScript logs", fetch: () => getCloudScriptLogs(0, 100, "") },
  { id: "photon-webhooks", label: "Photon webhook logs", fetch: () => getPhotonWebhookLogs(0, 100, "") },
  { id: "photon-rooms", label: "Photon room states", fetch: () => getPhotonRoomStates(0, 100, "") },
];

export default function DataExplorerPage() {
  const [src, setSrc] = useState("players");
  const [data, setData] = useState<any>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");

  const run = async () => {
    setBusy(true); setErr(""); setData(null);
    try {
      const fn = SOURCES.find(s => s.id === src)?.fetch;
      if (!fn) return;
      setData(await fn());
    } catch (e: any) { setErr(e.message ?? String(e)); }
    finally { setBusy(false); }
  };

  useEffect(() => { run(); }, [src]);

  return (
    <PageChrome title="Data Explorer" subtitle="Raw JSON dump of any indexed collection">
      <Card>
        <div className="flex items-end gap-3 mb-4">
          <div style={{ width: 280 }}>
            <Select label="Source" value={src} onChange={(e) => setSrc(e.target.value)}>
              {SOURCES.map(s => <option key={s.id} value={s.id}>{s.label}</option>)}
            </Select>
          </div>
          <Btn variant="primary" onClick={run} disabled={busy}>{busy ? "Loading…" : "Refresh"}</Btn>
        </div>
        {err && (
          <div style={{ background: "#fde8e8", border: "1px solid rgb(var(--color-error-400))", color: "rgb(var(--color-error-700))", padding: 10, borderRadius: 2, marginBottom: 12, fontSize: 13 }}>
            {err}
          </div>
        )}
        {data && (
          <pre style={{ fontSize: 12, background: "rgb(var(--color-surface-100))", padding: 12, borderRadius: 2, overflow: "auto", maxHeight: "70vh", color: "rgb(var(--color-surface-900))", margin: 0 }}>
            {JSON.stringify(data, null, 2)}
          </pre>
        )}
      </Card>
    </PageChrome>
  );
}
