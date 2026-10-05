"use client";
import { CollectionPage } from "@/components/CollectionTable";

export default function Page() {
  return (
    <CollectionPage
      title="Rewarded Ads"
      subtitle="Ad placements and the rewards they grant"
      name="ad-placements"
      keyField="PlacementId"
      template={() => ({ TitleId: "", PlacementId: crypto.randomUUID(), Name: "New placement", Network: "Unity", RewardType: "VirtualCurrency", RewardId: "GD" })}
      emptyMessage="No ad placements configured."
      columns={[
        { label: "PlacementId", key: "PlacementId", mono: true },
        { label: "Name",        key: "Name" },
        { label: "Network",     key: "Network" },
        { label: "Reward type", key: "RewardType" },
        { label: "Reward",      key: "RewardId" },
      ]}
    />
  );
}