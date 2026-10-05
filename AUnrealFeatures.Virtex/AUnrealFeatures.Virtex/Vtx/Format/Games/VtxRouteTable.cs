using System.Collections.Generic;

namespace VTX.Format
{
    /// <summary>
    /// One routing rule: frames tagged with <see cref="SourceGameId"/> are
    /// delivered to consumers of <see cref="TargetStadium"/>. Normally 1:1
    /// (ExampleShooter → Stadium_ExampleShooter), but the mapping is data so a source can be
    /// re-pointed at a different stadium without code changes.
    /// </summary>
    public sealed class VtxRoute
    {
        public string SourceGameId;
        public string TargetStadium;
        public bool Enabled = true;

        public VtxRoute() { }
        public VtxRoute(string sourceGameId, string targetStadium)
        {
            SourceGameId = sourceGameId;
            TargetStadium = targetStadium;
        }
    }

    /// <summary>
    /// The configurable "what's being sent → who receives it" map that the backend
    /// LiveDataCommunications router consults. A single source game may fan out to
    /// multiple stadiums.
    /// </summary>
    public sealed class VtxRouteTable
    {
        private readonly List<VtxRoute> _routes = new List<VtxRoute>();

        public IReadOnlyList<VtxRoute> Routes => _routes;

        public VtxRouteTable Add(string sourceGameId, string targetStadium)
        {
            _routes.Add(new VtxRoute(sourceGameId, targetStadium));
            return this;
        }

        public VtxRouteTable Add(VtxRoute route)
        {
            if (route != null) _routes.Add(route);
            return this;
        }

        /// <summary>Enable/disable an existing route (e.g. pause a source without removing it).</summary>
        public void SetEnabled(string sourceGameId, string targetStadium, bool enabled)
        {
            foreach (var r in _routes)
                if (r.SourceGameId == sourceGameId && r.TargetStadium == targetStadium)
                    r.Enabled = enabled;
        }

        /// <summary>All enabled target stadiums for a source game.</summary>
        public IEnumerable<string> ResolveTargets(string sourceGameId)
        {
            foreach (var r in _routes)
                if (r.Enabled && r.SourceGameId == sourceGameId)
                    yield return r.TargetStadium;
        }

        /// <summary>Identity routes (each game → its own StadiumMap) from a registry.</summary>
        public static VtxRouteTable IdentityFrom(VtxGameRegistry registry)
        {
            var table = new VtxRouteTable();
            if (registry != null)
                foreach (var g in registry.Games)
                    if (!string.IsNullOrEmpty(g.StadiumMap))
                        table.Add(g.GameId, g.StadiumMap);
            return table;
        }
    }
}
