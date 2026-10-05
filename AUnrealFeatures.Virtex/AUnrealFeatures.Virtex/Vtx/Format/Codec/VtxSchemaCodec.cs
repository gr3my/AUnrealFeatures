namespace VTX.Format
{
    /// <summary>Binary (de)serializer for <see cref="VtxSchema"/>.</summary>
    public static class VtxSchemaCodec
    {
        public static void Write(VtxBufferWriter w, VtxSchema schema)
        {
            w.WriteString(schema.GameId ?? string.Empty);
            w.WriteVarUInt(schema.SchemaVersion);

            w.WriteVarUInt((ulong)schema.Buckets.Count);
            for (int i = 0; i < schema.Buckets.Count; i++)
                w.WriteString(schema.Buckets[i]);

            w.WriteVarUInt((ulong)schema.EntityTypes.Count);
            for (int i = 0; i < schema.EntityTypes.Count; i++)
            {
                var t = schema.EntityTypes[i];
                w.WriteI32(t.TypeId);
                w.WriteString(t.Name ?? string.Empty);
                w.WriteVarUInt((ulong)t.Properties.Count);
                for (int p = 0; p < t.Properties.Count; p++)
                {
                    var def = t.Properties[p];
                    w.WriteString(def.Name ?? string.Empty);
                    w.WriteU8((byte)def.Kind);
                    w.WriteVarUInt((ulong)def.Index);
                }
            }
        }

        public static VtxSchema Read(VtxBufferReader r)
        {
            var schema = new VtxSchema
            {
                GameId = r.ReadString(),
                SchemaVersion = (uint)r.ReadVarUInt(),
            };

            int bucketCount = (int)r.ReadVarUInt();
            for (int i = 0; i < bucketCount; i++)
                schema.Buckets.Add(r.ReadString());

            int typeCount = (int)r.ReadVarUInt();
            for (int i = 0; i < typeCount; i++)
            {
                int typeId = r.ReadI32();
                string name = r.ReadString();
                var t = schema.AddEntityType(typeId, name);
                int propCount = (int)r.ReadVarUInt();
                for (int p = 0; p < propCount; p++)
                {
                    string pname = r.ReadString();
                    var kind = (VtxPropKind)r.ReadU8();
                    int index = (int)r.ReadVarUInt();
                    t.Add(pname, kind, index);
                }
            }

            return schema;
        }

        public static byte[] ToBytes(VtxSchema schema)
        {
            var w = new VtxBufferWriter(256);
            Write(w, schema);
            return w.ToArray();
        }

        public static VtxSchema FromBytes(byte[] bytes) => Read(new VtxBufferReader(bytes));
    }
}
