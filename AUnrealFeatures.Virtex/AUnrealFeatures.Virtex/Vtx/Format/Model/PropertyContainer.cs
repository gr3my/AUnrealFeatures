using System.Collections.Generic;

namespace VTX.Format
{
    /// <summary>
    /// All properties of a single entity, organized by type into explicit typed
    /// lists (matches VTX <c>PropertyContainer</c>). The per-game schema maps
    /// property names to a (kind, index) pair into these lists — the container
    /// itself is positional and schema-agnostic. Lists are lazily allocated;
    /// an empty list is not serialized (see <see cref="MapContainer"/> for maps).
    /// </summary>
    public sealed class PropertyContainer
    {
        /// <summary>Type identifier (maps to a schema entity type). -1 = unknown.</summary>
        public int EntityTypeId = -1;

        /// <summary>Optional xxHash-style digest of property data for fast equality/delta checks. 0 = unset.</summary>
        public ulong ContentHash = 0;

        // --- Scalars ---
        public List<bool> BoolProperties;
        public List<int> Int32Properties;
        public List<long> Int64Properties;
        public List<float> FloatProperties;
        public List<double> DoubleProperties;
        public List<string> StringProperties;

        // --- Spatial ---
        public List<VtxVector> VectorProperties;
        public List<VtxQuat> QuatProperties;
        public List<VtxTransform> TransformProperties;
        public List<VtxFloatRange> RangeProperties;

        // --- Array properties (SoA) ---
        public FlatArray<byte> ByteArrayProperties;
        public FlatArray<int> Int32Arrays;
        public FlatArray<long> Int64Arrays;
        public FlatArray<float> FloatArrays;
        public FlatArray<double> DoubleArrays;
        public FlatArray<string> StringArrays;
        public FlatArray<bool> BoolArrays;
        public FlatArray<VtxVector> VectorArrays;
        public FlatArray<VtxQuat> QuatArrays;
        public FlatArray<VtxTransform> TransformArrays;
        public FlatArray<VtxFloatRange> RangeArrays;

        // --- Nested / recursive ---
        public List<PropertyContainer> AnyStructProperties;
        public FlatArray<PropertyContainer> AnyStructArrays;
        public List<MapContainer> MapProperties;
        public FlatArray<MapContainer> MapArrays;

        // Convenience mutators used by producers so call sites don't null-check lists.
        public void AddFloat(float v) => (FloatProperties ??= new List<float>()).Add(v);
        public void AddInt32(int v) => (Int32Properties ??= new List<int>()).Add(v);
        public void AddInt64(long v) => (Int64Properties ??= new List<long>()).Add(v);
        public void AddDouble(double v) => (DoubleProperties ??= new List<double>()).Add(v);
        public void AddBool(bool v) => (BoolProperties ??= new List<bool>()).Add(v);
        public void AddString(string v) => (StringProperties ??= new List<string>()).Add(v);
        public void AddVector(VtxVector v) => (VectorProperties ??= new List<VtxVector>()).Add(v);
        public void AddQuat(VtxQuat v) => (QuatProperties ??= new List<VtxQuat>()).Add(v);
        public void AddTransform(VtxTransform v) => (TransformProperties ??= new List<VtxTransform>()).Add(v);
        public void AddRange(VtxFloatRange v) => (RangeProperties ??= new List<VtxFloatRange>()).Add(v);
    }

    /// <summary>Key-value map: parallel string keys and PropertyContainer values (matches VTX <c>MapContainer</c>).</summary>
    public sealed class MapContainer
    {
        public List<string> Keys { get; } = new List<string>();
        public List<PropertyContainer> Values { get; } = new List<PropertyContainer>();

        public void Add(string key, PropertyContainer value)
        {
            Keys.Add(key);
            Values.Add(value);
        }
    }
}
