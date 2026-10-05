"use client";
import { useEffect, useState } from "react";
import { Card, CardHeader, Table, Tr, Td, Btn, Input, Textarea, Modal } from "@/components/Card";
import { getNews, addNews, deleteNews } from "@/lib/api";

export default function NewsPage() {
  const [news, setNews] = useState<any[]>([]);
  const [editing, setEditing] = useState<{ title: string; body: string } | null>(null);
  const [err, setErr] = useState("");

  const refresh = () => getNews().then((r) => setNews(r.News)).catch((e) => setErr(e.message));
  useEffect(() => { refresh(); }, []);

  const save = async () => {
    if (!editing?.title) return;
    await addNews(editing.title, editing.body);
    setEditing(null);
    refresh();
  };

  const remove = async (id: string) => {
    if (!confirm("Delete this news item?")) return;
    await deleteNews(id);
    refresh();
  };

  return (
    <div className="space-y-4">
      {err && <div className="rounded p-3 text-sm" style={{ background: "rgb(var(--color-error-500) / 0.1)" }}>{err}</div>}

      <Card>
        <CardHeader title="Title news" action={
          <Btn variant="primary" onClick={() => setEditing({ title: "", body: "" })}>+ New post</Btn>
        } />
        <Table headers={["When", "Title", "Body", ""]}>
          {news.map((n) => (
            <Tr key={n.NewsId}>
              <Td>{new Date(n.Timestamp * 1000).toLocaleString()}</Td>
              <Td><strong>{n.Title}</strong></Td>
              <Td>{n.Body?.length > 80 ? n.Body.substring(0, 80) + "…" : n.Body}</Td>
              <Td><Btn variant="danger" onClick={() => remove(n.NewsId)}>Delete</Btn></Td>
            </Tr>
          ))}
          {news.length === 0 && <Tr><Td>—</Td><Td>No news posted</Td><Td>—</Td><Td>—</Td></Tr>}
        </Table>
      </Card>

      <Modal open={editing != null} onClose={() => setEditing(null)} title="Post a news item">
        {editing && (
          <div className="space-y-3">
            <Input label="Title" value={editing.title} onChange={(e) => setEditing({ ...editing, title: e.target.value })} />
            <Textarea label="Body (markdown OK)" rows={8} value={editing.body} onChange={(e) => setEditing({ ...editing, body: e.target.value })} />
            <div className="flex justify-end gap-2">
              <Btn variant="ghost" onClick={() => setEditing(null)}>Cancel</Btn>
              <Btn variant="primary" onClick={save}>Post</Btn>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
}
