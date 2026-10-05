"use client";
import { CollectionPage } from "@/components/CollectionTable";

export default function Page() {
  return (
    <CollectionPage
      title="Audit History"
      subtitle="Recent administrative and system events"
      name="events"
      readOnly
      emptyMessage="No events captured yet."
      intro="Audit history is derived from the title.player and title.events PlayStream namespaces. Read-only."
      columns={[
        { label: "Event",     key: "EventName",      mono: true },
        { label: "Namespace", key: "EventNamespace" },
        { label: "Entity",    render: (r: any) => <span style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>{r.EntityId || "—"}</span> },
        { label: "Time",      render: (r: any) => r.Timestamp ? new Date(r.Timestamp).toLocaleString() : "—" },
        { label: "Payload",   render: (r: any) => <span style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>{(r.PayloadJson || "").slice(0, 80)}</span> },
      ]}
    />
  );
}