"use client";
import { CollectionPage } from "@/components/CollectionTable";

export default function Page() {
  return (
    <CollectionPage
      title="Leaderboards (Legacy)"
      subtitle="Legacy v1 statistic leaderboards — entries from PlayfabStatisticDbObject"
      name="stats-v1"
      keyField="StatisticName"
      template={() => ({ PlayFabId: "", CharacterId: "", StatisticName: "Score", Value: 0, Version: 1 })}
      emptyMessage="No legacy statistic entries recorded."
      columns={[
        { label: "Statistic", key: "StatisticName", mono: true },
        { label: "PlayFabId", key: "PlayFabId",     mono: true },
        { label: "Character", key: "CharacterId",   mono: true },
        { label: "Value",     key: "Value" },
        { label: "Version",   key: "Version" },
      ]}
    />
  );
}