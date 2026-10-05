import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "PlayFab",
  description: "Self-hosted PlayFab clone admin",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
