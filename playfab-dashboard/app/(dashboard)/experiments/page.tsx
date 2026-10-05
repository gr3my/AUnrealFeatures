"use client";
import { CollectionPage } from "@/components/CollectionTable";
import { Badge } from "@/components/Card";

export default function Page() {
  return (
    <CollectionPage
      title="Experiments"
      subtitle="A/B feature variants"
      name="experiments"
      keyField="ExperimentId"
      template={() => ({ TitleId: "", ExperimentId: crypto.randomUUID(), Name: "New experiment", Description: "", SegmentId: "", Duration: 168, StartDate: null, State: "New", ExperimentType: "Active", Variants: [], Exclusion: "" })}
      emptyMessage="No experiments defined."
      columns={[
        { label: "ExperimentId", key: "ExperimentId", mono: true },
        { label: "Name",         key: "Name" },
        { label: "Type",         key: "ExperimentType" },
        { label: "State",        render: (r: any) => <Badge label={r.State || "New"} color={r.State === "Running" ? "success" : "surface"} /> },
        { label: "Duration (h)", key: "Duration" },
        { label: "Start",        render: (r: any) => r.StartDate ? new Date(r.StartDate * 1000).toLocaleString() : "—" },
        { label: "Variants",     render: (r: any) => (r.Variants ?? []).length },
      ]}
    />
  );
}