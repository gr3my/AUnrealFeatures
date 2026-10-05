"use client";
import { CollectionPage } from "@/components/CollectionTable";

export default function Page() {
  return (
    <CollectionPage
      title="OpenID Connect"
      subtitle="Federated identity providers configured for the title"
      name="addons"
      keyField="AddonName"
      template={() => ({ TitleId: "", AddonName: "OpenIdConnect_MyProvider", ConfigJson: '{"Issuer":"","ClientId":"","ClientSecret":""}', LastUpdated: Math.floor(Date.now()/1000) })}
      emptyMessage="No OpenID providers registered."
      intro="OpenID Connect providers are stored as add-on configs with AddonName=OpenIdConnect_*"
      columns={[
        { label: "AddonName", key: "AddonName", mono: true },
        { label: "Title",     key: "TitleId" },
        { label: "Config",    render: (r: any) => <span style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>{(r.ConfigJson || "").slice(0, 80)}</span> },
        { label: "Updated",   render: (r: any) => r.LastUpdated ? new Date(r.LastUpdated * 1000).toLocaleString() : "—" },
      ]}
    />
  );
}