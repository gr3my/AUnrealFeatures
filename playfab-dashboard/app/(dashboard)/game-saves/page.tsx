"use client";
import { CollectionPage } from "@/components/CollectionTable";

export default function Page() {
  return (
    <CollectionPage
      title="Game Saves"
      subtitle="Per-player file saves stored as entity files"
      name="files"
      keyField="FileName"
      emptyMessage="No saves uploaded yet."
      intro="Saves are owned by player entities. Editing here lets you adjust metadata; uploads go through the SDK."
      columns={[
        { label: "Owner",      key: "EntityId", mono: true },
        { label: "FileName",   key: "FileName", mono: true },
        { label: "Size",       render: (r: any) => `${r.Size ?? 0} B` },
        { label: "Modified",   render: (r: any) => r.LastModified ? new Date(r.LastModified * 1000).toLocaleString() : "—" },
        { label: "ProfileVer", key: "ProfileVersion" },
      ]}
    />
  );
}