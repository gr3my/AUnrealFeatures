namespace VTX.Format
{
    /// <summary>
    /// A streamable title identity. Ties a stable game id (used to tag streams and
    /// route them) to the stadium map that renders it and the per-game
    /// <see cref="VtxSchema"/> that describes its frame data. One per source game
    /// (Example Game, ExampleShooter, ExampleTactical, ...).
    /// </summary>
    public sealed class VtxGame
    {
        /// <summary>Stable identity, e.g. "ExampleGame", "ExampleShooter", "ExampleTactical".</summary>
        public string GameId;

        /// <summary>Human-readable name for UI.</summary>
        public string DisplayName;

        /// <summary>Stadium level that renders this game's live data, e.g. "ExampleMap".</summary>
        public string StadiumMap;

        /// <summary>The frame-data schema for this game (carried inline on its stream).</summary>
        public VtxSchema Schema;

        public VtxGame() { }

        public VtxGame(string gameId, string stadiumMap, VtxSchema schema, string displayName = null)
        {
            GameId = gameId;
            StadiumMap = stadiumMap;
            Schema = schema;
            DisplayName = displayName ?? gameId;
        }
    }
}
