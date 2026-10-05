# Known limitations

AUnrealFeatures is a compatibility/backend framework, not a drop-in replacement for every upstream service.

- The projects currently target .NET 6, which is out of support. Migrate to a supported target before production deployment.
- Dashboard SSO callbacks are integration scaffolding and require a project-specific validation/session flow before public exposure.
- Optional identity-provider integrations require explicit endpoint URLs, application identifiers, and secrets.
- EOS discovery fixtures use neutral placeholder hosts for services not implemented by this repository. Replace or route those entries for the client features you enable.
- The optional `AUnrealFeatures.Virtex` adapter contains compatibility-oriented concepts such as partner portals and stadium routing. Treat it as an example adapter and customize or remove it when your title does not use that protocol.
- The allocator-agent TCP protocol does not provide transport-level authentication. It binds to loopback by default; use a private network or add authenticated transport before exposing it remotely.
- The content service is a basic static-file service. Use an object store/CDN for large production assets.
- No production load, abuse, penetration, or exact-client compatibility test suite is included.

Validate the repository with your exact game client, dedicated-server build, platform integrations, and deployment topology.
