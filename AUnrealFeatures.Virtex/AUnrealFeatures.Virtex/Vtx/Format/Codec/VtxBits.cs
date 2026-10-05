using System.Runtime.InteropServices;

namespace VTX.Format
{
    /// <summary>
    /// Portable IEEE-754 float↔bits conversion. <c>BitConverter.SingleToInt32Bits</c>
    /// only exists on netstandard2.1+/.NET Core, but this codec must also compile
    /// inside a .NET Framework 4.7.2 MelonLoader mod. An explicit-layout union is
    /// allocation-free and supported on every target (Mono, IL2CPP, .NET). Double
    /// stays on <c>BitConverter.DoubleToInt64Bits</c>, which exists everywhere.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct FloatIntUnion
    {
        [FieldOffset(0)] public float F;
        [FieldOffset(0)] public uint U;
    }

    internal static class VtxBits
    {
        public static uint FloatToUInt32(float value) => new FloatIntUnion { F = value }.U;
        public static float UInt32ToFloat(uint bits) => new FloatIntUnion { U = bits }.F;
    }
}
