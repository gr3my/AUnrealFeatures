"use client";
import { CollectionPage } from "@/components/CollectionTable";

export default function Page() {
  return (
    <CollectionPage
      title="Webhooks"
      subtitle="Outbound event webhooks (HTTP-triggered CloudScript functions)"
      name="cloudscript-functions"
      keyField="FunctionName"
      template={() => ({ TitleId: "", FunctionName: "MyWebhook", FunctionUrl: "https://example.invalid/webhook", TriggerType: "HTTP", ConnectionString: "", QueueName: "" })}
      emptyMessage="No webhook handlers registered."
      intro="HTTP-triggered CloudScript functions act as webhook receivers. The Photon webhook log lives at /photon."
      columns={[
        { label: "Function", key: "FunctionName", mono: true },
        { label: "Trigger",  key: "TriggerType" },
        { label: "URL",      render: (r: any) => <span style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>{r.FunctionUrl || "—"}</span> },
      ]}
    />
  );
}