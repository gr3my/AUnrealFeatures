"use client";
import { useEffect, useState } from "react";
import {
  getOffers, createOffer, getOfferDisplays, createOfferDisplay,
  getEntitlements, createEntitlement, getTransactions, createTransaction,
  type Offer, type OfferDisplay, type Entitlement, type Transaction,
} from "@/lib/api";
import { Card, CardHeader, Table, Tr, Td, Btn, Input, Modal, Select, Badge } from "@/components/Card";
import { Plus, RefreshCw } from "lucide-react";

type Tab = "offers" | "displays" | "entitlements" | "transactions";

export default function CommercePage() {
  const [tab, setTab] = useState<Tab>("offers");
  const [offers, setOffers] = useState<Offer[]>([]);
  const [displays, setDisplays] = useState<OfferDisplay[]>([]);
  const [entitlements, setEntitlements] = useState<Entitlement[]>([]);
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [modal, setModal] = useState(false);
  const [saving, setSaving] = useState(false);

  const [offerForm, setOfferForm] = useState({ name: "", transaction_id: "", discount_percent: "0" });
  const [displayForm, setDisplayForm] = useState({ name: "", description: "" });
  const [entForm, setEntForm] = useState({ name: "", type: "DURABLE", item_class: "" });
  const [txForm, setTxForm] = useState({ name: "" });

  const load = () => {
    setLoading(true);
    Promise.all([
      getOffers().then(r => setOffers(r.Results ?? [])),
      getOfferDisplays().then(r => setDisplays(r.Results ?? [])),
      getEntitlements().then(r => setEntitlements(r.Results ?? [])),
      getTransactions().then(r => setTransactions(r.Results ?? [])),
    ]).finally(() => setLoading(false));
  };

  useEffect(load, []);

  const save = async () => {
    setSaving(true);
    try {
      if (tab === "offers") {
        await createOffer({ name: offerForm.name, transaction_id: offerForm.transaction_id, discount_percent: parseInt(offerForm.discount_percent) });
        setOfferForm({ name: "", transaction_id: "", discount_percent: "0" });
      } else if (tab === "displays") {
        await createOfferDisplay(displayForm.name, displayForm.description);
        setDisplayForm({ name: "", description: "" });
      } else if (tab === "entitlements") {
        await createEntitlement(entForm.name, entForm.type, entForm.item_class);
        setEntForm({ name: "", type: "DURABLE", item_class: "" });
      } else {
        await createTransaction(txForm.name);
        setTxForm({ name: "" });
      }
      setModal(false);
      load();
    } finally { setSaving(false); }
  };

  const tabClass = (t: Tab) =>
    `px-4 py-2 text-sm font-medium border-b-2 transition-colors ${tab === t ? "border-primary-500 text-primary-500" : "border-transparent text-surface-400 hover:text-white"}`;

  return (
    <div className="space-y-4">
      <div className="flex gap-1 border-b border-surface-600">
        <button className={tabClass("offers")} onClick={() => setTab("offers")}>Offers ({offers.length})</button>
        <button className={tabClass("displays")} onClick={() => setTab("displays")}>Displays ({displays.length})</button>
        <button className={tabClass("entitlements")} onClick={() => setTab("entitlements")}>Entitlements ({entitlements.length})</button>
        <button className={tabClass("transactions")} onClick={() => setTab("transactions")}>Transactions ({transactions.length})</button>
      </div>

      {/* Offers */}
      {tab === "offers" && (
        <Card>
          <CardHeader title="Offers" action={
            <div className="flex gap-2">
              <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
              <Btn onClick={() => setModal(true)}><Plus size={14} className="mr-1 inline" />New Offer</Btn>
            </div>
          } />
          {loading ? <p className="text-surface-400 text-sm py-4 text-center">Loading…</p> : (
            <Table headers={["Offer ID", "Name", "Transaction ID", "Discount"]}>
              {offers.map(o => (
                <Tr key={o.offer_id}>
                  <Td mono>{o.offer_id.slice(0, 12)}…</Td>
                  <Td>{o.name || "–"}</Td>
                  <Td mono>{o.transaction_id || "–"}</Td>
                  <Td><Badge label={`${o.discount_percent}%`} color={o.discount_percent > 0 ? "success" : "surface"} /></Td>
                </Tr>
              ))}
              {offers.length === 0 && <Tr><Td><span className="text-surface-400">No offers</span></Td><Td>–</Td><Td>–</Td><Td>–</Td></Tr>}
            </Table>
          )}
        </Card>
      )}

      {/* Displays */}
      {tab === "displays" && (
        <Card>
          <CardHeader title="Offer Displays" action={
            <div className="flex gap-2">
              <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
              <Btn onClick={() => setModal(true)}><Plus size={14} className="mr-1 inline" />New Display</Btn>
            </div>
          } />
          {loading ? <p className="text-surface-400 text-sm py-4 text-center">Loading…</p> : (
            <Table headers={["Display ID", "Name", "Description"]}>
              {displays.map(d => (
                <Tr key={d.offer_display_id}>
                  <Td mono>{d.offer_display_id.slice(0, 12)}…</Td>
                  <Td>{d.name}</Td>
                  <Td>{d.description || "–"}</Td>
                </Tr>
              ))}
              {displays.length === 0 && <Tr><Td><span className="text-surface-400">No displays</span></Td><Td>–</Td><Td>–</Td></Tr>}
            </Table>
          )}
        </Card>
      )}

      {/* Entitlements */}
      {tab === "entitlements" && (
        <Card>
          <CardHeader title="Entitlement Catalog" action={
            <div className="flex gap-2">
              <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
              <Btn onClick={() => setModal(true)}><Plus size={14} className="mr-1 inline" />New Entitlement</Btn>
            </div>
          } />
          {loading ? <p className="text-surface-400 text-sm py-4 text-center">Loading…</p> : (
            <Table headers={["Entitlement ID", "Name", "Type", "Item Class"]}>
              {entitlements.map(e => (
                <Tr key={e.entitlement_id}>
                  <Td mono>{e.entitlement_id.slice(0, 12)}…</Td>
                  <Td>{e.name}</Td>
                  <Td><Badge label={e.type} color="primary" /></Td>
                  <Td>{e.item_class || "–"}</Td>
                </Tr>
              ))}
              {entitlements.length === 0 && <Tr><Td><span className="text-surface-400">No entitlements</span></Td><Td>–</Td><Td>–</Td><Td>–</Td></Tr>}
            </Table>
          )}
        </Card>
      )}

      {/* Transactions */}
      {tab === "transactions" && (
        <Card>
          <CardHeader title="Transaction Catalog" action={
            <div className="flex gap-2">
              <Btn variant="ghost" onClick={load}><RefreshCw size={14} /></Btn>
              <Btn onClick={() => setModal(true)}><Plus size={14} className="mr-1 inline" />New Transaction</Btn>
            </div>
          } />
          {loading ? <p className="text-surface-400 text-sm py-4 text-center">Loading…</p> : (
            <Table headers={["Transaction ID", "Name"]}>
              {transactions.map(t => (
                <Tr key={t.transaction_id}>
                  <Td mono>{t.transaction_id.slice(0, 12)}…</Td>
                  <Td>{t.name}</Td>
                </Tr>
              ))}
              {transactions.length === 0 && <Tr><Td><span className="text-surface-400">No transactions</span></Td><Td>–</Td></Tr>}
            </Table>
          )}
        </Card>
      )}

      {/* Create modals */}
      <Modal open={modal} onClose={() => setModal(false)}
        title={tab === "offers" ? "New Offer" : tab === "displays" ? "New Offer Display" : tab === "entitlements" ? "New Entitlement" : "New Transaction"}>
        <div className="space-y-3">
          {tab === "offers" && <>
            <Input label="Name *" value={offerForm.name} onChange={e => setOfferForm(f => ({ ...f, name: e.target.value }))} />
            <Input label="Transaction ID" value={offerForm.transaction_id} onChange={e => setOfferForm(f => ({ ...f, transaction_id: e.target.value }))} />
            <Input label="Discount %" type="number" value={offerForm.discount_percent} onChange={e => setOfferForm(f => ({ ...f, discount_percent: e.target.value }))} />
          </>}
          {tab === "displays" && <>
            <Input label="Name *" value={displayForm.name} onChange={e => setDisplayForm(f => ({ ...f, name: e.target.value }))} />
            <Input label="Description" value={displayForm.description} onChange={e => setDisplayForm(f => ({ ...f, description: e.target.value }))} />
          </>}
          {tab === "entitlements" && <>
            <Input label="Name *" value={entForm.name} onChange={e => setEntForm(f => ({ ...f, name: e.target.value }))} />
            <Select label="Type" value={entForm.type} onChange={e => setEntForm(f => ({ ...f, type: e.target.value }))}>
              <option value="DURABLE">DURABLE</option>
              <option value="CONSUMABLE">CONSUMABLE</option>
              <option value="SUBSCRIPTION">SUBSCRIPTION</option>
            </Select>
            <Input label="Item Class" value={entForm.item_class} onChange={e => setEntForm(f => ({ ...f, item_class: e.target.value }))} />
          </>}
          {tab === "transactions" && (
            <Input label="Name *" value={txForm.name} onChange={e => setTxForm(f => ({ ...f, name: e.target.value }))} />
          )}
          <div className="flex justify-end gap-2 pt-2">
            <Btn variant="ghost" onClick={() => setModal(false)}>Cancel</Btn>
            <Btn onClick={save} disabled={saving}>{saving ? "Saving…" : "Create"}</Btn>
          </div>
        </div>
      </Modal>
    </div>
  );
}
