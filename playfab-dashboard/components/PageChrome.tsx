"use client";
import { ChevronRight } from "lucide-react";
import Link from "next/link";

export interface BreadcrumbItem { label: string; href?: string; }
export interface TabItem { label: string; href?: string; active?: boolean; onClick?: () => void; }

export function PageChrome({
  title, subtitle, breadcrumbs, tabs, actions, children,
}: {
  title: string;
  subtitle?: string;
  breadcrumbs?: BreadcrumbItem[];
  tabs?: TabItem[];
  actions?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <div className="pf-page">
      <header className="pf-page-header">
        {breadcrumbs && breadcrumbs.length > 0 && (
          <ol className="pf-breadcrumb">
            {breadcrumbs.map((b, i) => (
              <li key={i}>
                {i > 0 && <ChevronRight size={12} className="pf-breadcrumb-sep" style={{ marginRight: 6 }} />}
                {b.href ? <Link href={b.href}>{b.label}</Link> : <span>{b.label}</span>}
              </li>
            ))}
          </ol>
        )}
        <div className="pf-page-header-title" style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
          <div>
            <h1 className="pf-page-h1">{title}</h1>
            {subtitle && <h2 className="pf-page-h2">{subtitle}</h2>}
          </div>
          {actions}
        </div>
        {tabs && tabs.length > 0 && (
          <div className="pf-tabs" style={{ marginTop: 16 }}>
            {tabs.map((t, i) => (
              t.href ? (
                <Link key={i} href={t.href} className={"pf-tab" + (t.active ? " active" : "")}>{t.label}</Link>
              ) : (
                <button key={i} type="button" onClick={t.onClick} className={"pf-tab" + (t.active ? " active" : "")}>{t.label}</button>
              )
            ))}
          </div>
        )}
      </header>
      {children}
    </div>
  );
}
