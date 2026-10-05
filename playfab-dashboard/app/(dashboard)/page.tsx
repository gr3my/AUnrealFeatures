"use client";
import { useEffect, useState } from "react";
import { Card, Btn, Select } from "@/components/Card";
import { PageChrome } from "@/components/PageChrome";
import { getTitleSettings } from "@/lib/api";
import { Filter, HelpCircle } from "lucide-react";

interface MetricRow { period: string; amount: string | number; change: string; }
interface MetricCardData {
  title: string;
  rows: MetricRow[];
  unit?: string;
  filter?: boolean;
  info?: boolean;
}

export default function OverviewPage() {
  const [s, setS] = useState<any>(null);
  const [err, setErr] = useState("");
  const [loadedAt, setLoadedAt] = useState<Date | null>(null);
  const [period, setPeriod] = useState("Last 24 hours");
  const [layout, setLayout] = useState("Classic");


  const refresh = () => {
    getTitleSettings()
      .then((d) => { setS(d); setLoadedAt(new Date()); setErr(""); })
      .catch((e) => setErr(e.message ?? String(e)));
  };

  useEffect(() => { refresh(); const id = setInterval(refresh, 10_000); return () => clearInterval(id); }, []);

  if (err) return (
    <PageChrome title="Title Overview">
      <div style={{ background: "#fde8e8", border: "1px solid rgb(var(--color-error-400))", color: "rgb(var(--color-error-700))", padding: 12, borderRadius: 2, marginBottom: 16 }}>
        Cannot reach the PlayFab-compatible backend: {err}
      </div>
      <Card>
        <p className="text-xs" style={{ color: "rgb(var(--color-surface-700))", margin: 0 }}>
          Check <code>PLAYFAB_URL</code> and <code>PLAYFAB_DEV_SECRET</code> on the dashboard server, then restart the dashboard.
        </p>
      </Card>
    </PageChrome>
  );

  // Build metric cards. Until we have real time-series telemetry, the rows are point-in-time
  // counts with N/A change — same shape as PlayFab's table so the layout matches 1:1.
  const metrics: MetricCardData[] = [
    { title: "Unique users", rows: [
      { period: "Last 24 hours", amount: s?.PlayerCount ?? 0,                      change: "NA" },
      { period: "Last 30 days",  amount: s?.PlayerCount ?? 0,                      change: "NA" },
      { period: "Last 24 hours", amount: 0,                                        change: "NA" },
    ]},
    { title: "API calls", filter: true, rows: [
      { period: "Last 24 hours", amount: 0, change: "NA" },
      { period: "1 day ago",     amount: 0, change: "NA" },
      { period: "7 days ago",    amount: 0, change: "NA" },
    ]},
    { title: "Cloud Script processing times", unit: "milliseconds", rows: [
      { period: "Last 24 hours", amount: 0, change: "NA" },
      { period: "1 day ago",     amount: 0, change: "NA" },
      { period: "7 days ago",    amount: 0, change: "NA" },
    ]},
    { title: "Logins", rows: [
      { period: "Last 24 hours", amount: 0, change: "NA" },
      { period: "1 day ago",     amount: 0, change: "NA" },
      { period: "7 days ago",    amount: 0, change: "NA" },
    ]},
    { title: "New users", rows: [
      { period: "Last 24 hours", amount: 0, change: "NA" },
      { period: "1 day ago",     amount: 0, change: "NA" },
      { period: "7 days ago",    amount: 0, change: "NA" },
    ]},
    { title: "Purchases", info: true, rows: [
      { period: "Last 24 hours", amount: "NA", change: "NA" },
      { period: "1 day ago",     amount: "NA", change: "NA" },
      { period: "7 days ago",    amount: "NA", change: "NA" },
    ]},
  ];

  return (
    <PageChrome
      title="Title Overview"
      tabs={[
        { label: "Overview",          active: true },
        { label: "PlayStream Monitor", active: false },
      ]}
    >
      {/* Time period + Layout dropdowns */}
      <div className="flex gap-4 mb-4" style={{ alignItems: "flex-end" }}>
        <div style={{ width: 180 }}>
          <Select label="Time period" value={period} onChange={(e) => setPeriod(e.target.value)}>
            <option>Last 24 hours</option>
            <option>Last 7 days</option>
            <option>Last 30 days</option>
          </Select>
        </div>
        <div style={{ width: 180 }}>
          <Select label="Layout" value={layout} onChange={(e) => setLayout(e.target.value)}>
            <option>Classic</option>
            <option>Compact</option>
          </Select>
        </div>
      </div>

      {/* 6-card metric grid (3 wide × 2 tall) */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        {metrics.map((m) => <MetricCard key={m.title} m={m} />)}
      </div>

      {/* Reports table */}
      <div style={{ marginTop: 24 }}>
        <Card>
          <div className="flex items-center justify-between mb-3">
            <h3 className="h3" style={{ margin: 0 }}>Reports</h3>
            <span className="text-xs" style={{ color: "rgb(var(--color-surface-700))" }}>Snapshot @ {loadedAt?.toLocaleTimeString() ?? "—"}</span>
          </div>
          <table className="table">
            <thead>
              <tr>
                <th>Reports</th>
                <th>Yesterday</th>
                <th>Last month</th>
              </tr>
            </thead>
            <tbody>
              <tr><td>Unique users</td><td>0</td><td>{s?.PlayerCount ?? 0}</td></tr>
              <tr><td>Catalog items</td><td>—</td><td>{s?.CatalogItemCount ?? 0}</td></tr>
              <tr><td>News count</td><td>—</td><td>{s?.NewsCount ?? 0}</td></tr>
              <tr><td>CloudScript revision</td><td>—</td><td>{s?.CloudScriptVersion ?? 1}</td></tr>
              <tr><td>Photon configured</td><td>—</td><td>{s?.PhotonConfigured ? "Yes" : "No"}</td></tr>
            </tbody>
          </table>
        </Card>
      </div>
    </PageChrome>
  );
}

/* ─── Metric card (mini table + sparkline-shaped placeholder) ─────────── */
function MetricCard({ m }: { m: MetricCardData }) {
  return (
    <div className="card pf-metric-card">
      <div className="pf-metric-header">
        <h3 className="pf-metric-title">{m.title}</h3>
        {m.info && <HelpCircle size={12} style={{ color: "rgb(var(--color-surface-700))" }} />}
        {m.filter && (
          <a href="#" className="pf-metric-filter">
            <Filter size={11} /> Filter
          </a>
        )}
      </div>
      <table className="pf-metric-table">
        <thead>
          <tr>
            <th></th>
            <th>Amount</th>
            <th>Change</th>
          </tr>
        </thead>
        <tbody>
          {m.rows.map((r, i) => (
            <tr key={i}>
              <td>{r.period}</td>
              <td>{i === 0 ? <span className="pf-metric-big">{r.amount}</span> : r.amount}</td>
              <td>{r.change}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="pf-metric-chart">
        {m.unit && <span className="pf-metric-unit">{m.unit}</span>}
        <svg viewBox="0 0 280 60" preserveAspectRatio="none" className="pf-metric-svg">
          <line x1="0" y1="59" x2="280" y2="59" stroke="rgb(var(--color-surface-300))" />
          <line x1="0" y1="0"  x2="0"   y2="59" stroke="rgb(var(--color-surface-300))" />
        </svg>
        <div className="pf-metric-xaxis">
          <span>12:15 AM</span><span>4:15 AM</span><span>7:45 AM</span><span>11:45 AM</span><span>3:45 PM</span><span>7:15 PM</span><span>11:15 PM</span>
        </div>
      </div>
    </div>
  );
}
