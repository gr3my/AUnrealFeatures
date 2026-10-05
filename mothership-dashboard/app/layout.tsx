import type { Metadata } from "next";
import "./globals.css";
import Sidebar from "@/components/Sidebar";
import Header from "@/components/Header";

export const metadata: Metadata = {
  title: "Mothership Dashboard",
  description: "AA Mothership Admin Dashboard",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className="dark">
      <body data-theme="aa-theme">
        <div className="flex h-screen overflow-hidden">
          <Sidebar />
          <div className="flex flex-col flex-1 overflow-hidden">
            <Header />
            <main
              className="flex-1 overflow-y-auto p-6"
              style={{ background: "rgb(var(--color-surface-900))" }}
            >
              {children}
            </main>
          </div>
        </div>
      </body>
    </html>
  );
}
