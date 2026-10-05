using System;
using System.Text;

namespace VTX.Format
{
    /// <summary>
    /// Growable little-endian binary writer — the single low-level encoder every
    /// VTX serializer writes through. Pure managed, no external dependencies, so
    /// the same source compiles for the Unity client and the .NET backend.
    ///
    /// Integer sizes/counts use LEB128 varints; fixed-width numerics are stored
    /// little-endian regardless of host endianness. Floats/doubles are written via
    /// their IEEE-754 bit patterns so the byte layout is deterministic everywhere.
    /// </summary>
    public sealed class VtxBufferWriter
    {
        private byte[] _buffer;
        private int _count;

        public VtxBufferWriter(int initialCapacity = 256)
        {
            _buffer = new byte[initialCapacity < 16 ? 16 : initialCapacity];
            _count = 0;
        }

        public int Length => _count;

        private void Ensure(int extra)
        {
            int needed = _count + extra;
            if (needed <= _buffer.Length) return;
            int cap = _buffer.Length;
            while (cap < needed) cap *= 2;
            Array.Resize(ref _buffer, cap);
        }

        public void WriteU8(byte value)
        {
            Ensure(1);
            _buffer[_count++] = value;
        }

        public void WriteBool(bool value) => WriteU8(value ? (byte)1 : (byte)0);

        public void WriteRaw(byte[] bytes, int offset, int length)
        {
            if (length <= 0) return;
            Ensure(length);
            Buffer.BlockCopy(bytes, offset, _buffer, _count, length);
            _count += length;
        }

        public void WriteRaw(byte[] bytes) => WriteRaw(bytes, 0, bytes.Length);

        /// <summary>Unsigned LEB128 varint.</summary>
        public void WriteVarUInt(ulong value)
        {
            Ensure(10);
            while (value >= 0x80)
            {
                _buffer[_count++] = (byte)(value | 0x80);
                value >>= 7;
            }
            _buffer[_count++] = (byte)value;
        }

        /// <summary>Signed varint (zigzag-encoded).</summary>
        public void WriteVarInt(long value) => WriteVarUInt((ulong)((value << 1) ^ (value >> 63)));

        /// <summary>Fixed 32-bit little-endian (used for framing length prefixes).</summary>
        public void WriteU32(uint value)
        {
            Ensure(4);
            _buffer[_count++] = (byte)value;
            _buffer[_count++] = (byte)(value >> 8);
            _buffer[_count++] = (byte)(value >> 16);
            _buffer[_count++] = (byte)(value >> 24);
        }

        public void WriteI32(int value) => WriteVarInt(value);
        public void WriteI64(long value) => WriteVarInt(value);
        public void WriteU64(ulong value)
        {
            Ensure(8);
            for (int i = 0; i < 8; i++) _buffer[_count++] = (byte)(value >> (i * 8));
        }

        public void WriteFloat(float value) => WriteU32(VtxBits.FloatToUInt32(value));

        public void WriteDouble(double value)
        {
            ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
            WriteU64(bits);
        }

        public void WriteString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteVarUInt(0);
                return;
            }
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            WriteVarUInt((ulong)utf8.Length);
            WriteRaw(utf8, 0, utf8.Length);
        }

        /// <summary>Copy the written bytes into a right-sized array.</summary>
        public byte[] ToArray()
        {
            var result = new byte[_count];
            Buffer.BlockCopy(_buffer, 0, result, 0, _count);
            return result;
        }

        /// <summary>Reset for reuse without reallocating the backing buffer.</summary>
        public void Clear() => _count = 0;
    }
}
