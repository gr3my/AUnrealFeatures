"use client";
import { CollectionPage } from "@/components/CollectionTable";
import { Badge } from "@/components/Card";

export default function Page() {
  return (
    <CollectionPage
      title="Rules"
      subtitle="PlayStream event rules — fire CloudScript handlers when matching events occur"
      name="cloudscript-functions"
      keyField="FunctionName"
      template={() => ({ TitleId: "", FunctionName: "OnPlayerLogin", FunctionUrl: "", TriggerType: "EventGrid", ConnectionString: "", QueueName: "" })}
      emptyMessage="No rules defined."
      intro="Rules are stored as CloudScript-Function bindings with the EventGrid trigger type."
      columns={[
        { label: "Function", key: "FunctionName", mono: true },
        { label: "Trigger",  render: (r: any) => <Badge label={r.TriggerType || "HTTP"} color="surface" /> },
        { label: "URL",      render: (r: any) => <span style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>{r.FunctionUrl || "—"}</span> },
        { label: "Queue",    key: "QueueName" },
      ]}
    />
  );
}