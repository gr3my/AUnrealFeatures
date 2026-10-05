import { NextRequest, NextResponse } from "next/server";

const backendBaseUrl = (process.env.PLAYFAB_URL ?? "http://localhost:9091").replace(/\/$/, "");

async function proxy(request: NextRequest, path: string[]) {
  const target = new URL(`${backendBaseUrl}/${path.join("/")}`);
  target.search = request.nextUrl.search;

  const headers = new Headers(request.headers);
  headers.delete("host");
  headers.delete("content-length");

  const authMode = headers.get("x-dashboard-auth");
  headers.delete("x-dashboard-auth");
  headers.delete("x-secretkey");

  if (authMode === "admin") {
    const secret = process.env.PLAYFAB_DEV_SECRET ?? "";
    if (!secret || secret.startsWith("CHANGE_ME")) {
      return NextResponse.json({ error: "PLAYFAB_DEV_SECRET is not configured on the dashboard server." }, { status: 503 });
    }
    headers.set("X-SecretKey", secret);
  }

  const body = request.method === "GET" || request.method === "HEAD"
    ? undefined
    : await request.arrayBuffer();

  const response = await fetch(target, {
    method: request.method,
    headers,
    body,
    cache: "no-store",
    redirect: "manual",
  });

  const responseHeaders = new Headers(response.headers);
  responseHeaders.delete("transfer-encoding");
  return new NextResponse(response.body, { status: response.status, headers: responseHeaders });
}

type RouteContext = { params: { path: string[] } };

export const GET = (request: NextRequest, context: RouteContext) => proxy(request, context.params.path);
export const POST = (request: NextRequest, context: RouteContext) => proxy(request, context.params.path);
export const PUT = (request: NextRequest, context: RouteContext) => proxy(request, context.params.path);
export const PATCH = (request: NextRequest, context: RouteContext) => proxy(request, context.params.path);
export const DELETE = (request: NextRequest, context: RouteContext) => proxy(request, context.params.path);
