"use client";

import { Card } from "@/components/Card";
import { PageChrome } from "@/components/PageChrome";

export default function SettingsPage() {
  return (
    <PageChrome title="Settings" subtitle="Dashboard configuration">
      <Card>
        <h3 className="h3" style={{ margin: "0 0 8px" }}>Backend connection</h3>
        <p style={{ fontSize: 13, color: "rgb(var(--color-surface-700))", margin: 0 }}>
          Configure <code>PLAYFAB_URL</code> and <code>PLAYFAB_DEV_SECRET</code> on the dashboard server.
          The browser talks to the local <code>/pf/*</code> proxy, which adds the admin secret server-side.
          Restart the dashboard after changing either value.
        </p>
      </Card>
    </PageChrome>
  );
}
