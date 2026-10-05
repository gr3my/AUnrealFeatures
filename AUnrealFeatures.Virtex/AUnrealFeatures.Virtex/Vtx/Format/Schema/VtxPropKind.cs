namespace VTX.Format
{
    /// <summary>
    /// Which typed container inside a <see cref="PropertyContainer"/> a schema
    /// property resolves to. The schema maps a property name to a
    /// (kind, index) pair; the decoder uses that to read the right list slot.
    /// Values are stable on the wire — append only, never renumber.
    /// </summary>
    public enum VtxPropKind : byte
    {
        // Scalars
        Bool = 0,
        Int32 = 1,
        Int64 = 2,
        Float = 3,
        Double = 4,
        String = 5,

        // Spatial
        Vector = 6,
        Quat = 7,
        Transform = 8,
        Range = 9,

        // Array properties (SoA sub-array index)
        ByteArray = 10,
        Int32Array = 11,
        Int64Array = 12,
        FloatArray = 13,
        DoubleArray = 14,
        StringArray = 15,
        BoolArray = 16,
        VectorArray = 17,
        QuatArray = 18,
        TransformArray = 19,
        RangeArray = 20,

        // Nested / recursive
        AnyStruct = 21,
        AnyStructArray = 22,
        Map = 23,
        MapArray = 24,
    }
}
