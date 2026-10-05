"use client";
import { useEffect, useState } from "react";
import { StatCard, Card, CardHeader, Table, Tr, Td, Badge } from "@/components/Card";
import { debugStorage, health } from "@/lib/api";
import { Activity, Clock } from "lucide-react";

export default function OverviewPage() {
  const [storage, setStorage] = useState<Record<string, number>>({});
  const [status, setStatus] = useState<{ status: string; timestamp: string } | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    health().then(setStatus).catch(() => setError("Cannot reach Mothership backend"));
    debugStorage().then(setStorage).catch(() => {});
  }, []);

  const storageEntries = Object.entries(storage);

  return (
    <div className="space-y-6">
      {error && (
        <div className="bg-error-500/10 border border-error-500/30 rounded p-3 text-error-400 text-sm">
          {error}
        </div>
      )}

      {/* Health banner */}
      <Card>
        <div className="flex items-center gap-4">
          <div className={`w-3 h-3 rounded-full ${status?.status === "healthy" ? "bg-success-400 animate-pulse" : "bg-error-500"}`} />
          <div>
            <p className="font-medium text-white">
              Mothership Backend — {status ? (
                <span className="text-success-400">Online</span>
              ) : (
                <span className="text-surface-400">Connecting…</span>
              )}
            </p>
            {status && (
              <p className="text-xs text-surface-400 flex items-center gap-1 mt-0.5">
                <Clock size={11} /> {new Date(status.timestamp).toLocaleString()}
              </p>
            )}
          </div>
          <div className="ml-auto text-xs text-surface-400">Mothership API</div>
        </div>
      </Card>

      {/* Key stats */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <StatCard label="Players"      value={storage.players ?? "–"}      color="primary" />
        <StatCard label="Active Bans"  value={storage.bans ?? "–"}         color="error" />
        <StatCard label="Deployments"  value={storage.deployments ?? "–"}  color="secondary" />
        <StatCard label="Analytics"    value={storage.analytics_events ?? "–"} color="warning" />
      </div>

      {/* Storage overview table */}
      <Card>
        <CardHeader title="Storage Overview" action={
          <div className="flex items-center gap-2 text-xs text-success-400">
            <Activity size={12} /> Live
          </div>
        } />
        <Table headers={["Collection", "Count"]}>
          {storageEntries.map(([key, count]) => (
            <Tr key={key}>
              <Td><span className="font-mono text-primary-500 text-xs">{key}</span></Td>
              <Td>
                <Badge
                  label={String(count)}
                  color={count > 0 ? "primary" : "surface"}
                />
              </Td>
            </Tr>
          ))}
          {storageEntries.length === 0 && (
            <Tr><Td><span className="text-surface-400">Loading…</span></Td><Td>–</Td></Tr>
          )}
        </Table>
      </Card>
    </div>
  );
}
