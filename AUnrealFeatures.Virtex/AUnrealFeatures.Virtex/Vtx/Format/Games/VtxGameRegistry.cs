using System.Collections.Generic;

namespace VTX.Format
{
    /// <summary>
    /// Known streamable games and their schemas. Shared between the backend (which
    /// validates/relays per game) and Unity (which picks the stadium and accessor
    /// for an incoming stream). A stream is self-describing, so a consumer can also
    /// decode a game absent from its registry — the registry is for config/routing
    /// and choosing the render stadium, not a decode prerequisite.
    /// </summary>
    public sealed class VtxGameRegistry
    {
        private readonly Dictionary<string, VtxGame> _games = new Dictionary<string, VtxGame>();

        public IReadOnlyCollection<VtxGame> Games => _games.Values;

        public VtxGameRegistry Register(VtxGame game)
        {
            if (game != null && !string.IsNullOrEmpty(game.GameId))
                _games[game.GameId] = game;
            return this;
        }

        public VtxGame Get(string gameId) =>
            gameId != null && _games.TryGetValue(gameId, out var g) ? g : null;

        public bool Contains(string gameId) => gameId != null && _games.ContainsKey(gameId);

        /// <summary>
        /// The three example titles wired to their stadiums with starter schemas.
        /// Each schema is a sensible baseline (a Players bucket with pose/health);
        /// the injected native reader for each title supplies the authoritative
        /// schema on its stream, which supersedes this.
        /// </summary>
        public static VtxGameRegistry CreateDefault()
        {
            var reg = new VtxGameRegistry();
            reg.Register(new VtxGame("ExampleGame", "ExampleMap", BuildDefaultSchema("ExampleGame"), "Example Game"));
            reg.Register(new VtxGame("ExampleShooter", "Stadium_ExampleShooter", BuildDefaultSchema("ExampleShooter"), "ExampleShooter"));
            reg.Register(new VtxGame("ExampleTactical", "Stadium_ExampleTactical", BuildDefaultSchema("ExampleTactical"), "ExampleTactical"));
            return reg;
        }

        /// <summary>
        /// A minimal per-game schema: one "Players" bucket, a "Player" entity type
        /// carrying pose (Transform), health (Float), team (Int32) and name (String).
        /// Real per-game schemas will be richer and title-specific.
        /// </summary>
        public static VtxSchema BuildDefaultSchema(string gameId)
        {
            var schema = new VtxSchema(gameId);
            schema.AddBucket("Players");
            schema.AddEntityType(0, "Player")
                  .Add("Pose", VtxPropKind.Transform, 0)
                  .Add("Health", VtxPropKind.Float, 0)
                  .Add("Team", VtxPropKind.Int32, 0)
                  .Add("Name", VtxPropKind.String, 0);
            return schema;
        }
    }
}
