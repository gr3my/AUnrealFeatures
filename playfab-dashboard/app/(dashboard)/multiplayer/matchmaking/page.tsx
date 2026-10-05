"use client";
import { CollectionPage } from "@/components/CollectionTable";
import { Badge } from "@/components/Card";

export default function Page() {
  return (
    <CollectionPage
      title="Matchmaking"
      subtitle="Active tickets — delete to cancel a stuck ticket"
      name="matchmaking-tickets"
      keyField="TicketId"
      readOnly={false}
      template={() => ({ TicketId: crypto.randomUUID(), QueueName: "default", CreatorEntityId: "", Status: "Searching", Created: Math.floor(Date.now()/1000) })}
      emptyMessage="No matchmaking tickets."
      intro="Tickets are normally created by the SDK; manual editing is for testing/recovery."
      columns={[
        { label: "TicketId", key: "TicketId", mono: true },
        { label: "Queue",    key: "QueueName" },
        { label: "Creator",  render: (r: any) => <span style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>{r.CreatorEntityId || "—"}</span> },
        { label: "Status",   render: (r: any) => <Badge label={r.Status || "—"} color={r.Status === "Matched" ? "success" : "surface"} /> },
        { label: "Created",  render: (r: any) => r.Created ? new Date(r.Created * 1000).toLocaleString() : "—" },
      ]}
    />
  );
}