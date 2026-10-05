"use client";
import { CollectionPage } from "@/components/CollectionTable";

export default function Page() {
  return (
    <CollectionPage
      title="Prize Tables (Legacy)"
      subtitle="Random drop tables — items granted by EvalRandomResultTable"
      name="title-data"
      keyField="Key"
      template={() => ({ TitleId: "", Key: "RandomResultTable_New", Value: '{"Nodes":[]}', Scope: "Title", LastUpdated: Math.floor(Date.now()/1000) })}
      emptyMessage="No prize tables defined."
      intro="Legacy v1 random-result tables are stored as title data with the prefix RandomResultTable_*"
      columns={[
        { label: "Key",     key: "Key", mono: true },
        { label: "Scope",   key: "Scope" },
        { label: "Value",   render: (r: any) => <span style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>{(r.Value || "").slice(0, 100)}</span> },
        { label: "Updated", render: (r: any) => r.LastUpdated ? new Date(r.LastUpdated * 1000).toLocaleString() : "—" },
      ]}
    />
  );
}