"use client";
import { CollectionPage } from "@/components/CollectionTable";
import { Badge } from "@/components/Card";

export default function Page() {
  return (
    <CollectionPage
      title="Telemetry Keys"
      subtitle="API keys for unauthenticated telemetry ingestion"
      name="telemetry-keys"
      keyField="Name"
      template={() => ({ TitleId: "", Name: "telemetry-prod", KeyValue: crypto.randomUUID().replace(/-/g, ""), IsActive: true, Created: Math.floor(Date.now()/1000) })}
      emptyMessage="No telemetry keys generated."
      columns={[
        { label: "Name",     key: "Name", mono: true },
        { label: "Key",      render: (r: any) => <span style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>{(r.KeyValue || "").slice(0, 32)}…</span> },
        { label: "State",    render: (r: any) => <Badge label={r.IsActive ? "active" : "inactive"} color={r.IsActive ? "success" : "surface"} /> },
        { label: "Created",  render: (r: any) => r.Created ? new Date(r.Created * 1000).toLocaleString() : "—" },
      ]}
    />
  );
}