"use client";
import { useEffect, useState } from "react";
import { Card, CardHeader, Table, Tr, Td, Badge, Input, Btn } from "@/components/Card";
import { getEvents } from "@/lib/api";

export default function EventsPage() {
  const [events, setEvents] = useState<any[]>([]);
  const [total, setTotal] = useState(0);
  const [skip, setSkip] = useState(0);
  const take = 100;
  const [query, setQuery] = useState("");
  const [err, setErr] = useState("");

  useEffect(() => {
    getEvents(skip, take, query)
      .then((r) => { setEvents(r.Events); setTotal(r.Total); })
      .catch((e) => setErr(e.message));
  }, [skip, query]);

  return (
    <div className="space-y-4">
      {err && <div className="rounded p-3 text-sm" style={{ background: "rgb(var(--color-error-500) / 0.1)" }}>{err}</div>}

      <Card>
        <div className="flex items-center gap-3 mb-3">
          <Input placeholder="Filter by event name / namespace / entity…" value={query} onChange={(e) => { setSkip(0); setQuery(e.target.value); }} className="flex-1" />
          <Badge label={`${total} total`} color="primary" />
        </div>
        <Table headers={["When", "Namespace", "Event", "Entity", "Payload"]}>
          {events.map((e, i) => (
            <Tr key={i}>
              <Td>{new Date(e.Timestamp).toLocaleTimeString()}</Td>
              <Td><Badge label={e.EventNamespace} color="surface" /></Td>
              <Td><span className="font-mono text-xs">{e.EventName}</span></Td>
              <Td><span className="font-mono text-xs">{e.EntityId || "—"}</span></Td>
              <Td>
                <span className="font-mono text-xs">
                  {e.PayloadJson?.length > 60 ? e.PayloadJson.substring(0, 60) + "…" : e.PayloadJson || "—"}
                </span>
              </Td>
            </Tr>
          ))}
          {events.length === 0 && <Tr><Td>—</Td><Td>No events recorded</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
        </Table>
        <div className="flex justify-between items-center mt-3">
          <Btn variant="ghost" onClick={() => setSkip(Math.max(0, skip - take))} disabled={skip === 0}>← Prev</Btn>
          <span className="text-xs" style={{ color: "rgb(var(--color-surface-600))" }}>{skip + 1}–{Math.min(skip + take, total)} of {total}</span>
          <Btn variant="ghost" onClick={() => setSkip(skip + take)} disabled={skip + take >= total}>Next →</Btn>
        </div>
      </Card>
    </div>
  );
}
