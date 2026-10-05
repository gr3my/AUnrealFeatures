"use client";
import { CollectionPage } from "@/components/CollectionTable";
import { Badge } from "@/components/Card";

export default function Page() {
  return (
    <CollectionPage
      title="Scheduled Tasks"
      subtitle="Cron-driven CloudScript executions and segment actions"
      name="cloudscript-functions"
      keyField="FunctionName"
      template={() => ({ TitleId: "", FunctionName: "DailyResetTask", FunctionUrl: "", TriggerType: "Queue", ConnectionString: "", QueueName: "scheduled" })}
      emptyMessage="No scheduled tasks defined."
      intro="Scheduled CloudScript invocations register here with TriggerType='Queue'."
      columns={[
        { label: "Function", key: "FunctionName", mono: true },
        { label: "Trigger",  render: (r: any) => <Badge label={r.TriggerType || "HTTP"} color={r.TriggerType === "Queue" ? "primary" : "surface"} /> },
        { label: "Queue",    key: "QueueName" },
        { label: "URL",      render: (r: any) => <span style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>{r.FunctionUrl || "—"}</span> },
      ]}
    />
  );
}