import { NextRequest, NextResponse } from "next/server";

const authEnforced = process.env.DASHBOARD_AUTH_ENFORCE === "true";
const sessionCookie = "aunreal_dashboard_session";

export function middleware(req: NextRequest) {
  if (!authEnforced) return NextResponse.next();

  const { pathname } = req.nextUrl;
  if (
    pathname.startsWith("/auth") ||
    pathname.startsWith("/api") ||
    pathname.startsWith("/game") ||
    pathname.startsWith("/_next") ||
    pathname === "/favicon.ico"
  ) {
    return NextResponse.next();
  }

  if (!req.cookies.get(sessionCookie)) {
    const url = req.nextUrl.clone();
    url.pathname = "/auth/login";
    return NextResponse.redirect(url);
  }

  return NextResponse.next();
}

export const config = {
  matcher: ["/((?!_next/static|_next/image|favicon.ico).*)"],
};
