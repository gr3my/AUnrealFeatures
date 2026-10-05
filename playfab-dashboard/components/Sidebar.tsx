"use client";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";
import {
  LayoutGrid, LogIn, Gamepad2, Store, Users2, MessageSquare, TrendingUp,
  BarChart3, Settings, ChevronDown, ChevronRight, MoreHorizontal,
} from "lucide-react";

interface NavItem { href: string; label: string; }
interface NavGroup { id: string; label: string; icon: any; items: NavItem[]; }

const TOP: NavItem  = { href: "/",         label: "Title Overview" };
const BOT: NavItem  = { href: "/settings", label: "Settings" };

const groups: NavGroup[] = [
  { id: "identity",   label: "Identity",   icon: LogIn, items: [
    { href: "/players",  label: "Players" },
    { href: "/openid",   label: "OpenID Connect" },
  ]},
  { id: "liveops",    label: "Live Ops",   icon: Gamepad2, items: [
    { href: "/addons",          label: "Add-ons" },
    { href: "/audit",           label: "Audit History" },
    { href: "/content",         label: "Content" },
    { href: "/cloudscript",     label: "Cloud Script" },
    { href: "/experiments",     label: "Experiments" },
    { href: "/rules",           label: "Rules" },
    { href: "/segments",        label: "Segments" },
    { href: "/scheduled-tasks", label: "Scheduled Tasks" },
  ]},
  { id: "economy",    label: "Economy & UGC", icon: Store, items: [
    { href: "/economy",       label: "Catalog" },
    { href: "/economy/settings", label: "Economy settings" },
    { href: "/rewarded-ads",  label: "Rewarded Ads" },
    { href: "/catalog",       label: "Catalogs (Legacy)" },
    { href: "/currencies",    label: "Currency (Legacy)" },
  ]},
  { id: "multiplayer", label: "Multiplayer", icon: Users2, items: [
    { href: "/multiplayer/party",       label: "Party" },
    { href: "/multiplayer",             label: "Servers" },
    { href: "/multiplayer/matchmaking", label: "Matchmaking" },
  ]},
  { id: "community",   label: "Community", icon: MessageSquare, items: [
    { href: "/groups",                label: "Groups" },
    { href: "/shared-groups",         label: "Shared Groups" },
    { href: "/prize-tables",          label: "Prize Tables (Legacy)" },
    { href: "/leaderboards",          label: "Leaderboards" },
    { href: "/leaderboards/legacy",   label: "Leaderboards (Legacy)" },
  ]},
  { id: "progression", label: "Progression", icon: TrendingUp, items: [
    { href: "/game-saves",     label: "Game Saves" },
    { href: "/stats",          label: "Statistics" },
    { href: "/title-data",     label: "Title Data" },
  ]},
  { id: "datastream",  label: "Game Data Stream", icon: BarChart3, items: [
    { href: "/events",         label: "Events" },
    { href: "/news",           label: "Title News" },
    { href: "/photon",         label: "Photon" },
    { href: "/bans",           label: "Bans" },
    { href: "/reports",        label: "Reports" },
    { href: "/telemetry-keys", label: "Telemetry Keys" },
    { href: "/webhooks",       label: "Webhooks" },
    { href: "/data-explorer",  label: "Data Explorer" },
  ]},
];

export default function Sidebar() {
  const path = usePathname();

  // A group is expanded by default if any of its items is the current page,
  // or if the user explicitly toggles it open.
  const initialOpen = () =>
    groups.reduce<Record<string, boolean>>((acc, g) => {
      acc[g.id] = g.items.some(i => path.startsWith(i.href));
      return acc;
    }, {});

  const [open, setOpen] = useState<Record<string, boolean>>(initialOpen);

  const isActive = (href: string) => href === "/" ? path === "/" : path === href || path.startsWith(href + "/");

  return (
    <aside className="pf-sidebar">
      {/* Studio chooser */}
      <button className="pf-studio-chooser" aria-label="Studio menu">
        <span className="pf-studio-avatar">
          <span style={{ width: 4, height: 4, background: "#ec407a", borderRadius: "50%", display: "inline-block" }} />
          <span style={{ width: 4, height: 4, background: "#42a5f5", borderRadius: "50%", display: "inline-block" }} />
        </span>
        <span className="pf-studio-text">
          <span className="pf-studio-name">My Game</span>
          <span className="pf-studio-env">Development</span>
        </span>
        <MoreHorizontal size={14} className="pf-studio-more" />
      </button>
      <div className="pf-studio-meter">
        <span className="pf-studio-meter-label">Development</span>
        <ChevronDown size={12} />
        <span className="pf-studio-meter-value">49/100K</span>
      </div>

      <div className="pf-sidebar-divider" />

      {/* Top item */}
      <NavLink item={TOP} icon={LayoutGrid} active={isActive(TOP.href)} />

      {/* Collapsible groups */}
      {groups.map((g) => {
        const expanded = !!open[g.id];
        const Icon = g.icon;
        return (
          <div key={g.id}>
            <button
              className="pf-nav-group-header"
              onClick={() => setOpen({ ...open, [g.id]: !expanded })}
              aria-expanded={expanded}
            >
              {expanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
              <Icon size={16} className="pf-nav-icon" />
              <span>{g.label}</span>
            </button>
            {expanded && g.items.map((i) => (
              <Link key={i.href + i.label} href={i.href} className={"pf-nav-item pf-nav-item-nested" + (isActive(i.href) ? " active" : "")}>
                <span>{i.label}</span>
              </Link>
            ))}
          </div>
        );
      })}

      <div className="pf-sidebar-divider" />

      <NavLink item={BOT} icon={Settings} active={isActive(BOT.href)} />
    </aside>
  );
}

function NavLink({ item, icon: Icon, active }: { item: NavItem; icon: any; active: boolean }) {
  return (
    <Link href={item.href} className={"pf-nav-item" + (active ? " active" : "")}>
      <Icon size={16} className="pf-nav-icon" />
      <span>{item.label}</span>
    </Link>
  );
}
