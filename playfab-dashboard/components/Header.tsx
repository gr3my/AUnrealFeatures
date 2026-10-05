"use client";
import Link from "next/link";
import { Bell, HelpCircle, ChevronDown, Sparkles } from "lucide-react";

export default function Header() {
  return (
    <header className="pf-topbar">
      {/* Left: PlayFab brand */}
      <Link href="/" className="pf-topbar-brand">
        <span className="pf-topbar-logo">PF</span>
      </Link>

      {/* Center: studio name */}
      <div className="pf-topbar-center">
        <span className="pf-topbar-studio-name">My Game Studio</span>
      </div>

      {/* Right: actions */}
      <div className="pf-topbar-trail">
        <a href="#" className="pf-topbar-link">Upgrade account</a>
        <button className="pf-topbar-icon-btn" aria-label="PlayFab Copilot"><Sparkles size={16} /></button>
        <button className="pf-topbar-icon-btn" aria-label="News"><Bell size={16} /></button>
        <button className="pf-topbar-icon-btn pf-topbar-menu-btn" aria-label="Help">
          <HelpCircle size={16} /><ChevronDown size={12} />
        </button>
        <button className="pf-topbar-profile" aria-label="Profile">
          <span className="pf-topbar-avatar">DR</span>
          <ChevronDown size={12} />
        </button>
      </div>
    </header>
  );
}
