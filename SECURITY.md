# Security

AUnrealFeatures is a self-hosted backend framework. Treat every service as untrusted until you have configured authentication, TLS, network policy, and secrets for your deployment.

## Before exposing a service

- Replace every `CHANGE_ME_*` and `YOUR_*` value used by an enabled feature.
- Keep secrets in environment variables or a deployment secret store; do not commit them.
- Keep `ASTRA_HTTP_BIND=127.0.0.1` when using a reverse proxy. Bind to all interfaces only when required.
- Leave `ASTRA_TRUST_PROXY_HEADERS=false` unless a trusted reverse proxy overwrites forwarding headers.
- Restrict `ASTRA_CORS_ORIGIN` for browser-facing deployments.
- The allocator-agent TCP hub binds to loopback by default via `GAME_AGENT_BIND`. Keep it on a trusted network.
- Remote GameSocket control requests require `GAME_SOCKET_CONTROL_KEY`; loopback requests are permitted for local development.
- Configure provider URLs explicitly before enabling Steam or Meta verification.
- Keep nonce verification fail-closed unless you intentionally set `META_NONCE_FAIL_OPEN=true`.
- Put public endpoints behind TLS and rate limiting.
- Do not publish runtime databases, logs, session files, signing keys, or player data.

## Reporting a vulnerability

Do not post credentials, player data, or exploitable production details in a public issue. Use a private disclosure channel maintained by the repository owner.
