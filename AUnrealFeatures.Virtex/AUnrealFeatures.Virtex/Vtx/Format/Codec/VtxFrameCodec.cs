using System.Collections.Generic;

namespace VTX.Format
{
    /// <summary>
    /// Binary (de)serializer for a <see cref="VtxFrame"/> and its entities.
    ///
    /// A <see cref="PropertyContainer"/> is written as: the entity type id, a
    /// varint presence bitmask (one bit per typed section), then only the
    /// non-empty sections in a fixed order. Most entities touch a handful of the
    /// 25 sections, so the bitmask keeps per-entity overhead to a few bytes.
    /// Nested structs and maps recurse through the same encoder.
    ///
    /// Bucket names are NOT written — they are positional and restored from the
    /// schema's bucket layout by <see cref="VtxStreamReader"/>.
    /// </summary>
    public static class VtxFrameCodec
    {
        // Presence bits (append-only; never renumber).
        private const int B_Bool = 0, B_Int32 = 1, B_Int64 = 2, B_Float = 3, B_Double = 4, B_String = 5;
        private const int B_Vector = 6, B_Quat = 7, B_Transform = 8, B_Range = 9;
        private const int B_ByteArr = 10, B_Int32Arr = 11, B_Int64Arr = 12, B_FloatArr = 13, B_DoubleArr = 14;
        private const int B_StringArr = 15, B_BoolArr = 16, B_VectorArr = 17, B_QuatArr = 18, B_TransformArr = 19, B_RangeArr = 20;
        private const int B_AnyStruct = 21, B_AnyStructArr = 22, B_Map = 23, B_MapArr = 24;
        private const int B_ContentHash = 25;

        // ---- Frame ----

        public static void WriteFrame(VtxBufferWriter w, VtxFrame frame)
        {
            w.WriteVarUInt((ulong)frame.Buckets.Count);
            for (int b = 0; b < frame.Buckets.Count; b++)
                WriteBucket(w, frame.Buckets[b]);
        }

        /// <summary>Decode a frame; bucket names are restored from <paramref name="schema"/> if provided.</summary>
        public static VtxFrame ReadFrame(VtxBufferReader r, VtxSchema schema = null)
        {
            var frame = new VtxFrame();
            int bucketCount = (int)r.ReadVarUInt();
            for (int b = 0; b < bucketCount; b++)
            {
                var bucket = ReadBucket(r);
                bucket.Name = schema != null ? schema.BucketNameAt(b) : null;
                frame.Buckets.Add(bucket);
            }
            return frame;
        }

        // ---- Bucket ----

        private static void WriteBucket(VtxBufferWriter w, VtxBucket bucket)
        {
            int n = bucket.Entities.Count;
            w.WriteVarUInt((ulong)n);
            for (int i = 0; i < n; i++)
            {
                w.WriteString(i < bucket.UniqueIds.Count ? bucket.UniqueIds[i] : string.Empty);
                WriteEntity(w, bucket.Entities[i]);
            }

            w.WriteVarUInt((ulong)bucket.TypeRanges.Count);
            for (int i = 0; i < bucket.TypeRanges.Count; i++)
            {
                w.WriteVarUInt((ulong)bucket.TypeRanges[i].Start);
                w.WriteVarUInt((ulong)bucket.TypeRanges[i].Count);
            }
        }

        private static VtxBucket ReadBucket(VtxBufferReader r)
        {
            var bucket = new VtxBucket();
            int n = (int)r.ReadVarUInt();
            for (int i = 0; i < n; i++)
            {
                string id = r.ReadString();
                var entity = ReadEntity(r);
                bucket.Add(id, entity);
            }

            int rangeCount = (int)r.ReadVarUInt();
            for (int i = 0; i < rangeCount; i++)
            {
                int start = (int)r.ReadVarUInt();
                int count = (int)r.ReadVarUInt();
                bucket.TypeRanges.Add(new VtxEntityRange(start, count));
            }
            return bucket;
        }

        // ---- Entity (PropertyContainer) ----

        private static void WriteEntity(VtxBufferWriter w, PropertyContainer e)
        {
            w.WriteI32(e.EntityTypeId);

            ulong mask = 0;
            void Bit(int bit, bool present) { if (present) mask |= 1UL << bit; }
            Bit(B_Bool, Has(e.BoolProperties));
            Bit(B_Int32, Has(e.Int32Properties));
            Bit(B_Int64, Has(e.Int64Properties));
            Bit(B_Float, Has(e.FloatProperties));
            Bit(B_Double, Has(e.DoubleProperties));
            Bit(B_String, Has(e.StringProperties));
            Bit(B_Vector, Has(e.VectorProperties));
            Bit(B_Quat, Has(e.QuatProperties));
            Bit(B_Transform, Has(e.TransformProperties));
            Bit(B_Range, Has(e.RangeProperties));
            Bit(B_ByteArr, Has(e.ByteArrayProperties));
            Bit(B_Int32Arr, Has(e.Int32Arrays));
            Bit(B_Int64Arr, Has(e.Int64Arrays));
            Bit(B_FloatArr, Has(e.FloatArrays));
            Bit(B_DoubleArr, Has(e.DoubleArrays));
            Bit(B_StringArr, Has(e.StringArrays));
            Bit(B_BoolArr, Has(e.BoolArrays));
            Bit(B_VectorArr, Has(e.VectorArrays));
            Bit(B_QuatArr, Has(e.QuatArrays));
            Bit(B_TransformArr, Has(e.TransformArrays));
            Bit(B_RangeArr, Has(e.RangeArrays));
            Bit(B_AnyStruct, Has(e.AnyStructProperties));
            Bit(B_AnyStructArr, Has(e.AnyStructArrays));
            Bit(B_Map, Has(e.MapProperties));
            Bit(B_MapArr, Has(e.MapArrays));
            Bit(B_ContentHash, e.ContentHash != 0);
            w.WriteVarUInt(mask);

            if (e.ContentHash != 0) w.WriteU64(e.ContentHash);

            if (Has(e.BoolProperties)) WriteList(w, e.BoolProperties, w.WriteBool);
            if (Has(e.Int32Properties)) WriteList(w, e.Int32Properties, w.WriteI32);
            if (Has(e.Int64Properties)) WriteList(w, e.Int64Properties, w.WriteI64);
            if (Has(e.FloatProperties)) WriteList(w, e.FloatProperties, w.WriteFloat);
            if (Has(e.DoubleProperties)) WriteList(w, e.DoubleProperties, w.WriteDouble);
            if (Has(e.StringProperties)) WriteList(w, e.StringProperties, w.WriteString);
            if (Has(e.VectorProperties)) WriteList(w, e.VectorProperties, v => WriteVector(w, v));
            if (Has(e.QuatProperties)) WriteList(w, e.QuatProperties, v => WriteQuat(w, v));
            if (Has(e.TransformProperties)) WriteList(w, e.TransformProperties, v => WriteTransform(w, v));
            if (Has(e.RangeProperties)) WriteList(w, e.RangeProperties, v => WriteRange(w, v));

            if (Has(e.ByteArrayProperties)) WriteFlat(w, e.ByteArrayProperties, w.WriteU8);
            if (Has(e.Int32Arrays)) WriteFlat(w, e.Int32Arrays, w.WriteI32);
            if (Has(e.Int64Arrays)) WriteFlat(w, e.Int64Arrays, w.WriteI64);
            if (Has(e.FloatArrays)) WriteFlat(w, e.FloatArrays, w.WriteFloat);
            if (Has(e.DoubleArrays)) WriteFlat(w, e.DoubleArrays, w.WriteDouble);
            if (Has(e.StringArrays)) WriteFlat(w, e.StringArrays, w.WriteString);
            if (Has(e.BoolArrays)) WriteFlat(w, e.BoolArrays, w.WriteBool);
            if (Has(e.VectorArrays)) WriteFlat(w, e.VectorArrays, v => WriteVector(w, v));
            if (Has(e.QuatArrays)) WriteFlat(w, e.QuatArrays, v => WriteQuat(w, v));
            if (Has(e.TransformArrays)) WriteFlat(w, e.TransformArrays, v => WriteTransform(w, v));
            if (Has(e.RangeArrays)) WriteFlat(w, e.RangeArrays, v => WriteRange(w, v));

            if (Has(e.AnyStructProperties)) WriteList(w, e.AnyStructProperties, v => WriteEntity(w, v));
            if (Has(e.AnyStructArrays)) WriteFlat(w, e.AnyStructArrays, v => WriteEntity(w, v));
            if (Has(e.MapProperties)) WriteList(w, e.MapProperties, v => WriteMap(w, v));
            if (Has(e.MapArrays)) WriteFlat(w, e.MapArrays, v => WriteMap(w, v));
        }

        private static PropertyContainer ReadEntity(VtxBufferReader r)
        {
            var e = new PropertyContainer { EntityTypeId = r.ReadI32() };
            ulong mask = r.ReadVarUInt();
            bool On(int bit) => (mask & (1UL << bit)) != 0;

            if (On(B_ContentHash)) e.ContentHash = r.ReadU64();

            if (On(B_Bool)) e.BoolProperties = ReadList(r, r.ReadBool);
            if (On(B_Int32)) e.Int32Properties = ReadList(r, r.ReadI32);
            if (On(B_Int64)) e.Int64Properties = ReadList(r, r.ReadI64);
            if (On(B_Float)) e.FloatProperties = ReadList(r, r.ReadFloat);
            if (On(B_Double)) e.DoubleProperties = ReadList(r, r.ReadDouble);
            if (On(B_String)) e.StringProperties = ReadList(r, r.ReadString);
            if (On(B_Vector)) e.VectorProperties = ReadList(r, () => ReadVector(r));
            if (On(B_Quat)) e.QuatProperties = ReadList(r, () => ReadQuat(r));
            if (On(B_Transform)) e.TransformProperties = ReadList(r, () => ReadTransform(r));
            if (On(B_Range)) e.RangeProperties = ReadList(r, () => ReadRange(r));

            if (On(B_ByteArr)) e.ByteArrayProperties = ReadFlat(r, r.ReadU8);
            if (On(B_Int32Arr)) e.Int32Arrays = ReadFlat(r, r.ReadI32);
            if (On(B_Int64Arr)) e.Int64Arrays = ReadFlat(r, r.ReadI64);
            if (On(B_FloatArr)) e.FloatArrays = ReadFlat(r, r.ReadFloat);
            if (On(B_DoubleArr)) e.DoubleArrays = ReadFlat(r, r.ReadDouble);
            if (On(B_StringArr)) e.StringArrays = ReadFlat(r, r.ReadString);
            if (On(B_BoolArr)) e.BoolArrays = ReadFlat(r, r.ReadBool);
            if (On(B_VectorArr)) e.VectorArrays = ReadFlat(r, () => ReadVector(r));
            if (On(B_QuatArr)) e.QuatArrays = ReadFlat(r, () => ReadQuat(r));
            if (On(B_TransformArr)) e.TransformArrays = ReadFlat(r, () => ReadTransform(r));
            if (On(B_RangeArr)) e.RangeArrays = ReadFlat(r, () => ReadRange(r));

            if (On(B_AnyStruct)) e.AnyStructProperties = ReadList(r, () => ReadEntity(r));
            if (On(B_AnyStructArr)) e.AnyStructArrays = ReadFlat(r, () => ReadEntity(r));
            if (On(B_Map)) e.MapProperties = ReadList(r, () => ReadMap(r));
            if (On(B_MapArr)) e.MapArrays = ReadFlat(r, () => ReadMap(r));

            return e;
        }

        // ---- MapContainer ----

        private static void WriteMap(VtxBufferWriter w, MapContainer map)
        {
            w.WriteVarUInt((ulong)map.Keys.Count);
            for (int i = 0; i < map.Keys.Count; i++)
            {
                w.WriteString(map.Keys[i]);
                WriteEntity(w, map.Values[i]);
            }
        }

        private static MapContainer ReadMap(VtxBufferReader r)
        {
            var map = new MapContainer();
            int n = (int)r.ReadVarUInt();
            for (int i = 0; i < n; i++)
                map.Add(r.ReadString(), ReadEntity(r));
            return map;
        }

        // ---- Element (de)serializers ----

        private static void WriteVector(VtxBufferWriter w, VtxVector v) { w.WriteDouble(v.X); w.WriteDouble(v.Y); w.WriteDouble(v.Z); }
        private static VtxVector ReadVector(VtxBufferReader r) => new VtxVector(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());

        private static void WriteQuat(VtxBufferWriter w, VtxQuat q) { w.WriteFloat(q.X); w.WriteFloat(q.Y); w.WriteFloat(q.Z); w.WriteFloat(q.W); }
        private static VtxQuat ReadQuat(VtxBufferReader r) => new VtxQuat(r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat());

        private static void WriteTransform(VtxBufferWriter w, VtxTransform t) { WriteVector(w, t.Translation); WriteQuat(w, t.Rotation); WriteVector(w, t.Scale); }
        private static VtxTransform ReadTransform(VtxBufferReader r) => new VtxTransform(ReadVector(r), ReadQuat(r), ReadVector(r));

        private static void WriteRange(VtxBufferWriter w, VtxFloatRange f) { w.WriteFloat(f.Min); w.WriteFloat(f.Max); w.WriteFloat(f.ValueNormalized); }
        private static VtxFloatRange ReadRange(VtxBufferReader r) => new VtxFloatRange(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());

        // ---- Generic list / FlatArray helpers ----

        private static bool Has<T>(List<T> list) => list != null && list.Count > 0;
        private static bool Has<T>(FlatArray<T> fa) => fa != null && !fa.IsEmpty;

        private static void WriteList<T>(VtxBufferWriter w, List<T> list, System.Action<T> writeElem)
        {
            w.WriteVarUInt((ulong)list.Count);
            for (int i = 0; i < list.Count; i++) writeElem(list[i]);
        }

        private static List<T> ReadList<T>(VtxBufferReader r, System.Func<T> readElem)
        {
            int n = (int)r.ReadVarUInt();
            var list = new List<T>(n);
            for (int i = 0; i < n; i++) list.Add(readElem());
            return list;
        }

        private static void WriteFlat<T>(VtxBufferWriter w, FlatArray<T> fa, System.Action<T> writeElem)
        {
            w.WriteVarUInt((ulong)fa.Data.Count);
            for (int i = 0; i < fa.Data.Count; i++) writeElem(fa.Data[i]);
            w.WriteVarUInt((ulong)fa.Offsets.Count);
            for (int i = 0; i < fa.Offsets.Count; i++) w.WriteVarUInt((ulong)fa.Offsets[i]);
        }

        private static FlatArray<T> ReadFlat<T>(VtxBufferReader r, System.Func<T> readElem)
        {
            var fa = new FlatArray<T>();
            int dataCount = (int)r.ReadVarUInt();
            for (int i = 0; i < dataCount; i++) fa.Data.Add(readElem());
            int offsetCount = (int)r.ReadVarUInt();
            for (int i = 0; i < offsetCount; i++) fa.Offsets.Add((int)r.ReadVarUInt());
            return fa;
        }
    }
}
