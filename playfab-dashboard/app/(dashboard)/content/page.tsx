"use client";
import { CollectionPage } from "@/components/CollectionTable";

export default function Page() {
  return (
    <CollectionPage
      title="Content"
      subtitle="Title-uploaded files served via the Content/* APIs"
      name="content"
      keyField="Key"
      template={() => ({ TitleId: "", Key: "myfile.bin", ContentType: "application/octet-stream", Size: 0, Uploaded: Math.floor(Date.now()/1000), Url: "" })}
      emptyMessage="No content uploaded."
      columns={[
        { label: "Key",        key: "Key", mono: true },
        { label: "Type",       key: "ContentType" },
        { label: "Size",       render: (r: any) => `${r.Size ?? 0} B` },
        { label: "Uploaded",   render: (r: any) => r.Uploaded ? new Date(r.Uploaded * 1000).toLocaleString() : "—" },
      ]}
    />
  );
}