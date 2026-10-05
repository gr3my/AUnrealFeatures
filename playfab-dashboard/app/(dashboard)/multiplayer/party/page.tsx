"use client";
import { CollectionPage } from "@/components/CollectionTable";

export default function Page() {
  return (
    <CollectionPage
      title="Party"
      subtitle="Voice and party rooms"
      name="parties"
      keyField="PartyId"
      readOnly
      emptyMessage="No active parties."
      intro="Parties are created by the SDK at runtime; this page is read-only."
      columns={[
        { label: "PartyId", key: "PartyId",   mono: true },
        { label: "Network", key: "NetworkId", mono: true },
        { label: "Members", render: (r: any) => (r.Members ?? []).length },
        { label: "Created", render: (r: any) => r.Created ? new Date(r.Created * 1000).toLocaleString() : "—" },
      ]}
    />
  );
}