"use client";
import { useEffect, useState } from "react";
import { Card, CardHeader, Table, Tr, Td, Badge, Btn, Input, Modal } from "@/components/Card";
import { listVirtualCurrencies, addVirtualCurrencies, VirtualCurrency } from "@/lib/api";

export default function CurrenciesPage() {
  const [vcs, setVcs] = useState<VirtualCurrency[]>([]);
  const [editing, setEditing] = useState<VirtualCurrency | null>(null);
  const [err, setErr] = useState("");

  const refresh = () => listVirtualCurrencies().then((r) => setVcs(r.VirtualCurrencies)).catch((e) => setErr(e.message));
  useEffect(() => { refresh(); }, []);

  const save = async () => {
    if (!editing?.CurrencyCode) return;
    await addVirtualCurrencies([editing]);
    setEditing(null);
    refresh();
  };

  return (
    <div className="space-y-4">
      {err && <div className="rounded p-3 text-sm" style={{ background: "rgb(var(--color-error-500) / 0.1)" }}>{err}</div>}

      <Card>
        <CardHeader title="Virtual currency types" action={
          <Btn variant="primary" onClick={() => setEditing({ CurrencyCode: "", DisplayName: "", InitialDeposit: 0, RechargeRate: 0, RechargeMax: 0 })}>+ New currency</Btn>
        } />
        <Table headers={["Code", "Display Name", "Initial", "Recharge / Day", "Recharge Max", ""]}>
          {vcs.map((v) => (
            <Tr key={v.CurrencyCode}>
              <Td><Badge label={v.CurrencyCode} color="primary" /></Td>
              <Td>{v.DisplayName || "â€”"}</Td>
              <Td>{v.InitialDeposit}</Td>
              <Td>{v.RechargeRate}</Td>
              <Td>{v.RechargeMax}</Td>
              <Td><Btn variant="ghost" onClick={() => setEditing(v)}>Edit</Btn></Td>
            </Tr>
          ))}
          {vcs.length === 0 && <Tr><Td>â€”</Td><Td><span style={{ color: "rgb(var(--color-surface-600))" }}>No VC types defined</span></Td><Td>â€”</Td><Td>â€”</Td><Td>â€”</Td><Td>â€”</Td></Tr>}
        </Table>
      </Card>

      <Modal open={editing != null} onClose={() => setEditing(null)} title={editing?.CurrencyCode ? `Edit ${editing.CurrencyCode}` : "New currency"}>
        {editing && (
          <div className="space-y-3">
            <Input label="Code (e.g. GD, GM)" maxLength={2} value={editing.CurrencyCode} onChange={(e) => setEditing({ ...editing, CurrencyCode: e.target.value.toUpperCase() })} />
            <Input label="Display Name" value={editing.DisplayName} onChange={(e) => setEditing({ ...editing, DisplayName: e.target.value })} />
            <Input label="Initial Deposit" type="number" value={editing.InitialDeposit} onChange={(e) => setEditing({ ...editing, InitialDeposit: parseInt(e.target.value || "0") })} />
            <Input label="Recharge Rate / day" type="number" value={editing.RechargeRate} onChange={(e) => setEditing({ ...editing, RechargeRate: parseInt(e.target.value || "0") })} />
            <Input label="Recharge Max" type="number" value={editing.RechargeMax} onChange={(e) => setEditing({ ...editing, RechargeMax: parseInt(e.target.value || "0") })} />
            <div className="flex justify-end gap-2">
              <Btn variant="ghost" onClick={() => setEditing(null)}>Cancel</Btn>
              <Btn variant="primary" onClick={save}>Save</Btn>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
}
