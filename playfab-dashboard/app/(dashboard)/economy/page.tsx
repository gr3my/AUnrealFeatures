"use client";
import { useEffect, useState } from "react";
import { Card, CardHeader, Table, Tr, Td, Badge, Input, Btn, Modal } from "@/components/Card";
import { listEconomyItems } from "@/lib/api";

export default function EconomyPage() {
  const [items, setItems] = useState<any[]>([]);
  const [total, setTotal] = useState(0);
  const [skip, setSkip] = useState(0);
  const take = 50;
  const [query, setQuery] = useState("");
  const [detail, setDetail] = useState<any>(null);
  const [err, setErr] = useState("");

  useEffect(() => {
    listEconomyItems(skip, take, query)
      .then((r) => { setItems(r.Items); setTotal(r.Total); }).catch((e) => setErr(e.message));
  }, [skip, query]);

  return (
    <div className="space-y-4">
      {err && <div className="rounded p-3 text-sm" style={{ background: "rgb(var(--color-error-500) / 0.1)" }}>{err}</div>}

      <Card>
        <div className="flex items-center gap-3 mb-3">
          <Input placeholder="Search items by id / type / tag…" value={query} onChange={(e) => { setSkip(0); setQuery(e.target.value); }} className="flex-1" />
          <Badge label={`${total} items`} color="primary" />
        </div>
        <Table headers={["Item ID", "Type", "Tags", "Status", "Created", ""]}>
          {items.map((i) => (
            <Tr key={i.Id}>
              <Td><span className="font-mono text-xs" style={{ color: "rgb(var(--color-primary-500))" }}>{i.ItemId}</span></Td>
              <Td><Badge label={i.Type || "?"} color="secondary" /></Td>
              <Td>
                <div className="flex flex-wrap gap-1">
                  {(i.Tags ?? []).slice(0, 3).map((t: string) => <Badge key={t} label={t} color="surface" />)}
                </div>
              </Td>
              <Td><Badge label={i.Status || "—"} color={i.Status === "Published" ? "success" : "warning"} /></Td>
              <Td>{i.Created ? new Date(i.Created * 1000).toLocaleString() : "—"}</Td>
              <Td><Btn variant="ghost" onClick={() => setDetail(i)}>View</Btn></Td>
            </Tr>
          ))}
          {items.length === 0 && <Tr><Td>—</Td><Td>No items</Td><Td>—</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
        </Table>
        <div className="flex justify-between items-center mt-3">
          <Btn variant="ghost" onClick={() => setSkip(Math.max(0, skip - take))} disabled={skip === 0}>← Prev</Btn>
          <span className="text-xs" style={{ color: "rgb(var(--color-surface-600))" }}>{skip + 1}–{Math.min(skip + take, total)} of {total}</span>
          <Btn variant="ghost" onClick={() => setSkip(skip + take)} disabled={skip + take >= total}>Next →</Btn>
        </div>
      </Card>

      <Modal open={detail != null} onClose={() => setDetail(null)} title={detail?.ItemId ?? ""}>
        {detail && (
          <pre className="text-xs overflow-auto max-h-[70vh]" style={{ color: "rgb(var(--color-surface-900))" }}>
            {JSON.stringify(detail, null, 2)}
          </pre>
        )}
      </Modal>
    </div>
  );
}
