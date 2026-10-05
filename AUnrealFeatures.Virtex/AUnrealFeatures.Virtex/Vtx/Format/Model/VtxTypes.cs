namespace VTX.Format
{
    /// <summary>3D position/direction, double precision (matches VTX <c>Vector</c>).</summary>
    public struct VtxVector
    {
        public double X, Y, Z;
        public VtxVector(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    /// <summary>Rotation quaternion, float precision (matches VTX <c>Quat</c>).</summary>
    public struct VtxQuat
    {
        public float X, Y, Z, W;
        public VtxQuat(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }

        public static VtxQuat Identity => new VtxQuat(0f, 0f, 0f, 1f);
    }

    /// <summary>Translation + rotation + scale (matches VTX <c>Transform</c>).</summary>
    public struct VtxTransform
    {
        public VtxVector Translation;
        public VtxQuat Rotation;
        public VtxVector Scale;

        public VtxTransform(VtxVector translation, VtxQuat rotation, VtxVector scale)
        {
            Translation = translation;
            Rotation = rotation;
            Scale = scale;
        }
    }

    /// <summary>A normalized range (e.g. a health bar 50/100), matches VTX <c>FloatRange</c>.</summary>
    public struct VtxFloatRange
    {
        public float Min, Max, ValueNormalized;
        public VtxFloatRange(float min, float max, float valueNormalized)
        {
            Min = min; Max = max; ValueNormalized = valueNormalized;
        }
    }

    /// <summary>An index range grouping entities of one type within a bucket (matches VTX <c>EntityRange</c>).</summary>
    public struct VtxEntityRange
    {
        public int Start;
        public int Count;
        public VtxEntityRange(int start, int count) { Start = start; Count = count; }
    }
}
