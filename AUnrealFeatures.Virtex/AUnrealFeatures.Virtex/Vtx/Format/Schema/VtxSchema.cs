using System.Collections.Generic;

namespace VTX.Format
{
    /// <summary>One named, typed property within an entity type.</summary>
    public sealed class VtxPropertyDef
    {
        public string Name;
        public VtxPropKind Kind;

        /// <summary>Slot index into the matching typed container (or SoA sub-array index).</summary>
        public int Index;

        public VtxPropertyDef() { }
        public VtxPropertyDef(string name, VtxPropKind kind, int index)
        {
            Name = name; Kind = kind; Index = index;
        }
    }

    /// <summary>An entity type (e.g. "Player", "Projectile") and its property layout.</summary>
    public sealed class VtxEntityType
    {
        public int TypeId;
        public string Name;
        public List<VtxPropertyDef> Properties { get; } = new List<VtxPropertyDef>();

        public VtxEntityType() { }
        public VtxEntityType(int typeId, string name) { TypeId = typeId; Name = name; }

        public VtxEntityType Add(string name, VtxPropKind kind, int index)
        {
            Properties.Add(new VtxPropertyDef(name, kind, index));
            return this;
        }
    }

    /// <summary>
    /// Self-describing, per-game schema. Carried inline in every VTX stream/replay
    /// so a consumer decodes a title it has never seen: the bucket layout gives
    /// positional bucket names, and each entity type maps property names to
    /// container slots. This is what makes the format "per game" — ExampleShooter, ExampleTactical
    /// and Example Game each ship their own schema on their own stream.
    /// </summary>
    public sealed class VtxSchema
    {
        /// <summary>Stable game identity (e.g. "ExampleGame", "ExampleShooter", "ExampleTactical").</summary>
        public string GameId;

        public uint SchemaVersion = 1;

        /// <summary>Ordered bucket layout: <c>Buckets[i]</c> names the bucket stored at index i.</summary>
        public List<string> Buckets { get; } = new List<string>();

        public List<VtxEntityType> EntityTypes { get; } = new List<VtxEntityType>();

        public VtxSchema() { }
        public VtxSchema(string gameId, uint schemaVersion = 1)
        {
            GameId = gameId;
            SchemaVersion = schemaVersion;
        }

        public VtxSchema AddBucket(string name)
        {
            Buckets.Add(name);
            return this;
        }

        public VtxEntityType AddEntityType(int typeId, string name)
        {
            var t = new VtxEntityType(typeId, name);
            EntityTypes.Add(t);
            return t;
        }

        public string BucketNameAt(int index) =>
            index >= 0 && index < Buckets.Count ? Buckets[index] : null;

        public VtxEntityType GetEntityType(int typeId)
        {
            for (int i = 0; i < EntityTypes.Count; i++)
                if (EntityTypes[i].TypeId == typeId) return EntityTypes[i];
            return null;
        }
    }
}
