"use client";
import { CollectionPage } from "@/components/CollectionTable";

export default function Page() {
  return (
    <CollectionPage
      title="Segments"
      subtitle="Player segments for targeting and analytics"
      name="segments"
      keyField="SegmentId"
      template={() => ({ TitleId: "", SegmentId: crypto.randomUUID(), Name: "New segment", DefinitionJson: "{}", Created: Math.floor(Date.now()/1000) })}
      emptyMessage="No segments defined."
      columns={[
        { label: "SegmentId", key: "SegmentId", mono: true },
        { label: "Name",      key: "Name" },
        { label: "Title",     key: "TitleId" },
        { label: "Created",   render: (r: any) => r.Created ? new Date(r.Created * 1000).toLocaleString() : "—" },
      ]}
    />
  );
}