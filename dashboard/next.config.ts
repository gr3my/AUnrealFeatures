import type { NextConfig } from "next";

const dashboardBackend = process.env.ARES_DASHBOARD_BACKEND_URL ?? "http://127.0.0.1:8080";
const gameBackend = process.env.ARES_GAME_BACKEND_URL ?? "http://127.0.0.1:78";

const nextConfig: NextConfig = {
  async rewrites() {
    return [
      { source: "/api/:path*", destination: `${dashboardBackend}/api/:path*` },
      { source: "/auth/:path*", destination: `${dashboardBackend}/auth/:path*` },
      { source: "/game/:path*", destination: `${gameBackend}/:path*` },
    ];
  },
};

export default nextConfig;
