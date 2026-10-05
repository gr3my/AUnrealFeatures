"use client";
import { useEffect, useState } from "react";
import { Card, CardHeader, Table, Tr, Td, Badge, Btn, Input, Select, Modal } from "@/components/Card";
import { listStatDefinitions, createStatDefinition } from "@/lib/api";

export default function StatsPage() {
  const [stats, setStats] = useState<any[]>([]);
  const [editing, setEditing] = useState<{ name: string; agg: string; reset: string } | null>(null);
  const [err, setErr] = useState("");

  const refresh = () => listStatDefinitions().then((r) => setStats(r.Statistics)).catch((e) => setErr(e.message));
  useEffect(() => { refresh(); }, []);

  const save = async () => {
    if (!editing?.name) return;
    await createStatDefinition(editing.name, editing.agg, editing.reset);
    setEditing(null);
    refresh();
  };

  return (
    <div className="space-y-4">
      {err && <div className="rounded p-3 text-sm" style={{ background: "rgb(var(--color-error-500) / 0.1)" }}>{err}</div>}

      <Card>
        <CardHeader title="Statistic definitions" action={
          <Btn variant="primary" onClick={() => setEditing({ name: "", agg: "Last", reset: "Never" })}>+ New stat</Btn>
        } />
        <Table headers={["Name", "Aggregation", "Reset", "Version"]}>
          {stats.map((s) => (
            <Tr key={s.StatisticName}>
              <Td><span className="font-mono text-xs">{s.StatisticName}</span></Td>
              <Td><Badge label={s.AggregationMethod} color="secondary" /></Td>
              <Td>{s.VersionChangeInterval}</Td>
              <Td>{s.CurrentVersion}</Td>
            </Tr>
          ))}
          {stats.length === 0 && <Tr><Td>â€”</Td><Td><span style={{ color: "rgb(var(--color-surface-600))" }}>No stats defined</span></Td><Td>â€”</Td><Td>â€”</Td></Tr>}
        </Table>
      </Card>

      <Modal open={editing != null} onClose={() => setEditing(null)} title="New statistic definition">
        {editing && (
          <div className="space-y-3">
            <Input label="Statistic Name (e.g. score, level)" value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} />
            <Select label="Aggregation Method" value={editing.agg} onChange={(e) => setEditing({ ...editing, agg: e.target.value })}>
              <option value="Last">Last (replace)</option>
              <option value="Min">Min</option>
              <option value="Max">Max (high score)</option>
              <option value="Sum">Sum (accumulate)</option>
            </Select>
            <Select label="Reset Interval" value={editing.reset} onChange={(e) => setEditing({ ...editing, reset: e.target.value })}>
              <option value="Never">Never</option>
              <option value="Hour">Hourly</option>
              <option value="Day">Daily</option>
              <option value="Week">Weekly</option>
              <option value="Month">Monthly</option>
            </Select>
            <div className="flex justify-end gap-2">
              <Btn variant="ghost" onClick={() => setEditing(null)}>Cancel</Btn>
              <Btn variant="primary" onClick={save}>Create</Btn>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
}
