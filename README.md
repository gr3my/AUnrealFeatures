# AUnrealFeatures

AUnrealFeatures is a self-hosted .NET backend toolkit for Unreal multiplayer projects. It provides PlayFab-compatible authentication/data APIs, an EOS-compatible gateway for sessions and matchmaking, dedicated-server registration/allocation, a socket control plane, and optional management dashboards.

The repository is designed as a starting point for your own game. No production credentials, title IDs, deployment IDs, private infrastructure addresses, or game-specific catalog data are included.

## Services

| Port | Service | Purpose |
| ---: | --- | --- |
| 50 | `EosGatewayServer` | EOS-compatible HTTP API and session/matchmaking endpoints |
| 78 | `StationDbServer` | Dedicated-server/station registration and deployment state |
| 80 | `EosWsServer` | EOS-compatible WebSocket endpoints |
| 90 | `MothershipServer` | Attestation, backend session and service-token endpoints |
| 8080 | `AresDashboardServer` | Dashboard/read API |
| 9091 | `PlayfabServer` | PlayFab-compatible game-service API |
| 9092 | `VirtexServer` | Optional compatibility adapter; customize or replace for your game |
| 9093 | `SkyboxServer` | Optional telemetry/replay adapter |
| 9094 | `ContentDownloadNetworkServer` | Static game-owned media/content |
| 9095 | `GameSocketServer` | Allocator HTTP control |
| 9100 | `AgentHub` | Allocator-agent TCP control channel |

## Requirements

- Visual Studio 2022 or a compatible .NET SDK
- .NET 6 target support (the projects currently target `net6.0`)
- Node.js only if you want to run the Next.js dashboards

> .NET 6 is out of support. The code remains on `net6.0` for compatibility with the existing projects; migrate to a supported target before production deployment.

## Configure

Configuration is read from process environment variables. `.env.example` documents the supported values, but the .NET host does **not** automatically load `.env` files. Set the variables in your IDE launch profile, shell, service manager, container configuration, or deployment platform.

At minimum, replace the placeholder secrets and identifiers for the services you enable:

- `PLAYFAB_TITLE_ID`, `PLAYFAB_DEV_SECRET`, `PLAYFAB_ENTITY_SECRET`
- `EOS_PRODUCT_ID`, `EOS_SANDBOX_ID`, `EOS_DEPLOYMENT_ID`, `EOS_ORGANIZATION_ID`, `EOS_CLIENT_ID`
- `ARES_API_SIGNING_SECRET`, `ARES_SERVER_MASTER_KEY`
- `GAME_SOCKET_CONTROL_KEY` if allocator control is accessed from another host
- `MOTHERSHIP_JWT_SECRET`, `MOTHERSHIP_SERVER_API_KEY`, `MOTHERSHIP_AUTOMATION_KEY`
- TURN/RTC credentials if voice or RTC is enabled
- `CONTENT_ROOT` if you want to serve media from a directory outside the repository
- Meta application values only if Meta attestation is enabled

Placeholder secrets beginning with `CHANGE_ME` are intentionally not accepted by protected authentication paths.

## Build and run

1. Open `AUnrealFeatures.sln`.
2. Restore NuGet packages.
3. Set `AUnrealFeatures.Ares` as the startup project.
4. Build the solution.
5. Run `AUnrealFeatures.Ares`.

From a terminal, the equivalent is:

```powershell
dotnet restore .\AUnrealFeatures.sln
dotnet run --project .\AUnrealFeatures.Ares\AUnrealFeatures.Ares.csproj
```

The optional verification script in `scripts/verify-backend.ps1` exercises server registration and EOS session synchronization after the backend is running. The content service listens on port `9094` by default and serves the repository's neutral placeholder asset unless `CONTENT_ROOT` is configured.

## Dashboards

Each dashboard is a separate Next.js app. Copy its `.env.example` values into your local environment, then run `npm install` and `npm run dev` from that dashboard directory.

Dashboard authentication is disabled by default for local development. Do not expose a dashboard publicly until you configure and test its authentication settings. The optional Meta SSO callback is intentionally project-specific and must be completed for your own application before enabling `DASHBOARD_AUTH_ENFORCE`.

## Game-specific customization

The public version intentionally ships without title-specific artwork, live catalog dumps, production endpoints, or private databases. Add your own:

- PlayFab title data/catalog content
- default maps and stream URLs
- content/media templates (a neutral placeholder is included)
- EOS identifiers and public endpoints
- game-server allocator implementation and launch arguments
- the optional compatibility adapter under `AUnrealFeatures.Virtex` (or replace it with your own adapter)

## Security

Do not commit real credentials, database files, runtime session files, private infrastructure addresses, or player data. Use environment variables or your deployment platform's secret store. Before exposing any service to the internet, place it behind TLS/reverse-proxy controls and review the authentication requirements for every enabled endpoint.

The HTTP listener binds to loopback by default. Set `ASTRA_HTTP_BIND` only when you intentionally need a different bind address. Forwarded client-IP headers are ignored unless `ASTRA_TRUST_PROXY_HEADERS=true`, and Meta nonce verification fails closed by default. See `SECURITY.md` for deployment guidance.

Provider URLs and game-owned URLs in the public template use localhost or `example.invalid` placeholders. Configure the services you enable before using them. EOS discovery templates may still contain protocol-defined third-party identifiers or scopes where changing them would alter compatibility behavior.

## Status

This repository is an extensible compatibility/backend framework rather than a turnkey hosted service. Core components are implemented, but production deployments still require game-specific configuration, security review, load testing, and end-to-end validation with your client/server builds. See `KNOWN_LIMITATIONS.md` for the remaining framework limitations.


## Public repository checklist

Before publishing a fork, search the repository for your own domains, IP addresses, title IDs, deployment IDs, platform application IDs, email addresses, database files, and secrets. Keep real values in environment variables or your deployment platform rather than source control.

Run `scripts/public-release-scan.ps1` before publishing. It checks for known legacy project identifiers and common runtime/secret file types.

The included dashboard privacy-policy page is a template only; replace it with a policy that accurately describes your game and deployment before release.
