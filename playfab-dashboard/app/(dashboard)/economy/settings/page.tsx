"use client";
import { CollectionPage } from "@/components/CollectionTable";

export default function Page() {
  return (
    <CollectionPage
      title="Economy settings"
      subtitle="Catalog v2 configuration — item types, display properties, content types"
      name="catalog-config"
      keyField="TitleId"
      template={() => ({ TitleId: "", ConfigJson: '{"ItemTypes":[],"DisplayProperties":[]}', LastUpdated: Math.floor(Date.now()/1000) })}
      emptyMessage="No catalog config persisted yet."
      columns={[
        { label: "Title",   key: "TitleId" },
        { label: "Config",  render: (r: any) => <span style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>{(r.ConfigJson || "").slice(0, 200)}</span> },
        { label: "Updated", render: (r: any) => r.LastUpdated ? new Date(r.LastUpdated * 1000).toLocaleString() : "—" },
      ]}
    />
  );
}