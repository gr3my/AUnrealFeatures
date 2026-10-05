"use client";
import { useEffect, useState } from "react";
import { Card, CardHeader, StatCard, Table, Tr, Td, Badge, Btn } from "@/components/Card";
import { getMultiplayerOverview } from "@/lib/api";

export default function MultiplayerPage() {
  const [data, setData] = useState<any>(null);
  const [err, setErr] = useState("");

  const refresh = () => getMultiplayerOverview().then(setData).catch((e) => setErr(e.message));
  useEffect(() => { refresh(); const id = setInterval(refresh, 5000); return () => clearInterval(id); }, []);

  if (err) return <div className="rounded p-3 text-sm" style={{ background: "rgb(var(--color-error-500) / 0.1)" }}>{err}</div>;
  if (!data) return <p style={{ color: "rgb(var(--color-surface-700))" }}>Loading…</p>;

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 sm:grid-cols-5 gap-4">
        <StatCard label="Lobbies"        value={data.LobbyCount}                 color="primary" />
        <StatCard label="MM Tickets"     value={data.TicketCount}                color="secondary" />
        <StatCard label="MM Queues"      value={data.QueueCount}                 color="success" />
        <StatCard label="Builds"         value={data.BuildCount}                 color="warning" />
        <StatCard label="Active Servers" value={`${data.ActiveServers} / ${data.ServerCount}`} color="primary" />
      </div>

      <Card>
        <CardHeader title="Active lobbies" />
        <Table headers={["Lobby ID", "Owner", "Members", "Max", "Access", "Created"]}>
          {(data.ActiveLobbies ?? []).map((l: any) => (
            <Tr key={l.LobbyId}>
              <Td><span className="font-mono text-xs">{l.LobbyId}</span></Td>
              <Td><span className="font-mono text-xs">{l.OwnerEntityId}</span></Td>
              <Td>{l.MemberCount}</Td>
              <Td>{l.MaxPlayers}</Td>
              <Td><Badge label={l.AccessPolicy} color={l.AccessPolicy === "Public" ? "success" : l.AccessPolicy === "Friends" ? "primary" : "surface"} /></Td>
              <Td>{new Date(l.Created * 1000).toLocaleString()}</Td>
            </Tr>
          ))}
          {(data.ActiveLobbies ?? []).length === 0 && <Tr><Td>—</Td><Td>No active lobbies</Td><Td>—</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
        </Table>
      </Card>

      <Card>
        <CardHeader title="Matchmaking queues" />
        <Table headers={["Queue", "Min", "Max"]}>
          {(data.Queues ?? []).map((q: any) => (
            <Tr key={q.QueueName}>
              <Td><span className="font-mono text-xs">{q.QueueName}</span></Td>
              <Td>{q.MinMatchSize}</Td>
              <Td>{q.MaxMatchSize}</Td>
            </Tr>
          ))}
          {(data.Queues ?? []).length === 0 && <Tr><Td>—</Td><Td>No queues</Td><Td>—</Td></Tr>}
        </Table>
      </Card>

      <Card>
        <CardHeader title="Server builds" />
        <Table headers={["Build ID", "Name", "Container", "Created"]}>
          {(data.Builds ?? []).map((b: any) => (
            <Tr key={b.BuildId}>
              <Td><span className="font-mono text-xs">{b.BuildId}</span></Td>
              <Td>{b.BuildName}</Td>
              <Td><Badge label={b.ContainerFlavor} color="secondary" /></Td>
              <Td>{new Date(b.Created * 1000).toLocaleString()}</Td>
            </Tr>
          ))}
          {(data.Builds ?? []).length === 0 && <Tr><Td>—</Td><Td>No builds</Td><Td>—</Td><Td>—</Td></Tr>}
        </Table>
      </Card>
    </div>
  );
}
