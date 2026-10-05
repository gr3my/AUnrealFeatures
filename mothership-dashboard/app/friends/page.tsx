"use client";
import { useState } from "react";
import { getFriends, type Friend } from "@/lib/api";
import { Card, CardHeader, Table, Tr, Td, Btn, Input, Badge } from "@/components/Card";
import { Search } from "lucide-react";

const privacyLabel = (s: number) => ["VISIBLE", "PUBLIC_ONLY", "HIDDEN"][s] ?? "UNKNOWN";
const privacyColor = (s: number): "success" | "warning" | "error" => {
  if (s === 0) return "success";
  if (s === 1) return "warning";
  return "error";
};

export default function FriendsPage() {
  const [playFabId, setPlayFabId] = useState("");
  const [friends, setFriends] = useState<Friend[]>([]);
  const [privacy, setPrivacy] = useState<number>(0);
  const [loading, setLoading] = useState(false);
  const [fetched, setFetched] = useState(false);
  const [error, setError] = useState("");

  const lookup = async () => {
    if (!playFabId.trim()) return;
    setLoading(true);
    setFetched(true);
    setError("");
    try {
      const r = await getFriends(playFabId.trim());
      setFriends(r.Result?.Friends ?? []);
      setPrivacy(r.Result?.MyPrivacyState ?? 0);
    } catch {
      setError("Failed to load friends — check the PlayFab ID");
      setFriends([]);
    } finally { setLoading(false); }
  };

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader title="Friends Lookup" />
        <div className="flex gap-2">
          <Input className="flex-1" placeholder="PlayFab ID…" value={playFabId}
            onChange={e => setPlayFabId(e.target.value)}
            onKeyDown={e => e.key === "Enter" && lookup()} />
          <Btn onClick={lookup} disabled={!playFabId.trim()}>
            <Search size={14} className="mr-1 inline" />Lookup
          </Btn>
        </div>
      </Card>

      {error && (
        <div className="bg-error-500/10 border border-error-500/30 rounded p-3 text-error-400 text-sm">{error}</div>
      )}

      {fetched && !error && (
        <>
          <Card>
            <CardHeader title="Player Info" />
            <div className="flex items-center gap-4">
              <div>
                <p className="text-xs text-surface-400">PlayFab ID</p>
                <p className="font-mono text-sm text-white">{playFabId}</p>
              </div>
              <div>
                <p className="text-xs text-surface-400">Privacy State</p>
                <Badge label={privacyLabel(privacy)} color={privacyColor(privacy)} />
              </div>
              <div>
                <p className="text-xs text-surface-400">Friend Count</p>
                <p className="text-sm text-white">{friends.length}</p>
              </div>
            </div>
          </Card>

          <Card>
            <CardHeader title={`Friends (${friends.length})`} />
            {loading ? (
              <p className="text-surface-400 text-sm py-4 text-center">Loading…</p>
            ) : (
              <Table headers={["Friend Link ID", "Username", "Room", "Zone", "Region", "Public", "Since"]}>
                {friends.map(f => (
                  <Tr key={f.Presence.FriendLinkId}>
                    <Td mono>{f.Presence.FriendLinkId.slice(0, 12)}…</Td>
                    <Td>{f.Presence.UserName}</Td>
                    <Td>{f.Presence.RoomId || "–"}</Td>
                    <Td>{f.Presence.Zone || "–"}</Td>
                    <Td>{f.Presence.Region || "–"}</Td>
                    <Td><Badge label={f.Presence.IsPublic ? "Yes" : "No"} color={f.Presence.IsPublic ? "success" : "surface"} /></Td>
                    <Td><span className="text-xs text-surface-400">{f.Created ? new Date(f.Created).toLocaleDateString() : "–"}</span></Td>
                  </Tr>
                ))}
                {friends.length === 0 && <Tr><Td><span className="text-surface-400">No friends</span></Td><Td>–</Td><Td>–</Td><Td>–</Td><Td>–</Td><Td>–</Td><Td>–</Td></Tr>}
              </Table>
            )}
          </Card>
        </>
      )}
    </div>
  );
}
