"use client";
import { useEffect, useState } from "react";
import { Card, Btn, Table, Tr, Td } from "@/components/Card";
import { PageChrome } from "@/components/PageChrome";
import { listCollection, getTitleSettings } from "@/lib/api";

export default function Page() {
  const [stats, setStats] = useState<any>({});
  const [busy, setBusy]   = useState(false);

  const refresh = async () => {
    setBusy(true);
    try {
      const [t, players, events, cs, txns, files, currencies, news] = await Promise.all([
        getTitleSettings(),
        listCollection<any>("title-data", 0, 1, ""),
        listCollection<any>("events", 0, 1, ""),
        listCollection<any>("cloudscript-logs", 0, 1, ""),
        listCollection<any>("transactions", 0, 1, ""),
        listCollection<any>("files", 0, 1, ""),
        listCollection<any>("currencies", 0, 1, ""),
        listCollection<any>("title-news", 0, 1, ""),
      ]);
      setStats({ titleSettings: t, players: players.Total, events: events.Total, cs: cs.Total, txns: txns.Total, files: files.Total, currencies: currencies.Total, news: news.Total });
    } finally { setBusy(false); }
  };

  useEffect(() => { refresh(); }, []);

  const t: any = stats.titleSettings || {};
  return (
    <PageChrome title="Reports" subtitle="Snapshot of every persisted record class">
      <Card>
        <div className="flex items-center justify-between mb-3">
          <h3 className="h3" style={{ margin: 0 }}>Title summary</h3>
          <Btn variant="secondary" onClick={refresh} disabled={busy}>{busy ? "Loading…" : "Refresh"}</Btn>
        </div>
        <Table headers={["Counter", "Value"]}>
          <Tr><Td>Title Id</Td><Td><span style={{ fontFamily: "var(--font-mono)" }}>{t.TitleId ?? "—"}</span></Td></Tr>
          <Tr><Td>Players</Td><Td>{t.PlayerCount ?? 0}</Td></Tr>
          <Tr><Td>Catalog items</Td><Td>{t.CatalogItemCount ?? 0}</Td></Tr>
          <Tr><Td>News count</Td><Td>{t.NewsCount ?? 0}</Td></Tr>
          <Tr><Td>CloudScript revision</Td><Td>{t.CloudScriptVersion ?? 1}</Td></Tr>
          <Tr><Td>Photon configured</Td><Td>{t.PhotonConfigured ? "Yes" : "No"}</Td></Tr>
          <Tr><Td>Title-data keys (public)</Td><Td>{stats.players ?? 0}</Td></Tr>
          <Tr><Td>Events</Td><Td>{stats.events ?? 0}</Td></Tr>
          <Tr><Td>CloudScript logs</Td><Td>{stats.cs ?? 0}</Td></Tr>
          <Tr><Td>Transactions (V2)</Td><Td>{stats.txns ?? 0}</Td></Tr>
          <Tr><Td>Files</Td><Td>{stats.files ?? 0}</Td></Tr>
          <Tr><Td>Virtual currency types</Td><Td>{stats.currencies ?? 0}</Td></Tr>
          <Tr><Td>Title news</Td><Td>{stats.news ?? 0}</Td></Tr>
        </Table>
      </Card>
    </PageChrome>
  );
}