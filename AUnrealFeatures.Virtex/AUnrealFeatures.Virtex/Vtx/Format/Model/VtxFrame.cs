using System.Collections.Generic;

namespace VTX.Format
{
    /// <summary>
    /// A named group of entities (matches VTX <c>Bucket</c>). <see cref="UniqueIds"/>
    /// and <see cref="Entities"/> are parallel: <c>UniqueIds[i]</c> identifies the
    /// entity whose data is in <c>Entities[i]</c>. Bucket names are positional on
    /// the wire (resolved via the schema's bucket layout); <see cref="Name"/> is
    /// restored on decode.
    /// </summary>
    public sealed class VtxBucket
    {
        public string Name;
        public List<string> UniqueIds { get; } = new List<string>();
        public List<PropertyContainer> Entities { get; } = new List<PropertyContainer>();
        public List<VtxEntityRange> TypeRanges { get; } = new List<VtxEntityRange>();

        public VtxBucket() { }
        public VtxBucket(string name) { Name = name; }

        public int EntityCount => Entities.Count;

        /// <summary>Append an entity with its unique id.</summary>
        public void Add(string uniqueId, PropertyContainer entity)
        {
            UniqueIds.Add(uniqueId);
            Entities.Add(entity);
        }
    }

    /// <summary>
    /// A single simulation tick (matches VTX <c>Frame</c>): one or more named
    /// buckets, plus streaming metadata (frame index and timing) carried alongside
    /// on the live wire.
    /// </summary>
    public sealed class VtxFrame
    {
        public List<VtxBucket> Buckets { get; } = new List<VtxBucket>();

        /// <summary>Sequential frame index within the stream/replay.</summary>
        public int FrameIndex;

        /// <summary>Game-time in seconds for this tick.</summary>
        public double GameTime;

        /// <summary>Unix timestamp (seconds) when the frame was produced. 0 = unset.</summary>
        public long UtcTimestamp;

        public VtxBucket CreateBucket(string name)
        {
            var bucket = new VtxBucket(name);
            Buckets.Add(bucket);
            return bucket;
        }

        public VtxBucket GetBucket(string name)
        {
            for (int i = 0; i < Buckets.Count; i++)
                if (Buckets[i].Name == name) return Buckets[i];
            return null;
        }
    }
}
