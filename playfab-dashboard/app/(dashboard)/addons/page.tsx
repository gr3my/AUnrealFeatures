"use client";
import { CollectionPage } from "@/components/CollectionTable";

export default function Page() {
  return (
    <CollectionPage
      title="Add-ons"
      subtitle="Title add-on configurations (Apple, Facebook, Google, Steam, etc.)"
      name="addons"
      keyField="AddonName"
      template={() => ({ TitleId: "", AddonName: "Steam", ConfigJson: "{}", LastUpdated: Math.floor(Date.now()/1000) })}
      emptyMessage="No add-ons configured."
      columns={[
        { label: "Add-on",  key: "AddonName", mono: true },
        { label: "Title",   key: "TitleId" },
        { label: "Config",  render: (r: any) => <span style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>{(r.ConfigJson || "").slice(0, 80)}</span> },
        { label: "Updated", render: (r: any) => r.LastUpdated ? new Date(r.LastUpdated * 1000).toLocaleString() : "—" },
      ]}
    />
  );
}