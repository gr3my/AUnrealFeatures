"use client";
import { useEffect, useState } from "react";
import { Card, CardHeader, Table, Tr, Td, Badge } from "@/components/Card";
import { listLeaderboards } from "@/lib/api";

export default function LeaderboardsPage() {
  const [boards, setBoards] = useState<any[]>([]);
  const [err, setErr] = useState("");
  useEffect(() => { listLeaderboards().then((r) => setBoards(r.Leaderboards)).catch((e) => setErr(e.message)); }, []);

  return (
    <div className="space-y-4">
      {err && <div className="rounded p-3 text-sm" style={{ background: "rgb(var(--color-error-500) / 0.1)" }}>{err}</div>}

      <Card>
        <CardHeader title="Leaderboard definitions (v2)" />
        <Table headers={["Name", "Entity Type", "Version", "Size Limit", "Entries", "Created"]}>
          {boards.map((b) => (
            <Tr key={b.Name}>
              <Td><span className="font-mono text-xs">{b.Name}</span></Td>
              <Td><Badge label={b.EntityType || "?"} color="secondary" /></Td>
              <Td>{b.Version}</Td>
              <Td>{b.SizeLimit}</Td>
              <Td>{b.EntryCount}</Td>
              <Td>{b.Created ? new Date(b.Created * 1000).toLocaleString() : "—"}</Td>
            </Tr>
          ))}
          {boards.length === 0 && <Tr><Td>—</Td><Td>No leaderboards defined</Td><Td>—</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
        </Table>
      </Card>
    </div>
  );
}
