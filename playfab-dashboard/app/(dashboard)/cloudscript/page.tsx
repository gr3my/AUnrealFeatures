"use client";
import { useEffect, useState } from "react";
import { Card, CardHeader, Table, Tr, Td, Badge, Btn, Input, Textarea, Modal } from "@/components/Card";
import {
  getCloudScriptRevision, getCloudScriptVersions, updateCloudScript,
  setPublishedRevision, getCloudScriptLogs, getCloudScriptLog,
  CloudScriptFile,
} from "@/lib/api";

export default function CloudScriptPage() {
  const [versions, setVersions] = useState<any[]>([]);
  const [files, setFiles] = useState<CloudScriptFile[]>([]);
  const [activeFile, setActiveFile] = useState<string>("");
  const [logs, setLogs] = useState<any[]>([]);
  const [logDetail, setLogDetail] = useState<any>(null);
  const [err, setErr] = useState("");

  const refresh = async () => {
    try {
      setVersions((await getCloudScriptVersions()).Versions);
      const cur = await getCloudScriptRevision();
      setFiles(cur.Files);
      if (cur.Files.length > 0 && !activeFile) setActiveFile(cur.Files[0].Filename);
      setLogs((await getCloudScriptLogs(0, 50)).Logs);
      setErr("");
    } catch (e: any) { setErr(e.message ?? String(e)); }
  };

  useEffect(() => { refresh(); }, []);

  const upload = async (publish: boolean) => {
    if (files.length === 0) return alert("Add a file first");
    await updateCloudScript(files, publish);
    refresh();
  };

  const addFile = () => {
    const name = prompt("New file name (e.g. main.js)?");
    if (!name) return;
    setFiles([...files, { Filename: name, FileContents: "" }]);
    setActiveFile(name);
  };

  const updateActive = (contents: string) => {
    setFiles(files.map((f) => f.Filename === activeFile ? { ...f, FileContents: contents } : f));
  };

  const removeFile = (name: string) => {
    if (!confirm(`Remove '${name}' from this revision?`)) return;
    setFiles(files.filter((f) => f.Filename !== name));
    if (activeFile === name && files.length > 1) setActiveFile(files[0].Filename);
  };

  const openLog = (id: string) => getCloudScriptLog(id).then(setLogDetail);

  const promote = async (rev: number) => {
    await setPublishedRevision(rev);
    refresh();
  };

  const active = files.find((f) => f.Filename === activeFile);

  return (
    <div className="space-y-4">
      {err && <div className="rounded p-3 text-sm" style={{ background: "rgb(var(--color-error-500) / 0.1)" }}>{err}</div>}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <Card className="lg:col-span-2">
          <CardHeader title="JavaScript files" action={
            <div className="flex gap-2">
              <Btn variant="ghost" onClick={addFile}>+ File</Btn>
              <Btn variant="secondary" onClick={() => upload(false)}>Save (test)</Btn>
              <Btn variant="primary"   onClick={() => upload(true)}>Save & Publish</Btn>
            </div>
          } />

          <div className="flex gap-1 mb-3 flex-wrap">
            {files.map((f) => (
              <button
                key={f.Filename}
                onClick={() => setActiveFile(f.Filename)}
                className="px-3 py-1 text-xs rounded font-mono"
                style={f.Filename === activeFile ? {
                  background: "rgb(var(--color-primary-500) / 0.15)",
                  color: "rgb(var(--color-primary-500))",
                  border: "1px solid rgb(var(--color-primary-500) / 0.3)",
                } : { background: "rgb(var(--color-surface-100))", color: "rgb(var(--color-surface-700))" }}
              >
                {f.Filename}
                <span onClick={(e) => { e.stopPropagation(); removeFile(f.Filename); }} className="ml-2 opacity-50 hover:opacity-100">Ã—</span>
              </button>
            ))}
          </div>

          {active ? (
            <Textarea
              rows={24}
              value={active.FileContents}
              onChange={(e) => updateActive(e.target.value)}
              className="font-mono text-xs"
              placeholder='handlers.helloWorld = function(args, context) {&#10;    log.info("hi from " + currentPlayerId);&#10;    return { greeting: "Hello " + args.name };&#10;};'
              style={{ width: "100%" }}
            />
          ) : (
            <p style={{ color: "rgb(var(--color-surface-600))" }}>No files. Click "+ File" to add one.</p>
          )}
        </Card>

        <Card>
          <CardHeader title="Revisions" />
          <Table headers={["Rev", "Status", ""]}>
            {versions.map((v) => (
              <Tr key={v.Version}>
                <Td>#{v.Version}</Td>
                <Td>{v.PublishedRevision === v.Version ? <Badge label="LIVE" color="success" /> : <Badge label="archived" color="surface" />}</Td>
                <Td>{v.PublishedRevision !== v.Version && <Btn variant="ghost" onClick={() => promote(v.Version)}>Promote</Btn>}</Td>
              </Tr>
            ))}
            {versions.length === 0 && <Tr><Td>â€”</Td><Td>No revisions yet</Td><Td>â€”</Td></Tr>}
          </Table>
        </Card>
      </div>

      <Card>
        <CardHeader title="Recent execution logs" action={<Btn variant="ghost" onClick={refresh}>Refresh</Btn>} />
        <Table headers={["When", "Function", "Caller", "Time", "Status", ""]}>
          {logs.map((l) => (
            <Tr key={l.ExecutionId}>
              <Td>{new Date(l.Timestamp * 1000).toLocaleTimeString()}</Td>
              <Td><span className="font-mono text-xs">{l.FunctionName}</span></Td>
              <Td><span className="font-mono text-xs">{l.PlayFabId || "â€”"}</span></Td>
              <Td>{l.ExecutionTimeSeconds}s</Td>
              <Td>{l.Error ? <Badge label="error" color="error" /> : <Badge label="ok" color="success" />}</Td>
              <Td><Btn variant="ghost" onClick={() => openLog(l.ExecutionId)}>View</Btn></Td>
            </Tr>
          ))}
          {logs.length === 0 && <Tr><Td>â€”</Td><Td>No executions yet</Td><Td>â€”</Td><Td>â€”</Td><Td>â€”</Td><Td>â€”</Td></Tr>}
        </Table>
      </Card>

      <Modal open={logDetail != null} onClose={() => setLogDetail(null)} title={logDetail?.FunctionName ?? ""}>
        {logDetail && (
          <pre className="text-xs overflow-auto max-h-[70vh]" style={{ color: "rgb(var(--color-surface-900))" }}>
            {JSON.stringify(logDetail, null, 2)}
          </pre>
        )}
      </Modal>
    </div>
  );
}
