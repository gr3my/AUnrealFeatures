"use client";
import { useEffect, useState } from "react";
import { Card, CardHeader, Table, Tr, Td, Badge, Input } from "@/components/Card";
import { listGroups } from "@/lib/api";

export default function GroupsPage() {
  const [groups, setGroups] = useState<any[]>([]);
  const [total, setTotal] = useState(0);
  const [query, setQuery] = useState("");
  const [err, setErr] = useState("");

  useEffect(() => {
    listGroups(0, 100, query).then((r) => { setGroups(r.Groups); setTotal(r.Total); }).catch((e) => setErr(e.message));
  }, [query]);

  return (
    <div className="space-y-4">
      {err && <div className="rounded p-3 text-sm" style={{ background: "rgb(var(--color-error-500) / 0.1)" }}>{err}</div>}

      <Card>
        <div className="flex items-center gap-3 mb-3">
          <Input placeholder="Search groups…" value={query} onChange={(e) => setQuery(e.target.value)} className="flex-1" />
          <Badge label={`${total} total`} color="primary" />
        </div>
        <Table headers={["Group ID", "Name", "Members", "Admin Role", "Member Role", "Created"]}>
          {groups.map((g) => (
            <Tr key={g.GroupId}>
              <Td><span className="font-mono text-xs">{g.GroupId}</span></Td>
              <Td>{g.GroupName}</Td>
              <Td>{g.MemberCount}</Td>
              <Td><Badge label={g.AdminRoleId} color="primary" /></Td>
              <Td><Badge label={g.MemberRoleId} color="secondary" /></Td>
              <Td>{new Date(g.Created * 1000).toLocaleString()}</Td>
            </Tr>
          ))}
          {groups.length === 0 && <Tr><Td>—</Td><Td>No groups</Td><Td>—</Td><Td>—</Td><Td>—</Td><Td>—</Td></Tr>}
        </Table>
      </Card>
    </div>
  );
}
