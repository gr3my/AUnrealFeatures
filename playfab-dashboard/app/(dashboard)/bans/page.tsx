"use client";
import { useEffect, useState } from "react";
import { Card, CardHeader, Table, Tr, Td, Badge, Btn } from "@/components/Card";
import { listPlayers, unbanUser, PlayerRow } from "@/lib/api";

export default function BansPage() {
  const [rows, setRows] = useState<PlayerRow[]>([]);
  const [err, setErr] = useState("");

  const refresh = () => {
    listPlayers(0, 500, "").then((r) => setRows(r.Players.filter((p) => p.IsBanned))).catch((e) => setErr(e.message));
  };

  useEffect(refresh, []);

  const unban = async (id: string) => {
    await unbanUser(id);
    refresh();
  };

  return (
    <div className="space-y-4">
      {err && <div className="rounded p-3 text-sm" style={{ background: "rgb(var(--color-error-500) / 0.1)" }}>{err}</div>}

      <Card>
        <CardHeader title="Banned players" action={<Badge label={`${rows.length} banned`} color="error" />} />
        <Table headers={["PlayFabId", "Display / Username", "Origin", "Last Login", ""]}>
          {rows.map((p) => (
            <Tr key={p.PlayFabId}>
              <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{p.PlayFabId}</span></Td>
              <Td>{p.DisplayName || p.Username || "—"}</Td>
              <Td><Badge label={p.OriginPlatform || "?"} color="surface" /></Td>
              <Td>{p.LastLoginUnix ? new Date(p.LastLoginUnix * 1000).toLocaleString() : "—"}</Td>
              <Td><Btn variant="secondary" onClick={() => unban(p.PlayFabId)}>Unban</Btn></Td>
            </Tr>
          ))}
          {rows.length === 0 && <Tr><Td>—</Td><Td>No banned players</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
        </Table>
      </Card>
    </div>
  );
}
