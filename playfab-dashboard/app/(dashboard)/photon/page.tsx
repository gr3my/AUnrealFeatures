"use client";
import { useEffect, useState } from "react";
import { Card, CardHeader, Table, Tr, Td, Badge, Btn, Input } from "@/components/Card";
import { getPhotonWebhookLogs, getPhotonRoomStates, getPhotonTickets } from "@/lib/api";

type Tab = "webhooks" | "rooms" | "tickets";

export default function PhotonPage() {
  const [tab, setTab] = useState<Tab>("webhooks");
  const [logs, setLogs] = useState<any[]>([]);
  const [rooms, setRooms] = useState<any[]>([]);
  const [tickets, setTickets] = useState<any[]>([]);
  const [query, setQuery] = useState("");
  const [err, setErr] = useState("");

  const refresh = async () => {
    try {
      if (tab === "webhooks") setLogs((await getPhotonWebhookLogs(0, 100, query)).Logs);
      if (tab === "rooms")    setRooms((await getPhotonRoomStates(0, 100, query)).Rooms);
      if (tab === "tickets")  setTickets((await getPhotonTickets()).Tickets);
      setErr("");
    } catch (e: any) { setErr(e.message); }
  };

  useEffect(() => { refresh(); }, [tab, query]);

  const tabClass = (t: Tab) =>
    `px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
      tab === t ? "border-primary-500 text-primary-500" : "border-transparent text-surface-400 hover:text-white"
    }`;

  return (
    <div className="space-y-4">
      {err && <div className="rounded p-3 text-sm" style={{ background: "rgb(var(--color-error-500) / 0.1)" }}>{err}</div>}

      <div className="flex gap-1" style={{ borderBottom: "1px solid rgb(var(--color-surface-600))" }}>
        <button className={tabClass("webhooks")} onClick={() => setTab("webhooks")}>Webhook Log</button>
        <button className={tabClass("rooms")}    onClick={() => setTab("rooms")}>Room State</button>
        <button className={tabClass("tickets")}  onClick={() => setTab("tickets")}>Auth Tickets</button>
      </div>

      {tab !== "tickets" && (
        <Input placeholder="Filter…" value={query} onChange={(e) => setQuery(e.target.value)} />
      )}

      {tab === "webhooks" && (
        <Card>
          <CardHeader title="Recent webhook calls from Photon" action={<Btn variant="ghost" onClick={refresh}>Refresh</Btn>} />
          <Table headers={["Time", "Handler", "Type", "Game", "Player", "Status"]}>
            {logs.map((l, i) => (
              <Tr key={i}>
                <Td>{new Date(l.Timestamp).toLocaleTimeString()}</Td>
                <Td><Badge label={l.Handler} color="primary" /></Td>
                <Td>{l.Type}</Td>
                <Td><span className="font-mono text-xs">{l.GameId || "—"}</span></Td>
                <Td><span className="font-mono text-xs">{l.PlayFabId || "—"}</span></Td>
                <Td>{l.Success ? <Badge label="ok" color="success" /> : <Badge label="fail" color="error" />}</Td>
              </Tr>
            ))}
            {logs.length === 0 && <Tr><Td>—</Td><Td>No webhook calls received yet</Td><Td>—</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
          </Table>
        </Card>
      )}

      {tab === "rooms" && (
        <Card>
          <CardHeader title="Persisted Photon room state" />
          <Table headers={["Game ID", "Region", "Actors", "Saved", "State Size"]}>
            {rooms.map((r, i) => (
              <Tr key={i}>
                <Td><span className="font-mono text-xs">{r.GameId}</span></Td>
                <Td><Badge label={r.Region || "—"} color="secondary" /></Td>
                <Td>{r.ActorCount}</Td>
                <Td>{r.Saved ? new Date(r.Saved * 1000).toLocaleString() : "—"}</Td>
                <Td>{r.HasState ? `${r.StateLength} bytes` : <span style={{ color: "rgb(var(--color-surface-600))" }}>empty</span>}</Td>
              </Tr>
            ))}
            {rooms.length === 0 && <Tr><Td>—</Td><Td>No rooms persisted</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
          </Table>
        </Card>
      )}

      {tab === "tickets" && (
        <Card>
          <CardHeader title="Issued Photon auth tickets" />
          <Table headers={["PlayFabId", "App", "Issued", "Expires", "Used"]}>
            {tickets.map((t, i) => (
              <Tr key={i}>
                <Td><span className="font-mono text-xs">{t.PlayFabId}</span></Td>
                <Td><span className="font-mono text-xs">{t.PhotonAppId || "—"}</span></Td>
                <Td>{new Date(t.Issued * 1000).toLocaleString()}</Td>
                <Td>{new Date(t.Expires * 1000).toLocaleString()}</Td>
                <Td>{t.Used ? <Badge label="used" color="surface" /> : <Badge label="active" color="success" />}</Td>
              </Tr>
            ))}
            {tickets.length === 0 && <Tr><Td>—</Td><Td>No tickets issued yet</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
          </Table>
        </Card>
      )}
    </div>
  );
}
