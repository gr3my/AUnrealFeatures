"use client";
import { useEffect, useState } from "react";
import { getAnalyticsEvents, type AnalyticsEvent } from "@/lib/api";
import { Card, CardHeader, Table, Tr, Td, Btn, Input, Badge } from "@/components/Card";
import { RefreshCw } from "lucide-react";

export default function AnalyticsPage() {
  const [events, setEvents] = useState<AnalyticsEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<AnalyticsEvent | null>(null);

  const load = () => {
    setLoading(true);
    getAnalyticsEvents()
      .then(r => setEvents(Array.isArray(r) ? r : []))
      .finally(() => setLoading(false));
  };

  useEffect(load, []);

  const filtered = events.filter(e =>
    e.platform?.toLowerCase().includes(search.toLowerCase()) ||
    e.event_id?.includes(search)
  );

  // Count by platform
  const byPlatform = filtered.reduce<Record<string, number>>((acc, e) => {
    acc[e.platform] = (acc[e.platform] ?? 0) + 1;
    return acc;
  }, {});

  return (
    <div className="space-y-4">
      {/* Platform summary */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        {Object.entries(byPlatform).map(([platform, count]) => (
          <div key={platform} className="bg-surface-800 border border-surface-600 rounded p-3">
            <p className="text-xs text-surface-400 uppercase tracking-wider">{platform}</p>
            <p className="text-2xl font-bold text-primary-500">{count}</p>
          </div>
        ))}
        {Object.keys(byPlatform).length === 0 && !loading && (
          <div className="col-span-4 bg-surface-800 border border-surface-600 rounded p-3 text-surface-400 text-sm">
            No analytics events recorded yet.
          </div>
        )}
      </div>

      <Card>
        <CardHeader title={`Analytics Events (${filtered.length})`} action={
          <div className="flex gap-2">
            <Input placeholder="Filter by platform…" value={search} onChange={e => setSearch(e.target.value)} />
            <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
          </div>
        } />

        {loading ? (
          <p className="text-surface-400 text-sm py-4 text-center">Loading…</p>
        ) : (
          <Table headers={["Event ID", "Platform", "Timestamp", "Data"]}>
            {filtered.slice(0, 200).map(e => (
              <Tr key={e.event_id} className="cursor-pointer" >
                <Td mono>{e.event_id.slice(0, 12)}…</Td>
                <Td><Badge label={e.platform || "unknown"} color="primary" /></Td>
                <Td><span className="text-xs text-surface-400">{e.timestamp ? new Date(e.timestamp).toLocaleString() : "–"}</span></Td>
                <Td>
                  <button
                    className="text-xs font-mono text-surface-300 hover:text-primary-500 text-left max-w-xs truncate transition-colors"
                    onClick={() => setSelected(e)}
                  >
                    {e.data?.slice(0, 60) ?? "–"}
                  </button>
                </Td>
              </Tr>
            ))}
            {filtered.length === 0 && <Tr><Td><span className="text-surface-400">No events</span></Td><Td>–</Td><Td>–</Td><Td>–</Td></Tr>}
          </Table>
        )}
      </Card>

      {/* Detail panel */}
      {selected && (
        <Card>
          <CardHeader title="Event Detail" action={
            <button onClick={() => setSelected(null)} className="text-surface-400 hover:text-white text-xl leading-none">&times;</button>
          } />
          <div className="space-y-2 text-xs">
            <p><span className="text-surface-400">Event ID:</span> <span className="font-mono text-white">{selected.event_id}</span></p>
            <p><span className="text-surface-400">Platform:</span> <span className="text-white">{selected.platform}</span></p>
            <p><span className="text-surface-400">Timestamp:</span> <span className="text-white">{selected.timestamp}</span></p>
            <pre className="bg-surface-900 rounded p-3 text-primary-500 overflow-x-auto font-mono text-xs whitespace-pre-wrap">
              {(() => {
                try { return JSON.stringify(JSON.parse(selected.data), null, 2); }
                catch { return selected.data; }
              })()}
            </pre>
          </div>
        </Card>
      )}
    </div>
  );
}
