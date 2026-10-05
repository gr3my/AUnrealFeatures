"use client";
import Link from "next/link";
import { usePathname } from "next/navigation";

const titles: Record<string, string> = {
  "/": "Overview",
  "/players": "Players",
  "/titles": "Title Management",
  "/environments": "Environments",
  "/deployments": "Deployments",
  "/moderation": "Moderation",
  "/title-data": "Title Data",
  "/user-data": "User Data",
  "/commerce": "Commerce",
  "/progression": "Progression",
  "/api-keys": "API Keys",
  "/friends": "Friends & Privacy",
  "/analytics": "Analytics",
};

export default function Header() {
  const path = usePathname();
  const title = titles[path] ?? "Mothership";

  return (
    <header>
      <div className="app-bar">
        <div className="app-bar-row-main">
          <div className="app-bar-slot-lead flex-none flex items-center">
            <Link href="/" className="head-leader">
              <div className="flex flex-row items-center gap-3">
                <span className="inline-flex h-9 w-9 items-center justify-center rounded bg-slate-700 font-semibold">AU</span>
                <span className="h3">Mothership</span>
              </div>
            </Link>
          </div>
          <div className="app-bar-slot-default flex-auto px-4">
            <p className="text-sm" style={{ color: "rgb(var(--color-surface-300))" }}>{title}</p>
          </div>
          <div className="app-bar-slot-trail flex-none flex items-center gap-4">
            <span className="badge variant-soft-success">● Live</span>
          </div>
        </div>
      </div>
    </header>
  );
}
