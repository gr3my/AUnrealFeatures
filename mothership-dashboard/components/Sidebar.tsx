"use client";
import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  LayoutDashboard, Users, BookOpen, Globe, Rocket,
  ShieldAlert, FileText, Database, ShoppingBag, TrendingUp,
  Key, UserCheck, BarChart2, ChevronRight,
} from "lucide-react";

const nav = [
  { href: "/",              icon: LayoutDashboard, label: "Overview" },
  { href: "/players",       icon: Users,           label: "Players" },
  { href: "/titles",        icon: BookOpen,        label: "Titles" },
  { href: "/environments",  icon: Globe,           label: "Environments" },
  { href: "/deployments",   icon: Rocket,          label: "Deployments" },
  { href: "/moderation",    icon: ShieldAlert,     label: "Moderation" },
  { href: "/title-data",    icon: FileText,        label: "Title Data" },
  { href: "/user-data",     icon: Database,        label: "User Data" },
  { href: "/commerce",      icon: ShoppingBag,     label: "Commerce" },
  { href: "/progression",   icon: TrendingUp,      label: "Progression" },
  { href: "/api-keys",      icon: Key,             label: "API Keys" },
  { href: "/friends",       icon: UserCheck,       label: "Friends" },
  { href: "/analytics",     icon: BarChart2,       label: "Analytics" },
];

export default function Sidebar() {
  const path = usePathname();
  return (
    <aside
      className="w-56 flex-shrink-0 flex flex-col"
      style={{ background: "rgb(var(--color-surface-800))", borderRight: "1px solid rgb(var(--color-surface-600))" }}
    >
      {/* Nav */}
      <nav className="flex-1 overflow-y-auto py-3">
        {nav.map(({ href, icon: Icon, label }) => {
          const active = path === href || (href !== "/" && path.startsWith(href));
          return (
            <Link
              key={href}
              href={href}
              style={active ? {
                display: "flex",
                alignItems: "center",
                gap: "0.75rem",
                padding: "0.625rem 1rem",
                margin: "0 0.5rem",
                borderRadius: "4px",
                fontSize: "0.875rem",
                fontWeight: 500,
                color: "rgb(var(--color-primary-500))",
                background: "rgb(var(--color-primary-500) / 0.1)",
                borderLeft: "2px solid rgb(var(--color-primary-500))",
                paddingLeft: "0.875rem",
              } : {
                display: "flex",
                alignItems: "center",
                gap: "0.75rem",
                padding: "0.625rem 1rem",
                margin: "0 0.5rem",
                borderRadius: "4px",
                fontSize: "0.875rem",
                fontWeight: 500,
                color: "rgb(var(--color-surface-300))",
                transition: "all 0.15s",
              }}
              className={active ? "" : "hover:text-white"}
            >
              <Icon size={16} />
              <span className="flex-1">{label}</span>
              {active && <ChevronRight size={14} style={{ opacity: 0.6 }} />}
            </Link>
          );
        })}
      </nav>

      {/* Footer */}
      <div
        className="p-4 text-xs"
        style={{ borderTop: "1px solid rgb(var(--color-surface-600))", color: "rgb(var(--color-surface-400))" }}
      >
        AUnrealFeatures
      </div>
    </aside>
  );
}
